using System.Globalization;
using System.Windows.Data;

namespace WordsEdit.Views;
public class DateTimeOffsetToStringConverter : IValueConverter {
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is DateTimeOffset dateTimeOffset && dateTimeOffset == DateTimeOffset.MinValue) {
			return "Unknown";
		}
		else if (value is null) {
			return null;
		}

		return ((DateTimeOffset)value).ToString();
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotImplementedException();
	}
}