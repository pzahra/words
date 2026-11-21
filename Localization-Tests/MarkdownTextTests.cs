using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Navigation;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// <see cref="Markdown"/>'s <c>Markdown.Text</c> (SPEC: Markdown into a TextBlock of one's
/// own): markdown that is a string already fills a TextBlock's own inlines, and the TextBlock
/// keeps its wrapping; a new value renders again, null empties it, and a link in it navigates
/// through the global handler. <c>AvaMarkdownTextTests</c> (Localization-Ava.Tests) is the twin.
/// </summary>
public class MarkdownTextTests {

	private static T RunSta<T>(Func<T> func) {
		T result = default!;
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try { result = func(); }
			catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
		return result;
	}

	//every inline in the collection, spans and what they hold
	private static IEnumerable<Inline> All(InlineCollection inlines) {
		foreach (Inline inline in inlines) {
			yield return inline;
			if (inline is Span span) {
				foreach (Inline inner in All(span.Inlines)) {
					yield return inner;
				}
			}
		}
	}

	private static string Text(TextBlock block) => new TextRange(block.ContentStart, block.ContentEnd).Text;

	[Fact]
	public void TheMarkdown_FillsTheTextBlocksOwnInlines_AndItKeepsItsWrapping() {
		RunSta<object?>(() => {
			var block = new TextBlock { TextWrapping = TextWrapping.Wrap };

			Markdown.SetText(block, "Plain, **bold** and [a link](appcmd:ping).");

			Assert.Equal("Plain, bold and a link.", Text(block));
			Assert.Contains(All(block.Inlines), inline => inline.FontWeight == FontWeights.Bold);
			var link = Assert.Single(All(block.Inlines).OfType<System.Windows.Documents.Hyperlink>());
			Assert.Equal("appcmd:ping", link.NavigateUri.OriginalString);
			Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
			Assert.Equal("Plain, **bold** and [a link](appcmd:ping).", Markdown.GetText(block));
			return null;
		});
	}

	[Fact]
	public void ANewValue_RendersAgain_AndNullEmptiesIt() {
		RunSta<object?>(() => {
			var block = new TextBlock();
			Markdown.SetText(block, "first");

			Markdown.SetText(block, "*second*");
			Assert.Equal("second", Text(block));

			Markdown.SetText(block, null);
			Assert.Empty(block.Inlines);
			return null;
		});
	}

	[Fact]
	public void ALinkInIt_NavigatesThroughTheGlobalHandler() {
		RunSta<object?>(() => {
			var seen = new List<Uri>();
			using var subscription = Wpf.Hyperlink.RegisterGlobalNavigateHandler(seen.Add);
			var block = new TextBlock();
			Markdown.SetText(block, "[go](appcmd:ping)");
			var link = All(block.Inlines).OfType<System.Windows.Documents.Hyperlink>().Single();

			link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri, null) {
				RoutedEvent = System.Windows.Documents.Hyperlink.RequestNavigateEvent,
				Source = link,
			});

			Assert.Equal("appcmd:ping", Assert.Single(seen).OriginalString);
			return null;
		});
	}

	[Fact]
	public void TheMarkup_SetsIt_ByTheWordsNamespace() {
		RunSta<object?>(() => {
			var block = (TextBlock)XamlReader.Parse(
				"""<TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:l="https://github.com/pzahra/words" TextWrapping="Wrap" l:Markdown.Text="**hi** there"/>""");

			Assert.Equal("hi there", Text(block));
			Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
			return null;
		});
	}
}
