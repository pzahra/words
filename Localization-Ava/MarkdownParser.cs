using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Path = System.IO.Path;
using PathGeometry = Avalonia.Controls.Shapes.Path;

namespace PatTech.Localization.Avalonia;

public class MarkdownParser(float baseFontSize = 13, ITakeException? logger = null) : MarkdownParser<Inline>(logger), IMarkdownParser {
	protected override Inline Run(string text) => new Run { Text = text };
	protected override Inline Span(IEnumerable<Inline> inlines) => new Span { Inlines = [..inlines] };
	protected override Inline Hyperlink(Inline content, Uri target, string? tooltip) {
		content.TextDecorations = TextDecorations.Underline;
		content.Foreground = Brushes.Blue;
		return new Hyperlink {
			Inlines = [content],
			Uri = target,
			ToolTip = tooltip,
		};
	}

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
				if (!parsedHeight.HasValue && c.Height == double.NaN && c is Image) c.Height = baseFontSize;
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

	protected override void Embolden(ref Inline content) => content.FontWeight = FontWeight.Bold;
	protected override void Italicize(ref Inline content) => content.FontStyle = FontStyle.Italic;
	protected override void Subscript(ref Inline content) {
		content.BaselineAlignment = BaselineAlignment.Subscript;
		content.FontSize = baseFontSize * 0.8f;
		content = Span([content]);
	}
	protected override void Superscript(ref Inline content) {
		content.BaselineAlignment = BaselineAlignment.Superscript;
		content.FontSize = baseFontSize * 0.8f;
		content = Span([content]);
	}
}

public interface IMarkdownParser : IMarkdownParser<Inline> { }