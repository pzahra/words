using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
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

	[AvaloniaFact]
	public void Dimension_Huge_IsCapped() {
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());

		var inline = parser.ToInline("![alt](fake:thing?width=100000&height=100000)");

		var container = Assert.IsType<InlineUIContainer>(inline);
		var textBlock = Assert.IsType<TextBlock>(container.Child);
		Assert.Equal(4096, textBlock.Width);
		Assert.Equal(4096, textBlock.Height);
	}

	[Theory]
	[InlineData("fake:thing?width=-5&height=NaN")]
	[InlineData("fake:thing?width=Infinity")]
	public void Dimension_NonFiniteOrNegative_IgnoredNotThrownAsMissingImage(string uri) {
		// a bad dimension must not throw (which the parser would surface as a
		// missing image); the visual renders at its natural size instead
		var parser = new MarkdownParser();
		parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());

		var inline = parser.ToInline($"![alt]({uri})");

		var container = Assert.IsType<InlineUIContainer>(inline); // not a Run of alt text
		var textBlock = Assert.IsType<TextBlock>(container.Child);
		Assert.True(double.IsNaN(textBlock.Width));
	}

	/// <summary>
	/// Shows <paramref name="inline"/> in a TextBlock in a Window carrying
	/// <paramref name="resources"/> (and, when given, a requested <paramref name="theme"/>),
	/// so a tree-scoped lookup has somewhere to look, and hands back the resource host
	/// the image resolved to. The window is shown first, so adding the inline is what
	/// attaches it — and resolves it.
	/// </summary>
	private static ResourceImage Attach(Inline inline, ResourceDictionary? resources = null, ThemeVariant? theme = null)
		=> Attach(inline, out _, resources, theme);

	/// <inheritdoc cref="Attach(Inline, ResourceDictionary?, ThemeVariant?)"/>
	/// <param name="window">The window, for tests that go on to change it.</param>
	private static ResourceImage Attach(Inline inline, out Window window, ResourceDictionary? resources = null, ThemeVariant? theme = null) {
		window = new Window();
		if (resources is not null) window.Resources = resources;
		if (theme is not null) window.RequestedThemeVariant = theme;
		var textBlock = new TextBlock();
		window.Content = textBlock;
		window.Show();
		textBlock.Inlines!.Add(inline);
		var container = Assert.IsType<InlineUIContainer>(inline);
		return Assert.IsType<ResourceImage>(container.Child);
	}

	/// <summary>A dictionary whose only content is <paramref name="light"/>/<paramref name="dark"/> entries for <paramref name="key"/> — the shape of the sample's ThemeIcon.</summary>
	private static ResourceDictionary Themed(string key, object light, object dark) {
		var themed = new ResourceDictionary();
		themed.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { [key] = light };
		themed.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { [key] = dark };
		return themed;
	}

	[AvaloniaFact]
	public void StaticRes_ThemeScopedResource_FoundForTheVariantInEffect() {
		// the sample's ThemeIcon lives in ThemeDictionaries, not the plain dictionary:
		// a theme-less lookup never sees it, so the static path must ask for the variant
		var light = StreamGeometry.Parse("M0,0 L4,4 L0,4 Z");
		var dark = StreamGeometry.Parse("M0,0 L8,8 L0,8 Z");

		var host = Attach(new MarkdownParser().ToInline("![a](staticres:avaThemedGeo)"), Themed("avaThemedGeo", light, dark), ThemeVariant.Light);

		Assert.Same(light, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child).Data);
	}

	[AvaloniaFact]
	public void DynRes_FollowsAThemeVariantChange() {
		var light = StreamGeometry.Parse("M0,0 L4,4 L0,4 Z");
		var dark = StreamGeometry.Parse("M0,0 L8,8 L0,8 Z");
		var host = Attach(new MarkdownParser().ToInline("![a](dynres:avaVariantGeo)"), out var window, Themed("avaVariantGeo", light, dark), ThemeVariant.Light);
		Assert.Same(light, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child).Data);

		window.RequestedThemeVariant = ThemeVariant.Dark; // the sample's dark-theme switch

		Assert.Same(dark, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child).Data);
	}

	[AvaloniaFact]
	public void StaticRes_GeometryResource_GivesAFreshHostEachResolve() {
		Application.Current!.Resources["avaGeo"] = StreamGeometry.Parse("M0,0 L10,10 L0,10 Z");
		var parser = new MarkdownParser();

		var a = Assert.IsType<global::Avalonia.Controls.Shapes.Path>(Attach(parser.ToInline("![a](staticres:avaGeo)")).Child);
		var b = Assert.IsType<global::Avalonia.Controls.Shapes.Path>(Attach(parser.ToInline("![b](staticres:avaGeo)")).Child);

		Assert.NotSame(a, b);        // reuse-safe: not one shared element
		Assert.Same(a.Data, b.Data); // the geometry value itself shares freely
	}

	[AvaloniaFact]
	public void StaticRes_TreeScopedResource_FoundFromTheElementNotJustTheApp() {
		// on the Window only: an app-level lookup would never see it
		var local = new ResourceDictionary { ["avaLocalGeo"] = StreamGeometry.Parse("M0,0 L4,4 L0,4 Z") };

		var host = Attach(new MarkdownParser().ToInline("![a](staticres:avaLocalGeo)"), local);

		Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child);
	}

	[AvaloniaFact]
	public void DynRes_FollowsAResourceSwap() {
		var first = StreamGeometry.Parse("M0,0 L4,4 L0,4 Z");
		var second = StreamGeometry.Parse("M0,0 L8,8 L0,8 Z");
		var local = new ResourceDictionary { ["avaLiveGeo"] = first };
		var host = Attach(new MarkdownParser().ToInline("![a](dynres:avaLiveGeo)"), local);
		Assert.Same(first, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child).Data);

		local["avaLiveGeo"] = second; // a theme swap, in miniature

		Assert.Same(second, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child).Data);
	}

	[AvaloniaFact]
	public void StaticRes_KeepsTheFirstValue_IgnoringLaterSwaps() {
		var first = StreamGeometry.Parse("M0,0 L4,4 L0,4 Z");
		var local = new ResourceDictionary { ["avaPinnedGeo"] = first };
		var host = Attach(new MarkdownParser().ToInline("![a](staticres:avaPinnedGeo)"), local);

		local["avaPinnedGeo"] = StreamGeometry.Parse("M0,0 L8,8 L0,8 Z");

		Assert.Same(first, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(host.Child).Data); // {StaticResource} semantics
	}

	[AvaloniaFact]
	public void StaticRes_DataTemplateResource_BuildsFreshContentEachTime() {
		// the reusable form of a control: a template, built anew per image. One
		// template, but a dictionary per window: an Avalonia ResourceDictionary
		// takes a single owner, so it can't be handed to two windows
		var template = new FuncDataTemplate<object?>((_, _) => new global::Avalonia.Controls.Shapes.Path());
		var parser = new MarkdownParser();

		var a = Attach(parser.ToInline("![a](staticres:avaTemplate)"), new ResourceDictionary { ["avaTemplate"] = template }).Child;
		var b = Attach(parser.ToInline("![b](staticres:avaTemplate)"), new ResourceDictionary { ["avaTemplate"] = template }).Child;

		Assert.IsType<global::Avalonia.Controls.Shapes.Path>(a);
		Assert.NotSame(a, b);
	}

	[AvaloniaFact]
	public void StaticRes_MissingResource_RendersAltText() {
		var host = Attach(new MarkdownParser().ToInline("![the alt](staticres:avaNoSuchKey)"));

		// a words.ini typo must not eat the sentence
		Assert.Equal("[🖼️!the alt]", Assert.IsType<TextBlock>(host.Child).Text);
	}

	[AvaloniaFact]
	public void ResourceVisualConverter_ControlResource_Throws() {
		// a bare control is one shared instance that can't live under two parents:
		// the wrong type, so it throws — as a wrong-typed resource would anywhere in Avalonia
		var ex = Assert.Throws<InvalidCastException>(() => ResourceVisualConverter.ToVisual(new Button(), new ImageOptions()));

		Assert.Contains("DataTemplate", ex.Message); // and says what the reusable form is
	}

	[AvaloniaFact]
	public void StaticRes_ControlResource_ThrowsNamingTheKey() {
		var local = new ResourceDictionary { ["avaElem"] = new Button { Content = "x" } };

		// a programming error, not a broken image: it throws where the resource meets the tree
		var ex = Assert.Throws<InvalidCastException>(() => Attach(new MarkdownParser().ToInline("![a](staticres:avaElem)"), local));

		Assert.Contains("avaElem", ex.Message);
	}
}
