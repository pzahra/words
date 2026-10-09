using System.Globalization;
using System.Numerics;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     Where a reference and a selector resolve (SPEC: Plural forms, Where it
///     resolves): each against the key whose text it was written in, a form's text
///     being its key's, along one recursion path; what FormatByName reads a name
///     from; and what counts as a count.
/// </summary>
[Collection("Words globals")]
public class RenderScopeTests {
	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	private const string Ini = """
		value=!en
		value-ru=Русский

		[sentence]
		value={0} file in {>.place}
		value#other={0} files in {>.place}

		[.place]
		value=the box {>.lid}

		[.place.lid]
		value=with a lid

		[picks]
		value={0#sentence}

		[forced]
		value={>sentence#other}

		[nested]
		value={>relative}

		[.n]
		value=WRONG
		value#other=WRONGS

		[relative]
		value={0} {0#.n}

		[.n]
		value=item
		value#other=items

		[files]
		value={0} {0#files}
		value#other={0} files

		[refers]
		value={>files}

		[item]
		value=item
		value#other={>item}s

		[named]
		value={Count} {Count#relative.n}

		[blank]
		value=[{Name}]

		[file]
		value=file
		value#other=files
		value-ru=файл
		value-ru#few=файла
		value-ru#many=файлов
		value-ru#other=файла

		""";

	private static IWords In(string language) => WordsBuilder.Create().LoadString(Ini).ToWords(language);

	[Fact]
	public void AFormsRelativeReference_ResolvesAgainstItsKey_AndChainsOnFromThere() {
		var en = In("en");

		//1.4.0's forms read #sentence#other.place#
		Assert.Equal("2 files in the box with a lid", en.Format("picks", 2));
		Assert.Equal("1 file in the box with a lid", en.Format("picks", 1));
		Assert.Equal("{0} files in the box with a lid", en["sentence", 2]);           //the count indexer too
		Assert.Equal("{0} files in the box with a lid", en["forced"]);                //and a form referred to by its entry
	}

	[Fact]
	public void AFormLookedUpByItsEntry_ResolvesAgainstItsKey_AsOneReferredToDoes() {
		var en = In("en");

		//1.5.0's first cut read #sentence#other.place#
		Assert.Equal("{0} files in the box with a lid", en["sentence#other"]);
		Assert.Equal("{0} files in the box with a lid", en.RenderKey("sentence#other"));
		Assert.Equal("{0} files in the box with a lid", Words.RenderKey(en.Provider, "sentence#other"));
		Assert.Equal("2 files in the box with a lid", en.Format("sentence#other", 2));
		Assert.Equal("2 files in the box with a lid", en.RenderKey("sentence#other", [2]));
	}

	[Fact]
	public void AReferencesSelectors_PickFromTheKeyTheyWereWrittenIn() {
		var en = In("en");

		Assert.Equal("2 items", en.Format("nested", 2)); //not nested.n's WRONGS
		Assert.Equal("1 item", en.Format("nested", 1));
	}

	[Fact]
	public void ReferringToAKeyThatSelectsItsOwnForms_IsCircular_ButAFormMayReferToItsPlainValue() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = In("en");

		Assert.Equal("2 # ∞ #", en.Format("refers", 2));
		Assert.Contains(logger.Messages, message => message.StartsWith("WORDS:CIRC:`files`"));
		Assert.Equal("items", en["item", 2]);
		Assert.Equal("items", en.RenderText("{0#item}", null, [2]));
	}

	[Fact]
	public void RenderTexts_TextIsTheCallers_SoItMayReferToItsBaseKey_WithArgumentsAsWithout() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = In("en");

		//1.5.0's first cut took the text for baseKey's own words, given arguments: # ∞ #
		Assert.Equal("file: 3", en.RenderText("{>file}: {0}", "file", [3]));
		Assert.Equal("file: 3", en.RenderText("{>file}: 3", "file"));
		Assert.Equal("2 items", en.RenderText("{>nested}", "relative", [2])); //by way of another key
		Assert.Equal("3 files", en.RenderText("{0} {0#file}", "file", [3]));   //and its forms
		Assert.Empty(logger.Messages);
	}

	[Fact]
	public void AnEscapedSelector_IsNoSelector() {
		var en = WordsBuilder.Create().LoadString("[word]\nvalue=word\nvalue#other=words\n\n[escaped]\nvalue={{0#word}\n").ToWords("en");

		Assert.Equal("{0#word}", en["escaped"]);
		Assert.Throws<FormatException>(() => en.Format("escaped", 2)); //string.Format sees a lone brace, as for {{>key}
	}

	[Fact]
	public void FormatByName_WithNoValue_FillsTheNamesWithNothing() {
		var en = In("en");

		//a bare null bound to 1.5.0's dictionary overload, which threw
		Assert.Equal("[]", en.FormatByName(CultureInfo.InvariantCulture, "blank", null));
		Assert.Equal("[]", en.FormatByName("blank", null));
		Assert.Equal("[]", en.ConvertValue(null, "blank", null));
	}

	[Fact]
	public void FormatByName_ReadsADictionaryByItsValues_NeverItsOwnMembers() {
		var en = In("en");

		//the dictionary's own Count is 1, which picked 'item'
		Assert.Equal("5 items", en.FormatByName("named", new Dictionary<string, object?> { ["Count"] = 5 }));
		Assert.Equal("5 items", en.FormatByName(null, "named", new Dictionary<string, object?> { ["Count"] = 5 }));
		Assert.Equal("5 items", en.FormatByName("named", new Dictionary<string, int> { ["Count"] = 5 }));
		Assert.Equal("5 items", en.FormatByName("named", (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Count"] = 5 }));
		Assert.Equal("7 met", Words.FormatByName("{Count} met", new Dictionary<string, object?> { ["Count"] = 7 }));
		Assert.Equal("#Count# met", Words.FormatByName("{Count} met", new Dictionary<string, object?>()));
	}

	[Fact]
	public void FormatByName_1_4_0DictionaryOverloads_ReadAsTheObjectOnes() {
		//the signatures a caller built against 1.4.0 binds to, a null included
		Func<string, IReadOnlyDictionary<string, object?>?, object?[], string> format = Words.FormatByName;
		Func<IFormatProvider?, string, IReadOnlyDictionary<string, object?>?, object?[], string> formatIn = Words.FormatByName;
		Func<string, IReadOnlyDictionary<string, object?>?, object?[], (string, object?[])> preFormat = Words.PreFormatByName;
		var values = new Dictionary<string, object?> { ["Count"] = 7 };

		Assert.Equal("7 met {0}", format("{Count} met {{0}}", values, []));
		Assert.Equal("7 met Sam", formatIn(null, "{Count} met {0}", values, ["Sam"]));
		Assert.Equal(" met", format("{Count} met", null, []));
		Assert.Equal(Words.PreFormatByName("{Count}", (object?)values).FormatArgs, preFormat("{Count}", values, []).Item2);
		Assert.Equal("{1:}", preFormat("{Count}", null, []).Item1);
	}

	//a dictionary of one's own whose indexer dresses the words up
	private sealed class Shouting(IWords inner) : IWords {
		public IWordsProvider Provider => inner.Provider;
		public string this[string key] => inner[key].ToUpperInvariant();
		public bool ContainsKey(string key) => inner.ContainsKey(key);
		public bool TryGetValue(string key, out string value) => inner.TryGetValue(key, out value!);
		public void SetCulture() { }
	}

	[Fact]
	public void RenderKey_ReadsTheWordsAsItDoesWithoutArguments() {
		var shouting = new Shouting(WordsBuilder.Create().LoadString("[count]\nvalue={0} plain\n").ToWords("en"));

		Assert.Equal("{0} plain", Words.RenderKey(shouting, "count"));
		Assert.Equal("5 plain", Words.RenderKey(shouting, "count", [5])); //was 5 PLAIN, through the indexer
		Assert.Equal("#NONE#", shouting.Format("none", 5));                //a key the provider lacks still reads through it
	}

	//a dictionary of one's own that answers keys its provider lacks
	private sealed class Virtual(IWords inner) : IWords {
		public IWordsProvider Provider => inner.Provider;
		public string Language => inner.Language;
		public string this[string key] => key switch {
			"virtual" => "{0} {0#file}, {0#.n} {{braces}}",
			"named" => "{Count} {Count#file}",
			_ => inner[key],
		};
		public bool ContainsKey(string key) => inner.ContainsKey(key);
		public bool TryGetValue(string key, out string value) => inner.TryGetValue(key, out value!);
		public void SetCulture() { }
	}

	[Fact]
	public void WordsAnIndexerOfOnesOwnAnswers_StillSelect() {
		var words = new Virtual(WordsBuilder.Create().LoadString("[file]\nvalue=file\nvalue#other=files\n\n[virtual.n]\nvalue=item\nvalue#other=items\n").ToWords("en"));

		//1.5.0's first cut handed string.Format the selectors, which threw
		Assert.Equal("2 files, items {braces}", words.Format("virtual", 2)); //a {{ pair is string.Format's
		Assert.Equal("1 file, item {braces}", words.Format("virtual", 1));
		Assert.Equal("2 files, items {braces}", words.FormatParams("virtual", new object[] { 2 }));
		Assert.Equal("3 files", words.FormatByName("named", new { Count = 3 }));
		Assert.Equal("{0} {0#file}, {0#.n} {{braces}}", words["virtual"]); //no arguments: as it is
	}

	public static TheoryData<object, string> Counts => new() {
		{ 1, "1 файл" },
		{ 1.0, "1 файл" },
		{ (Half)1, "1 файл" },
		{ (nint)21, "21 файл" },
		{ (Int128)22, "22 файла" },
		{ (UInt128)25, "25 файлов" },
		{ BigInteger.Pow(10, 40) + 1, $"{BigInteger.Pow(10, 40) + 1} файл" }, //past decimal, the low digits pick
		{ 1e30, $"{1e30} файлов" },
		{ 1.0000001f, $"{1.0000001f} файла" },                                 //a fraction is other, though decimal rounds it whole
		{ 1.0000000000000002, $"{1.0000000000000002} файла" },
	};

	[Theory]
	[MemberData(nameof(Counts))]
	public void AnyNumberCounts_AndAFractionIsOther(object count, string expected) {
		var ru = WordsBuilder.Create().LoadString(Ini + "[counted]\nvalue={0} {0#file}\n").ToWords("ru");

		Assert.Equal(expected, ru.Format(CultureInfo.InvariantCulture, "counted", count));
	}

	[Fact]
	public void TheRuntimesDefaultLanguage_IsTrimmedAndCased() {
		var builder = WordsBuilder.Create().LoadString("value=!EN-gb \nvalue-en-GB=English\n\n[k]\nvalue=x\n").Debug();

		Assert.Equal("en-GB", builder.DefaultLanguage);
		Assert.Equal("x", builder.Flatten("en-GB")["k"]); //the default speaks it: unbranded
	}

	[Theory]
	[InlineData("blo", 0, "zero")]
	[InlineData("blo", 1, "one")]
	[InlineData("blo", 2, "other")]
	[InlineData("cv", 0, "zero")]
	[InlineData("kok", 0, "one")]
	[InlineData("kok", 2, "other")]
	[InlineData("sgs", 1, "one")]
	[InlineData("sgs", 2, "two")]
	[InlineData("sgs", 3, "few")]
	[InlineData("sgs", 11, "other")]
	[InlineData("sgs", 21, "one")]
	[InlineData("sgs", 22, "few")]
	[InlineData("sgs", 0, "other")]
	public void TheTableHasCldrsLatest(string language, int count, string category) {
		Assert.Equal(category, PluralRules.Select(language, count));
	}

	[Fact]
	public void TheTablesHandOutNothingTheyCanBeChangedThrough() {
		Assert.Equal(["one", "two", "few", "other"], PluralRules.Categories("sgs"));
		Assert.Throws<NotSupportedException>(() => ((IList<string>)PluralRules.Categories("en"))[0] = "x");
		Assert.Throws<NotSupportedException>(() => ((IList<string>)PluralRules.Names)[0] = "x");
		Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)PluralRules.Optional("mt"))["two"] = "x");
		Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)PluralRules.Optional("en"))["two"] = "x");
	}

	[Fact]
	public void CulturedWordsIndexers_AreLocalized_AsIWordsOnesAre() {
		foreach (Type[] index in (Type[][])[[typeof(string)], [typeof(string), typeof(decimal)]]) {
			var indexer = typeof(CulturedWords).GetProperty("Item", index)!;
			Assert.Contains(indexer.GetCustomAttributes(true), attribute => attribute.GetType().Name == "LocalizedAttribute");
		}
	}

	//posts are counted and run at once
	private sealed class CountingContext : SynchronizationContext {
		public int Posts;
		public override void Post(SendOrPostCallback d, object? state) {
			Interlocked.Increment(ref Posts);
			d(state);
		}
	}

	[Fact]
	public void AWatchFromAThreadWithNoContext_LeavesTheTriggerWhereAUiThreadPutIt() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString("value-de=Deutsch\n\n[k]\nvalue=x\nvalue-de=y\n").Live().Digest("en");
		var ui = new CountingContext();
		var thread = new Thread(() => {
			SynchronizationContext.SetSynchronizationContext(ui);
			TriggerWords.Watch();
		});
		thread.Start();
		thread.Join();
		Task.Run(TriggerWords.Watch).Wait(); //a pool thread, no context
		int pulse = TriggerWords.Instance.Pulse;

		Words.SwitchLanguage("de");

		Assert.Equal(1, ui.Posts); //the pulse went to the UI thread's context
		Assert.Equal(pulse + 1, TriggerWords.Instance.Pulse);
	}
}
