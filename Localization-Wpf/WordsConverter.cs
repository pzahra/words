using System;
using System.Globalization;
using System.Windows.Data;

namespace PatTech.Localization.Wpf {
	[ValueConversion(typeof(object), typeof(string), ParameterType = typeof(string))]
	public class WordsConverter(IWords? words = null) : IValueConverter {
		private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (parameter is null) {
				logger.Warn("WORDS: ConverterParameter not specified.");
				var text = value?.ToString() ?? "";
				if (text.Length > 20) {
					text = text[..20];
				}
				return $"#{text}#";
			}
			else if (parameter is string key) {
				return Format(words ?? Words.Known, value, key, culture);
			}
			else {
				var text = parameter.ToString() ?? "";
				if (text.Length > 20) {
					text = text[..20];
				}
				logger.Warn($"WORDS: ConverterParameter expecting string, found `{text}`");
				return $"#{text}#";
			}
		}
		public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotSupportedException();
		}

		public static string? Format(IWords words, object? value, string key, CultureInfo culture) {
			if (value is Array) {
				return string.Format(culture, words[key], (object[])value);
			}
			else {
				return Words.FormatByName(culture, words[key], value);
			}
		}
	}
}
