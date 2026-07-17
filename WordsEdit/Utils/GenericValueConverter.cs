using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace WordsEdit.Utils;

[ContentProperty(nameof(Conditions))]
public class GenericValueConverter : DependencyObject, IValueConverter {
	public static readonly DependencyProperty ElseProperty =
		DependencyProperty.Register(nameof(Else), typeof(object), typeof(GenericValueConverter), new PropertyMetadata(null));

	public object Else {
		get => GetValue(ElseProperty);
		set => SetValue(ElseProperty, value);
	}

	public ObservableCollection<ConverterCondition> Conditions { get; } = [];

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		foreach (var condition in Conditions) {
			if (IsMatch(value, condition.When, culture))
				return condition.Then;
		}

		return Else;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

	static bool IsMatch(object value, object when, CultureInfo culture) {
		if (ReferenceEquals(value, when))
			return true;

		if (value is null || when is null)
			return value is null && when is null;

		if (when is string whenStr) {
			var valueStr = System.Convert.ToString(value, culture);
			if (string.Equals(valueStr, whenStr, StringComparison.Ordinal))
				return true;
			if (string.Equals(valueStr, whenStr, StringComparison.CurrentCultureIgnoreCase))
				return true;
		}

		try {
			var convertedWhen = System.Convert.ChangeType(when, value.GetType(), culture);
			if (Equals(value, convertedWhen))
				return true;
		} catch {  }

		try {
			var convertedValue = System.Convert.ChangeType(value, when.GetType(), culture);
			if (Equals(convertedValue, when))
				return true;
		} catch { }

		return Equals(value, when);
	}
}

public class ConverterCondition : DependencyObject {
	public static readonly DependencyProperty WhenProperty =
		DependencyProperty.Register(nameof(When), typeof(object), typeof(ConverterCondition), new PropertyMetadata(null));

	public object When {
		get => GetValue(WhenProperty);
		set => SetValue(WhenProperty, value);
	}

	public static readonly DependencyProperty ThenProperty =
		DependencyProperty.Register(nameof(Then), typeof(object), typeof(ConverterCondition), new PropertyMetadata(null));

	public object Then {
		get => GetValue(ThenProperty);
		set => SetValue(ThenProperty, value);
	}
}