using PatTech.Localization;
using Xunit;

namespace WordsEdit.Tests;

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

	/// <summary>
	/// Renders inlines as plain strings so the abstract parser can be tested
	/// without a UI framework.
	/// </summary>
	private sealed class TextMarkdownParser() : MarkdownParser<string>(null) {
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

	[Fact]
	public void Markdown_BasicStyles_Nest() {
		var parser = new TextMarkdownParser();

		var inline = parser.ToInline("***loud*** and ~low~");

		Assert.Equal("<i><b>loud</b></i> and <sub>low</sub>", inline);
	}
}
