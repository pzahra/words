using System;
using System.Globalization;
using System.Windows.Data;

namespace PatTech.Markdown.Wpf {
	public class ArrayMultiConverter : IMultiValueConverter {
		public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
			=> values.Clone();

		public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
			=> throw new NotImplementedException();
	}
}
