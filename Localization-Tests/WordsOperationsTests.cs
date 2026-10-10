using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;
public class WordsOperationsTests {

	private static WordsKey Key(string blockKey, string defaultValue, params (string Code, string Value)[] entries) {
		var key = new WordsKey(blockKey) { DefaultValue = defaultValue };
		foreach (var (code, value) in entries) {
			key.Entries[code] = new WordsEntry { Value = value };
		}
		return key;
	}

	// base file A (defaults + en), translated file B (fr)
	private static Dictionary<string, WordsKey> TwoFiles() => new() {
		["A.title"] = Key("A.title", "Title", ("en", "EN title"), ("fr", "")),
		["A.body"] = Key("A.body", "Body", ("en", "EN body"), ("fr", "")),
		["B.title"] = Key("B.title", "Titre", ("fr", "FR title")),
		["B.body"] = Key("B.body", "", ("fr", "FR body")),
	};

	[Fact]
	public void WordsOperations_MergeTakesEachLanguageFromItsSource() {
		var merged = WordsOperations.Merge(TwoFiles(), "A",
			new Dictionary<string, string> { ["fr"] = "B" }, "M", out var conflicts);

		Assert.NotNull(merged);
		Assert.Empty(conflicts);
		Assert.Equal("Title", merged["M.title"].DefaultValue);
		Assert.Equal("EN title", merged["M.title"].Entries["en"].Value);
		Assert.Equal("FR title", merged["M.title"].Entries["fr"].Value);
		Assert.Equal("FR body", merged["M.body"].Entries["fr"].Value);
	}

	[Fact]
	public void WordsOperations_MergeRefusesMismatchedKeySets() {
		var keys = TwoFiles();
		keys["B.extra"] = Key("B.extra", "spurious");

		var merged = WordsOperations.Merge(keys, "A",
			new Dictionary<string, string> { ["fr"] = "B" }, "M", out var conflicts);

		Assert.Null(merged);
		Assert.Contains("extra", conflicts);
	}

	[Fact]
	public void WordsOperations_MergeCopiesParametersAndReviewFlags() {
		// the WordsKey copy constructor used to drop these silently
		var keys = TwoFiles();
		keys["A.title"].Parameters.Add(new WordsParameter("0", WordsParameterType.Int, "the files"));
		keys["A.title"].NeedsReview = true;

		var merged = WordsOperations.Merge(keys, "A", new Dictionary<string, string>(), "M", out _);

		Assert.NotNull(merged);
		var parameter = Assert.Single(merged["M.title"].Parameters);
		Assert.Equal(WordsParameterType.Int, parameter.DataType);
		Assert.Equal("the files", parameter.Description);
		Assert.True(merged["M.title"].NeedsReview);
	}

	[Fact]
	public void WordsOperations_SplitRoundTripsThroughMerge() {
		// split carries one language plus the unlocalised reference fields, and
		// merge consumes its output straight back
		var keys = TwoFiles();

		var split = WordsOperations.Split(keys, "A", "en", "S");

		Assert.Equal("Title", split["S.title"].DefaultValue);
		Assert.Equal("EN title", split["S.title"].Entries["en"].Value);
		Assert.DoesNotContain("fr", split["S.title"].Entries.Keys);

		var everything = new Dictionary<string, WordsKey>(keys);
		foreach (var (blockKey, key) in split) {
			everything[blockKey] = key;
		}
		var merged = WordsOperations.Merge(everything, "A",
			new Dictionary<string, string> { ["en"] = "S" }, "M", out _);

		Assert.NotNull(merged);
		Assert.Equal("EN title", merged["M.title"].Entries["en"].Value);
	}

	[Fact]
	public void WordsOperations_ShiftMovesEntriesAndParksCollisions() {
		WordsKey plain = Key("F.plain", "", ("en-GB", "moved"));
		WordsKey collision = Key("F.collision", "", ("en-GB", "displaced"), ("en", "kept"));
		WordsKey emptyTarget = Key("F.empty", "", ("en-GB", "adopted"), ("en", ""));
		WordsKey untouched = Key("F.untouched", "", ("en", "stays"));

		WordsOperations.Shift([plain, collision, emptyTarget, untouched], "en-GB", "en");

		Assert.Equal("moved", plain.Entries["en"].Value);
		Assert.False(plain.Entries.ContainsKey("en-GB"));
		Assert.Null(plain.Entries["en"].Stale);
		Assert.Equal("kept", collision.Entries["en"].Value);
		Assert.Equal("displaced", collision.Entries["en"].Context);
		//stale content is freeform; any non-null marker queues the collision for review
		Assert.NotNull(collision.Entries["en"].Stale);
		Assert.False(collision.Entries.ContainsKey("en-GB"));
		Assert.Equal("adopted", emptyTarget.Entries["en"].Value);
		Assert.Null(emptyTarget.Entries["en"].Stale);
		Assert.Equal("stays", untouched.Entries["en"].Value);
	}

