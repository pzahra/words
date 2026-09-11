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

	[Fact]
	public void IniWriter_BackslashValuesRoundTrip() {
		// internal, UNC-doubled and trailing backslashes all survive: the writer
		// escapes each `\` as `\\`, the parser collapses it back
		var tree = new FakeNode("F",
			new FakeNode("F.path"),
			new FakeNode("F.unc"),
			new FakeNode("F.trailing"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.path"] = new WordsKey("F.path") { DefaultValue = @"C:\net\share" },
			["F.unc"] = new WordsKey("F.unc") { DefaultValue = @"\\server\share\" },
			["F.trailing"] = new WordsKey("F.trailing") { DefaultValue = @"one\" },
		};

		var reloaded = Reload(Write(tree, allKeys));

		Assert.Equal(@"C:\net\share", reloaded["path"].DefaultValue);
		Assert.Equal(@"\\server\share\", reloaded["unc"].DefaultValue);
		Assert.Equal(@"one\", reloaded["trailing"].DefaultValue);
	}

	[Fact]
	public void IniWriter_TerminalBackslashInContextDoesNotSwallowTheNextField() {
		// a context ending in a literal backslash used to write as a naked
		// continuation and eat the following `value=` line whole
		var tree = new FakeNode("F", new FakeNode("F.k"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { Context = @"see path\", DefaultValue = "V" },
		};

		var reloaded = Reload(Write(tree, allKeys));

		Assert.Equal(@"see path\", reloaded["k"].Context);
		Assert.Equal("V", reloaded["k"].DefaultValue);
	}

	[Fact]
	public void IniWriter_BackslashAndNewlineValue_RoundTripsWithoutBreakingTheFile() {
		// a multi-line, backslash-laden value ending in a backslash, whose second
		// line even looks like a header, reloads intact and the next block survives
		var tree = new FakeNode("F", new FakeNode("F.k"), new FakeNode("F.after"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = "C:\\one\n[two]\\" },
			["F.after"] = new WordsKey("F.after") { DefaultValue = "after" },
		};

		var reloaded = Reload(Write(tree, allKeys));

		Assert.Equal("C:\\one\n[two]\\", reloaded["k"].DefaultValue);
		Assert.Equal("after", reloaded["after"].DefaultValue);
	}

	[Fact]
	public void IniWriter_WrapDoesNotSplitAnEscapedBackslash() {
		// a long value with no spaces to break at, full of backslashes and
		// ending in one: the soft wrap must not fall right after a `\`, which
		// would strand half of an escaped pair and end the line prematurely
		var value = @"C:\" + new string('x', 60) + @"\a\b\c\d\e\f\g\h\i\j\";
		var tree = new FakeNode("F", new FakeNode("F.k"), new FakeNode("F.after"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = value },
			["F.after"] = new WordsKey("F.after") { DefaultValue = "after" },
		};

		var reloaded = Reload(Write(tree, allKeys));

		Assert.Equal(value, reloaded["k"].DefaultValue);
		Assert.Equal("after", reloaded["after"].DefaultValue);
	}

	[Fact]
	public void IniWriter_BackslashValues_SaveLoadSaveStable() {
		// the second save matches the first byte for byte once backslashes are in
		var tree = new FakeNode("F", new FakeNode("F.k"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = @"a\b\" },
		};

		var firstSave = Write(tree, allKeys);
		Dictionary<string, WordsKey> reloaded = Reload(firstSave).ToDictionary(
			pair => "F." + pair.Key,
			pair => new WordsKey(pair.Value) { BlockKey = "F." + pair.Value.BlockKey });
		var secondSave = Write(tree, reloaded);

		Assert.Equal(firstSave, secondSave);
	}

	[Fact]
	public void IniWriter_WrapDoesNotSplitAnEscapedApostrophe() {
		// apostrophes double to a `''` pair and, like backslashes, are non-word
		// chars a wrap could fall inside; a long, spaceless, apostrophe-laden
		// value must round-trip whole
		var value = new string('x', 70) + "'a'b'c'd'e'f'g'h'i'j'k";
		var tree = new FakeNode("F", new FakeNode("F.k"), new FakeNode("F.after"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = value },
			["F.after"] = new WordsKey("F.after") { DefaultValue = "after" },
		};

		var reloaded = Reload(Write(tree, allKeys));

		Assert.Equal(value, reloaded["k"].DefaultValue);
		Assert.Equal("after", reloaded["after"].DefaultValue);
	}

	[Fact]
	public void IniWriter_WrapKeepsEscapedUnderscorePairsIntact() {
		// underscores double too, but `_` is a word char: a break never lands
		// before the second half of a pair, so a long value packed with them
		// (and long enough to wrap at its spaces) round-trips
		var value = new string('a', 55) + " middle_word_here_" + new string('b', 55) + " tail_";
		var tree = new FakeNode("F", new FakeNode("F.k"), new FakeNode("F.after"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = value },
			["F.after"] = new WordsKey("F.after") { DefaultValue = "after" },
		};

		var reloaded = Reload(Write(tree, allKeys));

		Assert.Equal(value, reloaded["k"].DefaultValue);
		Assert.Equal("after", reloaded["after"].DefaultValue);
	}

	[Fact]
	public void IniWriter_DoublesEveryApostropheInValues_SoNoLoneQuoteReachesAValueLine() {
		// Doubling apostrophes is not for the parser's sake: it reads a lone `'`
		// back unchanged (see Parser_ReadsALoneApostropheVerbatim below), so a
		// write/read/compare passes with or without it. The point is an ini syntax
		// highlighter that treats `'` as a multiline string delimiter — a lone
		// apostrophe sends it colouring the rest of the file as a string. So the
		// guard is on the bytes written: every value-bearing line carries an even
		// number of apostrophes, never a stray one.
		var tree = new FakeNode("F", new FakeNode("F.k"));
		var key = new WordsKey("F.k") {
			DefaultValue = "don't stop",
			Context = "the user's account",
			Comment = "O'Brien's note",
		};
		key.Entries["it"] = new WordsEntry {
			Value = "l'italiano's quote",
			Context = "l'contesto",
			Comment = "l'commento",
		};
		Dictionary<string, WordsKey> allKeys = new() { ["F.k"] = key };

		var ini = Write(tree, allKeys);

		foreach (var line in ini.Split('\n')) {
			var trimmed = line.TrimEnd('\r');
			if (trimmed.StartsWith(';')) continue; // comments are written verbatim, by design
			var apostrophes = trimmed.Count(c => c == '\'');
			Assert.True(apostrophes % 2 == 0, $"lone apostrophe on line: {trimmed}");
		}
		// and it really is doubling, not stripping the apostrophe out
		Assert.Contains("don''t stop", ini);
		Assert.Contains("l''italiano''s quote", ini);
	}

	[Fact]
	public void Parser_ReadsALoneApostropheVerbatim_SoRoundTripAloneCannotGuardDoubling() {
		// A hand-written lone apostrophe reads back as-is: the unescape only
		// collapses doubled pairs. This is why the guard above checks the written
		// bytes — a plain round trip would pass even if the doubling regressed.
		var reloaded = Reload("[k]\nvalue=don't stop\n");

		Assert.Equal("don't stop", reloaded["k"].DefaultValue);
	}

	[Fact]
	public void IniWriter_CommentLines_KeepASingleApostrophe_TheParserReadsThemVerbatim() {
		// The one place apostrophes stay single: `;` comment lines. The parser hands
		// comment text back raw (no unescape), so doubling here would corrupt the
		// comment on reload. A standalone comment node marks that boundary.
		var tree = new FakeNode("F", new FakeComment("don't double me"), new FakeNode("F.k"));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.k"] = new WordsKey("F.k") { DefaultValue = "v" },
		};

		var ini = Write(tree, allKeys);

		Assert.Contains(";don't double me", ini);
		Assert.DoesNotContain("don''t double me", ini);
	}

	[Fact]
	public void WriteAtomic_ReplacesTheFileAndLeavesNoTemp() {
		var dir = Path.Combine(Path.GetTempPath(), $"IniWriterAtomic-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		try {
			var path = Path.Combine(dir, "out.ini");
			File.WriteAllText(path, "OLD");

			IniWriter.WriteAtomic(path, w => w.Write("NEW"));

			Assert.Equal("NEW", File.ReadAllText(path));
			Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
		}
		finally {
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void WriteAtomic_LeavesTheOriginalWhenTheWriteThrows() {
		var dir = Path.Combine(Path.GetTempPath(), $"IniWriterAtomic-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		try {
			var path = Path.Combine(dir, "out.ini");
			File.WriteAllText(path, "OLD");

			Assert.Throws<InvalidOperationException>(() =>
				IniWriter.WriteAtomic(path, w => { w.Write("HALF"); throw new InvalidOperationException("boom"); }));

			Assert.Equal("OLD", File.ReadAllText(path)); //original untouched
			Assert.Empty(Directory.GetFiles(dir, "*.tmp")); //temp cleaned up
		}
		finally {
			Directory.Delete(dir, recursive: true);
		}
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void GroupCuts_RejectsMinimumKeysBelowOne(int minimumKeys) {
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GroupCuts(new Dictionary<string, WordsKey>(), minimumKeys));
	}

	[Fact]
	public void GroupCuts_SnapshotsTheKeySet_LaterDictChangesDoNotDesyncIt() {
		var tree = new FakeNode("F",
			new FakeNode("F.deep",
				new FakeNode("F.deep.a"),
				new FakeNode("F.deep.b")));
		Dictionary<string, WordsKey> allKeys = new() {
			["F.deep.a"] = new WordsKey("F.deep.a") { DefaultValue = "A" },
			["F.deep.b"] = new WordsKey("F.deep.b") { DefaultValue = "B" },
		};
		var cuts = new GroupCuts(allKeys);

		// mutating the dictionary the strategy was built from must not change its
		// decisions: it snapshotted the key set at construction
		allKeys["F.deep"] = new WordsKey("F.deep") { DefaultValue = "now keyed" };

		// F.deep is still seen as keyless (as it was at construction), so it cuts
		Assert.True(cuts.Cuts(tree.Children.First(), 0));
	}
}
