using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using System.Globalization;

namespace PatTech.Localization.Ava;

public class MarkdownConverter : IValueConverter {
	private static readonly MarkdownParser markdown = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		var text = value as string ?? value?.ToString();

		Inline inline;
		if (!string.IsNullOrEmpty(text)) {
			inline = markdown.ToInline(text);
		}
		else {
			inline = new Run();
		}

		if (typeof(TextBlock).IsAssignableFrom(targetType)) {
			// Try to facilitate usage in flow context, if applicable
			return inline;
		}
		else {
			// otherwise, assume it's in a Visual context
			return new TextBlock { Inlines = [inline] };
		}
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}