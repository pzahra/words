using Avalonia.Data.Converters;
using System.Globalization;

namespace PatTech.Localization.Ava;

/// <summary>
/// Provides an implementation of the IMultiValueConverter interface that converts a collection of values into an array.
/// </summary>
/// <remarks>Use this converter in data binding scenarios where multiple input values need to be combined into a
/// single array for further processing or display. The converter does not perform any transformation on the input
/// values; it simply aggregates them into an array. This is useful for controls or APIs that require array input from
/// multiple sources.</remarks>
public class ArrayMultiConverter : IMultiValueConverter {
	/// <summary>Does the thing.</summary>
	/// <returns>The bound value, as array.</returns>
	public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
		=> values.ToArray();
}
