using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Covers the WPF image-scheme registry: custom scheme registration, options
/// parsing, uniform size/background/tooltip handling, and alt-text fallback.
/// <see cref="AvaImageSchemeTests"/> is the Avalonia twin; both swap the
/// Words.Logger global, hence the shared collection.
/// </summary>
[Collection("Words globals")]
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
			Assert.Equal("[🖼️!the alt]", run.Text);
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
			Assert.Equal("[🖼️!the alt]", run.Text);
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
			Assert.Equal("[🖼️!the alt]", run.Text);
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
	public void NoQueryScheme_QueryIsSplitOffByHand() {
		// Avalonia registers `avares` with a UriParser that has no query support,
		// leaving the `?` glued to the asset path. Emulate that registration: the
		// options must still apply, and the resolver must get a query-less URI.
		UriParser.Register(new GenericUriParser(
			GenericUriParserOptions.GenericAuthority
			| GenericUriParserOptions.NoQuery
			| GenericUriParserOptions.NoFragment), "fakeres", -1);
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			var resolver = new FakeResolver((_, _) => new TextBlock());
			parser.ImageSchemes["fakeres"] = resolver;

			var inline = parser.ToInline("![alt](fakeres://host/thing.png?width=32&height=16)");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var textBlock = Assert.IsType<TextBlock>(container.Child);
			Assert.Equal(32, textBlock.Width);
			Assert.Equal(16, textBlock.Height);
			Assert.Equal("fakeres://host/thing.png", resolver.LastSource?.OriginalString);
			return null;
		});
	}

	[Fact]
	public void RegularScheme_QueryAlsoTrimmedFromResolverUri() {
		// the query carries display options, not asset identity, so the resolver
		// gets a query-less URI even when System.Uri parsed the query itself
		// (a pack: resource named `x.png?width=32` exists nowhere)
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			var resolver = new FakeResolver((_, _) => new TextBlock());
			parser.ImageSchemes["fake"] = resolver;

			parser.ToInline("![alt](fake:thing?width=32)");

			Assert.Equal("fake:thing", resolver.LastSource?.OriginalString);
			Assert.Equal(32, resolver.LastOptions?.Width);
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

	private static Image TinyImage(int pixelWidth, int pixelHeight) => new() {
		Source = System.Windows.Media.Imaging.BitmapSource.Create(
			pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null,
			new byte[4 * pixelWidth * pixelHeight], 4 * pixelWidth),
		Stretch = Stretch.Uniform,
	};

	[Fact]
	public void RasterImage_WithSource_PinnedToNaturalSize() {
		// measured with the whole line's constraint, an unpinned Stretch.Uniform
		// image balloons to fill it; no options means natural size, so pin it
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => TinyImage(10, 8));

			var inline = parser.ToInline("![alt](fake:thing)");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var image = Assert.IsType<Image>(container.Child);
			Assert.Equal(10, image.Width);
			Assert.Equal(8, image.Height);
			return null;
		});
	}

	[Fact]
	public void RasterImage_OneSizeOption_LeavesTheOtherToAspectRatio() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => TinyImage(10, 8));

			var inline = parser.ToInline("![alt](fake:thing?height=16)");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var image = Assert.IsType<Image>(container.Child);
			Assert.True(double.IsNaN(image.Width));
			Assert.Equal(16, image.Height);
			return null;
		});
	}

	private sealed class CaptureLogger : PatTech.Localization.ITakeException {
		public readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
		public void Warn(string text) => Messages.Enqueue(text);
		public void Error(Exception exception, string message) => Messages.Enqueue(message);
	}

	[Fact]
	public void DefaultParser_GripesThroughWordsLogger_EvenWhenAssignedLate() {
		RunSta<object?>(() => {
			var capture = new CaptureLogger();
			var original = PatTech.Localization.Words.Logger;
			try {
				// assigned long after MarkdownParser.Default was constructed
				PatTech.Localization.Words.Logger = capture;
				MarkdownParser.Default.ToInline("![alt](nosuch:thing)");
			}
			finally {
				PatTech.Localization.Words.Logger = original;
			}

			Assert.Contains(capture.Messages, m => m.Contains("IMG:RES") && m.Contains("nosuch:thing"));
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
	public void AssetsResolver_ExistingFile_LoadsBitmap() {
		RunSta<object?>(() => {
			var assetsDir = Directory.CreateDirectory(
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"));
			var file = Path.Combine(assetsDir.FullName, "tiny image.png");
			if (!File.Exists(file)) {
				var pixel = System.Windows.Media.Imaging.BitmapSource.Create(
					1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
				var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
				encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pixel));
				using var stream = File.Create(file);
				encoder.Save(stream);
			}

			var visual = new AssetsImageResolver().Resolve(new Uri("assets:tiny%20image.png"), new ImageOptions());

			var image = Assert.IsType<Image>(visual);
			Assert.NotNull(image.Source);
			return null;
		});
	}

	[Fact]
	public void ImageOptions_Parse_ReadsKnownAndCustomOptions() {
		var options = RunSta(() => ImageOptions.Parse("?width=24&height=12.5&foreground=DarkRed&kind=ContentSave"));

		Assert.Equal(24, options.Width);
		Assert.Equal(12.5, options.Height);
		Assert.Equal(Colors.DarkRed, Assert.IsType<SolidColorBrush>(options.Foreground?.Brush).Color);
		Assert.Null(options.Background);
		Assert.Equal("ContentSave", options.Query["kind"]);
	}

	[Fact]
	public void ImageOptions_Parse_IgnoresNonsenseValues() {
		var options = RunSta(() => ImageOptions.Parse("width=very&background=NotAColor"));

		Assert.Null(options.Width);
		Assert.Null(options.Background);
	}

	[Fact]
	public void BrushOption_Parse_ResourceKey_IsAReferenceNotABrush() {
		// the spelling itself is Core's (ImageQueryTests); here, that it comes through as a reference
		var option = BrushOption.Parse("dynres:wpfAccent");

		Assert.NotNull(option);
		Assert.Null(option.Brush);
		Assert.Equal("wpfAccent", option.ResourceKey);
		Assert.True(option.IsDynamic);
	}

	[Theory]
	[InlineData("NotAColor")]
	[InlineData("dynres:")]
	[InlineData("staticres:   ")]
	[InlineData("")]
	public void BrushOption_Parse_Nonsense_IsNull(string value) {
		// ignored, as if never asked for: a typo must not become a lookup
		Assert.Null(RunSta(() => BrushOption.Parse(value)));
	}

	[Fact]
	public void Dimension_Huge_IsCapped() {
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());

			var inline = parser.ToInline("![alt](fake:thing?width=100000&height=100000)");

			var container = Assert.IsType<InlineUIContainer>(inline);
			var textBlock = Assert.IsType<TextBlock>(container.Child);
			Assert.Equal(4096, textBlock.Width);
			Assert.Equal(4096, textBlock.Height);
			return null;
		});
	}

	[Theory]
	[InlineData("fake:thing?width=-5&height=NaN")]
	[InlineData("fake:thing?width=Infinity")]
	public void Dimension_NonFiniteOrNegative_IgnoredNotThrownAsMissingImage(string uri) {
		// a bad dimension must not throw (which the parser would surface as a
		// missing image); the visual renders at its natural size instead
		RunSta<object?>(() => {
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());

			var inline = parser.ToInline($"![alt]({uri})");

			var container = Assert.IsType<InlineUIContainer>(inline); // not a Run of alt text
			var textBlock = Assert.IsType<TextBlock>(container.Child);
			Assert.True(double.IsNaN(textBlock.Width));
			return null;
		});
	}

	/// <summary>
	/// Puts <paramref name="inline"/> in a TextBlock under a Grid carrying
	/// <paramref name="resources"/>, so a tree-scoped lookup has somewhere to look,
	/// and hands back the resource host the image resolved to.
	/// </summary>
	private static ResourceImage Attach(Inline inline, ResourceDictionary? resources = null)
		=> Assert.IsType<ResourceImage>(AttachChild(inline, resources));

	/// <summary>Same, for an image that resolved on the spot: hands back whatever the container holds.</summary>
	private static FrameworkElement AttachChild(Inline inline, ResourceDictionary? resources = null) {
		var grid = new Grid();
		if (resources is not null) grid.Resources = resources;
		var textBlock = new TextBlock();
		grid.Children.Add(textBlock);
		textBlock.Inlines.Add(inline);
		var container = Assert.IsType<InlineUIContainer>(inline);
		return (FrameworkElement)container.Child;
	}

	private static SolidColorBrush Frozen(Color color) {
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	[Fact]
	public void Foreground_DynResBrush_FollowsAResourceSwap() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var local = new ResourceDictionary {
				["wpfTintGeo"] = Geometry.Parse("M0,0 L4,4 L0,4 Z"),
				["wpfTint"] = Frozen(Colors.Red),
			};
			var host = Attach(new MarkdownParser().ToInline("![a](staticres:wpfTintGeo?foreground=dynres:wpfTint)"), local);
			var path = Assert.IsType<System.Windows.Shapes.Path>(host.Child);
			Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(path.Fill).Color);

			local["wpfTint"] = Frozen(Colors.Blue); // a theme swap, in miniature

			Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(path.Fill).Color);
			return null;
		});
	}

	[Fact]
	public void Foreground_StaticResBrush_KeepsTheFirstValue() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var local = new ResourceDictionary {
				["wpfPinGeo"] = Geometry.Parse("M0,0 L4,4 L0,4 Z"),
				["wpfPinTint"] = Frozen(Colors.Red),
			};
			var host = Attach(new MarkdownParser().ToInline("![a](staticres:wpfPinGeo?foreground=staticres:wpfPinTint)"), local);
			var path = Assert.IsType<System.Windows.Shapes.Path>(host.Child);

			local["wpfPinTint"] = Frozen(Colors.Blue);

			Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(path.Fill).Color); // {StaticResource} semantics
			return null;
		});
	}

	[Fact]
	public void Foreground_ColorResource_BecomesABrush() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var local = new ResourceDictionary {
				["wpfColorGeo"] = Geometry.Parse("M0,0 L4,4 L0,4 Z"),
				["wpfTintColor"] = Colors.Green, // a Color, the way theme dictionaries often keep them
			};
			var host = Attach(new MarkdownParser().ToInline("![a](staticres:wpfColorGeo?foreground=dynres:wpfTintColor)"), local);

			var path = Assert.IsType<System.Windows.Shapes.Path>(host.Child);
			Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(path.Fill).Color);
			return null;
		});
	}

	[Fact]
	public void Foreground_MissingResource_LeavesTheDefaultBlack() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var local = new ResourceDictionary { ["wpfBlackGeo"] = Geometry.Parse("M0,0 L4,4 L0,4 Z") };
			var host = Attach(new MarkdownParser().ToInline("![a](staticres:wpfBlackGeo?foreground=dynres:wpfNoSuchBrush)"), local);

			// a typo'd key keeps the icon visible: not transparent
			var path = Assert.IsType<System.Windows.Shapes.Path>(host.Child);
			Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(path.Fill).Color);
			return null;
		});
	}

	[Fact]
	public void Foreground_WrongTypeResource_ThrowsNamingTheKey() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var local = new ResourceDictionary {
				["wpfWrongGeo"] = Geometry.Parse("M0,0 L4,4 L0,4 Z"),
				["wpfNotABrush"] = "text",
			};

			// a programming error, not a broken image: it throws where the resource meets the tree
			var ex = Assert.Throws<InvalidCastException>(() => Attach(new MarkdownParser().ToInline("![a](staticres:wpfWrongGeo?foreground=staticres:wpfNotABrush)"), local));

			Assert.Contains("wpfNotABrush", ex.Message);
			return null;
		});
	}

	[Fact]
	public void Background_DynResBrush_WrapsInABorderThatFollows() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var parser = new MarkdownParser();
			parser.ImageSchemes["fake"] = new FakeResolver((_, _) => new TextBlock());
			var local = new ResourceDictionary { ["wpfPaper"] = Frozen(Colors.Red) };
			var border = Assert.IsType<Border>(AttachChild(parser.ToInline("![a](fake:thing?background=dynres:wpfPaper)"), local));
			Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(border.Background).Color);

			local["wpfPaper"] = Frozen(Colors.Blue);

			Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(border.Background).Color);
			return null;
		});
	}

	[Fact]
	public void StaticRes_GeometryResource_GivesAFreshHostEachResolve() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			Application.Current!.Resources["wpfGeo"] = Geometry.Parse("M0,0 L10,10 L0,10 Z");
			var parser = new MarkdownParser();

			var a = Assert.IsType<System.Windows.Shapes.Path>(Attach(parser.ToInline("![a](staticres:wpfGeo)")).Child);
			var b = Assert.IsType<System.Windows.Shapes.Path>(Attach(parser.ToInline("![b](staticres:wpfGeo)")).Child);

			Assert.NotSame(a, b);        // reuse-safe: not one shared element
			Assert.Same(a.Data, b.Data); // the geometry value itself shares freely
			return null;
		});
	}

	[Fact]
	public void StaticRes_TreeScopedResource_FoundFromTheElementNotJustTheApp() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			// on the Grid only: an app-level lookup would never see it
			var local = new ResourceDictionary { ["wpfLocalGeo"] = Geometry.Parse("M0,0 L4,4 L0,4 Z") };

			var host = Attach(new MarkdownParser().ToInline("![a](staticres:wpfLocalGeo)"), local);

			Assert.IsType<System.Windows.Shapes.Path>(host.Child);
			return null;
		});
	}

	[Fact]
	public void DynRes_FollowsAResourceSwap() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var first = Geometry.Parse("M0,0 L4,4 L0,4 Z");
			var second = Geometry.Parse("M0,0 L8,8 L0,8 Z");
			var local = new ResourceDictionary { ["wpfLiveGeo"] = first };
			var host = Attach(new MarkdownParser().ToInline("![a](dynres:wpfLiveGeo)"), local);
			Assert.Same(first, Assert.IsType<System.Windows.Shapes.Path>(host.Child).Data);

			local["wpfLiveGeo"] = second; // a theme swap, in miniature

			Assert.Same(second, Assert.IsType<System.Windows.Shapes.Path>(host.Child).Data);
			return null;
		});
	}

	[Fact]
	public void StaticRes_KeepsTheFirstValue_IgnoringLaterSwaps() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			var first = Geometry.Parse("M0,0 L4,4 L0,4 Z");
			var local = new ResourceDictionary { ["wpfPinnedGeo"] = first };
			var host = Attach(new MarkdownParser().ToInline("![a](staticres:wpfPinnedGeo)"), local);

			local["wpfPinnedGeo"] = Geometry.Parse("M0,0 L8,8 L0,8 Z");

			Assert.Same(first, Assert.IsType<System.Windows.Shapes.Path>(host.Child).Data); // {StaticResource} semantics
			return null;
		});
	}

	[Fact]
	public void StaticRes_DataTemplateResource_LoadsFreshContentEachTime() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			// the reusable form of an element: a template, loaded anew per image
			var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path)) };
			template.Seal();
			var local = new ResourceDictionary { ["wpfTemplate"] = template };
			var parser = new MarkdownParser();

			var a = Attach(parser.ToInline("![a](staticres:wpfTemplate)"), local).Child;
			var b = Attach(parser.ToInline("![b](staticres:wpfTemplate)"), local).Child;

			Assert.IsType<System.Windows.Shapes.Path>(a);
			Assert.NotSame(a, b);
			return null;
		});
	}

	[Fact]
	public void StaticRes_MissingResource_RendersAltText() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();

			var host = Attach(new MarkdownParser().ToInline("![the alt](staticres:wpfNoSuchKey)"));

			// a words.ini typo must not eat the sentence
			Assert.Equal("[🖼️!the alt]", Assert.IsType<TextBlock>(host.Child).Text);
			return null;
		});
	}

	[Fact]
	public void ResourceVisualConverter_ElementResource_Throws() {
		RunSta<object?>(() => {
			// a bare element is one shared instance that can't live under two parents:
			// the wrong type, so it throws — as a wrong-typed resource would anywhere in WPF
			var ex = Assert.Throws<InvalidCastException>(() => ResourceVisualConverter.ToVisual(new Button(), new ImageOptions()));

			Assert.Contains("DataTemplate", ex.Message); // and says what the reusable form is
			return null;
		});
	}

	[Fact]
	public void StaticRes_ElementResource_ThrowsNamingTheKey() {
		RunSta<object?>(() => {
			_ = Application.Current ?? new Application();
			Application.Current!.Resources["wpfElem"] = new Button { Content = "x" };

			// a programming error, not a broken image: the parser lets it through
			var ex = Assert.Throws<InvalidCastException>(() => new MarkdownParser().ToInline("![a](staticres:wpfElem)"));

			Assert.Contains("wpfElem", ex.Message);
			return null;
		});
	}
}
