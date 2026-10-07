using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The document without a UI: files by path, one ordered key store, the
///     language table, and the round trips through the writer.
/// </summary>
public class WordsSessionTests {
	//exactly what the writer emits (a comment- label per language, a blank line
	//after each block), so the round trip below is byte for byte
	private const string Main = @"; about Main
value-en=English
comment-en=English
value-fr=Français
comment-fr=French

[greeting]
value=Hello
value-fr=Bonjour

[menu]
; the file menu
[.file]
value=File
value-fr=Fichier

[.file.open]
value=Open
value-fr=Ouvrir

";

	private static WordsSession Load(string ini, string path = "Main") {
		var session = new WordsSession();
		session.Load(new StringReader(ini), path);
		return session;
	}

	private static string Save(WordsSession session, WordsFile file) {
		var output = new StringWriter();
		session.Save(file, KeyTree.Build(session, file), output);
		return output.ToString();
	}

	[Fact]
	public void Load_PrefixesKeysDropsEmptyOnesAndKeepsDocumentOrder() {
		var session = Load(Main);
		WordsFile file = Assert.Single(session.Files);

		Assert.Equal("Main", file.Label);
		Assert.Equal("Main", file.Path);
		Assert.Equal(["Main.greeting", "Main.menu.file", "Main.menu.file.open"], session.Keys.Keys);
		Assert.False(session.Keys.ContainsKey("Main.menu")); //the bare header is a group, not a key
		Assert.Equal(" about Main", file.Preamble);
		Assert.Equal(["en", "fr"], file.Languages);
		Assert.Equal(" the file menu", file.BlockComments["Main.menu.file"]);
		Assert.Empty(file.Errors);
	}

	[Fact]
	public void Load_RoundTripsByteForByte() {
		var session = Load(Main);

		Assert.Equal(Main, Save(session, session.Files[0]));
	}

	[Theory]
	[InlineData("\n")]
	[InlineData("\r\n")]
	public void Save_KeepsTheFilesLineBreak(string newLine) {
		//whichever the checkout gave Main, a file comes back with the breaks it went in with
		string ini = Main.ReplaceLineEndings(newLine);
		var session = Load(ini);

		Assert.Equal(newLine, session.Files[0].NewLine);
		Assert.Equal(ini, Save(session, session.Files[0]));
	}

	[Fact]
	public void Save_RefusesATreeThatIsNotTheFiles() {
		// a tree rooted on another label would write the wrong keys
		var session = Load(Main);
		WordsFile file = session.Files[0];
		var alien = KeyTree.Build("Other", session.KeysOf(file), file.BlockComments);

		var ex = Assert.Throws<InvalidOperationException>(() => session.Save(file, alien, new StringWriter()));
		Assert.Contains("does not own", ex.Message);
	}

	[Fact]
	public void Save_RefusesATreeMissingSomeOfTheFilesKeys() {
		// the writer only emits keys the walk reaches; a short tree would silently
		// drop the rest, so it is refused before anything is written
		var session = Load(Main);
		WordsFile file = session.Files[0];
		var partial = KeyTree.Build(file.Label, session.KeysOf(file).Take(1), file.BlockComments);

		var ex = Assert.Throws<InvalidOperationException>(() => session.Save(file, partial, new StringWriter()));
		Assert.Contains("missing", ex.Message);
	}

	[Fact]
	public void Save_LeavesTheFileOnDiskWhenTheTreeIsRejected() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionSave-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Main.ini");
			File.WriteAllText(path, "ORIGINAL");
			var session = new WordsSession();
			WordsFile file = session.Load(new StringReader(Main), path);
			var partial = KeyTree.Build(file.Label, session.KeysOf(file).Take(1), file.BlockComments);

			Assert.Throws<InvalidOperationException>(() => session.Save(file, partial));

