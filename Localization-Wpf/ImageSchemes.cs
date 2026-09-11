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
		///     makes sense; sizing, the <see cref="ImageOptions.Background"/> border and
		///     the tooltip are applied uniformly by the parser afterwards. Exceptions are
		///     treated the same as <see langword="null"/>.
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
		/// <summary>Brush from <c>?background=</c>; the parser wraps the visual in a <see cref="Border"/> painted with it.</summary>
		public Brush? Background { get; init; }
		/// <summary>Brush from <c>?foreground=</c>; resolvers apply it to fillable content such as geometry.</summary>
		public Brush? Foreground { get; init; }
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
				Background = TryParseColorBrush(values, "background"),
				Foreground = TryParseColorBrush(values, "foreground"),
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

		private static Brush? TryParseColorBrush(Dictionary<string, string> query, string key) {
			if (!query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return null;
			try {
				var converter = new ColorConverter();
				if (converter.ConvertFromString(null, CultureInfo.InvariantCulture, value) is Color color) {
					var brush = new SolidColorBrush(color);
					// frozen brushes are thread-free and cheaper to render
					brush.Freeze();
					return brush;
				}
			}
			catch {
				// unknown color: pretend it was never asked for
			}
			return null;
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
