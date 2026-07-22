using PatTech.Localization.Authoring;
using Xunit;

namespace WordsEdit.Tests;
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
}
