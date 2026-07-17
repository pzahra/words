using Avalonia.Data.Converters;
using System.Globalization;

namespace PatTech.Localization.Avalonia;

/// <summary>
/// Provides value conversion for localization by substituting values into localized string templates using a specified
/// key and culture information.
/// </summary>
/// <remarks>Supports culture-aware formatting and substitution of values into localized templates. Use this
/// converter to integrate localization into data binding scenarios, such as WPF or other XAML-based frameworks. Thread
/// safety depends on the underlying IWords implementation.</remarks>
/// <param name="words">An object that provides localized string templates for formatting.</param>
/// <param name="logger">An interface for passing on logging instructions to the caller.</param>
public class WordsConverter(IWords? words = null, ITakeException? logger = null) : IValueConverter {
	private readonly ITakeException logger = logger ?? ITakeException.Dummy;

	/// <summary>
	/// Inserts the provided value into the Words specified by the key provided through ConverterParameter.
	/// </summary>
	/// <param name="value">The value to localize.</param>
	/// <param name="targetType"></param>
	/// <param name="parameter">The key to the Words entry.</param>
	/// <param name="culture">Used for numeric formatting rules; doesn't change the selected Words language.</param>
	/// <returns>A localized string.</returns>
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

	/// <summary>
	/// Not Implemented.
	/// </summary>
	/// <exception cref="NotSupportedException"></exception>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();

	/// <summary>
	/// Formats a localized string using the specified value and key, applying culture-specific formatting rules.
	/// </summary>
	/// <remarks>If the value parameter is an array, each element is substituted into the template using string
	/// formatting. Otherwise, the value is formatted by Property name via reflection. The method supports
	/// culture-aware formatting for both templates and values.</remarks>
	/// <param name="words">An object that provides localized string templates for formatting.</param>
	/// <param name="value">The value or array of values to substitute into the localized string template. May be null.</param>
	/// <param name="key">The key identifying the localized string template to use for formatting.</param>
	/// <param name="culture">The culture information used to format the string and values according to locale-specific conventions.</param>
	/// <returns>A formatted string with values substituted into the localized template, or null if formatting fails.</returns>
	public static string? Format(IWords words, object? value, string key, CultureInfo culture) {
		if (value is Array) {
			return string.Format(culture, words[key], (object[])value);
		}
		else {
			return Words.FormatByName(culture, words[key], value);
		}
	}
}
