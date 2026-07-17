using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace PatTech.Markdown.Wpf {
	/// <summary>
	///     Converts a markdown string into a formatted <see cref="Span"/> or
	///     <see cref="TextBlock"/>. Currently only supports basic formatting and hyperlinks.
	/// </summary>
	[ValueConversion(typeof(string), typeof(Span))]
	[ValueConversion(typeof(string), typeof(TextBlock))]
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
				return new TextBlock(inline);
			}
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
			throw new NotSupportedException();
		}
	}
}
