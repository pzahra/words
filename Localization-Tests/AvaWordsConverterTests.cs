using System.Globalization;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The Avalonia <see cref="WordsConverter"/> twin of <see cref="WordsConverterTests"/>:
/// it formats with the culture the binding hands it — Avalonia passes the binding's
/// ConverterCulture, else CurrentCulture. Explicit words and an explicit culture, no
/// globals touched.
/// </summary>
public class AvaWordsConverterTests {

	private static readonly IWords Templates = WordsBuilder.Create()
		.Load(new StringReader("value-en=English\n\n[n]\nvalue={0}\n\n[since]\nvalue={When:d}\n"))
		.ToWords("en");

	[Fact]
	public void Convert_PositionalArgs_FormatWithTheCultureHandedToIt() {
		var converter = new WordsConverter(Templates);

		Assert.Equal("1,5", converter.Convert(new object[] { 1.5 }, typeof(string), "n", new CultureInfo("de-DE")));
		Assert.Equal("1.5", converter.Convert(new object[] { 1.5 }, typeof(string), "n", new CultureInfo("en-US")));
	}

	[Fact]
	public void Convert_NamedPlaceholders_FormatWithTheCultureHandedToIt() {
		var converter = new WordsConverter(Templates);
		var value = new { When = new DateTime(2026, 9, 11) };

		Assert.Equal("11.09.2026", converter.Convert(value, typeof(string), "since", new CultureInfo("de-DE")));
		Assert.Equal("9/11/2026", converter.Convert(value, typeof(string), "since", new CultureInfo("en-US")));
	}
}
