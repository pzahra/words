using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Path = System.IO.Path;
using PathGeometry = Avalonia.Controls.Shapes.Path;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     Renders Words markdown as Avalonia <see cref="Inline"/> content: basic formatting
///     (bold, italic, sub/superscript), hyperlinks, and images.
/// </summary>
/// <remarks>
///     Image URIs honor the schemes <c>avares:</c> (embedded assets), <c>assets:</c>
///     (files under the application's <c>Assets</c> folder), and <c>staticres:</c>
///     (application resource by <c>x:Key</c> — <see cref="IImage"/>, <see cref="Geometry"/>,
///     or any <see cref="Control"/>). The query string may carry <c>width</c>, <c>height</c>,
///     <c>background</c>, and <c>foreground</c> options. Anything that fails to resolve
///     degrades to the image's alt text.
/// </remarks>
/// <param name="baseFontSize">Font size the output is destined for; sets the sub/superscript size (80% of it) and the default height of geometry icons.</param>
/// <param name="logger">An interface for passing on logging instructions to the caller.</param>
public class MarkdownParser(float baseFontSize = 13, ITakeException? logger = null) : MarkdownParser<Inline>(logger), IMarkdownParser {
	/// <summary>Creates a plain <see cref="global::Avalonia.Controls.Documents.Run"/> for unformatted text.</summary>
	protected override Inline Run(string text) => new Run { Text = text };
	/// <summary>Groups multiple inlines into a single <see cref="global::Avalonia.Controls.Documents.Span"/>.</summary>
	protected override Inline Span(IEnumerable<Inline> inlines) => new Span { Inlines = [..inlines] };
	/// <summary>
	///     Wraps <paramref name="content"/> in a <see cref="PatTech.Localization.Avalonia.Hyperlink"/>
	///     pointing at <paramref name="target"/>, underlined and blue in the traditional manner.
	/// </summary>
	protected override Inline Hyperlink(Inline content, Uri target, string? tooltip) {
		content.TextDecorations = TextDecorations.Underline;
		content.Foreground = Brushes.Blue;
		return new Hyperlink {
			Inlines = [content],
			Uri = target,
			ToolTip = tooltip,
		};
	}