	[Fact]
	public void WordsOperations_ShiftFillsAValuelessTargetWithoutLosingItsMetadata() {
		// a target with no value but a context/comment used to be replaced whole;
		// now it keeps them and takes the moved value
		WordsKey key = new("F.k") {
			Entries = {
				{ "en-GB", new WordsEntry { Value = "moved" } },
				{ "en", new WordsEntry { Context = "keep ctx", Comment = "keep note" } },
			},
		};

		WordsOperations.Shift([key], "en-GB", "en");

		Assert.Equal("moved", key.Entries["en"].Value);
		Assert.Equal("keep ctx", key.Entries["en"].Context);
		Assert.Equal("keep note", key.Entries["en"].Comment);
	}

	[Fact]
	public void WordsOperations_ShiftClashPreservesBothContextsAndTheSourceComment() {
		// the target value wins, but the existing note stays, the displaced value
		// is appended (not swapped in), and the source's comment carries over
		WordsKey key = new("F.k") {
			Entries = {
				{ "en-GB", new WordsEntry { Value = "displaced", Comment = "src note" } },
				{ "en", new WordsEntry { Value = "kept", Context = "target ctx" } },
			},
		};

		WordsOperations.Shift([key], "en-GB", "en");

		var target = key.Entries["en"];
		Assert.Equal("kept", target.Value);
		Assert.Contains("target ctx", target.Context);
		Assert.Contains("displaced", target.Context);
		Assert.Equal("src note", target.Comment);
		Assert.NotNull(target.Stale);
	}

	[Fact]
	public void WordsOperations_ShiftEqualValuesKeepTheSourceStaleAndComment() {
		// equal values are no collision, but the source's stale marker and comment
		// must not vanish with the removed entry
		WordsKey key = new("F.k") {
			Entries = {
				{ "en-GB", new WordsEntry { Value = "same", Comment = "src note", Stale = "2026-01-01" } },
				{ "en", new WordsEntry { Value = "same" } },
			},
		};

		WordsOperations.Shift([key], "en-GB", "en");

		var target = key.Entries["en"];
		Assert.Equal("same", target.Value);
		Assert.Equal("src note", target.Comment);
		Assert.Equal("2026-01-01", target.Stale);
	}

	[Fact]
	public void WordsOperations_HaveSameKeysComparesAcrossFilePrefixes() {
		List<Dictionary<string, WordsKey>> keySets = [
			new() {
				["dictionary1.test1"] = new WordsKey("dictionary1.test1"),
				["dictionary1.test2"] = new WordsKey("dictionary1.test2"),
			},
			new() {
				["dictionary2.test1"] = new WordsKey("dictionary2.test1"),
				["dictionary2.test2"] = new WordsKey("dictionary2.test2"),
			},
		];

		Assert.True(WordsOperations.HaveSameKeys(keySets, out var conflicts));
		Assert.Empty(conflicts);

		keySets[1]["dictionary2.test3"] = new WordsKey("dictionary2.test3");
		Assert.False(WordsOperations.HaveSameKeys(keySets, out conflicts));
		Assert.Contains("test3", conflicts);
	}

	private static Dictionary<string, WordsKey> Keys(params string[] blockKeys)
		=> blockKeys.ToDictionary(k => k, k => new WordsKey(k) { DefaultValue = k });

