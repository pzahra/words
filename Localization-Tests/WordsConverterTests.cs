using System.Globalization;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The WPF <see cref="WordsConverter"/> formats with the culture the binding hands it,
/// as any converter does — WPF passes the target element's Language, which the WPF
/// Digest flag repoints. Explicit words and an explicit culture, no globals touched.
/// <see cref="AvaWordsConverterTests"/> is the twin.
/// </summary>
public class WordsConverterTests {

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
