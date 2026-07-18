using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Path = System.IO.Path;
using PathGeometry = System.Windows.Shapes.Path;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     Renders Words markdown as WPF <see cref="Inline"/> content: basic formatting
	///     (bold, italic, sub/superscript), hyperlinks, and images.
	/// </summary>
	/// <remarks>
	///     Image URIs honor the schemes <c>staticres:</c> (application resource by
	///     <c>x:Key</c> — <see cref="ImageSource"/>, <see cref="Geometry"/>, or any
	///     <see cref="FrameworkElement"/>), <c>pack:</c> (WPF pack URIs), <c>resx:</c>
	///     (a <c>Resources</c> class found in loaded assemblies), and <c>assets:</c>
	///     (files under the application's <c>Assets</c> folder). The query string may carry
	///     <c>width</c>, <c>height</c>, <c>background</c>, and <c>foreground</c> options.
	///     Anything that fails to resolve degrades to the image's alt text.
	/// </remarks>
	/// <param name="baseFontSize">Font size the output is destined for; sets the default image height and the sub/superscript size (80% of it).</param>
	/// <param name="logger">An interface for passing on logging instructions to the caller.</param>
	public class MarkdownParser(float baseFontSize = 13, ITakeException? logger = null) : MarkdownParser<Inline>(logger), IMarkdownParser {
		/// <summary>Creates a plain <see cref="System.Windows.Documents.Run"/> for unformatted text.</summary>
		protected override Inline Run(string text) => new Run { Text = text };
		/// <summary>Groups multiple inlines into a single <see cref="System.Windows.Documents.Span"/>.</summary>
		protected override Inline Span(IEnumerable<Inline> inlines) {
			var span = new Span();
			span.Inlines.AddRange(inlines);
			return span;
		}

		/// <summary>
		///     Wraps <paramref name="content"/> in a <see cref="System.Windows.Documents.Hyperlink"/>
		///     pointing at <paramref name="target"/>, underlined and blue in the traditional manner.
		/// </summary>
		protected override Inline Hyperlink(Inline content, Uri target, string? tooltip) {
			content.TextDecorations = TextDecorations.Underline;
			content.Foreground = Brushes.Blue;
			return new Hyperlink {
				Inlines = { content },
				NavigateUri = target,
				ToolTip = tooltip,
			};
		}

		/// <summary>
		///     Resolves an image URI to inline visual content according to its scheme
		///     (<c>staticres:</c>, <c>pack:</c>, <c>resx:</c>, or <c>assets:</c> — see the class
		///     remarks), applying any <c>width</c>/<c>height</c>/<c>background</c>/<c>foreground</c>
		///     query options. Unknown schemes, missing resources, and load errors all fall back to
		///     a <see cref="System.Windows.Documents.Run"/> holding <paramref name="altText"/>.
		/// </summary>
		protected override Inline Image(Uri source, string? altText, string? tooltip) {
			try {
				var scheme = (source.Scheme ?? string.Empty).ToLowerInvariant();
				var query = ParseQuery(source.Query);

				double? parsedWidth = TryParseDoubleFromQuery(query, "width");
				double? parsedHeight = TryParseDoubleFromQuery(query, "height");
				Brush? backgroundBrush = TryParseColorBrushFromQuery(query, "background");
				Brush? foregroundBrush = TryParseColorBrushFromQuery(query, "foreground");

				void ApplySize(FrameworkElement e) {
					if (parsedWidth.HasValue) e.Width = parsedWidth.Value;
					if (parsedHeight.HasValue) e.Height = parsedHeight.Value;
					// sensible default height if none specified
					if (!parsedHeight.HasValue && e.Height.Equals(double.NaN) && e is Image) e.Height = baseFontSize;
					if (!parsedHeight.HasValue && !parsedWidth.HasValue && e is PathGeometry) e.Height = baseFontSize;
				}

				// STATICRES - lookup Application resources (x:Key)
				if (scheme == "staticres") {
					var key = (source.AbsolutePath ?? source.OriginalString).TrimStart('/');
					object? resource = TryFindInApplicationResources(key);

					if (resource is null) return new Run { Text = altText };

					// Image source
					if (resource is ImageSource imgSrc) {
						var img = new Image { Source = imgSrc, Stretch = Stretch.Uniform };
						ApplySize(img);
						FrameworkElement outer = img;
						if (backgroundBrush != null) outer = new Border { Background = backgroundBrush, Child = img };
						if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(outer, tooltip);
						return new InlineUIContainer(outer);
					}

					// Geometry resource
					if (resource is Geometry geom) {
						var path = new PathGeometry { Data = geom, Fill = foregroundBrush ?? Brushes.Black, Stretch = Stretch.Uniform };
						ApplySize(path);
						FrameworkElement outer = path;
						if (backgroundBrush != null) outer = new Border { Background = backgroundBrush, Child = path };
						if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(outer, tooltip);
						return new InlineUIContainer(outer);
					}

					// If resource is a FrameworkElement - size and return
					if (resource is FrameworkElement fe) {
						if (foregroundBrush != null && fe is Shape s) s.Fill = foregroundBrush;
						if (backgroundBrush != null) {
							var b = new Border { Background = backgroundBrush, Child = fe };
							ApplySize(b);
							if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(b, tooltip);
							return new InlineUIContainer(b);
						}
						ApplySize(fe);
						if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(fe, tooltip);
						return new InlineUIContainer(fe);
					}

					// fallback to alt text
					return new Run { Text = altText };
				}

				// PACK - WPF Pack URIs (pack://application:,,,/Assembly;component/Path)
				if (scheme == "pack") {
					// let BitmapImage handle pack URIs
					var bmp = new BitmapImage();
					bmp.BeginInit();
					bmp.UriSource = source;
					bmp.CacheOption = BitmapCacheOption.OnLoad;
					bmp.EndInit();
					var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
					ApplySize(img);
					FrameworkElement outer = img;
					if (backgroundBrush != null) outer = new Border { Background = backgroundBrush, Child = img };
					if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(outer, tooltip);
					return new InlineUIContainer(outer);
				}

				// RESX - attempt to locate a ResourceManager in loaded assemblies (Properties.Resources or Resources)
				if (scheme == "resx") {
					var key = (source.AbsolutePath ?? source.OriginalString).TrimStart('/');
					var obj = TryGetResxObject(key);
					if (obj is null) return new Run { Text = altText };

					if (obj is ImageSource isrc) {
						var img = new Image { Source = isrc, Stretch = Stretch.Uniform };
						ApplySize(img);
						FrameworkElement outer = img;
						if (backgroundBrush != null) outer = new Border { Background = backgroundBrush, Child = img };
						if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(outer, tooltip);
						return new InlineUIContainer(outer);
					}

					if (obj is Drawing.Bitmap bmpObj) {
						// convert System.Drawing.Bitmap to BitmapSource if possible
						var ms = new MemoryStream();
						bmpObj.Save(ms, Drawing.Imaging.ImageFormat.Png);
						ms.Position = 0;
						var bmp = new BitmapImage();
						bmp.BeginInit();
						bmp.StreamSource = ms;
						bmp.CacheOption = BitmapCacheOption.OnLoad;
						bmp.EndInit();
						var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
						ApplySize(img);
						FrameworkElement outer = img;
						if (backgroundBrush != null) outer = new Border { Background = backgroundBrush, Child = img };
						if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(outer, tooltip);
						return new InlineUIContainer(outer);
					}

					return new Run { Text = altText };
				}

				// ASSETS - local filesystem rooted at Assets folder in application directory
				if (scheme == "assets") {
					var rel = (source.AbsolutePath ?? source.OriginalString).TrimStart('/');
					var root = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
					var filePath = Path.Combine(root, "Assets", rel.Replace('/', Path.DirectorySeparatorChar));
					if (!File.Exists(filePath)) return new Run { Text = altText };

					try {
						var bmp = new BitmapImage();
						bmp.BeginInit();
						bmp.UriSource = new Uri(filePath, UriKind.Absolute);
						bmp.CacheOption = BitmapCacheOption.OnLoad;
						bmp.EndInit();
						var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
						ApplySize(img);
						FrameworkElement outer = img;
						if (backgroundBrush != null) outer = new Border { Background = backgroundBrush, Child = img };
						if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(outer, tooltip);
						return new InlineUIContainer(outer);
					} catch {
						return new Run { Text = altText };
					}
				}
			} catch {
				// swallow to avoid breaking markdown pipeline
			}

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
				var conv = new ColorConverter();
				var colObj = conv.ConvertFromString(null, System.Globalization.CultureInfo.InvariantCulture, v);
				if (colObj is Color c) return new SolidColorBrush(c);
			} catch {
				// ignore parse errors
			}
			return null;
		}

		private static object? TryFindInApplicationResources(string key) {
			if (Application.Current == null) return null;
			// direct lookup
			if (Application.Current.Resources.Contains(key)) return Application.Current.Resources[key];
			// search merged dictionaries
			foreach (var md in Application.Current.Resources.MergedDictionaries) {
				if (md.Contains(key)) return md[key];
			}
			// fallback: try FindResource which will throw if not found - avoid
			return null;
		}

		private static object? TryGetResxObject(string key) {
			// search loaded assemblies for a type named Properties.Resources or Resources with a ResourceManager property
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
				Type? t = asm.GetTypes().FirstOrDefault(x =>
					x.FullName != null &&
					(x.FullName.EndsWith(".Properties.Resources", StringComparison.OrdinalIgnoreCase) ||
					 x.FullName.EndsWith(".Resources", StringComparison.OrdinalIgnoreCase)));
				if (t is null) continue;

				var prop = t.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (prop == null) continue;
				if (prop.GetValue(null) is ResourceManager rm) {
					try {
						var obj = rm.GetObject(key);
						if (obj != null) return obj;
					} catch {
						/* ignore */
					}
				}
			}
			return null;
		}

		/// <summary>Makes the content bold.</summary>
		protected override void Embolden(ref Inline content) => content.FontWeight = FontWeights.Bold;
		/// <summary>Makes the content italic.</summary>
		protected override void Italicize(ref Inline content) => content.FontStyle = FontStyles.Italic;
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
	/// A markdown parser that produces WPF <see cref="Inline"/> content.
	/// </summary>
	public interface IMarkdownParser : IMarkdownParser<Inline> { }
}
