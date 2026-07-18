using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PatTech.Localization.Wpf;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
/// Covers the WPF image-scheme registry: custom scheme registration, options
/// parsing, uniform size/background/tooltip handling, and alt-text fallback.
/// (The Avalonia twin mirrors the same design; it has no test host here.)
/// </summary>
public class ImageSchemeTests {

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap.</summary>
	private static T RunSta<T>(Func<T> func) {
		T result = default!;
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try {
				result = func();
			}
			catch (Exception e) {
				error = ExceptionDispatchInfo.Capture(e);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
		return result;
	}

	private sealed class FakeResolver(Func<Uri, ImageOptions, FrameworkElement?> resolve) : IImageSchemeResolver {
		public Uri? LastSource;
		public ImageOptions? LastOptions;

		public FrameworkElement? Resolve(Uri source, ImageOptions options) {
			LastSource = source;
			LastOptions = options;
			return resolve(source, options);
		}
	}

	[Fact]
	public void CustomScheme_ResolvesThroughRegistry() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			var resolver = new FakeResolver((_, _) => new TextBlock { Text = "icon" });
			parser.ImageSchemes["fake"] = resolver;

			var inline = parser.ToInline("![alt](fake:thing)");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var textBlock = Assert.IsType<TextBlock>(container.Child);
			Assert.Equal("icon", textBlock.Text);
			Assert.Equal("fake", resolver.LastSource?.Scheme);
			return null;
		});
	}

	[Fact]
	public void UnknownScheme_FallsBackToAltText() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();

			var inline = parser.ToInline("![the alt](nosuch:thing)");

			var run = Assert.IsType<Run>(inline);
			Assert.Equal("the alt", run.Text);
			return null;
		});
	}

	[Fact]
	public void ResolverReturningNull_FallsBackToAltText() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => null);

			var inline = parser.ToInline("![the alt](fake:thing)");

			var run = Assert.IsType<Run>(inline);
			Assert.Equal("the alt", run.Text);
			return null;
		});
	}

	[Fact]
	public void ResolverThrowing_FallsBackToAltText() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => throw new InvalidOperationException("no image for you"));

			var inline = parser.ToInline("![the alt](fake:thing)");

			var run = Assert.IsType<Run>(inline);
			Assert.Equal("the alt", run.Text);
			return null;
		});
	}

	[Fact]
	public void SizeBackgroundAndTooltip_AppliedUniformlyByParser() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());

			var inline = parser.ToInline(@"![alt](fake:thing?width=32&height=16&background=Red ""tip"")");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var border = Assert.IsType<Border>(container.Child);
			Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(border.Background).Color);
			Assert.Equal("tip", ToolTipService.GetToolTip(border));
			var textBlock = Assert.IsType<TextBlock>(border.Child);
			Assert.Equal(32, textBlock.Width);
			Assert.Equal(16, textBlock.Height);
			return null;
		});
	}

	[Fact]
	public void RasterImage_WithoutSizeOptions_KeepsNaturalSize() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new Image());

			var inline = parser.ToInline("![alt](fake:thing)");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var image = Assert.IsType<Image>(container.Child);
			Assert.True(double.IsNaN(image.Width));
			Assert.True(double.IsNaN(image.Height));
			return null;
		});
	}

	[Fact]
	public void Registry_IsPerInstance() {
		var schooled = new MarkdownParser();
		schooled.ImageSchemes["fake"] = new FakeResolver((_, _) => null);

		Assert.False(new MarkdownParser().ImageSchemes.ContainsKey("fake"));
		Assert.True(schooled.ImageSchemes.ContainsKey("fake"));
	}

	[Fact]
	public void ImageOptions_Parse_ReadsKnownAndCustomOptions() {
		var options = RunSta(() => ImageOptions.Parse("?width=24&height=12.5&foreground=DarkRed&kind=ContentSave"));

		Assert.Equal(24, options.Width);
		Assert.Equal(12.5, options.Height);
		Assert.Equal(Colors.DarkRed, Assert.IsType<SolidColorBrush>(options.Foreground).Color);
		Assert.Null(options.Background);
		Assert.Equal("ContentSave", options.Query["kind"]);
	}

	[Fact]
	public void ImageOptions_Parse_IgnoresNonsenseValues() {
		var options = RunSta(() => ImageOptions.Parse("width=very&background=NotAColor"));

		Assert.Null(options.Width);
		Assert.Null(options.Background);
	}
}
