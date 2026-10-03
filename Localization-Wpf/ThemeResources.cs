using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PatTech.Localization.Wpf;
/// <summary>
///     The look of rendered markdown that a theme may restyle: a link's colour, and a code
///     span's monospace font and subtle background. Each is a resource — <c>WordsLinkBrush</c>,
///     <c>WordsCodeFont</c>, <c>WordsCodeBackground</c> — found from where the inline lands,
///     the window then the application, and followed when it changes, the way
///     <c>{DynamicResource}</c> is. Where one is defined nowhere, a fixed look stands in.
/// </summary>
internal static class ThemeResources {
	private static readonly FontFamily FallbackCodeFont = new("Consolas, Courier New");
	private static readonly Brush FallbackCodeBackground = Frozen(Color.FromArgb(0x22, 0x80, 0x80, 0x80));

	private static readonly Look LinkBrush = new("WordsLinkBrush",
		(element, value) => element.Foreground = ToBrush(value) ?? Brushes.Blue);
	private static readonly Look CodeFont = new("WordsCodeFont",
		(element, value) => element.FontFamily = value switch {
			FontFamily font => font,
			string name => new FontFamily(name),
			_ => FallbackCodeFont,
		});
	private static readonly Look CodeBackground = new("WordsCodeBackground",
		(element, value) => element.Background = ToBrush(value) ?? FallbackCodeBackground);

	/// <summary>Colours <paramref name="link"/> from <c>WordsLinkBrush</c>, blue without it.</summary>
	public static void ApplyLink(TextElement link) => LinkBrush.Follow(link);

	/// <summary>Styles <paramref name="run"/> as code.</summary>
	public static void ApplyCode(TextElement run) {
		CodeFont.Follow(run);
		CodeBackground.Follow(run);
	}

	// one resource: an attached slot holds it as the framework resolves it, and each change
	// lands on the element through apply — nothing found, the fixed look
	private sealed class Look {
		private readonly string key;
		private readonly Action<TextElement, object?> apply;
		private readonly DependencyProperty slot;

		public Look(string key, Action<TextElement, object?> apply) {
			this.key = key;
			this.apply = apply;
			slot = DependencyProperty.RegisterAttached(key, typeof(object), typeof(ThemeResources),
				new PropertyMetadata(null, (d, e) => apply((TextElement)d, e.NewValue)));
		}

		public void Follow(TextElement element) {
			apply(element, null);
			element.SetResourceReference(slot, key);
		}
	}

	private static Brush? ToBrush(object? value) => value switch {
		Brush brush => brush,
		Color color => Frozen(color),
		_ => null,
	};

	private static Brush Frozen(Color color) {
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}
}
