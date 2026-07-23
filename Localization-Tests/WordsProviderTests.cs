using PatTech.Localization;
using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;
public class WordsProviderTests {

	// two loaded files: bare references must resolve across both,
	// with the later-loaded file winning, like a host app stacking dictionaries
	private static Dictionary<string, WordsKey> TwoFiles() => new() {
		["A.msg"] = new WordsKey("A.msg") { DefaultValue = "see {>other.key} at {$unit}" },
		["A.shared"] = new WordsKey("A.shared") { DefaultValue = "from A" },
		["B.shared"] = new WordsKey("B.shared") { DefaultValue = "from B" },
		["B.other.key"] = new WordsKey("B.other.key") { DefaultValue = "yonder" },
		["B.$unit"] = new WordsKey("B.$unit") { DefaultValue = "m2·K/W", IsConstant = true },
	};

	[Fact]
	public void DefaultWordsProvider_ResolvesBareKeysAcrossFiles() {
		var provider = new DefaultWordsProvider(TwoFiles(), ["A", "B"]);

		Assert.True(provider.TryGetValue("A.msg", out var exact));
		Assert.Equal("see {>other.key} at {$unit}", exact);

		Assert.True(provider.TryGetValue("other.key", out var cross));
		Assert.Equal("yonder", cross);

		Assert.True(provider.TryGetValue("shared", out var shared));
		Assert.Equal("from B", shared);

		Assert.True(provider.ContainsKey("$unit"));
		Assert.False(provider.TryGetValue("missing", out _));
	}

	[Fact]
	public void WordsRenderKey_ResolvesReferencesAndConstantsAcrossFiles() {
		var provider = new DefaultWordsProvider(TwoFiles(), ["A", "B"]);

		Assert.Equal("see yonder at m2·K/W", Words.RenderKey(provider, "A.msg"));
	}

	[Fact]
	public void LanguageWordsProvider_FallsBackFamilyThenDefault() {
		Dictionary<string, WordsKey> keys = new() {
			["A.k"] = new WordsKey("A.k") {
				DefaultValue = "default",
				Entries = {
					{ "en", new WordsEntry { Value = "family" } },
					{ "en-GB", new WordsEntry() },
				},
			},
			["A.d"] = new WordsKey("A.d") {
				DefaultValue = "default only",
				Entries = { { "en", new WordsEntry() } },
			},
		};
		var provider = new LanguageWordsProvider(keys, "en-GB", ["A"]);

		Assert.True(provider.TryGetValue("k", out var familyValue));
		Assert.Equal("family", familyValue);

		Assert.True(provider.TryGetValue("d", out var defaultValue));
		Assert.Equal("default only", defaultValue);
	}
}
