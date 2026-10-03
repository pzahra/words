using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The Avalonia twin of <see cref="LiveWpfTests"/> (SPEC: Live language switching):
/// live, <c>{l:Words}</c> hands a styled property — an object-typed one too — a binding
/// that follows <see cref="Words.SwitchLanguage"/>, and <see cref="WordsInline"/> renders
/// again; off, the markup is the snapshot it always was. Swaps the Words.Known global,
/// hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class AvaLiveTests {
	private const string Ini =
		"value-en=English\n" +
		"value-de=Deutsch\n" +
		"\n" +
		"[k]\n" +
		"value-en=English value\n" +
		"value-de=Deutscher Wert\n";

	private const string Xmlns =
		"xmlns=\"https://github.com/avaloniaui\" " +
		"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
		"xmlns:l=\"https://github.com/pzahra/words\"";

	private static T Parse<T>(string xaml) => AvaloniaRuntimeXamlLoader.Parse<T>(xaml, typeof(LiveSource).Assembly);

	private const string Ini2 = Ini +
		"\n" +
		"[fmt]\n" +
		"value-en=N {0}\n" +
		"value-de=Nr. {0}\n" +
		"\n" +
		"[k2]\n" +
		"value-en=Second\n" +
		"value-de=Zweiter\n";

	//a converter for the converted shape, where a nested StaticResource can find it
	private const string Resources = "<StackPanel.Resources><l:WordsConverter x:Key=\"Format\"/></StackPanel.Resources>";
	private const string Converted = "Text=\"{l:Words {Binding Count, Converter={StaticResource Format}, ConverterParameter=fmt}}\"";

	//compiled: the binding inside {l:Words} arrives as Avalonia's compiled binding, not a Binding
	private static T InPanel<T>(string element, object source, bool compiled = false) where T : Control {
		string typed = compiled ? " xmlns:t=\"using:PatTech.Localization.Tests\" x:CompileBindings=\"True\" x:DataType=\"t:LiveSource\"" : "";
		var panel = Parse<StackPanel>($"<StackPanel {Xmlns}{typed}>{Resources}{element}</StackPanel>");
		panel.DataContext = source;
		return (T)panel.Children[0];
	}

	[AvaloniaFact]
	public void Live_ABoundKeyFollowsItsSourceAndTheSwitch() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini2).Live().Digest("en");
		var source = new LiveSource();
		var block = InPanel<TextBlock>("<TextBlock Text=\"{l:Words {Binding Key}}\"/>", source);
		Assert.Equal("English value", block.Text);

		Words.SwitchLanguage("de");
		Assert.Equal("Deutscher Wert", block.Text);

		source.Key = "k2";
		Assert.Equal("Zweiter", block.Text);
	}

	[AvaloniaFact]
	public void Live_AConvertedBindingConvertsAgainOnTheSwitch() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini2).Live().Digest("en");
		var source = new LiveSource();
		var block = InPanel<TextBlock>($"<TextBlock {Converted}/>", source);
		Assert.Equal("N 5", block.Text);

		Words.SwitchLanguage("de");
		Assert.Equal("Nr. 5", block.Text);

		source.Count = 6;
		Assert.Equal("Nr. 6", block.Text);
	}

	[AvaloniaFact]
	public void Live_CompiledBindingsAreWrappedToo() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini2).Live().Digest("en");
		var source = new LiveSource();
		var key = InPanel<TextBlock>("<TextBlock Text=\"{l:Words {Binding Key}}\"/>", source, compiled: true);
		var converted = InPanel<TextBlock>($"<TextBlock {Converted}/>", source, compiled: true);
		Assert.Equal("English value", key.Text);
		Assert.Equal("N 5", converted.Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", key.Text);
		Assert.Equal("Nr. 5", converted.Text);
	}

	[AvaloniaFact]
	public void Live_AnObjectPropertyTakesABoundKey() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini2).Live().Digest("en");
		var source = new LiveSource();
		var control = InPanel<ContentControl>("<ContentControl Content=\"{l:Words {Binding Key}}\"/>", source);
		Assert.Equal("English value", control.Content);

		Words.SwitchLanguage("de");
		Assert.Equal("Deutscher Wert", control.Content);

		source.Key = "k2";
		Assert.Equal("Zweiter", control.Content);
	}

	[AvaloniaFact]
	public void Off_TheBoundShapesFollowTheirSourceOnly() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini2);
		builder.Digest("en");
		var source = new LiveSource();
		var key = InPanel<TextBlock>("<TextBlock Text=\"{l:Words {Binding Key}}\"/>", source);
		var converted = InPanel<TextBlock>($"<TextBlock {Converted}/>", source);
		Assert.Equal("English value", key.Text);
		Assert.Equal("N 5", converted.Text);

		Words.Known = builder.ToWords("de");
		Assert.Equal("English value", key.Text); //a snapshot until the source moves
		Assert.Equal("N 5", converted.Text);

		source.Key = "k2";
		source.Count = 6;
		Assert.Equal("Zweiter", key.Text);       //looked up in whatever Known is now
		Assert.Equal("Nr. 6", converted.Text);
	}

	private static string TextOf(Span span) => string.Concat(span.Inlines.Select(TextOf));
	private static string TextOf(Inline inline) => inline switch {
		Run run => run.Text ?? "",
		Span span => TextOf(span),
		_ => "",
	};

	[AvaloniaFact]
	public void Live_AStyledPropertyFollowsTheSwitch() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var block = Parse<TextBlock>($"<TextBlock {Xmlns} Text=\"{{l:Words k}}\"/>");
		Assert.Equal("English value", block.Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", block.Text);
	}

	[AvaloniaFact]
	public void Live_AnObjectPropertyFollowsToo() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		//Content is object-typed: the loader would take a binding as the content itself
		var control = Parse<ContentControl>($"<ContentControl {Xmlns} Content=\"{{l:Words k}}\"/>");
		Assert.Equal("English value", control.Content);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", control.Content);
	}

	[AvaloniaFact]
	public void Live_WordsInlineRendersAgain() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var inline = new WordsInline { Key = "k" };
		Assert.Equal("English value", TextOf(inline));

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", TextOf(inline));
	}

	[AvaloniaFact]
	public void Live_SurvivesACollection() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var block = Parse<TextBlock>($"<TextBlock {Xmlns} Text=\"{{l:Words k}}\"/>");
		var control = Parse<ContentControl>($"<ContentControl {Xmlns} Content=\"{{l:Words k}}\"/>");
		Assert.Equal("English value", block.Text);
		//Avalonia holds a binding's source weakly: the shared holder needs another owner
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", block.Text);
		Assert.Equal("Deutscher Wert", control.Content);
	}

	[AvaloniaFact]
	public void Off_TheMarkupIsTheSnapshot() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini);
		builder.Digest("en");
		var block = Parse<TextBlock>($"<TextBlock {Xmlns} Text=\"{{l:Words k}}\"/>");
		var inline = new WordsInline { Key = "k" };
		Assert.Equal("English value", block.Text);

		Words.Known = builder.ToWords("de");

		Assert.Equal("English value", block.Text);
		Assert.Equal("English value", TextOf(inline));
	}
}
