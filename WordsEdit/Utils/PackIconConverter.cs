using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace WordsEdit.Utils;

/// <summary>
///     A <see cref="PackIconKind"/> as a fresh <see cref="PackIcon"/>: a menu
///     item's Icon is an element, and one element cannot sit in two menus.
/// </summary>
public sealed class PackIconConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is PackIconKind kind ? new PackIcon { Kind = kind } : null;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
