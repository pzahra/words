using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Live language switching on the WPF side (SPEC: Live language switching): live,
/// <c>{l:Words}</c> hands a dependency property, or a setter, a binding that follows
/// <see cref="Words.SwitchLanguage"/>, hands a plain property the string as ever, and
/// <see cref="WordsInline"/> renders again; off, the markup is the snapshot it always
/// was. Swaps the Words.Known global, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class LiveWpfTests {
	private const string Ini =
		"value-en=English\n" +
		"value-de=Deutsch\n" +
		"\n" +
		"[k]\n" +
		"value-en=English value\n" +
		"value-de=Deutscher Wert\n" +
		"\n" +
		"[fmt]\n" +
		"value-en=N {0}\n" +
		"value-de=Nr. {0}\n";

	private const string Xmlns =
		"xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
		"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
		"xmlns:l=\"https://github.com/pzahra/words\"";

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap.</summary>
	private static void RunSta(Action action) {
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try { action(); }
			catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
	}

	private static TextBlock Parse(string attributes) => (TextBlock)XamlReader.Parse($"<TextBlock {Xmlns} {attributes}/>");

	private static string TextOf(Span span) => new TextRange(span.ContentStart, span.ContentEnd).Text;

	[Fact]
	public void Live_ADependencyPropertyFollowsTheSwitch() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		TextBlock block = Parse("Text=\"{l:Words k}\"");
		Assert.Equal("English value", block.Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", block.Text);
	});

	[Fact]
	public void Live_ASetterFollowsToo() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var block = (TextBlock)XamlReader.Parse(
			$"<TextBlock {Xmlns}><TextBlock.Style><Style TargetType=\"TextBlock\">" +
			"<Setter Property=\"Text\" Value=\"{l:Words k}\"/>" +
			"</Style></TextBlock.Style></TextBlock>");
		Assert.Equal("English value", block.Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", block.Text);
	});

	[Fact]
	public void Live_APlainPropertyTakesTheString() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		//a StringFormat can hold no binding: the string, resolved once, as before
		TextBlock block = Parse("Text=\"{Binding Source=5, StringFormat={l:Words fmt}}\"");
		Assert.Equal("N 5", block.Text);

		Words.SwitchLanguage("de");

		Assert.Equal("N 5", block.Text);
	});

	[Fact]
	public void Live_WordsInlineRendersAgain() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var inline = new WordsInline { Key = "k" };
		Assert.Equal("English value", TextOf(inline));

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", TextOf(inline));
	});

	[Fact]
	public void Off_TheMarkupIsTheSnapshot() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini);
		builder.Digest("en");
		TextBlock block = Parse("Text=\"{l:Words k}\"");
		var inline = new WordsInline { Key = "k" };
		Assert.Equal("English value", block.Text);

		Words.Known = builder.ToWords("de");

		Assert.Equal("English value", block.Text);
		Assert.Equal("English value", TextOf(inline));
	});
}
