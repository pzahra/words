using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Covers the Avalonia image-scheme registry — the twin of the WPF
/// <see cref="ImageSchemeTests"/>, running in the headless test host.
/// Shares the Words.Logger global with the WPF suite, hence the collection.
/// </summary>
[Collection("Words globals")]
public class AvaImageSchemeTests {

	private sealed class FakeResolver(Func<Uri, ImageOptions, Control?> resolve) : IImageSchemeResolver {
		public Uri? LastSource;
		public ImageOptions? LastOptions;

		public Control? Resolve(Uri source, ImageOptions options) {
			LastSource = source;
			LastOptions = options;
			return resolve(source, options);
		}
	}

	[AvaloniaFact]
	public void CustomScheme_ResolvesThroughRegistry() {
		var parser = new MarkdownParser();
		var resolver = new FakeResolver((_, _) => new TextBlock { Text = "icon" });
		parser.ImageSchemes["fake"] = resolver;

		var inline = parser.ToInline("![alt](fake:thing)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var textBlock = Assert.IsType<TextBlock>(container.Child);
		Assert.Equal("icon", textBlock.Text);
		Assert.Equal("fake", resolver.LastSource?.Scheme);
	}

	[AvaloniaFact]
	public void UnknownScheme_FallsBackToAltText() {
		var parser = new MarkdownParser();

		var inline = parser.ToInline("![the alt](nosuch:thing)");

		var run = Assert.IsType<Run>(inline);
		Assert.Equal("[🖼️!the alt]", run.Text);
	}

	[AvaloniaFact]
	public void ResolverReturningNull_FallsBackToAltText() {
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => null);

		var inline = parser.ToInline("![the alt](fake:thing)");

		var run = Assert.IsType<Run>(inline);
		Assert.Equal("[🖼️!the alt]", run.Text);
	}

	[AvaloniaFact]
	public void ResolverThrowing_FallsBackToAltText() {
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => throw new InvalidOperationException("no image for you"));

		var inline = parser.ToInline("![the alt](fake:thing)");

