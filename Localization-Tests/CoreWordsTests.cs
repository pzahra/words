using PatTech.Localization;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Covers Localization-Core behavior that has no UI: named formatting and the
/// markdown parser, exercised through a plain-string test renderer.
/// </summary>
public class CoreWordsTests {

	[Fact]
	public void FormatByName_NamedAndPositional() {
		var result = Words.FormatByName("{Name} met {0}.", new { Name = "Pat" }, "Sam");

		Assert.Equal("Pat met Sam.", result);
	}

	[Fact]
	public void FormatByName_RepeatedName_WithPositionalArgs_ReusesSameValue() {
		var result = Words.FormatByName("{Name} met {0}, and {Name} waved.", new { Name = "Pat" }, "Sam");

		Assert.Equal("Pat met Sam, and Pat waved.", result);
	}

	[Fact]
	public void FormatByName_MissingMember_RendersHashName() {
		var result = Words.FormatByName("{Nope}", new { Name = "Pat" });

		Assert.Equal("#Nope#", result);
	}

	[Fact]
	public void FormatByName_FromDictionary_NamedAndPositional() {
		// runtime-assembled names: same slots, same repeats, same #missing#
		var values = new Dictionary<string, object?> { ["Name"] = "Pat", ["Top"] = 1.2345 };

		var result = Words.FormatByName(System.Globalization.CultureInfo.InvariantCulture,
			"{Name} met {0}; N{Top:g2}; {Name} again; {Nope}", values, "Sam");

		Assert.Equal("Pat met Sam; N1.2; Pat again; #Nope#", result);
	}

	/// <summary>
	/// Renders inlines as plain strings so the abstract parser can be tested
	/// without a UI framework.
	/// </summary>
	private sealed class TextMarkdownParser(ITakeException? logger = null) : MarkdownParser<string>(logger) {
		protected override string Span(IEnumerable<string> inlines) => string.Concat(inlines);
		protected override string Run(string text) => text;
		protected override string Hyperlink(string content, Uri target, string? tooltip) => $"link({content}|{target}|{tooltip})";
		protected override string Image(Uri source, string? altText, string? tooltip) => $"image({source}|{altText}|{tooltip})";
		protected override void Embolden(ref string content) => content = $"<b>{content}</b>";
		protected override void Italicize(ref string content) => content = $"<i>{content}</i>";
		protected override void Subscript(ref string content) => content = $"<sub>{content}</sub>";
		protected override void Superscript(ref string content) => content = $"<sup>{content}</sup>";
		protected override string Code(string text) => $"<code>{text}</code>";
	}

	/// <summary>A parser of one's own written before code spans: it overrides no <c>Code</c>.</summary>
	private sealed class CodelessMarkdownParser() : MarkdownParser<string>(null) {
		protected override string Span(IEnumerable<string> inlines) => string.Concat(inlines);
		protected override string Run(string text) => $"[{text}]";
		protected override string Hyperlink(string content, Uri target, string? tooltip) => content;
		protected override string Image(Uri source, string? altText, string? tooltip) => altText ?? "";
		protected override void Embolden(ref string content) { }
		protected override void Italicize(ref string content) { }
		protected override void Subscript(ref string content) { }
		protected override void Superscript(ref string content) { }
	}

	[Fact]
	public void Markdown_Image_CapturesAltTextAndTitle() {
		var parser = new TextMarkdownParser();

		var inline = parser.ToInline(@"see ![a diagram](https://example.test/d.png ""hover"") here");

		Assert.Equal("see image(https://example.test/d.png|a diagram|hover) here", inline);
	}

	[Fact]
	public void Markdown_FullLink_RendersHyperlink() {
		var parser = new TextMarkdownParser();

		var inline = parser.ToInline("go [there](https://example.test/) now");

		Assert.Equal("link(there|https://example.test/|) now", inline[3..]);
	}

	[Fact]
	public void Markdown_SimpleLink_RendersHyperlink() {
		var parser = new TextMarkdownParser();

		var inline = parser.ToInline("go <https://example.test/> now");

		Assert.Equal("link(https://example.test/|https://example.test/|) now", inline[3..]);
	}

	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	[Fact]
	public void Markdown_MalformedImageUri_DegradesToAltText() {
		// translator-authored garbage must never take the paragraph down: a URI
		// that fails to parse renders like an unresolvable image and gripes
		var capture = new CaptureLogger();
		var parser = new TextMarkdownParser(capture);

		var inline = parser.ToInline("an ![icon](http://[) here");

		Assert.Equal("an [🖼️!icon] here", inline);
		Assert.Contains(capture.Messages, m => m.Contains("IMG:URI") && m.Contains("http://["));
	}

