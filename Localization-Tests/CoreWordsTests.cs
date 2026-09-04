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
}
