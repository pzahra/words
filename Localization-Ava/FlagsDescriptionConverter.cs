using Avalonia;
using Avalonia.Data.Converters;
using PatTech.Utils;
using System.Globalization;

namespace PatTech.Localization.Ava;

/// <summary>
/// Provides value conversion for enumeration types, producing formatted descriptions of flag values according to
/// configurable options.
/// </summary>
/// <remarks>Use this converter to display or process descriptions of Flags enums in UI scenarios, such as data
/// binding. The output format can be customized using properties like IncludeNone, AsArray, Delimiter, and Format.
/// Supports conversion to either a delimited string or an enumerable of descriptions, depending on configuration. Only
/// enumeration values are supported; non-enum values are not converted.</remarks>
public class FlagsDescriptionConverter : IValueConverter {
	public bool IncludeNone { get; set; } = true;
	public bool AsArray { get; set; } = true;
	public string Delimiter { get; set; } = ", ";
	public string Format { get; set; } = "G";

	private static readonly string[] separator = [", "];

	/// <summary>
	/// Converts an enumeration value to its string representation or a collection of descriptions, based on the specified
	/// formatting options.
	/// </summary>
	/// <remarks>If the enumeration is a Flags enum, the method returns descriptions for each flag set in the value.
	/// The output format depends on configuration options such as delimiter and array output. Non-enum values are not
	/// converted.</remarks>
	/// <param name="value">The enumeration value to convert. Can be null.</param>
	/// <param name="targetType">The type to convert the value to. Typically a string or an enumerable type.</param>
	/// <param name="parameter">An optional formatting parameter used to customize the description output. Can be null.</param>
	/// <param name="culture">The culture information used for formatting the output.</param>
	/// <returns>A string containing the formatted description(s) of the enumeration value, or an enumerable of descriptions if
	/// array output is enabled. Returns AvaloniaProperty.UnsetValue if the input is not an enumeration.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the enumeration value's string representation is unexpectedly null.</exception>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is Enum @enum) {
			// A bit hacky, but it'll do.
			// Relies on the base Enum object's ability to find the optimal set of flags,
			// then pulls those and gets their descriptions.
			var type = @enum.GetType();
			var names = (value.ToString() ?? throw new InvalidOperationException("ToString is null"))
				.Split(separator, StringSplitOptions.None)
				.Select(name => (isFlag: Enum.TryParse(type, name, out var res), flag: res))
				.Where(isf => IncludeNone || (isf.isFlag && (System.Convert.ToInt64(isf.flag) != 0)))
				.Select(isf
					=> isf.isFlag
					? (isf.flag as Enum)?.Describe((parameter?.ToString()) ?? Format)
					: isf.flag?.ToString()
				);
			if (AsArray) {
				return names;
			}
			return string.Join(Delimiter, names);
		}
		return AvaloniaProperty.UnsetValue;
	}

	/// <summary>
	/// Not Implemented.
	/// </summary>
	/// <exception cref="NotImplementedException"></exception>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotImplementedException();
}