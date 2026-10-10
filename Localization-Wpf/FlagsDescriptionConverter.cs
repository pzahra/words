using PatTech.Utils;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace PatTech.Localization.Wpf;

/// <summary>
/// Provides value conversion for enumeration types, producing formatted descriptions of flag values according to
/// configurable options.
/// </summary>
/// <remarks>Use this converter to display or process descriptions of Flags enums in UI scenarios, such as data
/// binding. The output format can be customized using properties like IncludeNone, AsArray, Delimiter, and Format.
/// Supports conversion to either a delimited string or an enumerable of descriptions, depending on configuration. Only
/// enumeration values are supported; non-enum values are not converted.</remarks>
public class FlagsDescriptionConverter : IValueConverter {
	/// <summary>
	/// Gets or sets a value indicating whether a 'None' option is included in the selection.
	/// </summary>
	public bool IncludeNone { get; set; } = true;
	/// <summary>
	/// Gets or sets a value indicating whether the data should be represented as an array.
	/// </summary>
	public bool AsArray { get; set; } = true;
	/// <summary>
	/// Gets or sets the string used to separate items in formatted output.
	/// </summary>
	public string Delimiter { get; set; } = ", ";
	/// <summary>
	/// Gets or sets the format string used to control how values are formatted.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The format string determines the output representation of values, following standard .NET
	/// formatting conventions. The default value is "G", which specifies the general format. Changing this property
	/// affects how values are displayed or parsed in related operations.
	/// </para>
	/// <para>
	/// See <seealso cref="Extensions.Describe(Enum, string?, IWords?)"/> for more details on formatting Enums.
	/// </para>
	/// </remarks>
	public string Format { get; set; } = "G";

	/// <summary>
	/// Converts an enumeration value to its string representation or a collection of descriptions, based on the specified
	/// formatting options.
	/// </summary>
	/// <remarks>If the enumeration is a Flags enum, the method returns descriptions for each flag set in the value.
	/// The output format depends on configuration options such as delimiter and array output. A flag with nothing for
	/// the format is left out, and a joined string with nothing in it is <see langword="null"/>. Non-enum values are not
	/// converted.</remarks>
	/// <param name="value">The enumeration value to convert. Can be null.</param>
	/// <param name="targetType">The type to convert the value to. Typically a string or an enumerable type.</param>
	/// <param name="parameter">An optional formatting parameter used to customize the description output. Can be null.</param>
	/// <param name="culture">The culture information used for formatting the output.</param>
	/// <returns>A string containing the formatted description(s) of the enumeration value, or an enumerable of descriptions if
	/// array output is enabled. Returns <see cref="Binding.DoNothing"/> if the input is not an enumeration.</returns>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is Enum @enum) {
			// the members .NET names for the value, overlaps settled its way
			// (runtime SPEC: Describe without the type)
			var names = Describable.Members(@enum)
				.Where(member => IncludeNone || member.Number != "0")
				.Describe(parameter?.ToString() ?? Format)
				// a flag with nothing for the format is left out, not shown as a gap
				.Where(name => name != "");
			if (AsArray) {
				return names;
			}
			// nothing left is null, as for EnumDescriptionConverter, so a tooltip stays hidden
			return string.Join(Delimiter, names) is { Length: > 0 } text ? text : null;
		}
		return Binding.DoNothing;
	}

	/// <summary>
	/// Not supported; this converter is one-way.
	/// </summary>
	/// <exception cref="NotSupportedException">Always.</exception>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}