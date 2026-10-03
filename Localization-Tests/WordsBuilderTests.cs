using System.Globalization;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// <see cref="WordsBuilder.Digest(string)"/> is the one-call startup — build and
/// install as <see cref="Words.Known"/> — and <see cref="WordsBuilder.Debug"/> is the
/// fluent switch that brands fallback values. Digest swaps the Words.Known global
/// and the thread cultures, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class WordsBuilderTests {

	private const string Ini =
		"value-en=English\n" +
		"value-en-GB=British\n" +
		"value-de=Deutsch\n" +
		"\n" +
		"[k]\n" +
		"value=default\n" +
		"value-en=English value\n";

	// the fallback brands, spelled as code points so the source stays ASCII
	private static readonly string FamilyBrand = char.ConvertFromUtf32(0x1F56E);  // 🕮
	private static readonly string DefaultBrand = char.ConvertFromUtf32(0x1F4DA); // 📚

	[Fact]
	public void Digest_InstallsKnown_AndAppliesCulture() {
		var originalKnown = Words.Known;
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
		var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
		try {
			var words = WordsBuilder.Create().Load(new StringReader(Ini)).Digest("de");

			Assert.Same(words, Words.Known);            // installed, not just built
			Assert.Equal("default", Words.Known["k"]); // and resolving
			var expected = CultureInfo.CreateSpecificCulture("de");
			Assert.Equal(expected, CultureInfo.CurrentCulture);   // the setter's culture side-effect,
			Assert.Equal(expected, CultureInfo.CurrentUICulture); // now a named, deliberate step
		}
		finally {
			Words.Known = originalKnown;
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			CultureInfo.DefaultThreadCurrentCulture = originalDefault;
			CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
		}
	}

	[Fact]
	public void Digest_OutLanguages_HandsBackTheMenu() {
		var originalKnown = Words.Known;
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
		var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
		try {
			WordsBuilder.Create().Load(new StringReader(Ini)).Digest("en", out var languages);

			Assert.Equal(
				new[] { ("en", "English"), ("en-GB", "British"), ("de", "Deutsch") },
				languages.Select(l => (l.Key, l.Value)).ToArray());
		}
		finally {
			Words.Known = originalKnown;
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			CultureInfo.DefaultThreadCurrentCulture = originalDefault;
			CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
		}
	}

	/// <summary>A language the system is not in, so the two cultures can be told apart.</summary>
	private static string NotTheSystemsLanguage
		=> Words.SystemCulture.TwoLetterISOLanguageName == "de" ? "fr" : "de";

	[Fact]
	public void UseSystemNumbers_KeepsSystemFormatting_WordsFollowTheLanguage() {
		var originalKnown = Words.Known;
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
		var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
		try {
			var lang = NotTheSystemsLanguage;
			var words = WordsBuilder.Create().Load(new StringReader(Ini)).UseSystemNumbers().Digest(lang);

			Assert.Same(words, Words.Known);
			var language = CultureInfo.CreateSpecificCulture(lang);
			Assert.Equal(language, CultureInfo.CurrentUICulture);                 // the text's language...
			Assert.Equal(language, CultureInfo.DefaultThreadCurrentUICulture);
			Assert.Equal(Words.SystemCulture, CultureInfo.CurrentCulture);        // ...the system's numbers
			Assert.Equal(Words.SystemCulture, CultureInfo.DefaultThreadCurrentCulture);
		}
		finally {
			Words.Known = originalKnown;
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			CultureInfo.DefaultThreadCurrentCulture = originalDefault;
			CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
		}
	}

	[Fact]
	public void UseSystemNumbers_IsOffByDefault_AndSwitchesBackOff() {
		var originalKnown = Words.Known;
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
		var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
		try {
			var lang = NotTheSystemsLanguage;
			var language = CultureInfo.CreateSpecificCulture(lang);

			WordsBuilder.Create().Load(new StringReader(Ini)).Digest(lang);
			Assert.Equal(language, CultureInfo.CurrentCulture); // off by default: numbers in the language
			WordsBuilder.Create().Load(new StringReader(Ini)).UseSystemNumbers().UseSystemNumbers(false).Digest(lang);
			Assert.Equal(language, CultureInfo.CurrentCulture); // and back off again
		}
		finally {
			Words.Known = originalKnown;
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			CultureInfo.DefaultThreadCurrentCulture = originalDefault;
			CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
		}
	}

	[Fact]
	public void ToWords_BuildsWithoutInstalling() {
		var originalKnown = Words.Known;

		var words = WordsBuilder.Create().Load(new StringReader(Ini)).ToWords("de");

		Assert.NotSame(words, Words.Known);
		Assert.Same(originalKnown, Words.Known);
	}

	[Fact]
	public void Debug_BrandsFallbackValues() {
		var wb = WordsBuilder.Create().Load(new StringReader(Ini)).Debug();

		// en-GB has no `k` of its own: it falls back to the en family
		Assert.Equal(FamilyBrand + "English value", wb.ToWords("en-GB")["k"]);
		// de has no `k` at all: it falls back to the default
		Assert.Equal(DefaultBrand + "default", wb.ToWords("de")["k"]);
	}

	[Fact]
	public void Debug_LeavesConstantsUnbranded() {
		// a constant is language-less by design, so it has nothing to fall back from;
		// branded, an image base or a unit spliced into a value would be corrupted
		var wb = WordsBuilder.Create().LoadString(
			"value-de=Deutsch\n" +
			"[$base]\nvalue=avares://App/Assets/\n" +
			"[k]\nvalue=![pic]({$base}pic.png)\n").Debug();
		var words = wb.ToWords("de");

		Assert.Equal("avares://App/Assets/", words["$base"]);
		Assert.Equal(DefaultBrand + "![pic](avares://App/Assets/pic.png)", words["k"]); // the value is still branded
	}

	[Fact]
	public void Debug_IsOffByDefault_AndSwitchesBackOff() {
		var wb = WordsBuilder.Create().Load(new StringReader(Ini));

		Assert.Equal("default", wb.ToWords("de")["k"]);                      // off by default
		Assert.Equal("default", wb.Debug().Debug(false).ToWords("de")["k"]); // and back off again
	}

	private sealed class Gripes : ITakeException {
		public List<string> Warnings { get; } = [];
		public void Warn(string text) => Warnings.Add(text);
		public void Error(Exception exception, string message) => Warnings.Add(message);
	}

	[Fact]
	public void Load_StackedFile_RelabelsALanguageSilently_ButWarnsOnAClobberedKey() {
		// a subordinate file declares its languages with !labels and the host lists
		// them: the relabel is the stacking pattern, not a clobbered key
		var gripes = new Gripes();
		var wb = WordsBuilder.Create(gripes)
			.Load(new StringReader("value-en=!English\n\n[k]\nvalue=lib\n\n[lib.only]\nvalue=L\n"))
			.Load(new StringReader("value-en=English\n\n[k]\nvalue=host\n"));

		string gripe = Assert.Single(gripes.Warnings);
		Assert.StartsWith("WB:KOVR:`k`", gripe);
		Assert.Equal("host", wb.ToWords("en")["k"]);
		Assert.Equal("L", wb.ToWords("en")["lib.only"]);
		Assert.Equal(["en"], wb.GetLanguages().Select(language => language.Key));
		Assert.Equal("English", wb.GetLanguages().Single().Value); //the host's label wins
	}
}
