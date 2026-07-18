using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Globalization;
using PathGeometry = Avalonia.Controls.Shapes.Path;

namespace PatTech.Localization.Avalonia;

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
	/// <param name="source">The image URI, scheme and all.</param>
	/// <param name="options">The pre-parsed query options.</param>
	Control? Resolve(Uri source, ImageOptions options);
}

/// <summary>
///     The pre-parsed query options of an image URI, e.g.
///     <c>staticres:icon?height=16&amp;foreground=DarkRed</c>. The well-known options
///     get typed properties; everything else stays available in <see cref="Query"/>
///     for custom <see cref="IImageSchemeResolver"/>s with bespoke needs.
/// </summary>
public class ImageOptions {
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
			return new SolidColorBrush(Color.Parse(value));
		}
		catch {
			// unknown color: pretend it was never asked for
			return null;
		}
	}
}

/// <summary>
///     Resolves <c>avares:</c> URIs (Avalonia embedded assets) by handing them
///     straight to <see cref="Bitmap"/>.
/// </summary>
public class AvaresImageResolver : IImageSchemeResolver {
	/// <inheritdoc/>
	public Control? Resolve(Uri source, ImageOptions options)
		=> new Image {
			Source = new Bitmap(source.AbsoluteUri),
			Stretch = Stretch.Uniform,
		};
}

/// <summary>
///     Resolves <c>assets:path</c> from files under the <c>Assets</c> folder next to
///     the application — and only that folder: the path is canonicalized and clamped,
///     so <c>../</c> trickery, rooted paths and UNC shares resolve to nothing.
///     Environment variables are never expanded; a <c>%</c> is just a filename
///     character. A missing (or clamped) file resolves to nothing, which the parser
///     renders as the alt text.
/// </summary>
public class AssetsImageResolver : IImageSchemeResolver {
	/// <inheritdoc/>
	public Control? Resolve(Uri source, ImageOptions options) {
		var filePath = ResolveAssetPath(source);
		if (filePath is null || !File.Exists(filePath)) return null;

		return new Image {
			Source = new Bitmap(filePath),
			Stretch = Stretch.Uniform,
		};
	}

	/// <summary>
	///     Canonicalizes the URI's path under the <c>Assets</c> root and returns it,
	///     or <see langword="null"/> if it would land anywhere else.
	/// </summary>
	internal static string? ResolveAssetPath(Uri source) {
		var relative = Uri.UnescapeDataString((source.AbsolutePath ?? source.OriginalString).TrimStart('/'));
		var root = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
		var assetsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "Assets"))
			+ System.IO.Path.DirectorySeparatorChar;
		// GetFullPath resolves any ../ and ./ segments; a rooted or UNC path survives
		// Path.Combine untouched. Either way, anything outside the root is refused.
		var filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			assetsRoot,
			relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
		return filePath.StartsWith(assetsRoot, StringComparison.Ordinal) ? filePath : null;
	}
}

/// <summary>
///     Resolves <c>staticres:key</c> from the application's resources (<c>x:Key</c>
///     lookup): an <see cref="IImage"/> becomes an <see cref="Image"/>, a
///     <see cref="Geometry"/> becomes a filled <see cref="PathGeometry"/>, and any
///     <see cref="Control"/> is used as-is (with <see cref="ImageOptions.Foreground"/>
///     applied when it is a <see cref="Shape"/>).
/// </summary>
public class StaticResImageResolver : IImageSchemeResolver {
	/// <inheritdoc/>
	public Control? Resolve(Uri source, ImageOptions options) {
		var key = (source.AbsolutePath ?? source.OriginalString).TrimStart('/');
		return FindResource(key, source.OriginalString) switch {
			IImage image => new Image { Source = image, Stretch = Stretch.Uniform },
			Geometry geometry => new PathGeometry {
				Data = geometry,
				Fill = options.Foreground ?? (IBrush)Brushes.Black,
				Stretch = Stretch.Uniform,
			},
			Shape shape when options.Foreground is not null => WithFill(shape, options.Foreground),
			Control control => control,
			_ => null,
		};
	}

	private static Shape WithFill(Shape shape, Brush fill) {
		shape.Fill = fill;
		return shape;
	}

	private static object? FindResource(string key, string fallbackKey) {
		var resources = Application.Current?.Resources;
		if (resources is null) return null;
		if (resources.TryGetValue(key, out var value)) return value;
		if (resources.TryGetValue(fallbackKey, out var fallbackValue)) return fallbackValue;
		return null;
	}
}
