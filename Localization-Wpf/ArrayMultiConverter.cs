using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace PatTech.Localization.Wpf;

/// <summary>
/// Provides an implementation of the IMultiValueConverter interface that converts a collection of values into an array.
/// </summary>
/// <remarks>Use this converter in data binding scenarios where multiple input values need to be combined into a
/// single array for further processing or display. The converter does not perform any transformation on the input
/// values; it simply aggregates them into an array. This is useful for controls or APIs that require array input from
/// multiple sources.</remarks>
public class ArrayMultiConverter : IMultiValueConverter {
	/// <summary>
	///     Copies the bound values into an <see cref="object"/> array, in binding order, exactly
	///     as they arrived. Handy for feeding a <c>MultiBinding</c> into targets that expect
	///     positional format arguments, such as <see cref="WordsInline.Params"/>.
	/// </summary>
	/// <param name="values">The values produced by the multi-binding. Copied, because WPF reuses the array between calls.</param>
	/// <param name="targetType">Ignored.</param>
	/// <param name="parameter">Ignored.</param>
	/// <param name="culture">Ignored.</param>
	/// <returns>The bound values, as an array.</returns>
	public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
		=> values.ToArray();

	/// <summary>
	/// Not Implemented.
	/// </summary>
	/// <exception cref="NotImplementedException"></exception>
	public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
		=> throw new NotImplementedException();
}
