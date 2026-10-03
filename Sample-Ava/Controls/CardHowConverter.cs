using Avalonia.Data.Converters;
using Sample_Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sample_Ava.Controls {
	/// <summary>
	///     A card's How (SPEC: How each card is made): from the page view model, the card's
	///     key and its <see cref="DemoCard.MoreKeys"/>, the files it is made of.
	/// </summary>
	public class CardHowConverter : IMultiValueConverter {
		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
			=> values is [PageViewModel page, string key, var moreKeys] ? page.How(key, moreKeys as string) : null;
	}
}