	[Fact]
	public void Markdown_MalformedLinkUri_LeavesTheLabelUnlinked() {
		var capture = new CaptureLogger();
		var parser = new TextMarkdownParser(capture);

		var inline = parser.ToInline("go [**there**](http://[) now");

		Assert.Equal("go <b>there</b> now", inline);
		Assert.Contains(capture.Messages, m => m.Contains("MD:HURI") && m.Contains("http://["));
	}

	[Fact]
	public void Markdown_EntitiesAndEmoji_Decode() {
		var parser = new TextMarkdownParser();

		Assert.Equal("Fish & Chips © 2026", parser.ToInline("Fish &amp; Chips &copy; 2026"));
		Assert.Equal("ship it \U0001F680", parser.ToInline("ship it :rocket:"));
		Assert.Equal("A and A", parser.ToInline("&#65; and &#x41;"));
	}

	[Fact]
	public void Markdown_BasicStyles_Nest() {
		var parser = new TextMarkdownParser();

		var inline = parser.ToInline("***loud*** and ~low~");

		Assert.Equal("<i><b>loud</b></i> and <sub>low</sub>", inline);
	}

	[Fact]
	public void Markdown_CodeSpan_ReadsNoMarkupInside() {
		var parser = new TextMarkdownParser();

		Assert.Equal("use <code>{l:Words key}</code> here", parser.ToInline("use `{l:Words key}` here"));
		Assert.Equal("<code>**bold** *it* ^up^ ~down~</code>", parser.ToInline("`**bold** *it* ^up^ ~down~`"));
		Assert.Equal("<code>[there](https://example.test/) <https://example.test/></code>",
			parser.ToInline("`[there](https://example.test/) <https://example.test/>`"));
		Assert.Equal("<code>![alt](assets:a.png)</code>", parser.ToInline("`![alt](assets:a.png)`"));
		Assert.Equal("<code>&amp; &#65; :rocket:</code>", parser.ToInline("`&amp; &#65; :rocket:`"));
	}

	[Fact]
	public void Markdown_CodeSpan_BindsTighterThanEmphasis() {
		var parser = new TextMarkdownParser();

		// the asterisk inside the span neither opens nor closes the italic around it
		Assert.Equal("<i>times <code>*</code> here</i>", parser.ToInline("*times `*` here*"));
		Assert.Equal("<code>2*3</code> and <code>4*5</code>", parser.ToInline("`2*3` and `4*5`"));
		// the span's own text comes back inside the emphasis and the link label
		Assert.Equal("<b>see <code>a</code></b>", parser.ToInline("**see `a`**"));
		Assert.Equal("link(<code>key]</code>|https://example.test/|)", parser.ToInline("[`key]`](https://example.test/)"));
	}

	[Fact]
	public void Markdown_CodeSpan_RunsOfBackticks() {
		var parser = new TextMarkdownParser();

		Assert.Equal("<code>a`b</code>", parser.ToInline("``a`b``"));
		Assert.Equal("<code>`</code>", parser.ToInline("`` ` ``"));
		// an unclosed run is literal, and a run of another length does not close it
		Assert.Equal("a ` b", parser.ToInline("a ` b"));
		Assert.Equal("``a` <i>b</i>", parser.ToInline("``a` *b*"));
	}

	[Fact]
	public void Markdown_CodeSpan_TrimsOneSpaceOrOneLineBreakAtEachEnd() {
		var parser = new TextMarkdownParser();

		Assert.Equal("<code>a</code>", parser.ToInline("` a `"));
		Assert.Equal("<code> a</code>", parser.ToInline("`  a `"));
		Assert.Equal("<code>a </code>", parser.ToInline("`a `"));
		Assert.Equal("<code>   </code>", parser.ToInline("`   `"));
		// on lines of its own a fence holds a block, its inner line breaks kept
		Assert.Equal("<code>[greeting]\nvalue=Hello</code>", parser.ToInline("```\n[greeting]\nvalue=Hello\n```"));
		Assert.Equal("<code>one\r\ntwo</code>", parser.ToInline("```\r\none\r\ntwo\r\n```"));
		Assert.Equal("<code>a\nb</code>", parser.ToInline("`a\nb`"));
	}

	[Fact]
	public void Markdown_CodeSpan_InAltTextAndTitle_IsWritten() {
		var parser = new TextMarkdownParser();

		var inline = parser.ToInline(@"![a `&amp;` b](https://example.test/d.png ""`&copy;` &copy;"")");

		Assert.Equal("image(https://example.test/d.png|a &amp; b|&copy; ©)", inline);
	}

	[Fact]
	public void Markdown_CodeSpan_DefaultsToARun() {
		var parser = new CodelessMarkdownParser();

		Assert.Equal("[use ][*x*][ here]", parser.ToInline("use `*x*` here"));
	}
}
