using PatTech.Localization.Authoring;
using System.Globalization;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     Plural forms on the authoring side (editor SPEC: Plural forms, Order): the
///     reader keeps them and gripes about the ones a runtime never reads, the writer
///     puts each after its plain value, a save is stable, and copies, a recode and
///     the preview providers carry them.
/// </summary>
public class PluralFormsAuthoringTests {
	private static WordsParserToLocalizationProvider Read(string ini) {
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(ini));
		return consumer;
	}

	private static string Save(WordsSession session, WordsFile file) {
		var output = new StringWriter();
		session.Save(file, KeyTree.Build(session, file), output);
		return output.ToString();
	}

	private const string Maltese = @"value=!en
value-en=English
value-mt=Malti

[word]
value=Word
value#other=Words
value-mt=Kelma
value-mt#two=Kelmtejn
value-mt#few=Kelmiet
value-mt#other=Kelma

";

	[Fact]
	public void Reader_KeepsEachForm_LowercasedAndContinued() {
		var consumer = Read(@"value=!en
value-en=English
value-mt=Malti

[word]
value=Word
value#other=Words
value-mt=Kelma
value-mt#two=Kelmtejn
value-mt#FEW=Kel_
miet
value-mt#other=Kelma
");

		Assert.Empty(consumer.Errors);
		WordsKey word = consumer.WordKeys["word"];
		Assert.Equal("Word", word.DefaultValue);
		Assert.Equal(new Dictionary<string, string> { ["other"] = "Words" }, word.Forms);
		Assert.Equal("Kelma", word.Entries["mt"].Value);
		Assert.Equal(new Dictionary<string, string> { ["two"] = "Kelmtejn", ["few"] = "Kelmiet", ["other"] = "Kelma" }, word.Entries["mt"].Forms);
		Assert.Empty(word.Entries["en"].Forms);
	}

	[Fact]
	public void Reader_GripesAboutAFormOnAnyLanguageLabel() {
		var consumer = Read("value=!en\nvalue-fr=Français\ncomment-fr=French\ncomment-fr#other=Frenches\n\n[word]\nvalue=Word\n");

		Assert.Equal("French", consumer.KnownLanguages["fr"].EnglishName);
		Assert.StartsWith("comment-fr#other at the top of the file: a language label has no plural forms", Assert.Single(consumer.Errors));
	}

	[Fact]
	public void Reader_GripesAboutAValueDeclaredAgain_WhoseLastOneWins() {
		var consumer = Read("value=!en\nvalue-fr=Français\n\n[word]\nvalue=first\nvalue-fr#other=un\nvalue=second\ncontext=a\ncontext=b\n\n[word]\nvalue-fr#other=deux\n");

		Assert.Equal("second", consumer.WordKeys["word"].DefaultValue);
		Assert.Equal("deux", consumer.WordKeys["word"].Entries["fr"].Forms["other"]);
		Assert.Equal(["word: value is declared again, and the last one wins; Save writes only it",
			"word: value-fr#other is declared again, and the last one wins; Save writes only it"], consumer.Errors);
	}

	[Fact]
	public void Reader_GripesAboutFormsARuntimeNeverReads_AndDropsWhatIsNoForm() {
		var consumer = Read(@"value=!en
value-en=English
value-ja=日本語
value-pl=Polski
value-pl#other=Polskie

[word]
value=Word
value#one=Word too
value#few=Words, few
value-ja#other=単語
value-pl#other=słowa
value-pl#lots=dużo_
słów
context#other=no form here

[word#few]
value=a key named like a form
");

		WordsKey word = consumer.WordKeys["word"];
		//what is a CLDR category is kept, read or not, so nothing written is out of reach
		Assert.Equal(new Dictionary<string, string> { ["one"] = "Word too", ["few"] = "Words, few" }, word.Forms);
		Assert.Equal(new Dictionary<string, string> { ["other"] = "単語" }, word.Entries["ja"].Forms);
		Assert.Equal(new Dictionary<string, string> { ["other"] = "słowa" }, word.Entries["pl"].Forms); //the continuation of #lots went with it
		Assert.Equal("Polski", consumer.KnownLanguages["pl"].NativeName);

		Assert.Collection(consumer.Errors,
			error => Assert.StartsWith("value-pl#other at the top of the file: a language label has no plural forms", error),
			error => Assert.StartsWith("word: value#one is kept, but a runtime ignores it", error),
			error => Assert.StartsWith("word: value#few is kept, but 'en' counts no whole number as few (one, other)", error),
			error => Assert.StartsWith("word: value-ja#other is kept, but 'ja' has one form", error),
			error => Assert.StartsWith("word: value-pl#other is kept, but 'pl' counts no whole number as other (one, few, many)", error),
			error => Assert.StartsWith("word: value-pl#lots names no plural category", error),
			error => Assert.Contains("unrecognized `word.context#other-`", error),
			error => Assert.StartsWith("[word#few]: 'word#few' is no key name, so a runtime skips it", error));
	}

	[Fact]
	public void Reader_ChecksTheDefaultsFormsAgainstTheDefaultsLanguage_EnglishWithoutOne() {
		Assert.Empty(Read("value=!mt\nvalue-mt=Malti\n\n[word]\nvalue=Kelma\nvalue#two=Kelmtejn\n").Errors);

		string error = Assert.Single(Read("value-mt=Malti\n\n[word]\nvalue=Kelma\nvalue#two=Kelmtejn\n").Errors);
		Assert.StartsWith("word: value#two is kept, but 'en' counts no whole number as two", error);
	}

	[Fact]
	public void Writer_PutsEachFormAfterItsPlainValue_InCldrOrder() {
		var word = new WordsKey("F.word") { DefaultValue = "Word" };
		word.Forms["other"] = "Words";
		word.Forms["zero"] = "No words";
		word.Forms["few"] = ""; //an empty form is none
		word.Forms["lots"] = "x"; //no category, not written
		var mt = new WordsEntry { Value = "Kelma", Stale = "2026-10-04" };
		mt.Forms["other"] = "Kelma";
		mt.Forms["few"] = "Kelmiet";
		mt.Forms["two"] = "Kelmtejn";
		word.Entries["mt"] = mt;
		var it = new WordsEntry();
		it.Forms["other"] = "Parole"; //a form without its plain value
		word.Entries["it"] = it;

		var output = new StringWriter();
		using (var writer = new IniWriter(output)) {
			writer.WriteBlock(word);
		}

		Assert.Equal(@"[word]
value=Word
value#zero=No words
value#other=Words
value-mt=Kelma
value-mt#two=Kelmtejn
value-mt#few=Kelmiet
value-mt#other=Kelma
stale-mt=2026-10-04
value-it#other=Parole

".ReplaceLineEndings(), output.ToString());
	}

	[Fact]
	public void SaveLoadSave_KeepsEveryForm_ByteForByte() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(Maltese.ReplaceLineEndings()), "Main.ini");
		Assert.Empty(file.Errors);
		Assert.Equal(Maltese.ReplaceLineEndings(), Save(session, file));

		//a form long enough to fold comes back whole, and the fold is the fixed point
		string longer = string.Join(' ', Enumerable.Repeat("Kelmiet twal ħafna,", 12));
		session.Keys["Main.word"].Entries["mt"].Forms["few"] = longer;
		string first = Save(session, file);
		Assert.Contains("value-mt#few=Kelmiet", first);
		Assert.Contains("_" + Environment.NewLine, first);

		var again = new WordsSession();
		WordsFile reloaded = again.Load(new StringReader(first), "Main.ini");
		Assert.Empty(reloaded.Errors);
		Assert.Equal(longer, again.Keys["Main.word"].Entries["mt"].Forms["few"]);
		Assert.Equal(first, Save(again, reloaded));
	}

	[Fact]
	public void Copies_CarryTheForms_AndAFormIsWords() {
		var word = new WordsKey("F.word");
		word.Forms["other"] = "Words";
		var entry = new WordsEntry();
		entry.Forms["few"] = "Kelmiet";
		word.Entries["mt"] = entry;

		var copy = new WordsKey(word);
		copy.Forms["other"] = "changed";
		copy.Entries["mt"].Forms["few"] = "changed";
		Assert.Equal("Words", word.Forms["other"]);
		Assert.Equal("Kelmiet", entry.Forms["few"]);
		Assert.Equal("Kelmiet", new WordsEntry(entry).Forms["few"]);

		//a form alone keeps a key and an entry from being empty; an empty form does not
		Assert.False(word.IsEmpty());
		Assert.False(entry.IsEmpty());
		var blank = new WordsEntry();
		blank.Forms["few"] = "";
		Assert.True(blank.IsEmpty());
		var bare = new WordsKey("F.bare");
		bare.Forms["other"] = "";
		Assert.True(bare.IsEmpty());
	}

	[Fact]
	public void Shift_CarriesTheForms_AndParksAClashingOne() {
		var adopted = new WordsKey("F.adopted");
		var british = new WordsEntry();
		british.Forms["other"] = "Colours";
		adopted.Entries["en-GB"] = british;
		adopted.Entries["en"] = new WordsEntry();

		var filled = new WordsKey("F.filled");
		filled.Entries["en"] = new WordsEntry { Value = "Colour" };
		filled.Entries["en-GB"] = new WordsEntry { Value = "Colour" };
		filled.Entries["en-GB"].Forms["other"] = "Colours";

		var clashing = new WordsKey("F.clashing");
		clashing.Entries["en"] = new WordsEntry { Value = "Color" };
		clashing.Entries["en"].Forms["other"] = "Colors";
		clashing.Entries["en-GB"] = new WordsEntry { Value = "Color" };
		clashing.Entries["en-GB"].Forms["other"] = "Colours";
		clashing.Entries["en-GB"].Forms["zero"] = "No colours";

		WordsOperations.Shift([adopted, filled, clashing], "en-GB", "en");

		Assert.Same(british, adopted.Entries["en"]); //an empty target adopts the entry whole
		Assert.Equal("Colours", filled.Entries["en"].Forms["other"]);
		Assert.Null(filled.Entries["en"].Stale);
		WordsEntry target = clashing.Entries["en"];
		Assert.Equal("Colors", target.Forms["other"]); //the target's wins
		Assert.Equal("No colours", target.Forms["zero"]); //a form it lacked fills in
		Assert.Equal("#other: Colours", target.Context);
		Assert.NotNull(target.Stale);
		Assert.All(new[] { adopted, filled, clashing }, key => Assert.False(key.Entries.ContainsKey("en-GB")));
	}

	[Fact]
	public void Providers_ReadAFormAsTheRuntimeFlattensIt_SoACountSelects() {
		var word = new WordsKey("A.word") { DefaultValue = "Word" };
		word.Forms["other"] = "Words";
		word.Entries["en"] = new WordsEntry { Value = "Word (en)" };
		word.Entries["en"].Forms["other"] = "Words (en)";
		word.Entries["en-GB"] = new WordsEntry();
		word.Entries["mt"] = new WordsEntry { Value = "Kelma" };
		word.Entries["mt"].Forms["few"] = "Kelmiet";
		word.Entries["fil"] = new WordsEntry();
		Dictionary<string, WordsKey> keys = new() { ["A.word"] = word };

		var defaults = new DefaultWordsProvider(keys, ["A"]);
		Assert.Equal("Words", defaults["word#other"]);
		Assert.True(defaults.ContainsKey("word"));
		Assert.False(defaults.ContainsKey("word#few"));
		Assert.False(defaults.ContainsKey("missing#other"));

		var british = new LanguageWordsProvider(keys, "en-GB", ["A"]);
		Assert.Equal("Words (en)", british["word#other"]); //no words of its own: the family's

		var untranslated = new LanguageWordsProvider(keys, "fil", ["A"]);
		Assert.Equal("Words", untranslated["word#other"]); //no words anywhere: the default's

		var maltese = new LanguageWordsProvider(keys, "mt", ["A"]);
		Assert.Equal("Kelmiet", maltese["word#few"]);
		Assert.False(maltese.ContainsKey("word#other")); //its own words keep the default's forms out

		//so a dictionary over the preview provider picks by count, as an app would
		var words = new CulturedWords(maltese, CultureInfo.GetCultureInfo("mt"));
		Assert.Equal("Kelma", words["word", 1]);
		Assert.Equal("Kelmiet", words["word", 2]); //two reads few
		Assert.Equal("Kelmiet", words["word", 5]);
		Assert.Equal("Kelma", words["word", 11]); //many reads other, and then its own plain value
	}

	[Fact]
	public void FormatSample_SelectsByTheSamples_InTheDictionarysLanguage() {
		var word = new WordsKey("A.word") { DefaultValue = "Word" };
		word.Forms["other"] = "Words";
		word.Entries["mt"] = new WordsEntry { Value = "Kelma" };
		word.Entries["mt"].Forms["few"] = "Kelmiet";
		var count = new WordsKey("A.count") { DefaultValue = "{0} {0#word}, {Name}" };
		count.Parameters.Add(new WordsParameter("0", WordsParameterType.All[1], "4"));
		count.Parameters.Add(new WordsParameter("Name", WordsParameterType.String, "Pat"));
		Dictionary<string, WordsKey> keys = new() { ["A.word"] = word, ["A.count"] = count };

		Assert.Equal("4 Words, Pat", WordsOperations.FormatSample(new CulturedWords(new DefaultWordsProvider(keys, ["A"]), CultureInfo.InvariantCulture), count));
		var maltese = CultureInfo.GetCultureInfo("mt");
		Assert.Equal("4 Kelmiet, Pat", WordsOperations.FormatSample(new CulturedWords(new LanguageWordsProvider(keys, "mt", ["A"]), maltese), count, maltese));
	}
}
