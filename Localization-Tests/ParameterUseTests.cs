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

	private static bool Follows(Dictionary<string, WordsKey> keys, string key, string code)
		=> ParameterUse.Follows(keys[key], code, new LanguageWordsProvider(keys, code, []));

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

		Assert.Equal(["0", "1", "3", "4", "6", "Name"], found.Names); //{{{{2}} and {{5} are braces
		Assert.Equal(["1"], found.Counted);
		Assert.False(found.Unfollowed);
	}

	[Fact]
	public void AnEscapedBrace_AndAMalformedOne_HoldNoUse_AsTheRuntimeShowsThem() {
		const string Ini = "[fmt]\nvalue=N2\n\n[k]\nvalue={{0} {{{{1}} {{2#word} {3:{>fmt}}\n";
		var keys = Keys(Ini);

		//until the runtime read each brace once, {{0} rendered {0} and was filled, and a
		//reference inside {3:…} formatted it while the finder saw no {3}
		Assert.Empty(InDefault(keys, "k").Names);
		IWords words = WordsBuilder.Create().LoadString(Ini).ToWords("en");
		Assert.Equal("{0} {{1}} {2#word} {3:{>fmt}}", words.Format("k", "zero", "one", "two", 3));
	}

	//caught where words are written (runtime SPEC: One pass for every brace): a file
	//that loads with one gripes, naming the key and the field, and still loads
	[Fact]
	public void ABraceThatOpensNothing_IsFoundValueByValue_AndGripedOnLoad() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader("""
			value=!en
			value-it=Italiano
			value-mt=Malti

			[$json]
			value={ "a": 1 }

			[k]
			value={0:{>fmt}} {{0} and {{
			value#other={0,x}
			context-it=a { b
			value-it={ e { e {}
			value-mt=tajjeb {0}

			"""), "T");

		Assert.Equal([
			"k: value holds `{0:{>fmt}}`, a brace that opens nothing, shown as written in the app; {{ writes a brace",
			"k: value#other holds `{0,x}`, a brace that opens nothing, shown as written in the app; {{ writes a brace",
			"k: value-it holds `{`, `{}`, braces that open nothing, shown as written in the app; {{ writes a brace"], file.Errors);
		Assert.Equal("{0:{>fmt}} {{0} and {{", session.Keys["T.k"].DefaultValue); //loaded all the same
		Assert.Empty(ParameterUse.Malformed(session.Keys["T.$json"])); //a constant's words are text
		Assert.Empty(ParameterUse.Malformed("a } b {{ c { Name }")); //a closing brace is itself, {{ one, and a name may stand in spaces
		Assert.Equal(["{"], ParameterUse.Malformed("{0 and {"));     //each once
	}

	[Fact]
	public void ANamedCount_ARelativeReference_AndACircle_AreFollowedAsWordsFollowsThem_AndAConstantIsText() {
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

		Assert.Equal(["8", "Count"], found.Names);                 //$unit's {7} is text, as the runtime shows it
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
		Assert.False(InDefault(Keys("[k]\nvalue={$nowhere}\n"), "k").Unfollowed); //a constant is text, and hides no parameter
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

	//which keys an edit to another checks again (editor SPEC: Parameters → Translation
	//check): those whose check reads another key's words, read as the check reads them
	[Fact]
	public void ACheck_FollowsAnotherKey_WhenTheWordsItReadsBringOneIn() {
		var keys = Keys("""
			[target]
			value={0} left
			value-fr=il reste {0}

			[default]
			value=Note: {>target}
			value-fr=Note

			[form]
			value=file
			value#other={0#target}
			value-fr=fichier

			[translation]
			value=Note
			value-fr#other={>target}

			[fallback]
			value=Note
			value-fr=Note : {>target}
			value-fr-CA#other=Notes

			[text]
			value={{>target}} {$unit}
			value-fr=texte

			[missing]
			value=Note: {>target}
			value-fr=
			comment-fr=to do
			""");

		Assert.True(Follows(keys, "default", "fr"));
		Assert.True(Follows(keys, "form", "fr"));
		Assert.True(Follows(keys, "translation", "fr"));
		Assert.True(Follows(keys, "fallback", "fr-CA")); //its forms read the plain value French gives it
		Assert.False(Follows(keys, "target", "fr"));
		Assert.False(Follows(keys, "text", "fr"));       //an escaped brace and a constant bring in text
		Assert.False(Follows(keys, "missing", "fr"));    //no words, so nothing to check again
	}

	[Fact]
	public void TheSlots_AreTheDefinitionsInTheirOrder_ThenWhatIsFoundAndUndefined() {
		// a definition the default does not use is still a slot, marked unused; one
		// found and not defined takes the type guessed, int for a count
		var keys = Keys("""
			[word]
			value=word

			[k]
			value={Name} {01} {2} {3#word}
			param-1=real:the share
			param-9=the spare
			param-Name=the name
			""");
		WordsKey key = keys["k"];

		IReadOnlyList<ParameterSlot> slots = ParameterUse.Slots(key, InDefault(keys, "k"));

		Assert.Equal([
			("1", WordsParameterType.Real, true, true),
			("9", WordsParameterType.Str, true, false),
			("Name", WordsParameterType.Str, true, true),
			("2", WordsParameterType.Str, false, true),
			("3", WordsParameterType.Int, false, true),
		], slots.Select(slot => (slot.Name, slot.Type, slot.Definition is not null, slot.Used)));
		Assert.Same(key.Parameters[0], slots[0].Definition);
	}

	[Theory]
	[InlineData("0", true)]
	[InlineData("12", true)]
	[InlineData("Count", true)]
	[InlineData("_x1", true)]
	[InlineData("1x", false)]
	[InlineData("two words", false)]
	[InlineData("", false)]
	[InlineData("{0}", false)]
	public void AName_IsANumberOrAnIdentifier(string name, bool valid) => Assert.Equal(valid, ParameterUse.IsName(name));

	[Fact]
	public void TwoNames_AreTheSame_AsStringFormatReadsThem() {
		Assert.True(ParameterUse.SameName("1", "01"));
		Assert.False(ParameterUse.SameName("1", "10"));
		Assert.False(ParameterUse.SameName("Count", "count"));
	}

	private sealed class MismatchComparer : IEqualityComparer<ParameterMismatch> {
		public bool Equals(ParameterMismatch? x, ParameterMismatch? y)
			=> x is not null && y is not null && x.Dropped.SequenceEqual(y.Dropped) && x.Extra.SequenceEqual(y.Extra);
		public int GetHashCode(ParameterMismatch obj) => 0;
	}
}
