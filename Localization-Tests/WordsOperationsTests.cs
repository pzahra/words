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
		keys["A.title"].Parameters.Add(new WordsParameter("0", WordsParameterType.String, "sample"));
		keys["A.title"].NeedsReview = true;

		var merged = WordsOperations.Merge(keys, "A", new Dictionary<string, string>(), "M", out _);

		Assert.NotNull(merged);
		var parameter = Assert.Single(merged["M.title"].Parameters);
		Assert.Equal("sample", parameter.Value);
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
		var key = new WordsKey("F.k");
		key.Parameters.Add(new WordsParameter("0", WordsParameterType.Select("Double"), "22"));
		key.Parameters.Add(new WordsParameter("Top", WordsParameterType.Select("Double"), "1.2345"));
		key.Parameters.Add(new WordsParameter("1", WordsParameterType.String, "one"));

		var text = WordsOperations.FormatSample(key, "{0:N1} {1} N{Top:g2} {Nope}", System.Globalization.CultureInfo.InvariantCulture);

		Assert.Equal("22.0 one N1.2 #Nope#", text);
	}

	[Fact]
	public void WordsOperations_FormatSampleSurvivesMetacharactersAndReportsBadSamples() {
		// a parameter named with regex metacharacters used to throw from the
		// pattern the editor built; now names are only ever looked up
		var key = new WordsKey("F.k");
		key.Parameters.Add(new WordsParameter("a+(b)", WordsParameterType.String, "x"));

		Assert.Equal("plain", WordsOperations.FormatSample(key, "plain"));

		key.Parameters.Add(new WordsParameter("n", WordsParameterType.Select("Integer"), "not a number"));
		Assert.Throws<FormatException>(() => WordsOperations.FormatSample(key, "{n}"));
	}

	[Fact]
	public void WordsOperations_CultureForKnowsRealCodesAndFallsBackInvariant() {
		Assert.Equal("de-DE", WordsOperations.CultureFor("de-DE").Name);
		Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, WordsOperations.CultureFor(null));
		Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, WordsOperations.CultureFor("xx-Nowhere"));
	}
}
