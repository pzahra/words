using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using PatTech.Text.Ini;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     Save as a patch (the editor's SPEC: Save as a patch): Save edits the file as it was read
///     until it reads as the writer's text, so a hand-formatted file keeps its formatting but for
///     what was edited; what Wordsmith writes whole it writes canonically, through the driver.
/// </summary>
public sealed class SaveAsPatchTests : IDisposable {
	private readonly string folder = Path.Combine(Path.GetTempPath(), $"SaveAsPatch-{Guid.NewGuid():N}");

	public SaveAsPatchTests() => Directory.CreateDirectory(folder);

	public void Dispose() {
		IniFiles.Step = null;
		Directory.Delete(folder, recursive: true);
	}

	//laid out by hand: no blank line between some blocks, two between others, fields out of
	//the writer's order, full headers where the writer chains, comments it would place apart
	private const string Hand = """
		; my words
		value=!en
		value-fr=Français

		[greeting]
		value-fr=Bonjour
		value=Hello


		[farewell]
		value=Bye
		[menu.file]
		value=File
		; about edit
		[menu.edit]
		value=Edit

		""";

	[Fact]
	public void AFieldSet_ChangesItsLineAlone() {
		var (session, file, path) = Load(Hand);

		session.Set(file, "farewell", new WordsField("value", "", ""), "Goodbye");
		Save(session, file);

		Assert.Equal(Hand.Replace("value=Bye", "value=Goodbye"), Read(path));
	}

	[Fact]
	public void AFieldEmptied_IsRemoved() {
		var (session, file, path) = Load(Hand);

		session.Set(file, "greeting", new WordsField("value", "fr", ""), null);
		Save(session, file);

		Assert.Equal(Hand.Replace("value-fr=Bonjour\n", ""), Read(path));
	}

	[Fact]
	public void AFieldAdded_GoesWhereTheWriterPutsIt_AmongFieldsInItsOrder() {
		var (session, file, path) = Load(Hand);

		session.Set(file, "farewell", new WordsField("value", "fr", ""), "Au revoir");
		Save(session, file);

		Assert.Equal(Hand.Replace("value=Bye\n", "value=Bye\nvalue-fr=Au revoir\n"), Read(path));
	}

	[Fact]
	public void AKeyRemoved_TakesItsBlockAlone() {
		var (session, file, path) = Load(Hand);

		session.RemoveKeysUnder(file, "farewell");
		Save(session, file);

		Assert.Equal(Hand.Replace("[farewell]\nvalue=Bye\n", ""), Read(path));
	}

	[Fact]
	public void AKeyRenamed_RenamesItsHeader_InPlace() {
		var (session, file, path) = Load(Hand);

		Assert.True(session.TryRename(file, "greeting", "hello", out _));
		Save(session, file, Tree(file, "hello", "farewell", "menu.file", "menu.edit"));

		Assert.Equal(Hand.Replace("[greeting]", "[hello]"), Read(path));
	}

	[Fact]
	public void AKeyRenamedOntoTheNameOfOneRemoved_RenamesItsHeader_KeepingItsLines() {
		var (session, file, path) = Load("[a]\nvalue = Alpha\n\n\n[b]\nvalue=Beta\n");

		session.RemoveKeysUnder(file, "b");
		Assert.True(session.TryRename(file, "a", "b", out _));
		Save(session, file);

		Assert.Equal("[b]\nvalue = Alpha\n", Read(path));
	}

	[Fact]
	public void TwoKeysSwappingNames_SwapTheirHeaders_KeepingTheirLines() {
		var (session, file, path) = Load("[a]\nvalue = Alpha\n\n[b]\nvalue = Beta\n");

		Assert.True(session.TryRename(file, "a", "swap", out _));
		Assert.True(session.TryRename(file, "b", "a", out _));
		Assert.True(session.TryRename(file, "swap", "b", out _));
		Save(session, file, Tree(file, "b", "a"));

		Assert.Equal("[b]\nvalue = Alpha\n\n[a]\nvalue = Beta\n", Read(path));
	}

	[Fact]
	public void AKeyDragged_MovesItsBlock_AfterTheKeyBeforeIt() {
		var (session, file, path) = Load(Hand);

		Save(session, file, Tree(file, "farewell", "greeting", "menu.file", "menu.edit"));

		Assert.Equal(Canonical(session, file, Tree(file, "farewell", "greeting", "menu.file", "menu.edit")), Reread(path));
		Assert.Contains("[farewell]\nvalue=Bye\n", Read(path));
		Assert.Contains("[greeting]\nvalue-fr=Bonjour\nvalue=Hello\n", Read(path));
	}

	[Fact]
	public void ACommentEdited_IsSetInPlace() {
		var (session, file, path) = Load(Hand);

		Save(session, file, Tree(file, comments: new() { ["menu.edit"] = " about editing" }));

		Assert.Equal(Hand.Replace("; about edit\n", "; about editing\n"), Read(path));
	}

	[Fact]
	public void ACommentAdded_GoesAboveItsBlock_AndOneRemovedGoes() {
		var (session, file, path) = Load(Hand);

		Save(session, file, Tree(file, comments: new() { ["farewell"] = " parting" }));

		Assert.Equal(Hand.Replace("[farewell]", "; parting\n[farewell]").Replace("; about edit\n", ""), Read(path));
	}

	[Fact]
	public void ThePreambleAndTheTrailer_EachSave() {
		var (session, file, path) = Load(Hand);

		file.Preamble = " my words, edited";
		Save(session, file, Tree(file, trailer: " the end"));

		Assert.Equal(Hand.Replace("; my words\n", "; my words, edited\n") + "; the end\n", Read(path));
	}

	[Fact]
	public void ALanguageAdded_AndASettingsReference_EachSetTheirField() {
		var (session, file, path) = Load(Hand);

		session.Languages.Add(file, new LanguageEntry("de", "Deutsch"));
		file.Settings = "words.settings.ini";
		Save(session, file);

		Assert.Equal(Hand.Replace("value-fr=Français\n", "value-fr=Français\nvalue-de=Deutsch\nparam=words.settings.ini\n"), Read(path));
	}

	[Fact]
	public void TwoKeysOfNamesThatDifferInCase_AreTwoKeys() {
		const string text = "[A]\nvalue=1\n\n\n[a]\nvalue=2\n";
		var (session, file, path) = Load(text);

		session.Set(file, "a", new WordsField("value", "", ""), "two");
		Save(session, file);

		Assert.Equal("[A]\nvalue=1\n\n\n[a]\nvalue=two\n", Read(path));
	}

	[Fact]
	public void AFileWithNothingChanged_SavesItsBytes() {
		var (session, file, path) = Load(Hand);

		Save(session, file);

		Assert.Equal(Hand, Read(path));
	}

	//the fixtures and Wordsmith's own words: the file reads back as the writer would write it,
	//and a value set changes that value's lines alone, but in a file Core gripes about, which
	//saves whole (MergeTestFile.ini declares a field twice)
	public static TheoryData<string> Fixtures => new() {
		"WordsEdit/Resources/words.ini",
		"Localization-Authoring/authoring-words.ini",
		"Localization-Authoring/Codecs/csv.words.ini",
		"Localization-Authoring/Codecs/ini.words.ini",
		"Localization-Authoring/Codecs/resx.words.ini",
		"Localization-Authoring/Codecs/xliff.words.ini",
		"LocalizedSample/localized-words.ini",
		"Sample-Ava/Assets/framework-words.ini",
		"Sample-Console/Assets/words.ini",
		"Sample-Shared/sample-words.ini",
		"Sample-Wpf/Assets/framework-words.ini",
		"WordsEdit.Tests/Resources/ExampleFile.ini",
		"WordsEdit.Tests/Resources/MergeTestFile.ini",
		"WordsEdit.Tests/Resources/MergeTestFile2.ini",
	};

	[Theory]
	[MemberData(nameof(Fixtures))]
	public void AFixture_PatchesTheValueSet_AndReadsAsTheWriterWrites(string fixture) {
		byte[] original = File.ReadAllBytes(Path.Combine(Repo, fixture));
		string path = Path.Combine(folder, Path.GetFileName(fixture));
		File.WriteAllBytes(path, original);
		var session = new WordsSession();
		WordsFile file = session.Load(path);
		WordsKey key = file.Keys.Values.First(key => key.DefaultValue != "");

		session.Set(file, key.BlockKey, new WordsField("value", "", ""), "Patched");
		Save(session, file);

		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Reread(path));
		var gripes = new WordsParserToLocalizationProvider();
		new WordsParser(gripes).Load(new StringReader(Encoding.UTF8.GetString(original)));
		if (gripes.Errors.Count > 0) {
			Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(path));
			return;
		}
		string[] was = Lines(Encoding.UTF8.GetString(original)), now = Lines(Read(path));
		int head = was.Zip(now).TakeWhile(pair => pair.First == pair.Second).Count();
		int tail = Enumerable.Reverse(was).Zip(Enumerable.Reverse(now)).Take(Math.Min(was.Length, now.Length) - head).TakeWhile(pair => pair.First == pair.Second).Count();
		//what changed is lines of one block: no header and no blank line among them
		Assert.All(was[head..^tail].Concat(now[head..^tail]), line => Assert.False(line == "" || line.StartsWith('['), $"{fixture}: '{line}' changed"));
	}

	[Fact]
	public void BytesItsEncodingCannotRead_SaveWhole() {
		string path = Path.Combine(folder, "words.ini");
		File.WriteAllBytes(path, [.. "; caf"u8, 0xE9, .. "\n[a]\nvalue=A\n\n\n[b]\nvalue=B\n"u8]);
		var session = new WordsSession();
		WordsFile file = session.Load(path);

		session.Set(file, "b", new WordsField("value", "", ""), "Bee");
		Save(session, file);

		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(path));
	}

	[Fact]
	public void AFileThatReadsOtherwiseWhenPatched_SavesWhole() {
		//a field declared twice gripes; the writer writes it once
		const string text = "[a]\nvalue=A\nvalue=again\n\n\n[b]\nvalue=B\n";
		var (session, file, path) = Load(text);

		session.Set(file, "b", new WordsField("value", "", ""), "Bee");
		Save(session, file);

		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(path));
	}

	[Fact]
	public void AFileChangedOnDiskSinceItWasRead_IsRefused_AndSavedOverWhole() {
		var (session, file, path) = Load(Hand);
		File.WriteAllText(path, Hand + "[other]\nvalue=Theirs\n");

		session.Set(file, "farewell", new WordsField("value", "", ""), "Goodbye");
		var refused = Assert.Throws<IniCommitException>(() => Save(session, file));

		Assert.Equal(IniCommitFailure.Changed, refused.Failure);
		Assert.Equal(Hand + "[other]\nvalue=Theirs\n", Read(path));
		session.Save(file, KeyTree.Build(file), path);
		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(path));
	}

	[Fact]
	public void AFileGoneSinceItWasRead_IsWrittenWhole() {
		var (session, file, path) = Load(Hand);
		File.Delete(path);

		session.Set(file, "farewell", new WordsField("value", "", ""), "Goodbye");
		Save(session, file);

		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(path));
	}

	[Fact]
	public void APendingSave_LeavesItsLog_AndTheNextSaveFinishesIt_RefusingNothing() {
		var (session, file, path) = Load(Hand);
		session.Set(file, "farewell", new WordsField("value", "", ""), "Goodbye");

		IniFiles.Step = step => {
			if (step == "writing") {
				throw new IOException("the file went away");
			}
		};
		Assert.Throws<IniPendingException>(() => Save(session, file));
		IniFiles.Step = null;
		Assert.Equal(Hand, Read(path));

		session.Set(file, "greeting", new WordsField("value", "", ""), "Hi");
		Save(session, file);

		Assert.Equal(Hand.Replace("value=Bye", "value=Goodbye").Replace("value=Hello", "value=Hi"), Read(path));
		Assert.Equal(["words.ini"], Directory.GetFiles(folder).Select(name => Path.GetFileName(name)));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void WhatIsWrittenWhole_IsTheWritersText_InTheSourcesEncoding(bool there) {
		var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
		string source = Path.Combine(folder, "source.ini");
		File.WriteAllBytes(source, [.. encoding.GetPreamble(), .. encoding.GetBytes(Hand)]);
		var session = new WordsSession();
		WordsFile file = session.Load(source);
		string[] targets = [.. new[] { "saved-as.ini", "merged.ini", "split.ini" }.Select(name => Path.Combine(folder, name))];
		if (there) {
			foreach (string target in targets) {
				File.WriteAllText(target, "[old]\nvalue=Old\n");
			}
		}

		session.Save(file, KeyTree.Build(file), targets[0]);
		WordsFile merged = session.Merge(file, new Dictionary<string, WordsFile>(), KeyTree.Build(file), targets[1], out _)!;
		WordsFile split = session.Split(file, "fr", KeyTree.Build(file), targets[2]);

		Assert.Equal([.. encoding.GetPreamble(), .. encoding.GetBytes(Canonical(session, file, KeyTree.Build(file)))], File.ReadAllBytes(targets[0]));
		Assert.Equal([.. encoding.GetPreamble(), .. encoding.GetBytes(Canonical(session, merged, KeyTree.Build(merged)))], File.ReadAllBytes(targets[1]));
		Assert.Equal([.. encoding.GetPreamble(), .. encoding.GetBytes(Canonical(session, split, KeyTree.Build(split)))], File.ReadAllBytes(targets[2]));
		Assert.Equal(Hand, encoding.GetString(File.ReadAllBytes(source)[2..]));
	}

	[Fact]
	public void AMergeOrASplitLeftPending_IsLoadedAsWritten_AndItsSaveFinishesIt() {
		var (session, file, _) = Load(Hand);
		string merged = Path.Combine(folder, "merged.ini"), split = Path.Combine(folder, "split.ini");

		IniFiles.Step = step => {
			if (step == "writing") {
				throw new IOException("the file went away");
			}
		};
		Assert.Throws<IniPendingException>(() => session.Merge(file, new Dictionary<string, WordsFile>(), KeyTree.Build(file), merged, out _));
		Assert.Throws<IniPendingException>(() => session.Split(file, "fr", KeyTree.Build(file), split));
		IniFiles.Step = null;

		foreach (string path in (string[])[merged, split]) {
			Assert.False(File.Exists(path));
			WordsFile written = session.FileAt(path)!;
			string text = Canonical(session, written, KeyTree.Build(written));
			Save(session, written);
			Assert.Equal(text, Read(path));
		}
	}

	[Fact]
	public void SavedAsAtItsOwnPath_TheFileIsWrittenInTheWritersForm() {
		var (session, file, path) = Load(Hand);

		session.Save(file, KeyTree.Build(file), path);

		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(path));
	}

	[Fact]
	public void AnImportedFile_IsWrittenWhole_OverTheIniThere() {
		string resx = Path.Combine(folder, "Strings.resx");
		File.WriteAllText(resx, """
			<?xml version="1.0" encoding="utf-8"?>
			<root>
			  <data name="greeting" xml:space="preserve"><value>Hello</value></data>
			</root>
			""");
		var session = new WordsSession();
		var codec = new ResxCodec();
		WordsFile file = session.Import(codec, codec.Discover(resx));
		File.WriteAllText(file.Path!, Hand);

		Save(session, file);

		Assert.Equal(Canonical(session, file, KeyTree.Build(file)), Read(file.Path!));
	}

	private (WordsSession Session, WordsFile File, string Path) Load(string text) {
		string path = Path.Combine(folder, "words.ini");
		File.WriteAllText(path, text);
		var session = new WordsSession();
		return (session, session.Load(path), path);
	}

	private static void Save(WordsSession session, WordsFile file, IKeyTreeNode? tree = null) => session.Save(file, tree ?? KeyTree.Build(file));

	//the file's tree with its keys in order, its comments and trailer as loaded but where given
	private static KeyTreeNode Tree(WordsFile file, params string[] order) => Tree(file, order, null, null);

	private static KeyTreeNode Tree(WordsFile file, Dictionary<string, string>? comments = null, string? trailer = null) => Tree(file, [], comments, trailer);

	private static KeyTreeNode Tree(WordsFile file, string[] order, Dictionary<string, string>? comments, string? trailer)
		=> KeyTree.Build(order.Length == 0 ? file.Keys.Values : order.Select(name => file.Keys[name]), comments ?? file.BlockComments, trailer ?? file.Trailer);

	private static string Canonical(WordsSession session, WordsFile file, IKeyTreeNode tree) {
		var text = new StringWriter();
		session.Save(file, tree, text);
		return text.ToString();
	}

	//the file at path read again and written as the writer writes it
	private static string Reread(string path) {
		var session = new WordsSession();
		WordsFile file = session.Load(path);
		return Canonical(session, file, KeyTree.Build(file));
	}

	private static string Read(string path) => File.ReadAllText(path);

	private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

	private static string Repo {
		get {
			string? at = AppContext.BaseDirectory;
			while (at is not null && !File.Exists(Path.Combine(at, "Words.slnx"))) {
				at = Path.GetDirectoryName(at);
			}
			return at ?? throw new DirectoryNotFoundException("Words.slnx");
		}
	}
}
