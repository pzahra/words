using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The parameters a key uses, and the translation check (editor SPEC:
///     Parameters → Defined by hand, found to help; Translation check), read as the
///     runtime renders and then formats the words.
/// </summary>
public class ParameterUseTests {
	private static Dictionary<string, WordsKey> Keys(string ini) {
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(ini));
		return consumer.WordKeys.ToDictionary();
	}

	private static FoundParameters InDefault(Dictionary<string, WordsKey> keys, string key, string? language = null)
		=> ParameterUse.InDefault(keys[key], new DefaultWordsProvider(keys, []), language);

	private static FoundParameters InLanguage(Dictionary<string, WordsKey> keys, string key, string code)
		=> ParameterUse.InLanguage(keys[key], code, new LanguageWordsProvider(keys, code, []));

	private static ParameterMismatch Check(Dictionary<string, WordsKey> keys, string key, string code, string? defaultLanguage = null)
		=> ParameterUse.Check(keys[key], code, new DefaultWordsProvider(keys, []), defaultLanguage, new LanguageWordsProvider(keys, code, []));

	[Fact]
	public void TheDefault_FindsWhatItPrintsAndCounts_ReferencesAndFormsExpanded() {
		var keys = Keys("""
			[word]
			value=word
			value#other={4} words

			[ref]
			value=and {3}

			[k]
			value={Name} {0} {0:N2} {1#word} {{{{2}} {>ref} {{5}
			value#other={6,-4:N1} {01}
			""");

		FoundParameters found = InDefault(keys, "k");

		Assert.Equal(["0", "1", "3", "4", "5", "6", "Name"], found.Names);
		Assert.Equal(["1"], found.Counted);
		Assert.False(found.Unfollowed);
	}

	[Fact]
	public void AnEscape_CollapsesBeforeStringFormatReadsTheRest() {
		var keys = Keys("""
			[k]
			value={{0} {{{{1}} {{2#word}
			""");

		//{{0} renders {0}, which string.Format fills; {{{{1}} renders {{1}}, a brace pair;
		//{{2#word} renders {2#word}, no selector and no item
		Assert.Equal(["0"], InDefault(keys, "k").Names);
		//as the runtime reads them, a bug the finder follows (runtime SPEC: One escape for a brace)
		IWords words = WordsBuilder.Create().LoadString("[k]\nvalue={{0} {{{{1}}\n").ToWords("en");
		Assert.Equal("zero {1}", words.Format("k", "zero", "one"));
	}

	[Fact]
	public void ANamedCount_AConstant_ARelativeReference_AndACircle_AreFollowedAsWordsFollowsThem() {
		var keys = Keys("""
			[$unit]
			value={7}

			[k]
			value={Count#.word} {$unit} {>.sub} {>k}

			[.word]
			value=one
			value#other=many

			[.sub]
			value={8} {>k}
			""");

		FoundParameters found = InDefault(keys, "k");

		Assert.Equal(["7", "8", "Count"], found.Names);
		Assert.Equal(["Count"], found.Counted);
		Assert.False(found.Unfollowed); //a circle is cut, not missed
	}

	[Fact]
	public void AReferenceOrASelectorTheWordsDoNotHave_FindsNothing_AndSaysSo() {
		var keys = Keys("""
			[k]
			value={0} {>elsewhere.key} {1#elsewhere.word}
			""");

		FoundParameters found = InDefault(keys, "k");

		Assert.Equal(["0", "1"], found.Names);
		Assert.True(found.Unfollowed);
		Assert.True(InDefault(Keys("[k]\nvalue={$nowhere}\n"), "k").Unfollowed);
	}

	[Fact]
	public void ASelector_ReadsTheFormsItsLanguageCountsBy() {
		var keys = Keys("""
			value=!en
			value-mt=Malti

			[word]
			value=word
			value#other=words
			value-mt=kelma
			value-mt#few={2} kelmiet

			[k]
			value={0#word}
			value-mt={0#word}
			""");

		Assert.Equal(["0"], InDefault(keys, "k", "en").Names);   //English has no few form
		Assert.Equal(["0", "2"], InLanguage(keys, "k", "mt").Names);
	}

	[Fact]
	public void ATranslationWithFormsAndNoPlainValue_ReadsThePlainValueItFallsBackTo() {
		var keys = Keys("""
			[k]
			value={0} files in {1}
			value-mt#few={0} fajls
			""");

		Assert.Equal(["0", "1"], InLanguage(keys, "k", "mt").Names); //{1} from the default's plain value
	}

	[Fact]
	public void ATranslation_DropsWhatTheDefaultUses_AndAddsWhatNeitherItNorADefinitionNames() {
		var keys = Keys("""
			[dropped]
			value={0} files
			value-fr=des fichiers

			[extra]
			value={0} files
			value-fr={0} fichiers de {1}

			[both]
			value={0} files in {Folder}
			value-fr={1} fichiers
			""");

		Assert.Equal(new ParameterMismatch(["0"], []), Check(keys, "dropped", "fr"), new MismatchComparer());
		Assert.Equal(new ParameterMismatch([], ["1"]), Check(keys, "extra", "fr"), new MismatchComparer());
		Assert.Equal(new ParameterMismatch(["0", "Folder"], ["1"]), Check(keys, "both", "fr"), new MismatchComparer());
	}

	[Fact]
	public void OnlyUseIsCompared_NotHowNorInWhichForm() {
		var keys = Keys("""
			value=!en
			value-mt=Malti

			[file]
			value=file
			value#other=files

			[counts]
			value={0} {0#file}
			value-ja={0}個のファイル

			[forms]
			value=a file
			value#other={0} files
			value-fr=un fichier
			value-fr#other={0} fichiers

			[format]
			value={0:D}
			value-fr={0:d}
			""");

		Assert.False(Check(keys, "counts", "ja").Any); //a language whose words do not change with the count prints
		Assert.False(Check(keys, "forms", "fr").Any);  //a one form reading "un fichier"
		Assert.False(Check(keys, "format", "fr").Any);
	}

	[Fact]
	public void ADefinition_WidensWhatATranslationMayUse() {
		var keys = Keys("""
			value=!en
			value-mt=Malti

			[word]
			value=word
			value-mt=kelma
			value-mt#few=kelmiet

			[k]
			value=Words
			value-mt={0} {0#word}
			param-0=the count

			[undefined]
			value=Words
			value-mt={2}
			param-0=the count
			""");

		Assert.False(Check(keys, "k", "mt").Any);
		Assert.Equal(["2"], Check(keys, "undefined", "mt").Extra);
	}

	[Fact]
	public void AReferenceATranslationPrintsThrough_IsAUse() {
		var keys = Keys("""
			[count]
			value={0} files
			value-fr={0} fichiers

			[k]
			value={0} files
			value-fr={>count} trouvés
			""");

		Assert.False(Check(keys, "k", "fr").Any);
	}

	[Fact]
	public void ASideWithAReferenceItCannotFollow_MayCarryAnything() {
		var keys = Keys("""
			[translated]
			value={0} files
			value-fr={>elsewhere.files}

			[defaulted]
			value={>elsewhere.files}
			value-fr={0} fichiers
			""");

		Assert.False(Check(keys, "translated", "fr").Any); //nothing dropped from a translation with one
		Assert.False(Check(keys, "defaulted", "fr").Any);  //nothing extra beside a default with one
	}

	[Fact]
	public void AConstant_OrATranslationWithoutWords_IsChecked_ForNothing() {
		var keys = Keys("""
			[$brand]
			value={0}

			[missing]
			value={0} files
			value-fr=
			comment-fr=to do
			""");

		Assert.Same(ParameterMismatch.None, Check(keys, "$brand", "fr"));
		Assert.Same(ParameterMismatch.None, Check(keys, "missing", "fr"));
		Assert.Same(ParameterMismatch.None, Check(keys, "missing", "de"));
	}

	private sealed class MismatchComparer : IEqualityComparer<ParameterMismatch> {
		public bool Equals(ParameterMismatch? x, ParameterMismatch? y)
			=> x is not null && y is not null && x.Dropped.SequenceEqual(y.Dropped) && x.Extra.SequenceEqual(y.Extra);
		public int GetHashCode(ParameterMismatch obj) => 0;
	}
}
