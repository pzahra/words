using PatTech.Localization;
using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;
public class WordsProviderTests {

	// two loaded files, each its own keys by name: a key and a reference resolve
	// across both, the later file winning, like a host app stacking dictionaries
	private static Dictionary<string, WordsKey> FileA() => new() {
		["msg"] = new WordsKey("msg") { DefaultValue = "see {>other.key} at {$unit}" },
		["shared"] = new WordsKey("shared") { DefaultValue = "from A" },
	};

	private static Dictionary<string, WordsKey> FileB() => new() {
		["shared"] = new WordsKey("shared") { DefaultValue = "from B" },
		["other.key"] = new WordsKey("other.key") { DefaultValue = "yonder" },
		["$unit"] = new WordsKey("$unit") { DefaultValue = "m2·K/W", IsConstant = true },
	};

	[Fact]
	public void DefaultWordsProvider_ResolvesKeysAcrossFiles_TheLaterWinning() {
		var provider = new DefaultWordsProvider([FileA(), FileB()]);

		Assert.True(provider.TryGetValue("msg", out var own));
		Assert.Equal("see {>other.key} at {$unit}", own);

		Assert.True(provider.TryGetValue("other.key", out var cross));
		Assert.Equal("yonder", cross);

		Assert.True(provider.TryGetValue("shared", out var shared));
		Assert.Equal("from B", shared);
		Assert.Equal("from A", new DefaultWordsProvider([FileB(), FileA()])["shared"]);

		Assert.True(provider.ContainsKey("$unit"));
		Assert.False(provider.TryGetValue("missing", out _));
	}

	[Fact]
	public void WordsRenderKey_ResolvesReferencesAndConstantsAcrossFiles() {
		var provider = new DefaultWordsProvider([FileA(), FileB()]);

		Assert.Equal("see yonder at m2·K/W", Words.RenderKey(provider, "msg"));
	}

	[Fact]
	public void LanguageWordsProvider_FallsBackFamilyThenDefault() {
		Dictionary<string, WordsKey> keys = new() {
			["k"] = new WordsKey("k") {
				DefaultValue = "default",
				Entries = {
					{ "en", new WordsEntry { Value = "family" } },
					{ "en-GB", new WordsEntry() },
				},
			},
			["d"] = new WordsKey("d") {
				DefaultValue = "default only",
				Entries = { { "en", new WordsEntry() } },
			},
		};
		var provider = new LanguageWordsProvider(keys, "en-GB");

		Assert.True(provider.TryGetValue("k", out var familyValue));
		Assert.Equal("family", familyValue);

		Assert.True(provider.TryGetValue("d", out var defaultValue));
		Assert.Equal("default only", defaultValue);
	}

	[Fact]
	public void WordsProviderBase_IndexerResolvesKeysAndThrowsForMissing() {
		var provider = new DefaultWordsProvider([FileA(), FileB()]);

		// a key in either file resolves
		Assert.Equal("see {>other.key} at {$unit}", provider["msg"]);
		Assert.Equal("yonder", provider["other.key"]);

		// a missing key throws KeyNotFoundException, never NotImplementedException
		Assert.Throws<KeyNotFoundException>(() => provider["missing"]);
	}
}
