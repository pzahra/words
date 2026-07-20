using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Logging;
using Avalonia.Media;
using PathGeometry = Avalonia.Controls.Shapes.Path;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     Renders Words markdown as Avalonia <see cref="Inline"/> content: basic formatting
///     (bold, italic, sub/superscript), hyperlinks, and images.
/// </summary>
/// <remarks>
///     Image URIs are resolved through the <see cref="ImageSchemes"/> registry. Out of
///     the box that covers <c>avares:</c> (embedded assets), <c>assets:</c> (files under
///     the application's <c>Assets</c> folder), and <c>staticres:</c> (application
///     resource by <c>x:Key</c>); register your own <see cref="IImageSchemeResolver"/>
///     to teach it more. The query string may carry <c>width</c>, <c>height</c>,
///     <c>background</c>, and <c>foreground</c> options, applied uniformly whatever
///     the scheme. Anything that fails to resolve degrades to the image's alt text.
/// </remarks>
/// <param name="baseFontSize">Font size the output is destined for; sets the sub/superscript size (80% of it) and the default height of geometry icons.</param>
/// <param name="logger">An interface for passing on logging instructions to the caller.</param>
public class MarkdownParser(float baseFontSize = 13, ITakeException? logger = null) : MarkdownParser<Inline>(logger), IMarkdownParser {
	private static MarkdownParser _Default = new(logger: ITakeException.Global);
	/// <summary>
	///     The shared parser used by <see cref="WordsInline"/> and
	///     <see cref="MarkdownConverter"/>. It gripes through
	///     <see cref="ITakeException.Global"/>, i.e. wherever <see cref="Words.Logger"/>
	///     points when the gripe happens. Register custom image schemes on it at
	///     startup (<c>MarkdownParser.Default.ImageSchemes["md"] = …</c>), or replace
	///     it wholesale to change the base font size or logger.
	/// </summary>
	/// <exception cref="ArgumentNullException">The value assigned is <see langword="null"/>.</exception>
	public static MarkdownParser Default {
		get => _Default;
		set => _Default = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>
	///     The image scheme registry: maps a URI scheme (case-insensitive, no colon) to
	///     the resolver that produces its visual. Pre-loaded with the built-in schemes;
	///     add, replace or remove entries to taste. The registry is per-instance, so a
	///     specially-schooled parser doesn't leak its vocabulary into others.
	/// </summary>
	public Dictionary<string, IImageSchemeResolver> ImageSchemes { get; } = new(StringComparer.OrdinalIgnoreCase) {
		["avares"] = new AvaresImageResolver(),
		["assets"] = new AssetsImageResolver(),
		["staticres"] = new StaticResImageResolver(),
	};

	/// <summary>Creates a plain <see cref="global::Avalonia.Controls.Documents.Run"/> for unformatted text.</summary>
	protected override Inline Run(string text) => new Run { Text = text };
	/// <summary>Groups multiple inlines into a single <see cref="global::Avalonia.Controls.Documents.Span"/>.</summary>
	protected override Inline Span(IEnumerable<Inline> inlines) {
		// populate the existing collection: replacing it via the setter leaves the
		// children without a logical parent, so they stop inheriting text properties
		var span = new Span();
		span.Inlines.AddRange(inlines);
		return span;
	}
	/// <summary>
	///     Wraps <paramref name="content"/> in a <see cref="PatTech.Localization.Avalonia.Hyperlink"/>
	///     pointing at <paramref name="target"/>, underlined and blue in the traditional manner.
	/// </summary>
	protected override Inline Hyperlink(Inline content, Uri target, string? tooltip) {
		var link = new Hyperlink {
			Uri = target,
			ToolTip = tooltip,
		};
		link.Inlines.Add(content);
		return link;
	}

	/// <summary>
	///     Resolves an image URI through the <see cref="ImageSchemes"/> registry, then
	///     applies the <c>width</c>/<c>height</c> options (raster images default to
	///     their actual size; geometry, having none, defaults to the base font
	///     size), wraps in a <see cref="Border"/> when a
	///     <c>background</c> was asked for, and attaches the tooltip. Unknown schemes,
	///     resolvers that come back empty-handed, and resolver exceptions all fall back
	///     to a <see cref="global::Avalonia.Controls.Documents.Run"/> holding <paramref name="altText"/>,
	///     reporting the failed source to the logger as <c>IMG:RES</c>.
	/// </summary>
	protected override Inline Image(Uri source, string? altText, string? tooltip) {
		try {
			if (ImageSchemes.TryGetValue(source.Scheme ?? string.Empty, out var resolver)) {
				var options = ImageOptions.Parse(ref source);
				if (resolver.Resolve(source, options) is { } visual) {
					ApplySize(visual, options);
					Control outer = visual;
					if (options.Background is not null) {
						outer = new Border { Background = options.Background, Child = visual };
					}
					if (!string.IsNullOrEmpty(tooltip)) {
						ToolTip.SetTip(outer, tooltip);
					}
					return new InlineUIContainer { Child = outer };
				}
			}
			logger.Warn("IMG:RES:" + source);
		}
		catch(Exception ex) {
			// a broken image must not take the paragraph down with it
			logger.Error(ex, "IMG:RES:" + source);
		}

		return new Run { Text = $"[🖼️!{altText}]" };
	}

	private void ApplySize(Control control, ImageOptions options) {
		if (options.Width is double width) control.Width = width;
		if (options.Height is double height) control.Height = height;
		if (options.Height is null && options.Width is null) {
			// geometry has no natural size, so default it to the font height
			if (control is PathGeometry) control.Height = baseFontSize;
			// raster images get pinned to their natural size: the TextBlock measures
			// embedded controls with the whole line's constraint, and an unpinned
			// Stretch.Uniform image balloons to fill it
			else if (control is Image { Source: { } source }) {
				control.Width = source.Size.Width;
				control.Height = source.Size.Height;
			}
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
