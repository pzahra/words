using System.Globalization;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Plural forms (SPEC: Plural forms): CLDR's whole-number rules as a table, a key's
/// forms as <c>value-xx#form</c> entries beside its value, the <c>{0#word}</c> selector
/// in the Format family and the count indexer. The selector warns through
/// <see cref="Words.Logger"/>, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class PluralFormsTests {

	private const string Ini =
		"value=!en\n" +
		"value-en=English\n" +
		"value-ru=Русский\n" +
		"value-ja=日本語\n" +
		"value-fr=Français\n" +
		"\n" +
		"[word]\n" +
		"value=word\n" +
		"value#other=words\n" +
		"value-ru=слово\n" +
		"value-ru#few=слова\n" +
		"value-ru#many=слов\n" +
		"value-ja=単語\n" +
		"value-fr=mot\n" +
		"value-fr#other=mots\n" +
		"\n" +
		"[files]\n" +
		"value={0} {0#word}\n" +
		"\n" +
		"[named]\n" +
		"value={Count} {Count#word}\n" +
		"\n" +
		"[self]\n" +
		"value={0} {0#self}\n" +
		"value#other={0} selves\n" +
		"\n" +
		"[relative]\n" +
		"value={0} {0#.n}\n" +
		"[.n]\n" +
		"value=item\n" +
		"value#other=items\n" +
		"\n" +
		"[box]\n" +
		"value=box\n" +
		"value#other=boxes\n" +
		"value-ru=коробка\n" +
		"value-ru#other=коробки\n" +
		"\n" +
		"[solo]\n" +
		"value=solo\n" +
		"value-ru=соло\n" +
		"\n" +
		"[thing]\n" +
		"value=thing\n" +
		"value#other=things\n" +
		"value-ru=вещь\n" +
		"\n" +
		"[report]\n" +
		"value={0#sentence}\n" +
		"[sentence]\n" +
		"value=one file\n" +
		"value#other={0} files in {>place}\n" +
		"[place]\n" +
		"value=the box\n" +
		"\n" +
		"[nested]\n" +
		"value={0#outer}\n" +
		"[outer]\n" +
		"value=one outer\n" +
		"value#other={0} outer {0#word}\n" +
		"\n" +
		"[braces]\n" +
		"value={{{{0#word}}\n";

	private static readonly string DefaultBrand = char.ConvertFromUtf32(0x1F4DA); // 📚

	private static IWords In(string language, bool debug = false)
		=> WordsBuilder.Create().Load(new StringReader(Ini)).Debug(debug).ToWords(language);

	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	// ---- the table ----

	[Theory]
	[InlineData("en", 1, "one")]
	[InlineData("en", 0, "other")]
	[InlineData("en", 2, "other")]
	[InlineData("fr", 0, "one")]
	[InlineData("fr", 1, "one")]
	[InlineData("fr", 2, "other")]
	[InlineData("fr", 1_000_000, "many")]
	[InlineData("it", 0, "other")]
	[InlineData("it", 1_000_000, "many")]
	[InlineData("es", 2_000_000, "many")]
	[InlineData("pt", 0, "one")]
	[InlineData("pt-PT", 0, "other")]
	[InlineData("pt-BR", 0, "one")]
	[InlineData("ru", 21, "one")]
	[InlineData("ru", 22, "few")]
	[InlineData("ru", 11, "many")]
	[InlineData("ru", 112, "many")]
	[InlineData("pl", 22, "few")]
	[InlineData("pl", 21, "many")]
	[InlineData("ar", 0, "zero")]
	[InlineData("ar", 3, "few")]
	[InlineData("ar", 11, "many")]
	[InlineData("ar", 102, "other")]
	[InlineData("cy", 6, "many")]
	[InlineData("cy", 4, "other")]
	[InlineData("lv", 11, "zero")]
	[InlineData("lv", 21, "one")]
	[InlineData("he", 2, "two")]
	[InlineData("he", 20, "other")]
	[InlineData("ro", 101, "few")]
	[InlineData("ro", 120, "other")]
	[InlineData("mt", 2, "two")]
	[InlineData("mt", 102, "other")]
	[InlineData("ja", 1, "other")]
	[InlineData("zh-Hant-TW", 1, "other")]
	public void Select_FollowsCurrentCldr(string language, int number, string expected) {
		Assert.Equal(expected, PluralRules.Select(language, number));
	}

	[Fact]
	public void Select_IgnoresTheSign_CountsWholeValuesAsWhole_AndFractionsAsOther() {
		Assert.Equal("one", PluralRules.Select("en", -1));
		Assert.Equal("one", PluralRules.Select("en", 1.0m));
		Assert.Equal("other", PluralRules.Select("fr", 1.5m));
		Assert.Equal("one", PluralRules.Select("ru", 1_000_000_000_000_001m)); //the low digits still decide
		Assert.Equal("many", PluralRules.Select("fr", 3_000_000_000_000_000m));
	}

	[Fact]
	public void AnUnknownLanguageHasOnlyOther_AndTheInvariantCultureCountsAsEnglish() {
		Assert.Equal(["other"], PluralRules.Categories("xx"));
		Assert.Equal("other", PluralRules.Select("xx", 1));
		Assert.Equal(["one", "other"], PluralRules.Categories(""));
		Assert.Equal("one", PluralRules.Select("", 1));
	}

	[Theory]
	[InlineData("ja")]
	[InlineData("hi")]
	[InlineData("en")]
	[InlineData("it")]
	[InlineData("fr")]
	[InlineData("is")]
	[InlineData("fil")]
	[InlineData("tzm")]
	[InlineData("lv")]
	[InlineData("ksh")]
	[InlineData("he")]
	[InlineData("shi")]
	[InlineData("ro")]
	[InlineData("sr")]
	[InlineData("gd")]
	[InlineData("sl")]
	[InlineData("cs")]
	[InlineData("pl")]
	[InlineData("ru")]
	[InlineData("lt")]
	[InlineData("br")]
	[InlineData("mt")]
	[InlineData("ga")]
	[InlineData("gv")]
	[InlineData("kw")]
	[InlineData("ar")]
	[InlineData("cy")]
	public void Categories_AreWhatWholeNumbersReach_InCldrOrder(string language) {
		var numbers = Enumerable.Range(0, 10_001).Select(n => (long)n)
			.Concat(Enumerable.Range(1, 2_100).Select(k => k * 1000L));
		HashSet<string> reached = [.. numbers.Select(n => PluralRules.Select(language, n))];

		Assert.Equal(PluralRules.Names.Where(reached.Contains), PluralRules.Categories(language));
	}

	// ---- the file ----

	[Fact]
	public void AFormIsAnEntryBesideItsKeysValue_CasingNormalized_ContinuationsAppended() {
		var builder = WordsBuilder.Create().LoadString(
			"value-ru=Русский\n\n[word]\nvalue-RU#FEW=сло_\nва\nvalue#other=words\n");

		Assert.Equal("слова", builder.Flatten("ru")["word#few"]);
		Assert.Equal("words", builder.Flatten("")["word#other"]);
		Assert.Equal(["ru"], builder.GetLanguages().Select(language => language.Key));
	}

	[Fact]
	public void FormsThatAreNone_AreWarnedAboutAndLeftOut() {
		var logger = new CaptureLogger();
		var builder = WordsBuilder.Create(logger).LoadString(
			"value-en=English\nvalue-en#other=Englishes\n\n[word]\nvalue#one=word\nvalue#plural=words\n\n[odd#key]\nvalue=x\n");
		var words = builder.Flatten("");

		Assert.False(words.ContainsKey("word#one")); //the plain value is the one form
		Assert.False(words.ContainsKey("word#plural"));
		Assert.False(builder.Flatten("en").ContainsKey("#other")); //a label has no forms
		Assert.Equal(3, logger.Messages.Count(message => message.StartsWith("WP:FORM:")));
		Assert.Contains(logger.Messages, message => message == "WP:NAME:`odd#key`");
	}

	// ---- the selector and the count indexer ----

	[Theory]
	[InlineData(1, "1 word", "1 слово")]
	[InlineData(2, "2 words", "2 слова")]
	[InlineData(5, "5 words", "5 слов")]
	[InlineData(11, "11 words", "11 слов")]
	[InlineData(21, "21 words", "21 слово")]
	[InlineData(22, "22 words", "22 слова")]
	[InlineData(25, "25 words", "25 слов")]
	[InlineData(101, "101 words", "101 слово")]
	public void TheSelectorSplicesTheFormItsCountPicks_InEachLanguage(int count, string english, string russian) {
		Assert.Equal(english, In("en").Format("files", count));
		Assert.Equal(russian, In("ru").Format("files", count));
	}

	[Fact]
	public void AMissingFormFallsToOther_ThenToThePlainValue() {
		var ru = In("ru");

		Assert.Equal("коробки", ru["box", 2]); //no few: its other
		Assert.Equal("соло", ru["solo", 5]);   //no forms at all: its plain value
		Assert.Equal("mots", In("fr")["word", 1_000_000]); //French counts a million as many
	}

	[Fact]
	public void ATranslationNeverBorrowsTheDefaultsForms_ItsOwnPlainValueStandsIn() {
		var ru = In("ru", debug: true);

		Assert.Equal("вещь", ru["thing", 5]); //not the default's "things", branded
		Assert.Equal("вещь", ru["thing", 1]);
		Assert.False(WordsBuilder.Create().LoadString(Ini).Flatten("ru").ContainsKey("thing#other"));
	}

	[Fact]
	public void AKeyWithNoWordsInTheLanguage_TakesTheDefaultsForms_Branded() {
		Assert.Equal(DefaultBrand + "items", In("ru", debug: true)["relative.n", 5]);
		Assert.Equal(DefaultBrand + "item", In("ru", debug: true)["relative.n", 1]);
	}

	[Fact]
	public void TheFamilysWordsKeepTheDefaultsFormsOut_AsALanguagesOwnDo() {
		var builder = WordsBuilder.Create().LoadString(
			"value=!en\nvalue-fr=Français\nvalue-fr-CA=Français (Canada)\n\n[word]\nvalue=word\nvalue#other=words\nvalue-fr=mot\n\n[file]\nvalue=file\nvalue#other=files\nvalue-fr=fichier\nvalue-fr#other=fichiers\n");
		var canadian = builder.ToWords("fr-CA");

		Assert.Equal("mot", canadian["word", 2]);
		Assert.Equal("fichiers", canadian["file", 2]); //the family's own forms do flatten in
	}

	[Fact]
	public void ALanguageWithOneCategory_SpeaksOnlyItsPlainValue() {
		Assert.Equal("2 単語", In("ja").Format("files", 2));
		Assert.Equal("item", In("ja")["relative.n", 2]); //even where the default's forms flatten in
	}

	// ---- optional categories ----

	[Fact]
	public void OptionalCategories_ReadLikeAnother_ByLanguage() {
		Assert.Equal(new Dictionary<string, string> { ["two"] = "few", ["many"] = "other", ["other"] = "one" }, PluralRules.Optional("mt"));
		Assert.Equal(new Dictionary<string, string> { ["two"] = "other" }, PluralRules.Optional("he"));
		Assert.Equal(new Dictionary<string, string> { ["many"] = "other" }, PluralRules.Optional("pt-PT"));
		Assert.Equal(new Dictionary<string, string> { ["many"] = "other" }, PluralRules.Optional("br")); //exact millions, as French
		Assert.Empty(PluralRules.Optional("en"));
		Assert.Empty(PluralRules.Optional("ar")); //a true dual for every noun
		Assert.Empty(PluralRules.Optional(""));
	}

	[Theory]
	[InlineData("mt")]
	[InlineData("he")]
	[InlineData("iw")]
	[InlineData("br")]
	[InlineData("ca")]
	[InlineData("es")]
	[InlineData("fr")]
	[InlineData("it")]
	[InlineData("lld")]
	[InlineData("pt")]
	[InlineData("pt-PT")]
	[InlineData("scn")]
	[InlineData("vec")]
	public void AnOptionalCategory_AndWhatItReads_AreTheLanguagesOwn(string language) {
		var categories = PluralRules.Categories(language);
		Assert.NotEmpty(PluralRules.Optional(language));
		Assert.All(PluralRules.Optional(language), pair => {
			Assert.Contains(pair.Key, categories);
			Assert.Contains(pair.Value, categories);
			Assert.True(pair.Key != "other" || pair.Value == "one", "other may only read the plain value, where else every form ends up");
		});
	}

	[Theory]
	[InlineData(1, "1 fajl")]
	[InlineData(2, "2 fajls")]  //two reads few
	[InlineData(5, "5 fajls")]
	[InlineData(11, "11 fajl")] //many reads other, and Maltese's other reads its plain value
	[InlineData(20, "20 fajl")]
	public void AMissingOptionalForm_ReadsTheFormItStandsFor(int count, string expected) {
		var maltese = WordsBuilder.Create().LoadString(
			"value=!en\nvalue-mt=Malti\n\n[file]\nvalue=file\nvalue#other=files\nvalue-mt=fajl\nvalue-mt#few=fajls\n\n[files]\nvalue={0} {0#file}\n").ToWords("mt");

		Assert.Equal(expected, maltese.Format("files", count));
	}

	[Fact]
	public void AWrittenOptionalForm_IsReadAsAnyOther() {
		var maltese = WordsBuilder.Create().LoadString(
			"value=!en\nvalue-mt=Malti\n\n[year]\nvalue=year\nvalue#other=years\nvalue-mt=sena\nvalue-mt#two=sentejn\nvalue-mt#few=snin\n").ToWords("mt");

		Assert.Equal("sentejn", maltese["year", 2]); //a word that keeps its dual
		Assert.Equal("snin", maltese["year", 3]);
	}

	[Fact]
	public void ANamedSelectorPicksAsANumberedOneDoes() {
		var ru = In("ru");

		Assert.Equal(ru.Format("files", 5), ru.FormatByName("named", new { Count = 5 }));
		Assert.Equal("22 слова", ru.FormatParams("named", new { Count = 22 }));
	}

	[Fact]
	public void ALanguageDotNetDoesNotKnow_StillCountsByItsOwnRule() {
		//.NET turns ceb and iw into the invariant culture, which counts as English; the
		//dictionary keeps the code it was built for, and the code picks
		var builder = WordsBuilder.Create().LoadString(
			"value=!en\nvalue-ceb=Cebuano\nvalue-iw=Ivrit\n\n" +
			"[file]\nvalue=file\nvalue#other=files\nvalue-ceb=file\nvalue-ceb#other=mga file\nvalue-iw=kovetz\nvalue-iw#two=kvatzim shnayim\nvalue-iw#other=kvatzim\n\n" +
			"[files]\nvalue={0} {0#file}\n");

		var cebuano = builder.ToWords("ceb");
		Assert.Equal("", cebuano.UICulture.Name);
		Assert.Equal("ceb", cebuano.Language);
		Assert.Equal("5 file", cebuano.Format("files", 5));   //Cebuano's one takes 5, where English's other would
		Assert.Equal("4 mga file", cebuano.Format("files", 4));
		Assert.Equal("kvatzim shnayim", builder.ToWords("iw")["file", 2]); //a dual, by Hebrew's legacy code
	}

	[Fact]
	public void ADictionaryThatDoesNotSay_CountsByItsCulture() {
		Assert.Equal("mt", new CulturedWords(WordsProvider.Empty(), CultureInfo.GetCultureInfo("mt")).Language);
		Assert.Equal("", new CulturedWords(WordsProvider.Empty(), CultureInfo.InvariantCulture).Language);
		Assert.Equal(CultureInfo.CurrentUICulture.Name, ((IWords)new EchoWords()).Language);
	}

	[Fact]
	public void ADictionaryOfValuesSelectsByName_AndByNumber() {
		var ru = In("ru");

		Assert.Equal(ru.FormatByName("named", new { Count = 5 }), ru.FormatByName(null, "named", new Dictionary<string, object?> { ["Count"] = 5 }));
		Assert.Equal(ru.Format("files", 22), ru.FormatByName(null, "files", new Dictionary<string, object?>(), 22));
	}

	[Fact]
	public void FormKey_NamesTheEntryACountInAFormReads() {
		var maltese = WordsBuilder.Create().LoadString(
			"value=!en\nvalue-mt=Malti\n\n[file]\nvalue=file\nvalue#other=files\nvalue-mt=fajl\nvalue-mt#few=fajls\n").Flatten("mt");

		Assert.Equal("file#few", Words.FormKey(maltese, "mt", "file", "few"));
		Assert.Equal("file#few", Words.FormKey(maltese, "mt", "file", "two"));  //optional, reads few
		Assert.Equal("file", Words.FormKey(maltese, "mt", "file", "many"));     //reads other, which Maltese left to its plain value
		Assert.Equal("file", Words.FormKey(maltese, "mt", "file", "one"));
		Assert.Equal("file", Words.FormKey(maltese, "ja", "file", "few"));      //one category: the plain value alone
	}

	[Fact]
	public void TheIndexerKeepsTheSelector_TheCountIndexerReadsTheKeysOwnForm() {
		var ru = In("ru");

		Assert.Equal("{0} {0#word}", ru["files"]);
		Assert.Equal("слова", ru["word", 3]);
		Assert.Equal("слово", ru["word", 1]);
		Assert.Equal("words", In("en")["word", 0]);
		Assert.Equal("#nothing#", ru["nothing", 2]);
	}

	[Fact]
	public void AKeyDoesNotSelectAmongItsOwnForms() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;

		Assert.Equal("2 # ∞ #", In("en").Format("self", 2));
		Assert.Contains(logger.Messages, message => message.StartsWith("WORDS:CIRC:`self`"));
	}

	[Fact]
	public void FormsMayReferAndFormat_AndSelectInTurn() {
		var en = In("en");

		Assert.Equal("3 items", en.Format("relative", 3));               //{0#.n} is relative to the block
		Assert.Equal("3 files in the box", en.Format("report", 3));      //a form's {>key} and {0}
		Assert.Equal("one file", en.Format("report", 1));
		Assert.Equal("2 outer words", en.Format("nested", 2));           //a spliced form selects too
		Assert.Equal("{0#word}", en.Format("braces", 2));                //a {{ pair stays for string.Format
	}

	[Fact]
	public void EveryFormattingPathSelects() {
		var en = In("en");

		Assert.Equal("2 words", en.FormatParams("files", new object[] { 2 }));
		Assert.Equal("2 words", en.FormatParams("files", new[] { 2 }));
		Assert.Equal("{0} {0#word}", en.FormatParams("files", null)); //no arguments, the text as it is
		Assert.Equal("5 words", en.ConvertValue(5, "files", null));   //a converter's bound value is {0}
		Assert.Equal(" words", en.ConvertValue(null, "files", null)); //not there yet: other, quietly
		Assert.Equal("2 words", en.RenderKey("files", [2]));            //the console's path
		Assert.Equal("2 words", en.RenderText("{0} {0#word}", null, [2]));
	}

	[Fact]
	public void ANonNumericArgumentPicksOtherAndWarns_AMissingOneThrowsAsStringFormatWould() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = In("en");

		Assert.Equal("lots words", en.Format("files", "lots"));
		Assert.Contains(logger.Messages, message => message.StartsWith("WORDS:COUNT:`lots`"));
		Assert.Throws<FormatException>(() => en.Format("files"));
	}
}