	[Fact]
	public void WordsOperations_RenameCascadesOverAGroupWithChildren() {
		// the group has no key of its own; its descendants move, siblings stay,
		// and `view` never catches `viewer`
		var keys = Keys("F.view.a", "F.view.a.b", "F.view.c", "F.viewer", "F.other");

		Assert.True(WordsOperations.TryRename(keys, "F.view", "F.menu", out var collisions));

		Assert.Empty(collisions);
		Assert.Equal(["F.menu.a", "F.menu.a.b", "F.menu.c", "F.other", "F.viewer"], keys.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("F.menu.a.b", keys["F.menu.a.b"].BlockKey);
		Assert.Equal("F.view.a.b", keys["F.menu.a.b"].DefaultValue); //the same object, renamed
	}

	[Fact]
	public void WordsOperations_RenameIntoOwnSubtreeShiftsWithoutOverwriting() {
		// a → a.x: F.a.b must become F.a.x.b, not land on a key still to move
		var keys = Keys("F.a", "F.a.b");

		Assert.True(WordsOperations.TryRename(keys, "F.a", "F.a.x", out _));

		Assert.Equal(["F.a.x", "F.a.x.b"], keys.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("F.a", keys["F.a.x"].DefaultValue);
		Assert.Equal("F.a.b", keys["F.a.x.b"].DefaultValue);
	}

	[Fact]
	public void WordsOperations_RenameWithCollisionChangesNothing() {
		// one descendant would land on an existing key: report it, move nothing
		var keys = Keys("F.a", "F.a.x", "F.b.x");

		Assert.False(WordsOperations.TryRename(keys, "F.a", "F.b", out var collisions));

		Assert.Equal(["F.b.x"], collisions);
		Assert.Equal(["F.a", "F.a.x", "F.b.x"], keys.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("F.a.x", keys["F.a.x"].BlockKey);
	}

	[Fact]
	public void WordsOperations_MoveKeepsTheLastSegmentMarkerAndAll() {
		var keys = Keys("F.g.$c", "F.g.$c.sub", "F.h");

		Assert.True(WordsOperations.TryMove(keys, "F.g.$c", "F.h", out _));

		Assert.Equal(["F.h", "F.h.$c", "F.h.$c.sub"], keys.Keys.Order(StringComparer.Ordinal));

		//and a move onto an occupied name is refused as a whole
		keys = Keys("F.g.c", "F.h.c");
		Assert.False(WordsOperations.TryMove(keys, "F.g.c", "F.h", out var collisions));
		Assert.Equal(["F.h.c"], collisions);
		Assert.True(keys.ContainsKey("F.g.c"));
	}

	[Fact]
	public void WordsOperations_SetConstantMarksOnlyTheLastSegmentAndKeepsEntries() {
		var keys = Keys("F.view.key", "F.view.key.tip");
		keys["F.view.key"].Entries["fr"] = new WordsEntry { Value = "clé" };

		Assert.Equal("F.view.$key", WordsOperations.SetConstant(keys, "F.view.key", true));

		Assert.True(keys["F.view.$key"].IsConstant);
		Assert.Equal("clé", keys["F.view.$key"].Entries["fr"].Value);
		Assert.True(keys.ContainsKey("F.view.$key.tip"));
		Assert.False(keys.ContainsKey("F.view.key"));

		Assert.Equal("F.view.key", WordsOperations.SetConstant(keys, "F.view.$key", false, clearEntries: true));
		Assert.False(keys["F.view.key"].IsConstant);
		Assert.Equal("", keys["F.view.key"].Entries["fr"].Value);
	}

	[Fact]
	public void WordsOperations_SetConstantOnADollarSiblingIsRefused() {
		// `foo` beside `$foo`: the marked name is taken, so nothing changes —
		// this used to throw out of Dictionary.Add
		var keys = Keys("F.foo", "F.$foo");

		Assert.Null(WordsOperations.SetConstant(keys, "F.foo", true));

		Assert.False(keys["F.foo"].IsConstant);
		Assert.Equal(["F.$foo", "F.foo"], keys.Keys.Order(StringComparer.Ordinal));
		Assert.Null(WordsOperations.SetConstant(keys, "F.missing", true));
	}

	[Fact]
	public void WordsOperations_FormatSampleFillsNumberedAndNamedParameters() {
		Dictionary<string, object?> values = new() { ["0"] = 22.0, ["Top"] = 1.2345, ["1"] = "one" };

		var text = WordsOperations.FormatSample("{0:N1} {1} N{Top:g2} {Nope}", values, System.Globalization.CultureInfo.InvariantCulture);

		Assert.Equal("22.0 one N1.2 #Nope#", text);
	}

	[Fact]
	public void WordsOperations_FormatSampleSurvivesMetacharacters() {
		// a parameter named with regex metacharacters used to throw from the
		// pattern the editor built; now names are only ever looked up
		Assert.Equal("plain", WordsOperations.FormatSample("plain", new Dictionary<string, object?> { ["a+(b)"] = "x" }));
	}

	[Fact]
	public void WordsOperations_ReadInputsReadsEachTypeInTheInvariantCulture() {
		var (values, complaints) = WordsOperations.ReadInputs([
			("0", WordsParameterType.Str, "the file"),
			("1", WordsParameterType.Int, "-42"),
			("2", WordsParameterType.Real, "1,234.5"),
			("3", WordsParameterType.Time, "1:30:00"),
			("4", WordsParameterType.Date, "2026-10-10 09:30"),
			("5", WordsParameterType.Int, ""),
			("Count", WordsParameterType.Int, ""),
		]);

		Assert.Equal("the file", values["0"]);
		Assert.Equal(-42, values["1"]);
		Assert.Equal(1234.5, values["2"]);
		Assert.Equal(new TimeSpan(1, 30, 0), values["3"]);
		Assert.Equal(new DateTimeOffset(2026, 10, 10, 9, 30, 0, TimeSpan.Zero), values["4"]);
		Assert.Equal("{5}", values["5"]); //an empty input is its placeholder written out
		Assert.Equal("{Count}", values["Count"]);
		Assert.Empty(complaints);
	}

	[Theory]
	[InlineData("int", "1.5", "{n}: \"1.5\" is no whole number, such as 42")]
	[InlineData("int", "not a number", "{n}: \"not a number\" is no whole number, such as 42")]
	[InlineData("real", "1,5,x", "{n}: \"1,5,x\" is no number, such as 1.5")]
	[InlineData("time", "soon", "{n}: \"soon\" is no span of time, such as 1:30:00")]
	[InlineData("date", "someday", "{n}: \"someday\" is no moment, such as 2026-10-10 09:30")]
	[InlineData("enum(enums.brew)", "flat white", "{n}: \"flat white\" is no member under enums.brew")]
	public void WordsOperations_ReadInputsComplainsOfWhatItsTypeCannotRead(string type, string input, string complaint) {
		Assert.True(WordsParameterType.TryParse(type, out var parsed));

		var (values, complaints) = WordsOperations.ReadInputs([("n", parsed, input), ("m", WordsParameterType.Str, "fine")]);

		Assert.Equal([complaint], complaints);
		Assert.Equal("{n}", values["n"]); //its placeholder, as though nothing were typed
		Assert.Equal("fine", values["m"]);
	}

	[Fact]
	public void WordsOperations_AnEnumInputDescribesInTheLanguageItFormatsIn() {
		// the member's words in the pane's language, or its name where it has none,
		// and its tooltip through the template's letter
		var latte = new WordsKey("A.enums.brew.latte") { DefaultValue = "Latte" };
		latte.Entries["it"] = new WordsEntry { Value = "Caffellatte" };
		var tip = new WordsKey("A.enums.brew.latte.tooltip") { DefaultValue = "Milky" };
		tip.Entries["it"] = new WordsEntry { Value = "Con latte" };
		Dictionary<string, WordsKey> keys = new() { [latte.BlockKey] = latte, [tip.BlockKey] = tip };
		var brew = WordsParameterType.Enum("enums.brew");
		var english = new CulturedWords(new DefaultWordsProvider(keys, ["A"]), System.Globalization.CultureInfo.InvariantCulture);
		var italian = new CulturedWords(new LanguageWordsProvider(keys, "it", ["A"]), System.Globalization.CultureInfo.GetCultureInfo("it"));
		string Sample(IWords words, string input) => WordsOperations.FormatSample(words, "A.sample", WordsOperations.ReadInputs([("0", brew, input)]).Values);
		keys["A.sample"] = new WordsKey("A.sample") { DefaultValue = "{0}: {0:T}" };
		keys["A.sample"].Entries["it"] = new WordsEntry { Value = "{0}: {0:T}" };

		Assert.Equal("Latte: Milky", Sample(english, "latte"));
		Assert.Equal("Caffellatte: Con latte", Sample(italian, "latte"));
		Assert.Equal("affogato: ", Sample(italian, "affogato"));
	}

	[Fact]
	public void WordsOperations_MembersUnderAPrefixAreTheKeysDirectlyUnderIt() {
		// through every file, once each, in the files' order; a member's own slots
		// lie deeper, and the prefix's own slots and constants are no members
		Dictionary<string, WordsKey> keys = [];
		foreach (string label in (string[])["A.brew", "A.brew.tooltip", "A.brew.$house", "A.brew.latte", "A.brew.latte.tooltip", "A.brew.mocha",
				"B.brew.desc", "B.brew.sub", "B.brew.ristretto", "B.brew.latte", "B.other.cola"]) {
			keys[label] = new WordsKey(label);
		}

		Assert.Equal(["latte", "mocha", "ristretto"], WordsOperations.MembersUnder(keys, ["A", "B"], "brew"));
		Assert.Equal(["ristretto", "latte"], WordsOperations.MembersUnder(keys, ["B"], "brew"));
		Assert.Empty(WordsOperations.MembersUnder(keys, ["A", "B"], "nowhere"));
	}

	[Fact]
	public void WordsOperations_CultureForKnowsRealCodesAndFallsBackInvariant() {
		Assert.Equal("de-DE", WordsOperations.CultureFor("de-DE").Name);
		Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, WordsOperations.CultureFor(null));
		Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, WordsOperations.CultureFor("xx-Nowhere"));
	}
}
