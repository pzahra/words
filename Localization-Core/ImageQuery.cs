using System;
using System.Collections.Generic;
using System.Globalization;

namespace PatTech.Localization {
	/// <summary>
	/// The framework-free half of a markdown image's URI query —
	/// <c>scheme:path?width=W&amp;height=H&amp;background=B&amp;foreground=F</c>: the
	/// numbers parsed, the brush values kept raw for the framework to turn into brushes,
	/// every option available by name. The WPF and Avalonia <c>ImageOptions</c> are
	/// built from it.
	/// </summary>
	public sealed class ImageQuery {
		/// <summary>The query that asks for nothing.</summary>
		public static readonly ImageQuery Empty = new(null, null, null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

		/// <summary>The largest a requested dimension may be, so a runaway <c>?width=</c> can't ask the layout to allocate an enormous image.</summary>
		public const double MaxDimension = 4096;

		private ImageQuery(double? width, double? height, string? background, string? foreground, IReadOnlyDictionary<string, string> options) {
			Width = width;
			Height = height;
			Background = background;
			Foreground = foreground;
			Options = options;
		}

		/// <summary>Requested width from <c>?width=</c>, or <see langword="null"/> for natural sizing.</summary>
		public double? Width { get; }
		/// <summary>Requested height from <c>?height=</c>, or <see langword="null"/> for natural sizing.</summary>
		public double? Height { get; }
		/// <summary>The raw <c>?background=</c> value — a color name, or a <see cref="ResourceReference"/> spelling — or <see langword="null"/> when absent or blank.</summary>
		public string? Background { get; }
		/// <summary>The raw <c>?foreground=</c> value — a color name, or a <see cref="ResourceReference"/> spelling — or <see langword="null"/> when absent or blank.</summary>
		public string? Foreground { get; }
		/// <summary>Every option by name (case-insensitive), the well-known ones included.</summary>
		public IReadOnlyDictionary<string, string> Options { get; }

		/// <summary>
		/// Parses a URI query string (with or without the leading <c>?</c>). An
		/// unparseable number is treated as unspecified rather than thrown.
		/// </summary>
		/// <param name="query">The query portion of the image URI; <see langword="null"/> or empty gives <see cref="Empty"/>.</param>
		public static ImageQuery Parse(string? query) {
			if (string.IsNullOrEmpty(query)) return Empty;
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) {
				var parts = pair.Split('=', 2);
				values[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
			}
			return new ImageQuery(Dimension(values, "width"), Dimension(values, "height"), Raw(values, "background"), Raw(values, "foreground"), values);
		}

		/// <summary>
		/// Parses the query off a whole image URI and rewrites <paramref name="source"/> to
		/// the query-less remainder: the query carries display options, not asset
		/// identity, so resolvers always receive a clean URI (a <c>pack:</c> resource named
		/// <c>x.png?width=32</c> exists nowhere). The split is done by hand because
		/// <see cref="Uri"/> only recognizes a query in schemes it knows, and image schemes
		/// are often anything but: left alone, the <c>?</c> stays glued to the asset path.
		/// </summary>
		/// <param name="source">The image URI; rewritten without its query portion.</param>
		public static ImageQuery Parse(ref Uri source) {
			ArgumentNullException.ThrowIfNull(source);
			var raw = source.OriginalString;
			var q = raw.IndexOf('?');
			if (q < 0) return Empty;
			var query = Parse(raw[(q + 1)..]);
			source = new Uri(raw[..q]);
			return query;
		}

		/// <summary>
		/// The scheme-less remainder of an image URI, as resolvers read it:
		/// <c>staticres:key</c> gives <c>key</c>, <c>scheme://host/a/b</c> gives <c>a/b</c>.
		/// Percent-escapes are left as they are.
		/// </summary>
		/// <param name="source">The (query-less) image URI.</param>
		public static string PathOf(Uri source) {
			ArgumentNullException.ThrowIfNull(source);
			return (source.IsAbsoluteUri ? source.AbsolutePath : source.OriginalString).TrimStart('/');
		}

		/// <summary>
		/// A requested dimension made safe to lay out: finite and non-negative, capped at
		/// <see cref="MaxDimension"/>. Anything else is <see langword="null"/> — unspecified,
		/// natural sizing — never a throw.
		/// </summary>
		/// <param name="value">The requested dimension, as parsed.</param>
		public static double? CleanDimension(double? value)
			=> value is double v && double.IsFinite(v) && v >= 0 ? (v > MaxDimension ? MaxDimension : v) : null;

		private static double? Dimension(Dictionary<string, string> values, string key) {
			if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return null;
			return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
		}

		private static string? Raw(Dictionary<string, string> values, string key)
			=> values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
	}

	/// <summary>
	/// A resource asked for by key in an image query value, spelled the way the image
	/// schemes are: <c>staticres:key</c> keeps the first value found, <c>dynres:key</c>
	/// follows later changes. A bare word is not a reference — it is a color, for the
	/// framework to parse.
	/// </summary>
	/// <param name="Key">The resource key (<c>x:Key</c>) to look up.</param>
	/// <param name="IsDynamic"><see langword="true"/> for <c>dynres:</c>; <see langword="false"/> for <c>staticres:</c>.</param>
	public readonly record struct ResourceReference(string Key, bool IsDynamic) {
		/// <summary>
		/// Reads the spelling off a query value. A value without one of the two prefixes,
		/// or with nothing after it, is not a reference.
		/// </summary>
		/// <param name="value">The raw query value.</param>
		/// <param name="reference">The reference read, when there was one.</param>
		public static bool TryParse(string? value, out ResourceReference reference) {
			if (value is not null) {
				if (TryStrip(value, "dynres:", out var key)) {
					reference = new ResourceReference(key, IsDynamic: true);
					return true;
				}
				if (TryStrip(value, "staticres:", out key)) {
					reference = new ResourceReference(key, IsDynamic: false);
					return true;
				}
			}
			reference = default;
			return false;
		}

		private static bool TryStrip(string value, string scheme, out string key) {
			key = value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? value[scheme.Length..].Trim() : "";
			return key.Length > 0;
		}
	}
}
