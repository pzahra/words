using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The WPF look of rendered markdown a theme may restyle: a link's colour from
/// WordsLinkBrush, a code span's monospace and tint from WordsCodeFont and
/// WordsCodeBackground — looked up where the inline lands, a fixed look where there is
/// none. <see cref="AvaThemeResourceTests"/> is the Avalonia twin.
/// </summary>
public class ThemeResourceTests {

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap.</summary>
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

	private static SolidColorBrush Frozen(Color color) {
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	/// <summary>Puts <paramref name="inline"/> in a TextBlock under a Grid carrying <paramref name="resources"/>.</summary>
	private static T Attach<T>(Inline inline, ResourceDictionary resources) where T : Inline {
		var grid = new Grid { Resources = resources };
		var textBlock = new TextBlock();
		grid.Children.Add(textBlock);
		textBlock.Inlines.Add(inline);
		return Assert.IsType<T>(inline);
	}

	private static Color ColorOf(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

	[Fact]
	public void Link_WithNoResources_IsBlue() {
		RunSta<object?>(() => {
			var link = Assert.IsType<System.Windows.Documents.Hyperlink>(new MarkdownParser().ToInline("[docs](https://example.test/)"));

			Assert.Equal(Colors.Blue, ColorOf(link.Foreground));
			return null;
		});
	}

	[Fact]
	public void Link_FollowsASwappedThemeDictionary() {
		RunSta<object?>(() => {
			var resources = new ResourceDictionary();
			resources.MergedDictionaries.Add(new ResourceDictionary { ["WordsLinkBrush"] = Frozen(Colors.Gold) });
			var link = Attach<System.Windows.Documents.Hyperlink>(new MarkdownParser().ToInline("[docs](https://example.test/)"), resources);
			Assert.Equal(Colors.Gold, ColorOf(link.Foreground));

			// the sample's App.ApplyTheme in miniature: one merged theme swapped for another
			resources.MergedDictionaries[0] = new ResourceDictionary { ["WordsLinkBrush"] = Colors.Silver };

			Assert.Equal(Colors.Silver, ColorOf(link.Foreground));
			return null;
		});
	}

	[Fact]
	public void CodeSpan_WithNoResources_IsMonospaceOnATint() {
		RunSta<object?>(() => {
			var span = Assert.IsType<Span>(new MarkdownParser().ToInline("a `*b*` c"));
			var runs = span.Inlines.Cast<Run>().ToList();

			Assert.Equal(["a ", "*b*", " c"], runs.Select(run => run.Text));
			Assert.Equal("Consolas, Courier New", runs[1].FontFamily.Source);
			Assert.Equal(Color.FromArgb(0x22, 0x80, 0x80, 0x80), ColorOf(runs[1].Background));
			// the text around it keeps whatever it inherits
			Assert.Equal(DependencyProperty.UnsetValue, runs[0].ReadLocalValue(TextElement.FontFamilyProperty));
			return null;
		});
	}

	[Fact]
	public void CodeSpan_TakesTheResourcesWhereItLands_AndFollowsThem() {
		RunSta<object?>(() => {
			var local = new ResourceDictionary {
				["WordsCodeFont"] = new FontFamily("Cascadia Mono"),
				["WordsCodeBackground"] = Frozen(Colors.Gold),
			};
			var run = Attach<Run>(new MarkdownParser().ToInline("`x`"), local);

			Assert.Equal("Cascadia Mono", run.FontFamily.Source);
			Assert.Equal(Colors.Gold, ColorOf(run.Background));

			local["WordsCodeBackground"] = Colors.Silver; // a colour this time
			local.Remove("WordsCodeFont");

			Assert.Equal(Colors.Silver, ColorOf(run.Background));
			Assert.Equal("Consolas, Courier New", run.FontFamily.Source);
			return null;
		});
	}

	[Fact]
	public void CodeSpan_InALink_TakesTheLinkColour() {
		RunSta<object?>(() => {
			var resources = new ResourceDictionary { ["WordsLinkBrush"] = Frozen(Colors.Gold) };
			var link = Attach<System.Windows.Documents.Hyperlink>(new MarkdownParser().ToInline("[`key`](https://example.test/)"), resources);

			var run = Assert.IsType<Run>(Assert.Single(link.Inlines));
			Assert.Equal("key", run.Text);
			Assert.Equal(Colors.Gold, ColorOf(run.Foreground));
			Assert.Equal("Consolas, Courier New", run.FontFamily.Source);
			return null;
		});
	}
}
