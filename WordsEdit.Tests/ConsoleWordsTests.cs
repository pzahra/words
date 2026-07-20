using PatTech.Localization;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
/// Covers the terminal renderer: ANSI styling, OSC 8 hyperlinks, Unicode
/// sub/superscripts, plain-text degradation, and the Console.WriteWords glue.
/// </summary>
public class ConsoleWordsTests {
	private const string Esc = "\x1b";

	[Fact]
	public void Ansi_BoldItalic_WrapInSgrCodes() {
		var parser = new ConsoleMarkdownParser(useAnsi: true);

		var text = parser.ToInline("a **bold** and *sly* word");

		Assert.Equal($"a {Esc}[1mbold{Esc}[22m and {Esc}[3msly{Esc}[23m word", text);
	}

	[Fact]
	public void Ansi_Hyperlink_UsesOsc8() {
		var parser = new ConsoleMarkdownParser(useAnsi: true);

		var text = parser.ToInline("[docs](https://example.test/)");

		Assert.Equal(
			$"{Esc}]8;;https://example.test/{Esc}\\{Esc}[4;34mdocs{Esc}[24;39m{Esc}]8;;{Esc}\\",
			text);
	}

	[Fact]
	public void Plain_Hyperlink_SpellsOutUrl() {
		var parser = new ConsoleMarkdownParser(useAnsi: false);

		Assert.Equal("docs (https://example.test/)", parser.ToInline("[docs](https://example.test/)"));
		Assert.Equal("https://example.test/", parser.ToInline("<https://example.test/>"));
	}

	[Fact]
	public void Plain_Styles_PassThroughUndecorated() {
		var parser = new ConsoleMarkdownParser(useAnsi: false);

		Assert.Equal("a bold and sly word", parser.ToInline("a **bold** and *sly* word"));
	}

	[Fact]
	public void SubAndSuperscript_TranslateToUnicode() {
		var parser = new ConsoleMarkdownParser(useAnsi: false);

		Assert.Equal("m²·K/W and H₂O", parser.ToInline("m^2^·K/W and H~2~O"));
	}

	[Fact]
	public void Superscript_UnmappableCharacters_StayOnBaseline() {
		var parser = new ConsoleMarkdownParser(useAnsi: false);

		// 'q' famously has no superscript form; '?' has none either.
		Assert.Equal("ˢᵉq?", parser.ToInline("^seq?^"));
	}

	[Fact]
	public void Image_RendersAltText() {
		var parser = new ConsoleMarkdownParser(useAnsi: true);

		var text = parser.ToInline(@"see ![a diagram](https://example.test/d.png ""hover"")");

		Assert.Equal("see [🖼️!a diagram]", text);
	}

	[Fact]
	public void WriteWordsLine_MissingKey_WritesPlaceholder() {
		var original = Console.Out;
		try {
			var writer = new StringWriter();
			Console.SetOut(writer);

			Console.WriteWordsLine("console.test.missing-key");

			Assert.Contains("#console.test.missing-key#", writer.ToString());
		}
		finally {
			Console.SetOut(original);
		}
	}
}
