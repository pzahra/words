using PatTech.Localization;
using PatTech.Localization.Authoring;
using Xunit;

namespace WordsEdit.Tests;
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

		var ini = Write(tree, allKeys);

		var reloaded = Reload(ini);
		Assert.Equal("1A", reloaded["one.a"].DefaultValue);
		Assert.Equal("2A", reloaded["two.a"].DefaultValue);
	}
}
