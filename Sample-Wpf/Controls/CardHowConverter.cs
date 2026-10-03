using Sample_Shared.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace Sample_Wpf.Controls {
	/// <summary>
	///     A card's How (SPEC: How each card is made): from the page view model, the card's
	///     key and its <see cref="DemoCard.MoreKeys"/>, the files it is made of.
	/// </summary>
	public class CardHowConverter : IMultiValueConverter {
		public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
			=> values is [PageViewModel page, string key, var moreKeys] ? page.How(key, moreKeys as string) : null;

		public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
			=> throw new NotSupportedException();
	}
}
