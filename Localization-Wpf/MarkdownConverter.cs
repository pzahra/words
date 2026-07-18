using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     Converts a markdown string into a formatted <see cref="Span"/> or
	///     <see cref="TextBlock"/>. Currently only supports basic formatting and hyperlinks.
	/// </summary>
	[ValueConversion(typeof(string), typeof(Span))]
	[ValueConversion(typeof(string), typeof(TextBlock))]
	public class MarkdownConverter : IValueConverter {
		private static readonly MarkdownParser markdown = new();

		/// <summary>
		///     Parses the bound value (via <see cref="object.ToString"/> if it isn't already a
		///     string) as markdown and returns the formatted result.
		/// </summary>
		/// <remarks>
		///     When <paramref name="targetType"/> is <see cref="TextBlock"/> (or a subclass), the
		///     <see cref="Inline"/> is returned directly for use in flow content; for any other
		///     target the inline is wrapped in a new <see cref="TextBlock"/>. Null or empty input
		///     produces an empty <see cref="Run"/>.
		/// </remarks>
		/// <param name="value">The markdown text to render.</param>
		/// <param name="targetType">Decides between raw <see cref="Inline"/> and <see cref="TextBlock"/> output.</param>
		/// <param name="parameter">Ignored.</param>
		/// <param name="culture">Ignored; markdown is culture-agnostic.</param>
		/// <returns>An <see cref="Inline"/> or <see cref="TextBlock"/> holding the formatted text.</returns>
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

		/// <summary>
		/// Not supported; the markdown is not reconstructed from rendered inlines.
		/// </summary>
		/// <exception cref="NotSupportedException">Always.</exception>
		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
			throw new NotSupportedException();
		}
	}
}
