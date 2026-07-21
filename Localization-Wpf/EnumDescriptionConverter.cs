using PatTech.Utils;
using System;
using System.Globalization;
using System.Windows.Data;

namespace PatTech.Localization.Wpf;

/// <summary>
///     Converts a single enum value to its localized description via
///     <see cref="Extensions.Describe(Enum, string?, IWords?)"/>: the display text
///     from the member's <see cref="WordsAttribute"/> key, falling back through
///     <see cref="System.ComponentModel.DescriptionAttribute"/> to the symbol name.
/// </summary>
/// <remarks>
///     The ConverterParameter (or, failing that, <see cref="Format"/>) selects the
///     Describe format — e.g. <c>G</c> for the display text, <c>T</c> for the
///     tooltip, <c>d</c> for the long description, <c>U</c> for the unit. For
///     [Flags] combinations rendered flag by flag, use
///     <see cref="FlagsDescriptionConverter"/> instead.
/// </remarks>
[ValueConversion(typeof(Enum), typeof(string), ParameterType = typeof(string))]
public class EnumDescriptionConverter : IValueConverter {
	/// <summary>
	///     The <see cref="Extensions.Describe(Enum, string?, IWords?)"/> format used
	///     when the binding provides no ConverterParameter. Defaults to <c>G</c>,
	///     the general display text.
	/// </summary>
	public string Format { get; set; } = "G";

	/// <summary>
	///     Describes the bound enum value. Non-enum values are not converted.
	/// </summary>
	/// <param name="value">The enum value to describe. Can be null.</param>
	/// <param name="targetType">Ignored; the result is always a string.</param>
	/// <param name="parameter">An optional Describe format overriding <see cref="Format"/>.</param>
	/// <param name="culture">Ignored; the Words language decides.</param>
	/// <returns>The localized description, or <see cref="Binding.DoNothing"/> if the input is not an enumeration.</returns>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is Enum @enum) {
			return @enum.Describe(parameter?.ToString() ?? Format);
		}
		return Binding.DoNothing;
	}

	/// <summary>
	/// Not Implemented.
	/// </summary>
	/// <exception cref="NotImplementedException"></exception>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotImplementedException();
}
