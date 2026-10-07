using System.Runtime.ExceptionServices;
using System.Windows;
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
		"value-de=Nr. {0}\n" +
		"\n" +
		"[k2]\n" +
		"value-en=Second\n" +
		"value-de=Zweiter\n";

	//a converter for the converted shape, where a nested StaticResource can find it
	private const string Resources = "<StackPanel.Resources><l:WordsConverter x:Key=\"Format\"/></StackPanel.Resources>";
	private const string Converted = "Text=\"{l:Words {Binding Count, Converter={StaticResource Format}, ConverterParameter=fmt}}\"";

	private static TextBlock InPanel(string attributes, object source) {
		var panel = (StackPanel)XamlReader.Parse($"<StackPanel {Xmlns}>{Resources}<TextBlock {attributes}/></StackPanel>");
		panel.DataContext = source;
		Pump();
		return (TextBlock)panel.Children[0];
	}

	//a binding finds its DataContext on a dispatcher pass, and a test thread runs no loop:
	//an empty operation below DataBind priority lets the pending lookups through
	private static void Pump()
		=> System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

	private const string Xmlns =
		"xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
		"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
		"xmlns:l=\"https://github.com/pzahra/words\"";

	/// <summary>
	///     WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap, with a
	///     synchronization context, as a WPF UI thread has, so the one trigger homes here
	///     and not on the thread of a test before (<see cref="TriggerWords.Watch"/>).
	/// </summary>
	private static void RunSta(Action action) {
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			SynchronizationContext.SetSynchronizationContext(new InlineContext());
			try { action(); }
			catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
	}

	//a test's thread is gone when the next test switches, and a dispatcher's context would
	//queue a refresh to it for good: this one runs it at once, as the registry runs a
	//refresh for a thread with no context
	private sealed class InlineContext : SynchronizationContext {
		public override void Post(SendOrPostCallback d, object? state) => d(state);
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
	public void Live_SurvivesACollection() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		TextBlock block = Parse("Text=\"{l:Words k}\"");
		Assert.Equal("English value", block.Text);
		//the shared holder's only owners are weak but for the binding: it must keep it
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", block.Text);
	});

	[Fact]
	public void Live_ABoundKeyFollowsItsSourceAndTheSwitch() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var source = new LiveSource();
		TextBlock block = InPanel("Text=\"{l:Words {Binding Key}}\"", source);
		Assert.Equal("English value", block.Text);

		Words.SwitchLanguage("de");
		Assert.Equal("Deutscher Wert", block.Text);

		source.Key = "k2";
		Assert.Equal("Zweiter", block.Text);
	});

	[Fact]
	public void Live_AConvertedBindingConvertsAgainOnTheSwitch() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var source = new LiveSource();
		TextBlock block = InPanel(Converted, source);
		Assert.Equal("N 5", block.Text);

		Words.SwitchLanguage("de");
		Assert.Equal("Nr. 5", block.Text);

		source.Count = 6;
		Assert.Equal("Nr. 6", block.Text);
	});

	[Fact]
	public void Live_ASetterTakesABoundKey() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var block = (TextBlock)XamlReader.Parse(
			$"<TextBlock {Xmlns}><TextBlock.Style><Style TargetType=\"TextBlock\">" +
			"<Setter Property=\"Text\" Value=\"{l:Words {Binding Key}}\"/>" +
			"</Style></TextBlock.Style></TextBlock>");
		block.DataContext = new LiveSource();
		Pump();
		Assert.Equal("English value", block.Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", block.Text);
	});

	[Fact]
	public void Live_ATemplateLoadedTwiceConvertsInBoth() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		//the wrapped binding loses its converter to the multi-binding on first use: a second
		//instance must get the same multi-binding, not a bare binding read as a key
		var template = (DataTemplate)XamlReader.Parse(
			$"<DataTemplate {Xmlns}><StackPanel>{Resources}<TextBlock {Converted}/></StackPanel></DataTemplate>");
		var source = new LiveSource();
		var first = (StackPanel)template.LoadContent();
		var second = (StackPanel)template.LoadContent();
		first.DataContext = source;
		second.DataContext = source;
		Pump();
		Assert.Equal("N 5", ((TextBlock)first.Children[0]).Text);
		Assert.Equal("N 5", ((TextBlock)second.Children[0]).Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Nr. 5", ((TextBlock)first.Children[0]).Text);
		Assert.Equal("Nr. 5", ((TextBlock)second.Children[0]).Text);
	});

	//a key inside a control template, as a custom control's look carries its labels
	private static readonly string TemplatedKey =
		$"<ContentControl {Xmlns}><ContentControl.Template><ControlTemplate TargetType=\"ContentControl\">" +
		"<TextBlock Text=\"{l:Words k}\"/>" +
		"</ControlTemplate></ContentControl.Template></ContentControl>";

	private static TextBlock Templated(ContentControl control) {
		control.ApplyTemplate();
		return (TextBlock)System.Windows.Media.VisualTreeHelper.GetChild(control, 0);
	}

	[Fact]
	public void Live_AKeyInAControlTemplateFollows() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var first = (ContentControl)XamlReader.Parse(TemplatedKey);
		var second = (ContentControl)XamlReader.Parse(TemplatedKey);
		Assert.Equal("English value", Templated(first).Text);
		Assert.Equal("English value", Templated(second).Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", Templated(first).Text);
		Assert.Equal("Deutscher Wert", Templated(second).Text);
	});

	[Fact]
	public void Live_AKeyInATemplateParsedBeforeTheWordsFollows() => RunSta(() => {
		using var globals = new WordsGlobals();
		//an App.xaml style loads before startup code digests the words and goes live
		var control = (ContentControl)XamlReader.Parse(TemplatedKey);
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		Assert.Equal("English value", Templated(control).Text);

		Words.SwitchLanguage("de");

		Assert.Equal("Deutscher Wert", Templated(control).Text);
	});

	[Fact]
	public void Off_TheBoundShapesFollowTheirSourceOnly() => RunSta(() => {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini);
		builder.Digest("en");
		var source = new LiveSource();
		TextBlock key = InPanel("Text=\"{l:Words {Binding Key}}\"", source);
		TextBlock converted = InPanel(Converted, source);
		Assert.Equal("English value", key.Text);
		Assert.Equal("N 5", converted.Text);

		Words.Known = builder.ToWords("de");
		Assert.Equal("English value", key.Text); //a snapshot until the source moves
		Assert.Equal("N 5", converted.Text);

		source.Key = "k2";
		source.Count = 6;
		Assert.Equal("Zweiter", key.Text);       //looked up in whatever Known is now
		Assert.Equal("Nr. 6", converted.Text);
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