	/// <summary>
	///     Resolves an image URI to inline visual content according to its scheme
	///     (<c>avares:</c>, <c>assets:</c>, or <c>staticres:</c> — see the class remarks),
	///     applying any <c>width</c>/<c>height</c>/<c>background</c>/<c>foreground</c>
	///     query options. Unknown schemes, missing resources, and load errors all fall back to
	///     a <see cref="global::Avalonia.Controls.Documents.Run"/> holding <paramref name="altText"/>.
	/// </summary>
	protected override Inline Image(Uri source, string? altText, string? tooltip) {
		try {
			var scheme = (source.Scheme ?? string.Empty).ToLowerInvariant();
			var query = ParseQuery(source.Query);

			// parse optional width/height/background/foreground from query
			double? parsedWidth = TryParseDoubleFromQuery(query, "width");
			double? parsedHeight = TryParseDoubleFromQuery(query, "height");
			Brush? backgroundBrush = TryParseColorBrushFromQuery(query, "background");
			Brush? foregroundBrush = TryParseColorBrushFromQuery(query, "foreground");

			// helper to apply width/height on a control
			void ApplySize(Control c) {
				if (parsedWidth.HasValue) c.Width = parsedWidth.Value;
				if (parsedHeight.HasValue) c.Height = parsedHeight.Value;
				// default height if none specified
				if (!parsedHeight.HasValue && double.IsNaN(c.Height) && c is Image) c.Height = baseFontSize;
				if (!parsedHeight.HasValue && !parsedWidth.HasValue && c is PathGeometry) c.Height = baseFontSize;
			}

			// AVARES: treat as Avalonia embedded resource (avares://...)
			if (scheme == "avares") {
				var img = new Image {
					Source = new Bitmap(source.AbsoluteUri),
					Stretch = Stretch.Uniform,
				};
				ApplySize(img);

				Control outer = img;
				if (backgroundBrush != null) {
					outer = new Border { Background = backgroundBrush, Child = img };
					ApplySize(outer);
				}

				if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(outer, tooltip);
				return new InlineUIContainer { Child = outer };
			}

			// ASSETS: local filesystem rooted at Assets folder in application directory
			if (scheme == "assets") {
				// path portion
				var rel = source.AbsolutePath ?? source.OriginalString;
				rel = rel.TrimStart('/');
				var root = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
				var filePath = Path.Combine(root, "Assets", rel.Replace('/', Path.DirectorySeparatorChar));

				if (!File.Exists(filePath)) {
					return new Run { Text = altText };
				}

				try {
					var img = new Image {
						Source = new Bitmap(filePath),
						Stretch = Stretch.Uniform,
					};
					ApplySize(img);

					Control outer = img;
					if (backgroundBrush != null) {
						outer = new Border { Background = backgroundBrush, Child = img };
						ApplySize(outer);
					}

					if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(outer, tooltip);
					return new InlineUIContainer { Child = outer };
				} catch {
					return new Run { Text = altText };
				}
			}

			// STATICRES: lookup Application static resources (axaml)
			if (scheme == "staticres") {
				// resource key: try AbsolutePath without leading '/'
				var key = source.AbsolutePath ?? source.OriginalString;
				if (key.StartsWith("/")) key = key.Substring(1);

				object? resource = null;
				if (Application.Current?.Resources != null && Application.Current.Resources.TryGetValue((object)key, out var val)) {
					resource = val;
				}

				// Try original string key as fallback
				if (resource == null && Application.Current?.Resources != null && Application.Current.Resources.TryGetValue((object)source.OriginalString, out var val2)) {
					resource = val2;
				}

				if (resource is null) {
					return new Run { Text = altText };
				}

				// If image resource
				if (resource is IImage imgRes) {
					var img = new Image {
						Source = imgRes,
						Stretch = Stretch.Uniform,
					};
					ApplySize(img);

					Control outer = img;
					if (backgroundBrush != null) {
						outer = new Border { Background = backgroundBrush, Child = img };
						ApplySize(outer);
					}

					if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(outer, tooltip);
					return new InlineUIContainer { Child = outer };
				}

				// If resource is a Geometry (path), apply foreground (fill), size and optional background
				if (resource is Geometry geometry) {
					var path = new PathGeometry {
						Data = geometry,
						Fill = foregroundBrush ?? (IBrush)Brushes.Black,
						Stretch = Stretch.Uniform,
					};
					ApplySize(path);

					Control outer = path;
					if (backgroundBrush != null) {
						outer = new Border { Background = backgroundBrush, Child = path };
						ApplySize(outer);
					}

					if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(outer, tooltip);
					return new InlineUIContainer { Child = outer };
				}

				// If resource is a Control already, try to size and return it
				if (resource is Control ctl) {
					if (foregroundBrush != null && ctl is Shape shapeCtl) shapeCtl.Fill = foregroundBrush;
					if (backgroundBrush != null) {
						var b = new Border { Background = backgroundBrush, Child = ctl };
						ApplySize(b);
						if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(b, tooltip);
						return new InlineUIContainer { Child = b };
					}
					ApplySize(ctl);
					if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(ctl, tooltip);
					return new InlineUIContainer { Child = ctl };
				}

				// Unknown resource type - fallback to alternate text
				return new Run { Text = altText };
			}
		} catch {
			// Swallow rendering errors to avoid breaking markdown flow
		}

		// Default fallback
		return new Run { Text = altText };
	}

	// --- helpers ---
	private static Dictionary<string, string> ParseQuery(string? query) {
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(query)) return result;
		var q = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
		foreach (var kv in q) {
			var parts = kv.Split(new[] { '=' }, 2);
			var name = Uri.UnescapeDataString(parts[0]);
			var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
			result[name] = value;
		}
		return result;
	}

	private static double? TryParseDoubleFromQuery(Dictionary<string, string> q, string key) {
		if (!q.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return null;
		if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
		return null;
	}

	private static Brush? TryParseColorBrushFromQuery(Dictionary<string, string> q, string key) {
		if (!q.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return null;
		try {
			var c = Color.Parse(v);
			return new SolidColorBrush(c);
		} catch {
			return null;
		}
	}

	/// <summary>Makes the content bold.</summary>
	protected override void Embolden(ref Inline content) => content.FontWeight = FontWeight.Bold;
	/// <summary>Makes the content italic.</summary>
	protected override void Italicize(ref Inline content) => content.FontStyle = FontStyle.Italic;
	/// <summary>Drops the content to subscript at 80% of the base font size.</summary>
	protected override void Subscript(ref Inline content) {
		content.BaselineAlignment = BaselineAlignment.Subscript;
		content.FontSize = baseFontSize * 0.8f;
		content = Span([content]);
	}
	/// <summary>Raises the content to superscript at 80% of the base font size.</summary>
	protected override void Superscript(ref Inline content) {
		content.BaselineAlignment = BaselineAlignment.Superscript;
		content.FontSize = baseFontSize * 0.8f;
		content = Span([content]);
	}
}

/// <summary>
/// A markdown parser that produces Avalonia <see cref="Inline"/> content.
/// </summary>
public interface IMarkdownParser : IMarkdownParser<Inline> { }