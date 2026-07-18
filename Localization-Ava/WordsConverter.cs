using Avalonia.Data.Converters;
using System.Globalization;

namespace PatTech.Localization.Avalonia;

/// <summary>
/// Binds a runtime value into a localized string template, selected by the Words key given
/// as ConverterParameter.
/// </summary>
/// <remarks>Supports culture-aware formatting and substitution of values into localized
/// templates. Use this converter to integrate localization into data binding scenarios.
/// Thread safety depends on the underlying <see cref="IWords"/> implementation.</remarks>
/// <param name="words">The Words to draw templates from; <see langword="null"/> to use <see cref="Words.Known"/>.</param>
/// <param name="logger">An interface for passing on logging instructions to the caller.</param>
public class WordsConverter(IWords? words = null, ITakeException? logger = null) : IValueConverter {
	private readonly ITakeException logger = logger ?? ITakeException.Dummy;

	/// <summary>
	/// Inserts the provided value into the Words specified by the key provided through ConverterParameter.
	/// </summary>
	/// <remarks>ConverterParameter must be a string key; a missing or non-string parameter
	/// logs a warning and yields the bound value itself, hash-wrapped and truncated to 20
	/// characters (<c>#value#</c>), so the mistake shows up on screen.</remarks>
	/// <param name="value">The value to localize.</param>
	/// <param name="targetType">Ignored; the result is always a string.</param>
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
	/// Not supported; localization is a one-way trip.
	/// </summary>
	/// <exception cref="NotSupportedException">Always.</exception>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();

	/// <summary>
	/// Formats a localized string using the specified value and key, applying culture-specific formatting rules.
	/// </summary>
	/// <remarks>If <paramref name="value"/> is an array, each element is substituted into the
	/// template as a positional <c>{0}</c>-style argument. Otherwise, the value fills
	/// <c>{Name}</c>-style placeholders by field/property name via reflection (see
	/// <see cref="Words.FormatByName(IFormatProvider?, string, object?, object?[])"/>).</remarks>
	/// <param name="words">The Words to draw the template from.</param>
	/// <param name="value">The value or array of values to substitute into the localized string template. May be null.</param>
	/// <param name="key">The key identifying the localized string template to use for formatting.</param>
	/// <param name="culture">The culture information used to format the string and values according to locale-specific conventions.</param>
	/// <returns>A formatted string with values substituted into the localized template.</returns>
	public static string? Format(IWords words, object? value, string key, CultureInfo culture) {
		if (value is Array) {
			return string.Format(culture, words[key], (object[])value);
		}
		else {
			return Words.FormatByName(culture, words[key], value);
		}
	}
}
