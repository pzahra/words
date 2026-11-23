using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The Avalonia twin of <c>MarkdownTextTests</c> (Localization-Tests): <see cref="Markdown"/>'s
/// <c>Markdown.Text</c> (SPEC: Markdown into a TextBlock of one's own) fills a TextBlock's own
/// inlines, which keeps its wrapping; a new value renders again, null empties it, and a link
/// in it, clicked, navigates through the global handler. The navigate handler, the shared
/// parser and the logger are globals, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class AvaMarkdownTextTests {
	//a parser that throws on a run holding "boom", as a broken one of an app's own would
	private sealed class Throwing : MarkdownParser {
		protected override Inline Run(string text) => text.Contains("boom") ? throw new InvalidOperationException("boom") : base.Run(text);
	}

	private sealed class Gripes : ITakeException {
		public readonly List<string> Errors = [];
		public void Warn(string text) { }
		public void Error(Exception exception, string message) => Errors.Add(message);
	}

	[AvaloniaFact]
	public void AValueThatWillNotRender_IsSaid_AndShownAsWritten() {
		using var globals = new WordsGlobals();
		MarkdownParser parser = MarkdownParser.Default;
		var gripes = new Gripes();
		Words.Logger = gripes;
		try {
			var block = new TextBlock();
			Markdown.SetText(block, "previous");
			MarkdownParser.Default = new Throwing();

			Markdown.SetText(block, "a **boom**");

			Assert.Equal("a **boom**", string.Concat(All(block.Inlines).OfType<Run>().Select(run => run.Text))); //what it holds, never what stood
			Assert.Equal(["MD:TEXT"], gripes.Errors);
		}
		finally {
			MarkdownParser.Default = parser;
		}
	}

	//every inline in the collection, spans and what they hold
	private static IEnumerable<Inline> All(InlineCollection? inlines) {
		foreach (Inline inline in inlines ?? []) {
			yield return inline;
			if (inline is Span span) {
				foreach (Inline inner in All(span.Inlines)) {
					yield return inner;
				}
			}
		}
	}

	private static string Text(TextBlock block) => string.Concat(All(block.Inlines).OfType<Run>().Select(run => run.Text));

	[AvaloniaFact]
	public void TheMarkdown_FillsTheTextBlocksOwnInlines_AndItKeepsItsWrapping() {
		var block = new TextBlock { TextWrapping = TextWrapping.Wrap };

		Markdown.SetText(block, "Plain, **bold** and [a link](appcmd:ping).");

		Assert.Equal("Plain, bold and a link.", Text(block));
		Assert.Contains(All(block.Inlines), inline => inline.FontWeight == FontWeight.Bold);
		var link = Assert.Single(All(block.Inlines).OfType<Hyperlink>());
		Assert.Equal("appcmd:ping", link.Uri!.OriginalString);
		Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
		Assert.Equal("Plain, **bold** and [a link](appcmd:ping).", Markdown.GetText(block));
	}

	[AvaloniaFact]
	public void ANewValue_RendersAgain_AndNullEmptiesIt() {
		var block = new TextBlock();
		Markdown.SetText(block, "first");

		Markdown.SetText(block, "*second*");
		Assert.Equal("second", Text(block));

		Markdown.SetText(block, null);
		Assert.Empty(block.Inlines ?? []);
	}

	[AvaloniaFact]
	public void ALinkInIt_Clicked_NavigatesThroughTheGlobalHandler() {
		var seen = new List<Uri>();
		using var subscription = Hyperlink.RegisterGlobalNavigateHandler(seen.Add);
		//a background to press on: the headless hit test does not find a press on the text alone
		var block = new TextBlock { FontSize = 20, Background = Brushes.Transparent };
		var window = new Window { Content = block, Width = 400, Height = 100 };
		window.Show();
		Markdown.SetText(block, "[go there](appcmd:ping) and on");
		Dispatcher.UIThread.RunJobs();
		//a frame drawn, which is what a click is hit-tested against
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();

		window.MouseDown(new Point(4, block.Bounds.Height / 2), MouseButton.Left);

		Assert.Equal("appcmd:ping", Assert.Single(seen).OriginalString);
		window.Close();
	}

	[AvaloniaFact]
	public void TheMarkup_SetsIt_ByTheWordsNamespace() {
		var block = (TextBlock)AvaloniaRuntimeXamlLoader.Parse(
			"""<TextBlock xmlns="https://github.com/avaloniaui" xmlns:l="https://github.com/pzahra/words" TextWrapping="Wrap" l:Markdown.Text="**hi** there"/>""",
			typeof(Markdown).Assembly);

		Assert.Equal("hi there", Text(block));
		Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
	}
}
