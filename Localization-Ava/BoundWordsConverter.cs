using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     The converter behind a bound <c>{l:Words}</c> (SPEC: Live language switching).
///     Without an inner converter the bound value is a key, looked up in
///     <see cref="Words.Known"/>; with one, the inner converter runs with the parameter and
///     culture it came with. Live, it sits on a multi-binding whose first leg is the wrapped
///     binding, stripped of its converter, and whose second is <see cref="WordsTickle"/>: a
///     multi-binding never re-runs a leg's own converter when a sibling changes, so the
///     converter is hoisted here, where a pulse runs it again.
/// </summary>
internal sealed class BoundWordsConverter : IValueConverter, IMultiValueConverter {
	private readonly IValueConverter? inner;
	private readonly object? innerParameter;
	private readonly CultureInfo? innerCulture;

	/// <summary>A key lookup, or <paramref name="converter"/> with what it was given.</summary>
	public BoundWordsConverter(IValueConverter? converter = null, object? parameter = null, CultureInfo? culture = null) {
		inner = converter;
		innerParameter = parameter;
		innerCulture = culture;
	}

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> Apply(value, targetType, culture);

	public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
		=> Apply(values.Count > 0 ? values[0] : null, targetType, culture);

	private object? Apply(object? value, Type targetType, CultureInfo culture) {
		//the source has not resolved, or failed: let the binding fall back, as a lone binding would
		if (value == AvaloniaProperty.UnsetValue || value is BindingNotification) {
			return AvaloniaProperty.UnsetValue;
		}
		if (inner is not null) {
			return inner.Convert(value, targetType, innerParameter, innerCulture ?? culture);
		}
		return value?.ToString() is { Length: > 0 } key ? Words.Known[key] : null;
	}

	/// <summary>Not supported; localization is a one-way trip.</summary>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