			Assert.Equal("ORIGINAL", File.ReadAllText(path)); //untouched
			Assert.Empty(Directory.GetFiles(folder, "*.tmp")); //no temp left behind
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Theory]
	[InlineData("utf-8")]
	[InlineData("utf-8 BOM")]
	[InlineData("utf-16")]
	[InlineData("utf-16BE")]
	[InlineData("utf-32")]
	public void Save_KeepsTheFilesEncoding_BomAndAll(string name) {
		Encoding encoding = name switch {
			"utf-8" => new UTF8Encoding(false),
			"utf-8 BOM" => new UTF8Encoding(true),
			"utf-16" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
			"utf-16BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
			_ => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
		};
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionEncoding-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Main.ini");
			byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Main)];
			File.WriteAllBytes(path, bytes);
			var session = new WordsSession();
			WordsFile file = session.Load(path);

			session.Save(file, KeyTree.Build(session, file));

			Assert.Equal(bytes, File.ReadAllBytes(path));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Save_RefusesTextItsEncodingCannotHold_AndLeavesTheFile() {
		// a lone surrogate has no UTF-8: the encoder throws rather than write a
		// replacement character, and the file on disk stays as it was
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionEncoding-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Main.ini");
			File.WriteAllText(path, Main);
			var session = new WordsSession();
			WordsFile file = session.Load(path);
			session.Keys["Main.greeting"].DefaultValue = "Hello \uD83D";

			Assert.Throws<EncoderFallbackException>(() => session.Save(file, KeyTree.Build(session, file)));

			Assert.Equal(Main, File.ReadAllText(path));
			Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_OfAnIniIsLoadingIt_LineBreakAndEncodingKept() {
		// the line break the system doesn't use, and a BOM: both come back on Save
		string newLine = Environment.NewLine == "\n" ? "\r\n" : "\n";
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionImport-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Main.ini");
			var encoding = new UTF8Encoding(true);
			byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Main.ReplaceLineEndings(newLine))];
			File.WriteAllBytes(path, bytes);
			var session = new WordsSession();

			WordsFile file = session.Import(new IniCodec(), [path]);
			session.Save(file, KeyTree.Build(session, file));

			Assert.Equal(newLine, file.NewLine);
			Assert.Equal(bytes, File.ReadAllBytes(path));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Load_CopiesTheDocumentsKeys_SoItLoadsTwiceIntact() {
		// the store's keys are its own: loading one document as two files gives
		// each its keys, and leaves the document as it was read
		var loaded = new WordsParserToLocalizationProvider();
		new WordsParser(loaded).Load(new StringReader(Main));
		var session = new WordsSession();

		WordsFile one = session.Load(loaded, "One.ini");
		WordsFile two = session.Load(loaded, "Two.ini");
		session.Keys["One.greeting"].DefaultValue = "Hi";

		Assert.Equal(3, session.KeysOf(one).Count());
		Assert.Equal(3, session.KeysOf(two).Count());
		Assert.Equal("Hello", session.Keys["Two.greeting"].DefaultValue);
		Assert.Equal("greeting", loaded.WordKeys["greeting"].BlockKey);
		Assert.Equal("Hello", loaded.WordKeys["greeting"].DefaultValue);
	}

	[Fact]
	public void Reload_ReplacesInPlaceAndDropsKeysDeletedOnDisk() {
		var session = Load(Main);
		session.Load(new StringReader("value-en=English\n\n[other]\nvalue=O\n"), "Other");

		//the same path again, now without [menu.file.open], and with [greeting] moved last
		session.Load(new StringReader("value-en=English\n\n[menu.file]\nvalue=File\n\n[greeting]\nvalue=Hi\n"), "Main");

		Assert.Equal(["Main", "Other"], session.Files.Select(file => file.Label)); //kept its place
		Assert.False(session.Keys.ContainsKey("Main.menu.file.open"));
		Assert.Equal("Hi", session.Keys["Main.greeting"].DefaultValue);
		//document order, not the old slots: a plain Dictionary would come back reversed
		Assert.Equal(["Main.menu.file", "Main.greeting"], session.KeysOf(session.Files[0]).Select(key => key.BlockKey));
		//fr was declared by nobody else and holds no words any more: gone
		Assert.DoesNotContain(session.Languages.Known, language => language.Code == "fr");
		Assert.All(session.Keys.Values, key => Assert.False(key.Entries.ContainsKey("fr")));
	}

	[Fact]
	public void Load_TwoFilesWithTheSameNameGetDistinctLabels() {
		// two strings.ini in different folders used to write over each other
		var session = new WordsSession();
		string a = Path.Combine("one", "strings.ini");
		string b = Path.Combine("two", "strings.ini");
		session.Load(new StringReader("value-en=English\n\n[k]\nvalue=A\n"), a);
		session.Load(new StringReader("value-en=English\n\n[k]\nvalue=B\n"), b);

		Assert.Equal(["strings", "strings-2"], session.Files.Select(file => file.Label));
		Assert.Equal("A", session.Keys["strings.k"].DefaultValue);
		Assert.Equal("B", session.Keys["strings-2.k"].DefaultValue);
		Assert.Same(session.Files[1], session.FileAt(b));
		Assert.Same(session.Files[1], session.FileOf("strings-2"));
		Assert.Same(session.Files[0], session.FileOfKey("strings.k"));

		//a dotted file name would confuse the writer's one-segment prefix: dots go
		session.Load(new StringReader("[k]\nvalue=C\n"), "strings.v2.ini");
		Assert.Equal("strings-v2", session.Files[2].Label);
	}

	[Fact]
	public void Load_FileWithKeysAndNoLabelsKeepsADefaultLanguage() {
		// this used to leave the session with no language at all
		var session = Load("[k]\nvalue=x\n", "Bare");

		LanguageEntry only = Assert.Single(session.Languages.Known);
		Assert.Equal("en", only.Code);
		Assert.True(session.Keys["Bare.k"].Entries.ContainsKey("en"));
		Assert.Empty(session.Files[0].Languages);
		Assert.True(session.Files[0].IsLibrary);
		Assert.DoesNotContain("value-en", Save(session, session.Files[0])); //declares nothing, writes no table
	}

	[Fact]
	public void Load_LibraryBesideAMainFileKeepsBothTables() {
		var session = Load("value-en=English\n\n[a]\nvalue=A\n");
		WordsFile lib = session.Load(new StringReader("value-en=!English\nvalue-eo=!Esperanto\n\n[b]\nvalue=B\n"), "Lib");

		Assert.True(lib.IsLibrary);
		Assert.False(session.Files[0].IsLibrary);
		Assert.Equal(["en", "eo"], session.Languages.Known.Select(language => language.Code));
		Assert.Equal(["en"], session.Languages.For(session.Files[0]).Select(language => language.Code));
		Assert.Equal(["en", "eo"], session.Languages.For(lib).Select(language => language.Code));
		//the union backfills every key, so any known code indexes any key
		Assert.True(session.Keys["Main.a"].Entries.ContainsKey("eo"));
		Assert.DoesNotContain("value-eo", Save(session, session.Files[0]));
	}

	[Fact]
	public void Load_CommentLabelBeforeItsValueLabelIsAGripeNotACrash() {
		var session = Load("comment-fr=French\nvalue-fr=Français\ncomment-xx=Never named\n\n[k]\nvalue=x\n");
		WordsFile file = session.Files[0];

		Assert.Equal(2, file.Errors.Count);
		LanguageEntry fr = session.Languages.Find("fr")!;
		Assert.Equal("Français", fr.NativeName);
		Assert.Equal("French", fr.EnglishName);
		//named by a comment only: a placeholder, known but never declared or written
		Assert.True(session.Languages.Find("xx")!.IsPlaceholder);
		Assert.Equal(["fr"], file.Languages);
		Assert.DoesNotContain("value-xx", Save(session, file));
	}

	[Fact]
	public void Labels_WriteAnExonymOnlyWhereOneWasGiven() {
		var session = Load("value-en=English\nvalue-fr=Français\ncomment-fr=French\n\n[k]\nvalue=x\n");
		LanguageEntry en = session.Languages.Find("en")!;
		string saved = Save(session, session.Files[0]);

		Assert.Equal("", en.EnglishName);
		Assert.Equal("English", en.DisplayName);
		Assert.DoesNotContain("comment-en", saved);
		Assert.Contains("comment-fr=French", saved);
		//a later file's exonym fills in where the union had none; one already there stands
		session.Load(new StringReader("value-en=English\ncomment-en=Inglese\nvalue-fr=Francese\ncomment-fr=Francese\n\n[x]\nvalue=X\n"), "Extra");
		Assert.Equal("Inglese", en.EnglishName);
		Assert.Equal("French", session.Languages.Find("fr")!.EnglishName);
		//an empty session's language has no exonym to write either
		Assert.Equal("", LanguageTable.Default().EnglishName);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Labels_AreEachFilesOwn_ALibraryKeepsItsBangAndNoExonymLeaks(bool libraryFirst) {
		// whichever loads first, each file writes the labels it was read with: the
		// library its !, the host its exonym, neither the other's
		const string host = "value-en=English\nvalue-fr=Français\ncomment-fr=French\n\n[k]\nvalue=x\n\n";
		const string library = "value-en=!English\nvalue-fr=!Français\n\n[j]\nvalue=y\n\n";
		var session = new WordsSession();
		WordsFile hostFile, libraryFile;
		if (libraryFirst) {
			libraryFile = session.Load(new StringReader(library), "Lib");
			hostFile = session.Load(new StringReader(host), "Host");
		}
		else {
			hostFile = session.Load(new StringReader(host), "Host");
			libraryFile = session.Load(new StringReader(library), "Lib");
		}

		Assert.Equal(host, Save(session, hostFile).ReplaceLineEndings("\n"));
		Assert.Equal(library, Save(session, libraryFile).ReplaceLineEndings("\n"));
	}

	[Fact]
	public void Labels_ARelabelReachesEveryFileWithWhatItChanged() {
		var session = Load("value-en=English\nvalue-fr=Français\ncomment-fr=French\n\n[k]\nvalue=x\n");
		WordsFile library = session.Load(new StringReader("value-en=!English\nvalue-fr=!Français\n\n[j]\nvalue=y\n"), "Lib");

		//a new endonym: each file keeps its own !
		session.Languages.Rename("en", new LanguageEntry("en", "British"));
		//a new exonym: every file takes it, the library too
		session.Languages.Rename("fr", new LanguageEntry("fr", "Français") { EnglishName = "French (France)" });

		string main = Save(session, session.Files[0]), lib = Save(session, library);
		Assert.Contains("value-en=British", main);
		Assert.Contains("value-en=!British", lib);
		Assert.Contains("comment-fr=French (France)", main);
		Assert.Contains("comment-fr=French (France)", lib);
		Assert.Contains("value-fr=!Français", lib);
	}

	[Fact]
	public void Languages_ARecodeCarriesTheSettingsReference_AndARemovalTakesIt() {
		var session = Load("value-en=English\nvalue-it=Italiano\nparam-en=english-settings.ini\nparam-it=italian-settings.ini\n\n[k]\nvalue=x\n");
		WordsFile file = session.Files[0];

		session.Languages.Rename("en", new LanguageEntry("de", "Deutsch"));
		session.Languages.Remove("it");

		Assert.Equal(new Dictionary<string, string> { ["de"] = "english-settings.ini" }, file.LanguageSettings);
		Assert.NotNull(file.SettingsPath("de"));
		string saved = Save(session, file);
		Assert.Contains("param-de=english-settings.ini", saved);
		Assert.DoesNotContain("param-en", saved);
		Assert.DoesNotContain("param-it", saved);
	}

	[Fact]
	public void Unload_TakesTheKeysAndPrunesLanguagesNobodyHasLeft() {
		var session = Load(Main);
		WordsFile extra = session.Load(new StringReader("value-en=English\nvalue-de=Deutsch\n\n[x]\nvalue=X\nvalue-de=Ix\n"), "Extra");
		//a German word in Main's key, though Main never declared de
		session.Keys["Main.greeting"].Entries["de"].Value = "Hallo";

		Assert.True(session.Unload(extra));

		Assert.Equal(["Main"], session.Files.Select(file => file.Label));
		Assert.DoesNotContain(session.Keys.Keys, key => key.StartsWith("Extra."));
		//de has words in a remaining key: it stays
		Assert.Contains(session.Languages.Known, language => language.Code == "de");

		session.Keys["Main.greeting"].Entries["de"].Value = "";
		session.Unload(session.Files[0]);
		Assert.Empty(session.Files);
		Assert.Empty(session.Keys);
		Assert.Equal("en", Assert.Single(session.Languages.Known).Code);
	}

	[Fact]
	public void Keys_AddRemoveAndRemoveUnderKeepTheInvariantAndSpareSimilarNames() {
		var session = Load(Main);

		WordsKey added = session.AddKey("Main.menu");
		Assert.Same(added, session.AddKey("Main.menu"));
		Assert.Equal(["en", "fr"], added.Entries.Keys.Order(StringComparer.Ordinal));

		session.AddKey("Main.menu.filer");
		Assert.Equal(2, session.RemoveKeysUnder("Main.menu.file"));
		Assert.Equal(["Main.greeting", "Main.menu", "Main.menu.filer"], session.Keys.Keys.Order(StringComparer.Ordinal));
		Assert.True(session.RemoveKey("Main.menu"));
		Assert.False(session.RemoveKey("Main.menu"));
	}

	[Fact]
	public void Languages_AddRemoveReorderRoundTrip() {
		var session = Load(Main);
		session.Load(new StringReader("value-en=English\n\n[x]\nvalue=X\n"), "Extra");
		WordsFile main = session.Files[0];
		WordsFile extra = session.Files[1];

		Assert.True(session.Languages.Add(new LanguageEntry("de", "Deutsch") { EnglishName = "German" }));
		Assert.False(session.Languages.Add(new LanguageEntry("de", "again")));
		Assert.Equal(["en", "fr", "de"], main.Languages);
		Assert.Equal(["en", "de"], extra.Languages);
		Assert.All(session.Keys.Values, key => Assert.True(key.Entries.ContainsKey("de")));
		Assert.Contains("value-de=Deutsch", Save(session, extra));

		//the dropdown order becomes every file's order
		session.Languages.Reorder(2, 0);
		Assert.Equal(["de", "en", "fr"], session.Languages.Known.Select(language => language.Code));
		Assert.Equal(["de", "en", "fr"], main.Languages);
		Assert.Equal(["de", "en"], extra.Languages);
		string saved = Save(session, main);
		Assert.True(saved.IndexOf("value-de") < saved.IndexOf("value-en"));

		Assert.True(session.Languages.Remove("de"));
		Assert.Equal(["en", "fr"], main.Languages);
		Assert.All(session.Keys.Values, key => Assert.False(key.Entries.ContainsKey("de")));

		//never empty
		Assert.True(session.Languages.Remove("fr"));
		Assert.False(session.Languages.Remove("en"));
		Assert.Single(session.Languages.Known);
	}

	[Fact]
	public void Languages_RenameRecodesEntriesAndFilesOrAbsorbs() {
		var session = Load("value-en=English\nvalue-en-GB=British\n\n[k]\nvalue=x\nvalue-en=family\nvalue-en-GB=regional\n\n[j]\nvalue=y\nvalue-en-GB=only regional\n");
		WordsFile main = session.Files[0];

		//a relabel keeps the code: the entry is replaced, nothing shifts
		LanguageEntry relabelled = session.Languages.Rename("en", new LanguageEntry("en", "English (US)"));
		Assert.Same(relabelled, session.Languages.Find("en"));
		Assert.Equal("family", session.Keys["Main.k"].Entries["en"].Value);

		//re-coding onto an existing language absorbs into it
		LanguageEntry survivor = session.Languages.Rename("en-GB", new LanguageEntry("en", "English"));
		Assert.Same(relabelled, survivor);
		Assert.Equal(["en"], session.Languages.Known.Select(language => language.Code));
		Assert.Equal(["en"], main.Languages);
		WordsKey collided = session.Keys["Main.k"];
		Assert.Equal("family", collided.Entries["en"].Value);
		Assert.Equal("regional", collided.Entries["en"].Context);
		Assert.NotNull(collided.Entries["en"].Stale);
		Assert.Equal("only regional", session.Keys["Main.j"].Entries["en"].Value);
		Assert.All(session.Keys.Values, key => Assert.False(key.Entries.ContainsKey("en-GB")));

		//re-coding onto a new code moves the file's declaration
		session.Languages.Rename("en", new LanguageEntry("eo", "Esperanto"));
		Assert.Equal(["eo"], main.Languages);
		Assert.Equal("family", session.Keys["Main.k"].Entries["eo"].Value);
	}

	//value=!xx says which language the default is written in: no language of the
	//table's, written first, carried by a split and moved by the table's recodes
	private const string Declared = @"value=!en
value-en=English
comment-en=English
value-fr=Français
comment-fr=French

[greeting]
value=Hello
value-fr=Bonjour

";

	[Fact]
	public void DefaultLanguage_RoundTripsAndFollowsTheTable() {
		var session = Load(Declared);
		WordsFile file = session.Files[0];

		Assert.Equal("en", file.DefaultLanguage);
		Assert.Equal(["en", "fr"], file.Languages);
		Assert.Empty(file.Errors);
		Assert.Equal(Declared, Save(session, file));
		Assert.Equal("en", session.Languages.DefaultLanguage);

		session.Languages.Rename("en", new LanguageEntry("en-AU", "Australian"));
		Assert.Equal("en-AU", file.DefaultLanguage);
		session.Languages.Remove("en-AU");
		Assert.Null(file.DefaultLanguage);
		Assert.DoesNotContain("value=", Save(session, file).Split('\n')[0]);

		session.Languages.DefaultLanguage = "fr";
		Assert.StartsWith("value=!fr" + file.NewLine, Save(session, file)); //the fixture's line break, whichever the checkout gave it
	}

	[Fact]
	public void DefaultLanguage_WithoutItsMarkIsTakenWithAGripe_AndWrittenWithIt() {
		var session = Load("value=en\nvalue-fr=Français\n\n[k]\nvalue=x\n");
		WordsFile file = session.Files[0];

		Assert.Equal("en", file.DefaultLanguage);
		Assert.Equal(["fr"], file.Languages); //never a language with no code
		Assert.Contains(file.Errors, error => error.Contains("value=!en"));
		Assert.StartsWith("value=!en\n", Save(session, file));
	}

	[Fact]
	public void Merge_WritesTheBaseFilesTablePreambleAndSchemesThenLoads() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionMerge-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var session = new WordsSession();
			WordsFile basis = session.Load(new StringReader("; the base\nvalue-en=English\nparam=wordsmith.ini\n\n[k]\nvalue=x\nvalue-en=ex\n\n[k.sub]\nvalue=s\n"), Path.Combine(folder, "Base.ini"));
			WordsFile french = session.Load(new StringReader("value-fr=Français\n\n[k]\nvalue-fr=ix\n\n[k.sub]\nvalue-fr=esse\n"), Path.Combine(folder, "French.ini"));
			string outPath = Path.Combine(folder, "Merged.ini");

			WordsFile? merged = session.Merge(basis, new Dictionary<string, WordsFile> { ["fr"] = french }, KeyTree.Build(session, basis), outPath, out var conflicts);

			Assert.NotNull(merged);
			Assert.Empty(conflicts);
			Assert.Equal("Merged", merged.Label);
			Assert.Equal(3, session.Files.Count);
			Assert.Equal("ix", session.Keys["Merged.k"].Entries["fr"].Value);
			Assert.Equal("ex", session.Keys["Merged.k"].Entries["en"].Value);
			string text = File.ReadAllText(outPath);
			Assert.StartsWith("; the base", text);
			Assert.Contains("value-en=English", text);
			Assert.Contains("value-fr=Français", text);
			Assert.Contains("param=wordsmith.ini", text);
			Assert.Equal(["en", "fr"], merged.Languages);

			//a disagreement writes nothing
			session.Load(new StringReader("value-de=Deutsch\n\n[k]\nvalue-de=ix\n\n[extra]\nvalue-de=!\n"), Path.Combine(folder, "German.ini"));
			string refused = Path.Combine(folder, "Refused.ini");
			Assert.Null(session.Merge(basis, new Dictionary<string, WordsFile> { ["de"] = session.Files[3] }, KeyTree.Build(session, basis), refused, out conflicts));
			Assert.Contains("extra", conflicts);
			Assert.False(File.Exists(refused));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void SettingsFor_LayersTheLanguageFileOverTheDictionarysAndFollowsChanges() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionSettings-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string projectFile = Path.Combine(folder, "wordsmith.ini");
			File.WriteAllText(projectFile, "[images]\nshot=shots\nshot-decode=/^shot:(\\w+)$//$1.png\n[hyperlinks]\nhelp=popup\n");
			File.WriteAllText(Path.Combine(folder, "wordsmith-de.ini"), "[images]\nshot=shots-de\n");
			var session = new WordsSession();
			WordsFile file = session.Load(new StringReader("value-en=English\nvalue-de=Deutsch\nparam=wordsmith.ini\nparam-de=wordsmith-de.ini\n\n[k]\nvalue=x\n"), Path.Combine(folder, "strings.ini"));
			Assert.Equal(projectFile, file.SettingsPath());
			Assert.Equal(Path.Combine(folder, "wordsmith-de.ini"), file.SettingsPath("de"));
			Assert.Null(file.SettingsPath("fr"));
			Assert.Empty(file.Errors);

			ProjectSettings plain = session.SettingsFor(file);
			Assert.True(plain.TryLocate(new Uri("shot:Login"), out string root, out string path));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots")), root);
			Assert.Equal("Login.png", path);
			Assert.Same(plain, session.SettingsFor(file)); //the same rules while nothing changed
			Assert.Same(plain, session.SettingsFor(file, "fr")); //no file for fr: the dictionary's alone

			ProjectSettings german = session.SettingsFor(file, "de");
			Assert.True(german.TryLocate(new Uri("shot:Login"), out root, out path));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots-de")), root); //the folder from the language's file…
			Assert.Equal("Login.png", path); //…the decode from the dictionary's
			Assert.Same(german, session.SettingsFor(file, "de"));

			//the settings file changes on disk: the next ask sees it, layered or not
			File.WriteAllText(projectFile, "[images]\nshot=elsewhere\nshot-decode=/^shot:(\\w+)$//$1.png\n");
			File.SetLastWriteTimeUtc(projectFile, DateTime.UtcNow.AddMinutes(1));
			Assert.True(session.SettingsFor(file).TryLocate(new Uri("shot:Login"), out root, out _));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "elsewhere")), root);
			Assert.NotSame(german, session.SettingsFor(file, "de"));
			Assert.True(session.SettingsFor(file, "de").TryLocate(new Uri("shot:Login"), out root, out _));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots-de")), root);

			//a file naming no settings has none; one naming a file that is not there has a gripe
			WordsFile bare = session.Load(new StringReader("value-en=English\n\n[j]\nvalue=y\n"), Path.Combine(folder, "bare.ini"));
			Assert.Same(ProjectSettings.Empty, session.SettingsFor(bare));
			Assert.Same(ProjectSettings.Empty, session.SettingsFor(bare, "de"));
			WordsFile lost = session.Load(new StringReader("value-en=English\nparam=nowhere.ini\nparam-fr=fr.ini\n\n[j]\nvalue=y\n"), Path.Combine(folder, "lost.ini"));
			Assert.Single(session.SettingsFor(lost).Errors);
			Assert.Contains(lost.Errors, error => error.Contains("param-fr")); //a settings file for an undeclared language
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Split_WritesOneLanguageInTheSourcesShapeAndLoads() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionSplit-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var session = new WordsSession();
			WordsFile source = session.Load(new StringReader(Main.ReplaceLineEndings("\n")), Path.Combine(folder, "Main.ini"));
			string outPath = Path.Combine(folder, "Main.fr.ini");
			source.DefaultLanguage = "en";

			WordsFile split = session.Split(source, "fr", KeyTree.Build(session, source), outPath);

			Assert.Equal("Main-fr", split.Label);
			Assert.Equal("en", split.DefaultLanguage); //the defaults kept for reference are still English
			Assert.Equal(["fr"], split.Languages);
			string text = File.ReadAllText(outPath);
			Assert.StartsWith("; about Main", text);
			Assert.Contains("value-fr=Français", text);
			Assert.DoesNotContain("value-en=", text);
			Assert.Contains("; the file menu", text); //comments ride along
			Assert.DoesNotContain('\r', text); //and the source's line break
			Assert.Equal("Bonjour", session.Keys["Main-fr.greeting"].Entries["fr"].Value);
			Assert.Equal("Hello", session.Keys["Main-fr.greeting"].DefaultValue); //reference kept

			//and merge takes it straight back
			WordsFile? merged = session.Merge(source, new Dictionary<string, WordsFile> { ["fr"] = split }, KeyTree.Build(session, source), Path.Combine(folder, "Back.ini"), out _);
			Assert.NotNull(merged);
			Assert.Equal("Ouvrir", session.Keys["Back.menu.file.open"].Entries["fr"].Value);
			Assert.Equal("en", merged.DefaultLanguage);
			Assert.DoesNotContain('\r', File.ReadAllText(Path.Combine(folder, "Back.ini")));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Merge_RefusesASourceThatDoesNotDeclareItsLanguage_AndSplitWritesItsOwnLabel() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionSources-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var session = new WordsSession();
			WordsFile basis = session.Load(new StringReader(Main), Path.Combine(folder, "Main.ini"));
			WordsFile english = session.Load(new StringReader("value-en=English\n\n[greeting]\nvalue=Hello\n\n[menu.file]\nvalue=File\n\n[menu.file.open]\nvalue=Open\n"), Path.Combine(folder, "English.ini"));
			string merged = Path.Combine(folder, "Merged.ini");

			//its French entries are backfill: taking them would empty the base's
			Assert.Throws<ArgumentException>(() => session.Merge(basis, new Dictionary<string, WordsFile> { ["fr"] = english }, KeyTree.Build(session, basis), merged, out _));
			Assert.False(File.Exists(merged));

			//a split takes the language as its file labels it, and only its settings reference
			WordsFile library = session.Load(new StringReader("value-fr=!Français\nparam=all.ini\nparam-fr=french.ini\n\n[greeting]\nvalue-fr=Salut\n"), Path.Combine(folder, "Lib.ini"));
			session.Load(new StringReader("value-de=Deutsch\nparam-de=german.ini\n\n[x]\nvalue=X\n"), Path.Combine(folder, "Other.ini"));
			session.Languages.Rename("fr", new LanguageEntry("fr", "Français") { EnglishName = "French" });
			library.LanguageSettings["de"] = "german.ini"; //a stray reference the split leaves behind
			WordsFile split = session.Split(library, "fr", KeyTree.Build(session, library), Path.Combine(folder, "Lib.fr.ini"));
			string text = File.ReadAllText(split.Path);
			Assert.Contains("value-fr=!Français", text);
			Assert.Contains("param-fr=french.ini", text);
			Assert.DoesNotContain("param-de", text);
			Assert.Empty(split.Errors);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void MergeAndSplit_RefuseATreeMissingSomeOfTheFilesKeys() {
		// as Save does: the writer walks the tree, so a short one would write a
		// file with fewer keys, over an existing one
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionCover-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var session = new WordsSession();
			WordsFile source = session.Load(new StringReader(Main), Path.Combine(folder, "Main.ini"));
			WordsFile french = session.Load(new StringReader("value-fr=Français\n\n[greeting]\nvalue-fr=Salut\n"), Path.Combine(folder, "French.ini"));
			var partial = KeyTree.Build(source.Label, session.KeysOf(source).Take(1), source.BlockComments);
			string merged = Path.Combine(folder, "Merged.ini"), split = Path.Combine(folder, "Split.ini");
			File.WriteAllText(merged, "ORIGINAL");

			Assert.Throws<InvalidOperationException>(() => session.Merge(source, new Dictionary<string, WordsFile> { ["fr"] = french }, partial, merged, out _));
			Assert.Throws<InvalidOperationException>(() => session.Split(source, "fr", partial, split));

			Assert.Equal("ORIGINAL", File.ReadAllText(merged));
			Assert.False(File.Exists(split));
			Assert.Equal(2, session.Files.Count);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Provider_StacksFilesLaterWinning() {
		var session = Load("value-en=English\n\n[k]\nvalue=first\nvalue-fr=premier\n");
		session.Load(new StringReader("value-en=English\n\n[k]\nvalue=second\n"), "Over");

		Assert.Equal("second", session.Provider(["Main", "Over"]).GetValue("k"));
		Assert.Equal("first", session.Provider(["Over", "Main"]).GetValue("k"));
		Assert.Equal("premier", session.Provider(["Main"], "fr").GetValue("k"));
		Assert.Equal("first", session.Provider(["Main"], "de").GetValue("k")); //falls back to the default
	}
}