		var run = Assert.IsType<Run>(inline);
		Assert.Equal("[🖼️!the alt]", run.Text);
	}

	[AvaloniaFact]
	public void SizeBackgroundAndTooltip_AppliedUniformlyByParser() {
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());

		var inline = parser.ToInline(@"![alt](fake:thing?width=32&height=16&background=Red ""tip"")");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var border = Assert.IsType<Border>(container.Child);
		Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(border.Background).Color);
		Assert.Equal("tip", ToolTip.GetTip(border));
		var textBlock = Assert.IsType<TextBlock>(border.Child);
		Assert.Equal(32, textBlock.Width);
		Assert.Equal(16, textBlock.Height);
	}

	[AvaloniaFact]
	public void NoQueryScheme_QueryIsSplitOffByHand() {
		// avares registers a UriParser with no query support, leaving the `?`
		// glued to the asset path. Emulate that registration (with its own
		// scheme name — the WPF twin owns `fakeres` in this process): the
		// options must still apply, and the resolver must get a query-less URI.
		UriParser.Register(new GenericUriParser(
			GenericUriParserOptions.GenericAuthority
			| GenericUriParserOptions.NoQuery
			| GenericUriParserOptions.NoFragment), "avafakeres", -1);
		var parser = new MarkdownParser();
		var resolver = new FakeResolver((_, _) => new TextBlock());
		parser.ImageSchemes["avafakeres"] = resolver;

		var inline = parser.ToInline("![alt](avafakeres://host/thing.png?width=32&height=16)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var textBlock = Assert.IsType<TextBlock>(container.Child);
		Assert.Equal(32, textBlock.Width);
		Assert.Equal(16, textBlock.Height);
		Assert.Equal("avafakeres://host/thing.png", resolver.LastSource?.OriginalString);
	}

	[AvaloniaFact]
	public void RegularScheme_QueryAlsoTrimmedFromResolverUri() {
		// the query carries display options, not asset identity, so the resolver
		// gets a query-less URI even when System.Uri parsed the query itself
		var parser = new MarkdownParser();
		var resolver = new FakeResolver((_, _) => new TextBlock());
		parser.ImageSchemes["fake"] = resolver;

		parser.ToInline("![alt](fake:thing?width=32)");

		Assert.Equal("fake:thing", resolver.LastSource?.OriginalString);
		Assert.Equal(32, resolver.LastOptions?.Width);
	}

	[AvaloniaFact]
	public void RasterImage_WithoutSizeOptions_KeepsNaturalSize() {
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new Image());

		var inline = parser.ToInline("![alt](fake:thing)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var image = Assert.IsType<Image>(container.Child);
		Assert.True(double.IsNaN(image.Width));
		Assert.True(double.IsNaN(image.Height));
	}

	private static Image TinyImage(int pixelWidth, int pixelHeight) => new() {
		Source = new WriteableBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96)),
		Stretch = Stretch.Uniform,
	};

	[AvaloniaFact]
	public void RasterImage_WithSource_PinnedToNaturalSize() {
		// measured with the whole line's constraint, an unpinned Stretch.Uniform
		// image balloons to fill it; no options means natural size, so pin it
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => TinyImage(10, 8));

		var inline = parser.ToInline("![alt](fake:thing)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var image = Assert.IsType<Image>(container.Child);
		Assert.Equal(10, image.Width);
		Assert.Equal(8, image.Height);
	}

	[AvaloniaFact]
	public void RasterImage_OneSizeOption_LeavesTheOtherToAspectRatio() {
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => TinyImage(10, 8));

		var inline = parser.ToInline("![alt](fake:thing?height=16)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var image = Assert.IsType<Image>(container.Child);
		Assert.True(double.IsNaN(image.Width));
		Assert.Equal(16, image.Height);
	}

	[AvaloniaFact]
	public void Geometry_WithoutSizeOptions_DefaultsToFontHeight() {
		var parser = new MarkdownParser(baseFontSize: 20);
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new global::Avalonia.Controls.Shapes.Path {
			Data = StreamGeometry.Parse("M 0,0 L 8,0 8,8 0,8 Z"),
		});

		var inline = parser.ToInline("![alt](fake:thing)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var path = Assert.IsType<global::Avalonia.Controls.Shapes.Path>(container.Child);
		Assert.Equal(20, path.Height);
	}

	private sealed class CaptureLogger : ITakeException {
		public readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
		public void Warn(string text) => Messages.Enqueue(text);
		public void Error(Exception exception, string message) => Messages.Enqueue(message);
	}

	[AvaloniaFact]
	public void DefaultParser_GripesThroughWordsLogger_EvenWhenAssignedLate() {
		var capture = new CaptureLogger();
		var original = Words.Logger;
		try {
			// assigned long after MarkdownParser.Default was constructed
			Words.Logger = capture;
			MarkdownParser.Default.ToInline("![alt](nosuch:thing)");
		}
		finally {
			Words.Logger = original;
		}

		Assert.Contains(capture.Messages, m => m.Contains("IMG:RES") && m.Contains("nosuch:thing"));
	}

	[Fact]
	public void Registry_IsPerInstance() {
		var schooled = new MarkdownParser();
		schooled.ImageSchemes["fake"] = new FakeResolver((_, _) => null);

		Assert.False(new MarkdownParser().ImageSchemes.ContainsKey("fake"));
		Assert.True(schooled.ImageSchemes.ContainsKey("fake"));
	}

	[Theory]
	[InlineData("assets:../secret.png")]
	[InlineData("assets:icons/../../secret.png")]
	[InlineData("assets:..%5C..%5Csecret.png")]
	[InlineData("assets:C:/Windows/notepad.exe")]
	public void AssetPath_EscapeAttempts_AreClamped(string uri) {
		Assert.Null(AssetsImageResolver.ResolveAssetPath(new Uri(uri)));
	}

	[Fact]
	public void AssetPath_HonestPath_ResolvesUnderAssetsRoot() {
		var path = AssetsImageResolver.ResolveAssetPath(new Uri("assets:icons/save.png"));

		Assert.NotNull(path);
		Assert.StartsWith(AppContext.BaseDirectory, path);
		Assert.EndsWith(Path.Combine("Assets", "icons", "save.png"), path);
	}

	[Fact]
	public void AssetPath_PercentEncoding_UnescapesToRealFileName() {
		var path = AssetsImageResolver.ResolveAssetPath(new Uri("assets:tiny%20ava%20image.png"));

		Assert.NotNull(path);
		Assert.EndsWith(Path.Combine("Assets", "tiny ava image.png"), path);
	}

	[AvaloniaFact]
	public void AssetsResolver_ExistingFile_LoadsBitmap() {
		// its own file name: the WPF twin writes `tiny image.png` into the same
		// Assets folder, and xunit may run both classes concurrently
		var assetsDir = Directory.CreateDirectory(
			Path.Combine(AppContext.BaseDirectory, "Assets"));
		var file = Path.Combine(assetsDir.FullName, "tiny ava image.png");
		if (!File.Exists(file)) {
			using var bitmap = new RenderTargetBitmap(new PixelSize(1, 1));
			bitmap.Save(file);
		}

		var visual = new AssetsImageResolver().Resolve(new Uri("assets:tiny%20ava%20image.png"), new ImageOptions());

		var image = Assert.IsType<Image>(visual);
		Assert.NotNull(image.Source);
	}

	[AvaloniaFact]
	public void ImageOptions_Parse_ReadsKnownAndCustomOptions() {
		var options = ImageOptions.Parse("?width=24&height=12.5&foreground=DarkRed&kind=ContentSave");

		Assert.Equal(24, options.Width);
		Assert.Equal(12.5, options.Height);
		Assert.Equal(Colors.DarkRed, Assert.IsType<SolidColorBrush>(options.Foreground).Color);
		Assert.Null(options.Background);
		Assert.Equal("ContentSave", options.Query["kind"]);
	}

	[AvaloniaFact]
	public void ImageOptions_Parse_IgnoresNonsenseValues() {
		var options = ImageOptions.Parse("width=very&background=NotAColor");

		Assert.Null(options.Width);
		Assert.Null(options.Background);
	}
}
