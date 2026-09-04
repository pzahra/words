using PatTech.Localization;
using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;
public class IniWriterTests {

	private sealed class FakeNode(string fullLabel, params IKeyTreeNode[] children) : IKeyTreeNode {
		public string FullLabel { get; } = fullLabel;
		public IEnumerable<IKeyTreeNode> Children => children;
	}

	private sealed class FakeComment(string text) : ICommentNode {
		public string FullLabel => ";";
		public string Text { get; } = text;
		public IEnumerable<IKeyTreeNode> Children => [];
	}

	private sealed class CutAt(params string[] fullLabels) : ICutStrategy {
		public bool Cuts(IKeyTreeNode node, int depth) => fullLabels.Contains(node.FullLabel);
	}

	private static string Write(FakeNode fileNode, Dictionary<string, WordsKey> allKeys, ICutStrategy? cutStrategy = null) {
		var output = new StringWriter();
		using var iniWriter = new IniWriter(output, cutStrategy);
		iniWriter.WriteKeys(fileNode, allKeys);
		return output.ToString();
	}

	private static IReadOnlyDictionary<string, WordsKey> Reload(string ini) {
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(ini));
		Assert.Empty(consumer.Errors);
		return consumer.WordKeys;
	}

	[Fact]
	public void IniWriter_ChainsBlocksUnderTheLastFullHeader() {
		// a block whose key extends the previous base writes as [.suffix];
		// a sibling that doesn't resets the base with a full header
		var tree = new FakeNode("F",
			new FakeNode("F.group",
				new FakeNode("F.group.a",
					new FakeNode("F.group.a.b"))),
			new FakeNode("F.other"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.group"] = new WordsKey("F.group") { DefaultValue = "G" },
			["F.group.a"] = new WordsKey("F.group.a") { DefaultValue = "A" },
			["F.group.a.b"] = new WordsKey("F.group.a.b") { DefaultValue = "B" },
			["F.other"] = new WordsKey("F.other") { DefaultValue = "O" },
		};

		var ini = Write(tree, allKeys);

		var lines = ini.Split(Environment.NewLine);
		Assert.Contains("[group]", lines);
		Assert.Contains("[.a]", lines);
		Assert.Contains("[.a.b]", lines);
		Assert.Contains("[other]", lines);

		var reloaded = Reload(ini);
		Assert.Equal("G", reloaded["group"].DefaultValue);
		Assert.Equal("A", reloaded["group.a"].DefaultValue);
		Assert.Equal("B", reloaded["group.a.b"].DefaultValue);
		Assert.Equal("O", reloaded["other"].DefaultValue);
	}

	[Fact]
	public void IniWriter_CutStrategyWritesBareHeaderForKeylessBase() {
		// cutting at a keyless group node emits a bare [group] header so its
		// descendants shorten to [.suffix]; the bare header reloads as an empty key
		var tree = new FakeNode("F",
			new FakeNode("F.deep",
				new FakeNode("F.deep.a"),
				new FakeNode("F.deep.b")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.deep.a"] = new WordsKey("F.deep.a") { DefaultValue = "A" },
			["F.deep.b"] = new WordsKey("F.deep.b") { DefaultValue = "B" },
		};

		var ini = Write(tree, allKeys, new CutAt("F.deep"));

		var lines = ini.Split(Environment.NewLine);
		Assert.Contains("[deep]", lines);
		Assert.Contains("[.a]", lines);
		Assert.Contains("[.b]", lines);

		var reloaded = Reload(ini);
		Assert.Equal("", reloaded["deep"].DefaultValue);
		Assert.Equal("A", reloaded["deep.a"].DefaultValue);
		Assert.Equal("B", reloaded["deep.b"].DefaultValue);
	}

	[Fact]
	public void IniWriter_CommentsRoundTrip() {
		// the preamble tops the file; comment nodes write themselves wherever
		// they stand in the walk (above full or dot-relative headers alike), and
		// reload anchored to the block that follows them
		var tree = new FakeNode("F",
			new FakeComment(" about group"),
			new FakeNode("F.group",
				new FakeComment(" about a\n second line"),
				new FakeNode("F.group.a")),
			new FakeComment(" the trailer"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.group"] = new WordsKey("F.group") { DefaultValue = "G" },
			["F.group.a"] = new WordsKey("F.group.a") { DefaultValue = "A" },
		};
		List<LanguageEntry> languages = [new LanguageEntry("en", "English")];

		var output = new StringWriter();
		IniWriter.WriteFile(tree, output, allKeys, languages, preamble: " the preamble");
		var ini = output.ToString();

		var lines = ini.Split(Environment.NewLine);
		Assert.Equal("; the preamble", lines[0]);
		Assert.Contains("; about group", lines);
		Assert.Contains("; about a", lines);
		Assert.Contains("[.a]", lines);
		Assert.Equal("; the trailer", lines[^2]);

		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(ini));
		Assert.Empty(consumer.Errors);
		Assert.Equal(" the preamble", consumer.Preamble);
		Assert.Equal(" about group", consumer.BlockComments["group"]);
		Assert.Equal(" about a\n second line", consumer.BlockComments["group.a"]);
		Assert.Equal(" the trailer", consumer.Trailer);
	}

	[Fact]
	public void IniWriter_SettingsReferences_RoundTrip() {
		// the settings-file references write as keyless param fields in the
		// language section and come back through the provider's Settings and
		// LanguageSettings — never as keys, never as languages
		var tree = new FakeNode("F", new FakeNode("F.k"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = "V" },
		};
		List<LanguageEntry> languages = [new LanguageEntry("en", "English")];
		var perLanguage = new Dictionary<string, string> { ["de"] = "wordsmith-de.ini", ["fr"] = "" };

		var output = new StringWriter();
		IniWriter.WriteFile(tree, output, allKeys, languages, settings: "wordsmith.ini", languageSettings: perLanguage);
		var ini = output.ToString();

		var lines = ini.Split(Environment.NewLine);
		Assert.Contains("param=wordsmith.ini", lines);
		Assert.Contains("param-de=wordsmith-de.ini", lines);
		Assert.DoesNotContain(lines, line => line.StartsWith("param-fr")); //an empty path is no reference

		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(ini));
		Assert.Empty(consumer.Errors);
		Assert.Equal("wordsmith.ini", consumer.Settings);
		Assert.Equal(["de"], consumer.LanguageSettings.Keys);
		Assert.Equal("wordsmith-de.ini", consumer.LanguageSettings["de"]);
		Assert.False(consumer.WordKeys.ContainsKey("param"));
		Assert.Equal(["en"], consumer.KnownLanguages.Keys);
	}

	[Fact]
	public void IniWriter_SettingsReferences_SaveLoadSaveStable() {
		// the second save matches the first byte for byte: capture order is
		// preserved, so the param lines come back in the same shape
		var tree = new FakeNode("F", new FakeNode("F.k"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = "V" },
		};
		List<LanguageEntry> languages = [new LanguageEntry("en", "English")];
		var perLanguage = new Dictionary<string, string> { ["de"] = "wordsmith-de.ini", ["fr"] = "fr/wordsmith.ini" };

		var firstOut = new StringWriter();
		IniWriter.WriteFile(tree, firstOut, allKeys, languages, settings: "../wordsmith.ini", languageSettings: perLanguage);
		var firstSave = firstOut.ToString();

		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(firstSave));
		var reloadedKeys = consumer.WordKeys.ToDictionary(
			pair => "F." + pair.Key,
			pair => new WordsKey(pair.Value) { BlockKey = "F." + pair.Value.BlockKey });

		var secondOut = new StringWriter();
		IniWriter.WriteFile(tree, secondOut, reloadedKeys, [.. consumer.KnownLanguages.Values],
			settings: consumer.Settings, languageSettings: consumer.LanguageSettings);

		Assert.Equal(firstSave, secondOut.ToString());
	}

	[Fact]
	public void IniWriter_CutStrategyCannotBreakTheChain() {
		// a strategy that never cuts where it should still yields a correct file:
		// blocks outside the current base always force a full header
		var tree = new FakeNode("F",
			new FakeNode("F.one",
				new FakeNode("F.one.a")),
			new FakeNode("F.two",
				new FakeNode("F.two.a")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.one.a"] = new WordsKey("F.one.a") { DefaultValue = "1A" },
			["F.two.a"] = new WordsKey("F.two.a") { DefaultValue = "2A" },
		};

		var ini = Write(tree, allKeys, IniWriter.NeverCuts);

		var reloaded = Reload(ini);
		Assert.Equal("1A", reloaded["one.a"].DefaultValue);
		Assert.Equal("2A", reloaded["two.a"].DefaultValue);
	}

	[Fact]
	public void GroupCuts_IsTheDefault_CutsKeylessGroupsWithEnoughKeys() {
		// no strategy passed: a keyless group gathering two keyed blocks gets a
		// bare header and its children shorten to [.suffix]
		var tree = new FakeNode("F",
			new FakeNode("F.deep",
				new FakeNode("F.deep.a"),
				new FakeNode("F.deep.b")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.deep.a"] = new WordsKey("F.deep.a") { DefaultValue = "A" },
			["F.deep.b"] = new WordsKey("F.deep.b") { DefaultValue = "B" },
		};

		var ini = Write(tree, allKeys);

		var lines = ini.Split(Environment.NewLine);
		Assert.Contains("[deep]", lines);
		Assert.Contains("[.a]", lines);
		Assert.Contains("[.b]", lines);
	}

	[Fact]
	public void GroupCuts_OneKeyedDescendant_KeepsItsFullHeader() {
		// a single block doesn't pay for the bare header (which would reload as
		// an extra empty key)
		var tree = new FakeNode("F",
			new FakeNode("F.deep",
				new FakeNode("F.deep.only")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.deep.only"] = new WordsKey("F.deep.only") { DefaultValue = "O" },
		};

		var ini = Write(tree, allKeys);

		var lines = ini.Split(Environment.NewLine);
		Assert.Contains("[deep.only]", lines);
		Assert.DoesNotContain("[deep]", lines);
	}

	[Fact]
	public void GroupCuts_KeyedGroups_NeverCut() {
		// a keyed group re-bases the chain with its own header; forcing a cut
		// there would gain nothing
		var tree = new FakeNode("F",
			new FakeNode("F.group",
				new FakeNode("F.group.a"),
				new FakeNode("F.group.b")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.group"] = new WordsKey("F.group") { DefaultValue = "G" },
			["F.group.a"] = new WordsKey("F.group.a") { DefaultValue = "A" },
			["F.group.b"] = new WordsKey("F.group.b") { DefaultValue = "B" },
		};

		Assert.False(new GroupCuts(allKeys).Cuts(tree.Children.First(), 0));

		var reloaded = Reload(Write(tree, allKeys));
		Assert.Equal("G", reloaded["group"].DefaultValue);
	}

	[Fact]
	public void GroupCuts_KeysBeyondADeeperCut_DontCountForTheOuterGroup() {
		// all keys sit under the inner group, which cuts and re-bases; a bare
		// header on the outer group would shorten nothing
		var tree = new FakeNode("F",
			new FakeNode("F.outer",
				new FakeNode("F.outer.inner",
					new FakeNode("F.outer.inner.x"),
					new FakeNode("F.outer.inner.y"))));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.outer.inner.x"] = new WordsKey("F.outer.inner.x") { DefaultValue = "X" },
			["F.outer.inner.y"] = new WordsKey("F.outer.inner.y") { DefaultValue = "Y" },
		};

		var ini = Write(tree, allKeys);

		var lines = ini.Split(Environment.NewLine);
		Assert.Contains("[outer.inner]", lines);
		Assert.DoesNotContain("[outer]", lines);
		Assert.Contains("[.x]", lines);
		Assert.Contains("[.y]", lines);
	}

	[Fact]
	public void GroupCuts_BareHeader_IsSaveLoadSaveStable() {
		// the bare header reloads as an empty key, and that empty key writes
		// back as the same bare header — the second save matches the first byte
		// for byte
		var tree = new FakeNode("F",
			new FakeNode("F.deep",
				new FakeNode("F.deep.a"),
				new FakeNode("F.deep.b")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.deep.a"] = new WordsKey("F.deep.a") { DefaultValue = "A" },
			["F.deep.b"] = new WordsKey("F.deep.b") { DefaultValue = "B" },
		};

		var firstSave = Write(tree, allKeys);

		Dictionary<string, WordsKey> reloaded = Reload(firstSave).ToDictionary(
			pair => "F." + pair.Key,
			pair => new WordsKey(pair.Value) { BlockKey = "F." + pair.Value.BlockKey });
		Assert.Equal("", reloaded["F.deep"].DefaultValue);
		var secondSave = Write(tree, reloaded);

		Assert.Equal(firstSave, secondSave);
	}
}
