using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using PathGeometry = System.Windows.Shapes.Path;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     Resolves one image URI scheme to visual content for
	///     <see cref="MarkdownParser"/>. Register implementations in
	///     <see cref="MarkdownParser.ImageSchemes"/> to teach the parser new schemes —
	///     for example <c>md:ContentSave</c> resolving to a Material Design icon.
	/// </summary>
	public interface IImageSchemeResolver {
		/// <summary>
		///     Produces the visual for <paramref name="source"/>, or <see langword="null"/>
		///     when there is nothing to show (the parser then falls back to the image's
		///     alt text). Apply <see cref="ImageOptions.Foreground"/> yourself where it
		///     makes sense (<see cref="BrushOption.ApplyTo"/> on the property to paint);
		///     sizing, the <see cref="ImageOptions.Background"/> border and the tooltip are
		///     applied uniformly by the parser afterwards. Exceptions are treated the same
		///     as <see langword="null"/>.
		/// </summary>
		/// <param name="source">The image URI, scheme and all — but query-less: the query is pre-parsed into <paramref name="options"/> (raw pairs in <see cref="ImageOptions.Query"/>).</param>
		/// <param name="options">The pre-parsed query options.</param>
		FrameworkElement? Resolve(Uri source, ImageOptions options);
	}

	/// <summary>
	///     The pre-parsed query options of an image URI, e.g.
	///     <c>staticres:icon?height=16&amp;foreground=DarkRed</c>. The well-known options
	///     get typed properties; everything else stays available in <see cref="Query"/>
	///     for custom <see cref="IImageSchemeResolver"/>s with bespoke needs.
	/// </summary>
	public record class ImageOptions {
		private static readonly IReadOnlyDictionary<string, string> EmptyQuery
			= new Dictionary<string, string>();

		/// <summary>Requested width from <c>?width=</c>, or <see langword="null"/> for natural sizing.</summary>
		public double? Width { get; init; }
		/// <summary>Requested height from <c>?height=</c>, or <see langword="null"/> for the natural size (geometry, having none, defaults to the base font size).</summary>
		public double? Height { get; init; }
		/// <summary>From <c>?background=</c>: a color, or a brush resource as <c>staticres:key</c>/<c>dynres:key</c>; the parser wraps the visual in a <see cref="Border"/> painted with it.</summary>
		public BrushOption? Background { get; init; }
		/// <summary>From <c>?foreground=</c>: a color, or a brush resource as <c>staticres:key</c>/<c>dynres:key</c>; resolvers apply it to fillable content such as geometry via <see cref="BrushOption.ApplyTo"/>.</summary>
		public BrushOption? Foreground { get; init; }
		/// <summary>Every query option by name (case-insensitive), including the well-known ones above.</summary>
		public IReadOnlyDictionary<string, string> Query { get; init; } = EmptyQuery;

		/// <summary>
		///     Rendering context rather than a query option: the font size the parser is
		///     rendering for. Geometry, having no natural size, defaults its height to it.
		///     The parser fills it in; built by hand it is the parser's default.
		/// </summary>
		public double BaseFontSize { get; init; } = MarkdownParser.DefaultBaseFontSize;
		/// <summary>Rendering context: the image's alt text, shown when the source resolves to nothing. The parser fills it in.</summary>
		public string? AltText { get; init; }

		/// <summary>
		///     Parses a URI query string (with or without the leading <c>?</c>) into options.
		///     Unparseable numbers and unknown color names are ignored rather than thrown.
		/// </summary>
		/// <param name="query">The query portion of the image URI; <see langword="null"/> or empty gives default options.</param>
		public static ImageOptions Parse(string? query) {
			var values = ParseQuery(query);
			return new ImageOptions {
				Width = TryParseDouble(values, "width"),
				Height = TryParseDouble(values, "height"),
				Background = BrushOption.Parse(values.GetValueOrDefault("background")),
				Foreground = BrushOption.Parse(values.GetValueOrDefault("foreground")),
				Query = values,
			};
		}

		/// <summary>
		///     Parses the options off a whole image URI and rewrites
		///     <paramref name="source"/> to the query-less remainder: the query carries
		///     display options, not asset identity, so resolvers always receive a clean
		///     URI (a <c>pack:</c> resource named <c>x.png?width=32</c> exists nowhere).
		///     The split is done by hand because <see cref="System.Uri"/> only
		///     recognizes a query in schemes it knows, and image schemes are often
		///     anything but: left alone, the <c>?</c> stays glued to the asset path.
		/// </summary>
		/// <param name="source">The image URI; rewritten without its query portion.</param>
		public static ImageOptions Parse(ref Uri source) {
			var raw = source.OriginalString;
			var q = raw.IndexOf('?');
			if (q < 0) return Parse((string?)null);
			var options = Parse(raw[(q + 1)..]);
			source = new Uri(raw[..q]);
			return options;
		}

		private static Dictionary<string, string> ParseQuery(string? query) {
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(query)) return result;
			var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
			foreach (var pair in pairs) {
				var parts = pair.Split(new[] { '=' }, 2);
				var name = Uri.UnescapeDataString(parts[0]);
				var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
				result[name] = value;
			}
			return result;
		}

		private static double? TryParseDouble(Dictionary<string, string> query, string key) {
			if (!query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return null;
			if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) return result;
			return null;
		}

	}

	/// <summary>
	///     A brush an image query asks for — <c>?foreground=</c> or <c>?background=</c>:
	///     a literal color (<c>DarkRed</c>, <c>#80FF0000</c>), or a resource by
	///     <c>x:Key</c> as <c>staticres:key</c> or <c>dynres:key</c>, found from where the
	///     image lands in the tree the way the image schemes of the same name are.
	///     <see cref="ApplyTo"/> is how it reaches an element: it sets the literal, or
	///     wires the framework's own resource reference — static keeps the first brush
	///     found, dynamic follows every swap, a theme change included.
	/// </summary>
	/// <remarks>
	///     A <see cref="Color"/> resource is accepted and wrapped in a brush; any other
	///     resource type throws, as a wrong-typed resource would anywhere else in WPF. A
	///     key that resolves to nothing leaves the element's own value standing (a black
	///     fill, no border) and gripes <c>IMG:RES</c> once the element is loaded, so a
	///     typo never makes an icon transparent.
	/// </remarks>
	public sealed class BrushOption {
		// one attached slot per element: the resource value as the framework resolves
		// it, and where it is to go
		private static readonly DependencyProperty ResourceValueProperty = DependencyProperty.RegisterAttached(
			"ResourceValue", typeof(object), typeof(BrushOption), new PropertyMetadata(null, OnResourceValueChanged));
		private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
			"Target", typeof(ResourceTarget), typeof(BrushOption));

		private sealed record ResourceTarget(DependencyProperty Property, string Key, bool IsDynamic);

		private BrushOption(Brush? brush, string? resourceKey, bool isDynamic) {
			Brush = brush;
			ResourceKey = resourceKey;
			IsDynamic = isDynamic;
		}

		/// <summary>The literal brush, frozen; <see langword="null"/> when a resource was asked for.</summary>
		public Brush? Brush { get; }
		/// <summary>The resource key to look up; <see langword="null"/> for a literal.</summary>
		public string? ResourceKey { get; }
		/// <summary><see langword="true"/> for <c>dynres:</c>, a reference that follows later changes; <see langword="false"/> for a literal or <c>staticres:</c>.</summary>
		public bool IsDynamic { get; }

		/// <summary>
		///     Parses one query value: <c>staticres:key</c>/<c>dynres:key</c> name a
		///     resource, anything else is tried as a color. An unknown color name, or a
		///     prefix with no key after it, parses to <see langword="null"/> — ignored, not
		///     thrown, as if never asked for.
		/// </summary>
		/// <param name="value">The raw <c>?foreground=</c> or <c>?background=</c> value.</param>
		public static BrushOption? Parse(string? value) {
			if (string.IsNullOrWhiteSpace(value)) return null;
			if (TryStripScheme(value, "dynres:", out var key)) return new BrushOption(null, key, isDynamic: true);
			if (TryStripScheme(value, "staticres:", out key)) return new BrushOption(null, key, isDynamic: false);
			try {
				if (new ColorConverter().ConvertFromString(null, CultureInfo.InvariantCulture, value) is Color color) {
					var brush = new SolidColorBrush(color);
					// frozen brushes are thread-free and cheaper to render
					brush.Freeze();
					return new BrushOption(brush, null, isDynamic: false);
				}
			}
			catch {
				// unknown color: pretend it was never asked for
			}
			return null;
		}

		private static bool TryStripScheme(string value, string scheme, out string key) {
			key = value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? value[scheme.Length..].Trim() : "";
			return key.Length > 0;
		}

		/// <summary>
		///     Puts this brush on <paramref name="element"/>'s <paramref name="property"/>:
		///     a literal is set outright; a resource becomes the framework's own reference,
		///     resolved from the element's place in the tree once it has one.
		/// </summary>
		/// <param name="element">The element to paint.</param>
		/// <param name="property">Its brush property, e.g. <see cref="Shape.FillProperty"/> or <see cref="Border.BackgroundProperty"/>.</param>
		/// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="property"/> is <see langword="null"/>.</exception>
		public void ApplyTo(FrameworkElement element, DependencyProperty property) {
			ArgumentNullException.ThrowIfNull(element);
			ArgumentNullException.ThrowIfNull(property);
			if (Brush is not null) {
				element.SetValue(property, Brush);
				return;
			}
			element.SetValue(TargetProperty, new ResourceTarget(property, ResourceKey!, IsDynamic));
			// evaluated from the element's place in the tree and re-evaluated when the
			// tree or the dictionaries change — what {DynamicResource} does
			element.SetResourceReference(ResourceValueProperty, ResourceKey!);
			element.Loaded += OnLoaded;
		}

		private static void OnLoaded(object sender, RoutedEventArgs e) {
			var element = (FrameworkElement)sender;
			element.Loaded -= OnLoaded;
			// in the tree, and still nothing: the key is wrong, so say so — once
			if (element.GetValue(ResourceValueProperty) is null && element.GetValue(TargetProperty) is ResourceTarget target) {
				ITakeException.Global.Warn($"IMG:RES:{(target.IsDynamic ? "dynres" : "staticres")}:{target.Key}");
			}
		}

		private static void OnResourceValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			// nothing found (yet, or any more): leave whatever the element has standing
			if (e.NewValue is null || d.GetValue(TargetProperty) is not ResourceTarget target) return;
			d.SetValue(target.Property, ToBrush(e.NewValue, target.Key));
			if (!target.IsDynamic) {
				// static: pin the first value found — a local value replaces the
				// resource reference, so later changes to the dictionary go unheard
				d.SetValue(ResourceValueProperty, e.NewValue);
			}
		}

		private static Brush ToBrush(object value, string key) {
			switch (value) {
				case Brush brush:
					return brush;
				case Color color: {
					var brush = new SolidColorBrush(color);
					brush.Freeze();
					return brush;
				}
				default:
					throw new InvalidCastException($"Resource '{key}': a {value.GetType().Name} is not a brush; expected a Brush or a Color");
			}
		}
	}

	/// <summary>
	///     Resolves <c>staticres:key</c> the way <c>{StaticResource key}</c> does: the
	///     resource is looked up from where the image lands in the tree — the window or
	///     user control it is in, then the application — and the first value found is
	///     kept. It renders through <see cref="ResourceVisualConverter"/> as a fresh
	///     visual each time (an <see cref="ImageSource"/> in an <see cref="Image"/>, a
	///     <see cref="Geometry"/> in a filled <see cref="PathGeometry"/>, a
	///     <see cref="DataTemplate"/> as newly loaded content), so one key renders safely
	///     in as many places as you like. Any other resource type throws, as it would
	///     anywhere else in WPF; a missing key renders the alt text.
	/// </summary>
	public class StaticResImageResolver : IImageSchemeResolver {
		/// <inheritdoc/>
		public FrameworkElement? Resolve(Uri source, ImageOptions options)
			=> new ResourceImage(ResourceKey(source), options, isDynamic: false);

		/// <summary>The <c>x:Key</c> named by a <c>scheme:key</c> URI.</summary>
		internal static string ResourceKey(Uri source)
			=> (source.AbsolutePath ?? source.OriginalString).TrimStart('/');
	}

	/// <summary>
	///     Resolves <c>dynres:key</c> the way <c>{DynamicResource key}</c> does: the same
	///     lookup and the same visuals as <see cref="StaticResImageResolver"/>, but the
	///     reference stays live — swap the resource (a theme change, say) and the image
	///     re-renders.
	/// </summary>
	public class DynResImageResolver : IImageSchemeResolver {
		/// <inheritdoc/>
		public FrameworkElement? Resolve(Uri source, ImageOptions options)
			=> new ResourceImage(StaticResImageResolver.ResourceKey(source), options, isDynamic: true);
	}

	/// <summary>
	///     Resolves <c>pack:</c> URIs (<c>pack://application:,,,/Assembly;component/Path</c>)
	///     by letting <see cref="BitmapImage"/> do what it does best.
	/// </summary>
	public class PackImageResolver : IImageSchemeResolver {
		/// <inheritdoc/>
		public FrameworkElement? Resolve(Uri source, ImageOptions options) {
			var bitmap = new BitmapImage();
			bitmap.BeginInit();
			bitmap.UriSource = source;
			bitmap.CacheOption = BitmapCacheOption.OnLoad;
			bitmap.EndInit();
			return new Image { Source = bitmap, Stretch = Stretch.Uniform };
		}
	}

	/// <summary>
	///     Resolves <c>resx:key</c> by hunting the loaded assemblies for a
	///     <c>Resources</c> class and asking its <see cref="ResourceManager"/>. Both
	///     <see cref="ImageSource"/> and <see cref="Drawing.Bitmap"/> resources are honored.
	/// </summary>
	public class ResxImageResolver : IImageSchemeResolver {
		/// <inheritdoc/>
		public FrameworkElement? Resolve(Uri source, ImageOptions options) {
			var key = (source.AbsolutePath ?? source.OriginalString).TrimStart('/');
			switch (GetResxObject(key)) {
				case ImageSource imageSource:
					return new Image { Source = imageSource, Stretch = Stretch.Uniform };
				case Drawing.Bitmap drawingBitmap: {
					var stream = new MemoryStream();
					drawingBitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
					stream.Position = 0;
					var bitmap = new BitmapImage();
					bitmap.BeginInit();
					bitmap.StreamSource = stream;
					bitmap.CacheOption = BitmapCacheOption.OnLoad;
					bitmap.EndInit();
					return new Image { Source = bitmap, Stretch = Stretch.Uniform };
				}
				default:
					return null;
			}
		}

		private static object? GetResxObject(string key) {
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				Type? type = assembly.GetTypes().FirstOrDefault(t =>
					t.FullName != null &&
					(t.FullName.EndsWith(".Properties.Resources", StringComparison.OrdinalIgnoreCase) ||
					 t.FullName.EndsWith(".Resources", StringComparison.OrdinalIgnoreCase)));
				if (type is null) continue;

				var property = type.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (property?.GetValue(null) is ResourceManager manager) {
					try {
						if (manager.GetObject(key) is { } resource) return resource;
					}
					catch {
						// this Resources class doesn't have it; keep hunting
					}
				}
			}
			return null;
		}
	}

	/// <summary>
	///     Resolves <c>scheme:path</c> to an image file under a fixed <paramref name="root"/>
	///     folder. Same discipline as <see cref="AssetsImageResolver"/> — the path is
	///     canonicalized and clamped, so <c>../</c> trickery, rooted paths and UNC shares
	///     resolve to nothing, and environment variables are never expanded — but the root
	///     is the caller's to choose. A tool can point a scheme at any directory this way
	///     (an editor mapping a preview scheme to a folder beside the file it is editing,
	///     say). A missing (or clamped) file resolves to nothing, rendered as the alt text.
	/// </summary>
	/// <param name="root">The one folder paths are allowed to resolve under.</param>
	public class FolderImageResolver(string root) : IImageSchemeResolver {
		/// <inheritdoc/>
		public FrameworkElement? Resolve(Uri source, ImageOptions options) {
			var filePath = ResolvePath(source, root);
			if (filePath is null || !File.Exists(filePath)) return null;

			var bitmap = new BitmapImage();
			bitmap.BeginInit();
			bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
			bitmap.CacheOption = BitmapCacheOption.OnLoad;
			bitmap.EndInit();
			return new Image { Source = bitmap, Stretch = Stretch.Uniform };
		}

		/// <summary>
		///     Canonicalizes the URI's path under <paramref name="root"/> and returns the
		///     absolute file path, or <see langword="null"/> if it would land anywhere else.
		/// </summary>
		internal static string? ResolvePath(Uri source, string root) {
			var relative = Uri.UnescapeDataString((source.AbsolutePath ?? source.OriginalString).TrimStart('/'));
			var fullRoot = System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar;
			// GetFullPath resolves any ../ and ./ segments; a rooted or UNC path survives
			// Path.Combine untouched. Either way, anything outside the root is refused.
			var filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				fullRoot,
				relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
			return filePath.StartsWith(fullRoot, StringComparison.Ordinal) ? filePath : null;
		}
	}

	/// <summary>
	///     Resolves <c>assets:path</c> from files under the <c>Assets</c> folder next to
	///     the application — and only that folder. A <see cref="FolderImageResolver"/>
	///     rooted at that fixed <c>Assets</c> directory, with all the same clamping.
	/// </summary>
	public class AssetsImageResolver : IImageSchemeResolver {
		private static string AssetsRoot => System.IO.Path.Combine(
			AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory, "Assets");

		/// <inheritdoc/>
		public FrameworkElement? Resolve(Uri source, ImageOptions options)
			=> new FolderImageResolver(AssetsRoot).Resolve(source, options);

		/// <summary>
		///     Canonicalizes the URI's path under the <c>Assets</c> root and returns it,
		///     or <see langword="null"/> if it would land anywhere else.
		/// </summary>
		internal static string? ResolveAssetPath(Uri source)
			=> FolderImageResolver.ResolvePath(source, AssetsRoot);
	}

	/// <summary>
	///     Applies an image's requested size — or its natural default — to the visual
	///     that renders it. Shared by the parser, for visuals a resolver hands back on
	///     the spot, and by <see cref="ResourceImage"/>, for a resource that resolves
	///     later, once the host is in the tree.
	/// </summary>
	internal static class ImageSizing {
		// the largest a requested dimension may be, so a runaway ?width= can't ask
		// the layout to allocate an enormous image
		private const double MaxDimension = 4096;

		public static void Apply(FrameworkElement element, ImageOptions options) {
			var width = Clean(options.Width);
			var height = Clean(options.Height);
			if (width is double w) element.Width = w;
			if (height is double h) element.Height = h;
			if (width is null && height is null) {
				// geometry has no natural size, so default it to the font height
				if (element is PathGeometry) element.Height = options.BaseFontSize;
				// raster images get pinned to their natural size: measured with the
				// whole line's constraint, an unpinned Stretch.Uniform image balloons
				else if (element is Image { Source: { } source }) {
					element.Width = source.Width;
					element.Height = source.Height;
				}
			}
		}

		// a requested dimension must be finite and non-negative, and is capped;
		// anything else is treated as unspecified (natural sizing), never thrown
		private static double? Clean(double? value)
			=> value is double v && double.IsFinite(v) && v >= 0 ? (v > MaxDimension ? MaxDimension : v) : null;
	}
}
