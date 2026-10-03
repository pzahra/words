using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PatTech.Localization.Avalonia;
/// <summary>
///     The look of rendered markdown that a theme may restyle: a link's colour, and a code
///     span's monospace font and subtle background. Each is bound to a resource —
///     <c>WordsLinkBrush</c>, <c>WordsCodeFont</c>, <c>WordsCodeBackground</c> — found from
///     where the inline lands, the window, the application and the theme variant, and followed
///     when it changes, the way <c>{DynamicResource}</c> is. Where one is defined nowhere, a
///     fixed look stands in.
/// </summary>
internal static class ThemeResources {
	private static readonly FontFamily FallbackCodeFont = new("Consolas, Menlo, Courier New");
	private static readonly IBrush FallbackCodeBackground = new ImmutableSolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80));

	/// <summary>Colours <paramref name="link"/> from <c>WordsLinkBrush</c>, blue without it.</summary>
	public static void ApplyLink(TextElement link)
		=> link.Bind(TextElement.ForegroundProperty, link.GetResourceObservable("WordsLinkBrush",
			value => ToBrush(value) ?? Brushes.Blue));

	/// <summary>Styles <paramref name="run"/> as code.</summary>
	public static void ApplyCode(TextElement run) {
		run.Bind(TextElement.FontFamilyProperty, run.GetResourceObservable("WordsCodeFont", value => value switch {
			FontFamily font => font,
			string name => new FontFamily(name),
			_ => FallbackCodeFont,
		}));
		run.Bind(TextElement.BackgroundProperty, run.GetResourceObservable("WordsCodeBackground",
			value => ToBrush(value) ?? FallbackCodeBackground));
	}

	private static IBrush? ToBrush(object? value) => value switch {
		IBrush brush => brush,
		Color color => new ImmutableSolidColorBrush(color),
		_ => null,
	};
}
