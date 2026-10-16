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
		session.Save(file, KeyTree.Build(file), output);
		return output.ToString();
	}

	[Fact]
	public void Load_HoldsTheFilesKeysByName_DropsEmptyOnes_AndKeepsDocumentOrder() {
		var session = Load(Main);
		WordsFile file = Assert.Single(session.Files);

		Assert.Equal("Main", file.Name);
		Assert.Equal("Main", file.Path);
		Assert.Equal(["greeting", "menu.file", "menu.file.open"], file.Keys.Keys);
		Assert.False(file.Keys.ContainsKey("menu")); //the bare header is a group, not a key
		Assert.Equal(file.Keys.Values, session.AllKeys);
		Assert.Equal(" about Main", file.Preamble);
		Assert.Equal(["en", "fr"], file.Languages);
		Assert.Equal(" the file menu", file.BlockComments["menu.file"]);
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
		// another file's tree, or one whose root is labelled, would write the wrong keys
		var session = Load(Main);
		WordsFile file = session.Files[0];
		WordsFile other = session.Load(new StringReader("value-en=English\n\n[other]\nvalue=x\n"), "Other");

		var ex = Assert.Throws<InvalidOperationException>(() => session.Save(file, KeyTree.Build(other), new StringWriter()));
		Assert.Contains("a file is written from its own tree", ex.Message);
		ex = Assert.Throws<InvalidOperationException>(() => session.Save(file, KeyTree.Build(other.Keys.Values, other.BlockComments), new StringWriter()));
		Assert.Contains("does not match its keys", ex.Message); //a tree from keys alone, which its keys tell
		var labelled = new KeyTreeNode("Main", "Main") { IsFile = true };
		ex = Assert.Throws<InvalidOperationException>(() => session.Save(file, labelled, new StringWriter()));
		Assert.Contains("is no file's", ex.Message);
	}

	[Fact]
	public void Save_RefusesATreeMissingSomeOfTheFilesKeys() {
		// the writer only emits keys the walk reaches; a short tree would silently
		// drop the rest, so it is refused before anything is written
		var session = Load(Main);
		WordsFile file = session.Files[0];
		var partial = KeyTree.Build(session.KeysOf(file).Take(1), file.BlockComments);

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
			var partial = KeyTree.Build(session.KeysOf(file).Take(1), file.BlockComments);

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

			session.Save(file, KeyTree.Build(file));

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
			session.FileNamed("Main").Keys["greeting"].DefaultValue = "Hello \uD83D";

			Assert.Throws<EncoderFallbackException>(() => session.Save(file, KeyTree.Build(file)));

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
			session.Save(file, KeyTree.Build(file));

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
		session.FileNamed("One").Keys["greeting"].DefaultValue = "Hi";

		Assert.Equal(3, session.KeysOf(one).Count());
		Assert.Equal(3, session.KeysOf(two).Count());
		Assert.Equal("Hello", session.FileNamed("Two").Keys["greeting"].DefaultValue);
		Assert.Equal("greeting", loaded.WordKeys["greeting"].BlockKey);
		Assert.Equal("Hello", loaded.WordKeys["greeting"].DefaultValue);
	}

	[Fact]
	public void Reload_ReplacesInPlaceAndDropsKeysDeletedOnDisk() {
		var session = Load(Main);
		session.Load(new StringReader("value-en=English\n\n[other]\nvalue=O\n"), "Other");

		//the same path again, now without [menu.file.open], and with [greeting] moved last
		session.Load(new StringReader("value-en=English\n\n[menu.file]\nvalue=File\n\n[greeting]\nvalue=Hi\n"), "Main");

		Assert.Equal(["Main", "Other"], session.Files.Select(file => file.Name)); //kept its place
		Assert.False(session.FileNamed("Main").Keys.ContainsKey("menu.file.open"));
		Assert.Equal("Hi", session.FileNamed("Main").Keys["greeting"].DefaultValue);
		//document order, not the old slots: a plain Dictionary would come back reversed
		Assert.Equal(["menu.file", "greeting"], session.KeysOf(session.Files[0]).Select(key => key.BlockKey));
		//fr was declared by nobody else and holds no words any more: gone
		Assert.DoesNotContain(session.Languages.Known, language => language.Code == "fr");
		Assert.All(session.AllKeys, key => Assert.False(key.Entries.ContainsKey("fr")));
	}

	[Fact]
	public void Load_TwoFilesWithTheSameName_StayTwo_EachHoldingItsOwnKeys() {
		// two strings.ini in different folders used to write over each other
		var session = new WordsSession();
		string a = Path.Combine("one", "strings.ini");
		string b = Path.Combine("two", "strings.ini");
		WordsFile one = session.Load(new StringReader("value-en=English\n\n[k]\nvalue=A\n"), a);
		WordsFile two = session.Load(new StringReader("value-en=English\n\n[k]\nvalue=B\n"), b);

		Assert.Equal(["strings", "strings"], session.Files.Select(file => file.Name)); //never numbered
		Assert.Equal("A", one.Keys["k"].DefaultValue);
		Assert.Equal("B", two.Keys["k"].DefaultValue);
		Assert.Same(one, session.FileAt(a));
		Assert.Same(two, session.FileAt(b));

		//a dotted file name keeps its dots: no key carries it
		WordsFile dotted = session.Load(new StringReader("[k]\nvalue=C\n"), "strings.v2.ini");
		Assert.Equal("strings.v2", dotted.Name);
		Assert.Equal(["k"], dotted.Keys.Keys);
	}

	[Fact]
	public void Load_FileWithKeysAndNoLabelsKeepsADefaultLanguage() {
		// this used to leave the session with no language at all
		var session = Load("[k]\nvalue=x\n", "Bare");

		LanguageEntry only = Assert.Single(session.Languages.Known);
		Assert.Equal("en", only.Code);
		Assert.True(session.FileNamed("Bare").Keys["k"].Entries.ContainsKey("en"));
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
		Assert.True(session.FileNamed("Main").Keys["a"].Entries.ContainsKey("eo"));
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

	private static string[] Table(WordsSession session, WordsFile file)
		=> [.. Save(session, file).Split('\n').Select(line => line.TrimEnd('\r')).TakeWhile(line => line != "")];

	[Fact]
	public void Labels_ARelabelIsTheFilesAlone() {
		var session = Load("value-en=English\nvalue-fr=Français\ncomment-fr=French\n\n[k]\nvalue=x\n");
		WordsFile main = session.Files[0];
		WordsFile library = session.Load(new StringReader("value-en=!English\nvalue-fr=!Français\n\n[j]\nvalue=y\n"), "Lib");

		session.Languages.Rename(main, "en", new LanguageEntry("en", "British"));
		session.Languages.Rename(library, "fr", new LanguageEntry("fr", "!Français") { EnglishName = "French (France)" });

		Assert.Equal(["value-en=British", "value-fr=Français", "comment-fr=French"], Table(session, main));
		Assert.Equal(["value-en=!English", "value-fr=!Français", "comment-fr=French (France)"], Table(session, library));
		Assert.Equal("British", session.Languages.Find("en")!.NativeName); //the session names it as its one listing file does
	}

	[Fact]
	public void Labels_AHostsRelabelLeavesItsLibrarysBang_AndALibrarysLeavesItsHostListed() {
		//the library loads first, yet the host's listed label names de; neither file's
		//relabel reaches the other, so the library's ! comes and goes on its own
		var session = new WordsSession();
		WordsFile library = session.Load(new StringReader("value-en=!English\nvalue-de=!Deutsch\n\n[j]\nvalue=y\n"), "Lib");
		WordsFile host = session.Load(new StringReader("value-en=English\nvalue-de=Deutsch\n\n[k]\nvalue=x\n"), "Host");
		Assert.Equal("Deutsch", session.Languages.Find("de")!.NativeName); //the listed label names it

		session.Languages.Rename(host, "de", new LanguageEntry("de", "Deutsch (DE)"));
		Assert.Equal(["value-en=English", "value-de=Deutsch (DE)"], Table(session, host));
		Assert.Equal(["value-en=!English", "value-de=!Deutsch"], Table(session, library));

		session.Languages.Rename(library, "de", new LanguageEntry("de", "Deutsch"));
		Assert.False(library.IsLibrary); //it lists de now, and only it
		session.Languages.Rename(library, "de", new LanguageEntry("de", "!Deutsch"));
		Assert.True(library.IsLibrary);
		Assert.Equal(["value-en=English", "value-de=Deutsch (DE)"], Table(session, host));
		Assert.Equal("Deutsch (DE)", session.Languages.Find("de")!.NativeName); //still listed
	}

	[Fact]
	public void Languages_ARecodeCarriesTheSettingsReference_AndARemovalTakesIt() {
		var session = Load("value-en=English\nvalue-it=Italiano\nparam-en=english-settings.ini\nparam-it=italian-settings.ini\n\n[k]\nvalue=x\n");
		WordsFile file = session.Files[0];

		session.Languages.Rename(file, "en", new LanguageEntry("de", "Deutsch"));
		session.Languages.Remove(file, "it");

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
		session.FileNamed("Main").Keys["greeting"].Entries["de"].Value = "Hallo";

		Assert.True(session.Unload(extra));

		Assert.Equal(["Main"], session.Files.Select(file => file.Name));
		Assert.DoesNotContain(extra.Keys["x"], session.AllKeys);
		//de has words in a remaining key: it stays
		Assert.Contains(session.Languages.Known, language => language.Code == "de");

		session.FileNamed("Main").Keys["greeting"].Entries["de"].Value = "";
		session.Unload(session.Files[0]);
		Assert.Empty(session.Files);
		Assert.Empty(session.AllKeys);
		Assert.Equal("en", Assert.Single(session.Languages.Known).Code);
	}

	//the host named Italian for the dropdown; once it leaves, the languages left read
	//as a load of the library alone would: its order, its hidden names
	[Fact]
	public void Unload_RelabelsTheLanguagesLeft_AsTheRemainingFilesNameThem() {
		var session = new WordsSession();
		WordsFile host = session.Load(new StringReader("value-en=English\nvalue-it=Italiano\ncomment-it=Italian\nvalue-de=Deutsch\n\n[k]\nvalue=x\n"), "Host");
		session.Load(new StringReader("value-it=!Italiano\nvalue-en=!English\n\n[j]\nvalue=y\n"), "Lib");
		Assert.Equal(["en", "it", "de"], session.Languages.Known.Select(language => language.Code));
		Assert.Equal("Italian", session.Languages.Find("it")!.DisplayName);

		Assert.True(session.Unload(host));

		Assert.Equal(["it", "en"], session.Languages.Known.Select(language => language.Code));
		LanguageEntry italian = session.Languages.Find("it")!;
		Assert.Equal("!Italiano", italian.NativeName);
		Assert.Equal("", italian.EnglishName);
		Assert.Equal("!English", session.Languages.Find("en")!.NativeName);
	}

	//the languages left after an unload, each as code=native/english
	private static string[] Named(WordsSession session) => [.. session.Languages.Known.Select(language => $"{language.Code}={language.NativeName}/{language.EnglishName}")];

	//what a load of these files alone names the languages
	private static string[] NamedByALoadOf(params string[] inis) {
		var session = new WordsSession();
		foreach (string ini in inis) {
			session.Load(new StringReader(ini), $"F{session.Files.Count}");
		}
		return Named(session);
	}

	//the load's own precedence, file by file: a language the rest only use reads !code,
	//and a real label upgrades a placeholder, unlisted though both are
	[Theory]
	[InlineData("value-en=English\n\n[u]\nvalue=U\nvalue-it=Ui\n", null)]
	[InlineData("value-it=!it\n\n[a]\nvalue=A\n", "value-it=!Italiano\n\n[b]\nvalue=B\n")]
	public void Unload_NamesTheLanguagesLeft_AsALoadOfTheRestWould(string first, string? second) {
		string[] rest = second is null ? [first] : [first, second];
		var session = new WordsSession();
		WordsFile host = session.Load(new StringReader("value-it=Italian\ncomment-it=Italian\n\n[h]\nvalue=H\n"), "Host");
		foreach (string ini in rest) {
			session.Load(new StringReader(ini), $"F{session.Files.Count}");
		}

		session.Unload(host);

		Assert.Equal(NamedByALoadOf(rest), Named(session));
	}

	[Fact]
	public void Keys_AddRemoveAndRemoveUnderKeepTheInvariantAndSpareSimilarNames() {
		var session = Load(Main);
		WordsFile main = session.Files[0];

		WordsKey added = session.AddKey(main, "menu");
		Assert.Same(added, session.AddKey(main, "menu"));
		Assert.Equal(["en", "fr"], added.Entries.Keys.Order(StringComparer.Ordinal));

		session.AddKey(main, "menu.filer");
		Assert.Equal(2, session.RemoveKeysUnder(main, "menu.file"));
		Assert.Equal(["greeting", "menu", "menu.filer"], main.Keys.Keys.Order(StringComparer.Ordinal));
		Assert.True(session.RemoveKey(main, "menu"));
		Assert.False(session.RemoveKey(main, "menu"));
	}

	[Fact]
	public void Languages_AddRemoveReorderRoundTrip_FileByFile() {
		var session = Load(Main);
		session.Load(new StringReader("value-en=English\n\n[x]\nvalue=X\n"), "Extra");
		WordsFile main = session.Files[0];
		WordsFile extra = session.Files[1];

		//the file declares it, the other file doesn't, every key has an entry
		Assert.True(session.Languages.Add(main, new LanguageEntry("de", "Deutsch") { EnglishName = "German" }));
		Assert.False(session.Languages.Add(main, new LanguageEntry("de", "again")));
		Assert.Equal(["en", "fr", "de"], main.Languages);
		Assert.Equal(["en"], extra.Languages);
		Assert.All(session.AllKeys, key => Assert.True(key.Entries.ContainsKey("de")));
		Assert.DoesNotContain("value-de", Save(session, extra));
		Assert.True(session.Languages.Add(extra, new LanguageEntry("de", "!Deutsch")));
		Assert.Contains("value-de=!Deutsch", Save(session, extra));

		//the file's order; the dropdown follows the files', the first file's first
		session.Languages.Reorder(main, 2, 0);
		Assert.Equal(["de", "en", "fr"], main.Languages);
		Assert.Equal(["en", "de"], extra.Languages);
		Assert.Equal(["de", "en", "fr"], session.Languages.Known.Select(language => language.Code));
		string saved = Save(session, main);
		Assert.True(saved.IndexOf("value-de") < saved.IndexOf("value-en"));

		//a removal is the file's: its keys lose their words, the other file's keep theirs
		session.FileNamed("Main").Keys["greeting"].Entries["de"].Value = "Hallo";
		session.FileNamed("Extra").Keys["x"].Entries["de"].Value = "Ix";
		Assert.True(session.Languages.Remove(main, "de"));
		Assert.Equal(["en", "fr"], main.Languages);
		Assert.Equal("", session.FileNamed("Main").Keys["greeting"].Entries["de"].Value);
		Assert.Equal("Ix", session.FileNamed("Extra").Keys["x"].Entries["de"].Value);
		Assert.Contains(session.Languages.Known, language => language.Code == "de");
		//the last file to declare it takes it out of the session
		Assert.True(session.Languages.Remove(extra, "de"));
		Assert.DoesNotContain(session.Languages.Known, language => language.Code == "de");
		Assert.All(session.AllKeys, key => Assert.False(key.Entries.ContainsKey("de")));

		//a file keeps its last language
		Assert.False(session.Languages.Remove(extra, "en"));
		Assert.False(session.Languages.Remove(extra, "fr")); //nor drops one it never declared
	}

	[Fact]
	public void Lacks_WhatAHostListsThatALibraryHasNoWordsFor() {
		var session = new WordsSession();
		WordsFile host = session.Load(new StringReader("value-en=English\nvalue-en-AU=Australian\nvalue-fr=Français\nvalue-de=Deutsch\nvalue-it=!Italiano\n\n[k]\nvalue=x\n"), "Host");
		WordsFile library = session.Load(new StringReader("value-en=!English\nvalue-fr=!Français\n\n[j]\nvalue=y\n"), "Lib");
		session.Load(new StringReader("value-es=Español\nvalue-de=Deutsch\n\n[i]\nvalue=z\n"), "Other");
		session.Load(new StringReader("value-en=English\nvalue-de=!Deutsch\nvalue-es=!Español\n\n[h]\nvalue=w\n"), "Mute");
		string[] Lacks(WordsFile file) => [.. session.Languages.Lacks(file).Select(lack => $"{lack.Code}: {string.Join(", ", lack.Hosts)}")];

		//en and fr it declares, en-AU falls back to its en, it is hidden: no host offers it;
		//Mute hides de and es too, so it isn't named as offering them
		Assert.Equal(["de: Host, Other", "es: Other"], Lacks(library));
		Assert.Empty(Lacks(host)); //no library
		session.Languages.Add(library, new LanguageEntry("de", "!Deutsch"));
		library.DefaultLanguage = "es"; //its default speaks Spanish
		Assert.Empty(Lacks(library));
	}

	[Fact]
	public void Languages_ARecodeIsTheFilesAlone() {
		var session = Load("value-en=English\nvalue-de=Deutsch\n\n[k]\nvalue=x\nvalue-de=Ix\n");
		WordsFile host = session.Files[0];
		WordsFile library = session.Load(new StringReader("value-en=!English\nvalue-de=!Deutsch\n\n[j]\nvalue=y\nvalue-de=Ypsilon\n"), "Lib");

		session.Languages.Rename(host, "de", new LanguageEntry("de-DE", "Deutsch (Deutschland)"));

		Assert.Equal(["en", "de-DE"], host.Languages);
		Assert.Equal(["en", "de"], library.Languages); //another host may want its de
		Assert.Equal("Ix", session.FileNamed("Main").Keys["k"].Entries["de-DE"].Value);
		Assert.Equal("", session.FileNamed("Main").Keys["k"].Entries["de"].Value);
		Assert.Equal("Ypsilon", session.FileNamed("Lib").Keys["j"].Entries["de"].Value);
		Assert.Equal(["en", "de-DE", "de"], session.Languages.Known.Select(language => language.Code));
	}

	[Fact]
	public void Languages_RenameRecodesEntriesAndFilesOrAbsorbs() {
		var session = Load("value-en=English\nvalue-en-GB=British\n\n[k]\nvalue=x\nvalue-en=family\nvalue-en-GB=regional\n\n[j]\nvalue=y\nvalue-en-GB=only regional\n");
		WordsFile main = session.Files[0];

		//a relabel keeps the code: the label is replaced, nothing shifts
		LanguageEntry relabelled = session.Languages.Rename(main, "en", new LanguageEntry("en", "English (US)"));
		Assert.Same(relabelled, session.Languages.Find("en"));
		Assert.Equal("English (US)", relabelled.NativeName);
		Assert.Equal("family", session.FileNamed("Main").Keys["k"].Entries["en"].Value);

		//re-coding onto a language the file declares absorbs into it
		LanguageEntry survivor = session.Languages.Rename(main, "en-GB", new LanguageEntry("en", "English"));
		Assert.Same(relabelled, survivor);
		Assert.Equal(["en"], session.Languages.Known.Select(language => language.Code));
		Assert.Equal(["en"], main.Languages);
		WordsKey collided = session.FileNamed("Main").Keys["k"];
		Assert.Equal("family", collided.Entries["en"].Value);
		Assert.Equal("regional", collided.Entries["en"].Context);
		Assert.NotNull(collided.Entries["en"].Stale);
		Assert.Equal("only regional", session.FileNamed("Main").Keys["j"].Entries["en"].Value);
		Assert.All(session.AllKeys, key => Assert.False(key.Entries.ContainsKey("en-GB")));

		//re-coding onto a new code moves the file's declaration
		session.Languages.Rename(main, "en", new LanguageEntry("eo", "Esperanto"));
		Assert.Equal(["eo"], main.Languages);
		Assert.Equal("family", session.FileNamed("Main").Keys["k"].Entries["eo"].Value);
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

		session.Languages.Rename(file, "en", new LanguageEntry("en-AU", "Australian"));
		Assert.Equal("en-AU", file.DefaultLanguage);
		session.Languages.Remove(file, "en-AU");
		Assert.Null(file.DefaultLanguage);
		Assert.DoesNotContain("value=", Save(session, file).Split('\n')[0]);

		file.DefaultLanguage = "fr";
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

			WordsFile? merged = session.Merge(basis, new Dictionary<string, WordsFile> { ["fr"] = french }, KeyTree.Build(basis), outPath, out var conflicts);

			Assert.NotNull(merged);
			Assert.Empty(conflicts);
			Assert.Equal("Merged", merged.Name);
			Assert.Equal(3, session.Files.Count);
			Assert.Equal("ix", session.FileNamed("Merged").Keys["k"].Entries["fr"].Value);
			Assert.Equal("ex", session.FileNamed("Merged").Keys["k"].Entries["en"].Value);
			string text = File.ReadAllText(outPath);
			Assert.StartsWith("; the base", text);
			Assert.Contains("value-en=English", text);
			Assert.Contains("value-fr=Français", text);
			Assert.Contains("param=wordsmith.ini", text);
			Assert.Equal(["en", "fr"], merged.Languages);

			//a disagreement writes nothing
			session.Load(new StringReader("value-de=Deutsch\n\n[k]\nvalue-de=ix\n\n[extra]\nvalue-de=!\n"), Path.Combine(folder, "German.ini"));
			string refused = Path.Combine(folder, "Refused.ini");
			Assert.Null(session.Merge(basis, new Dictionary<string, WordsFile> { ["de"] = session.Files[3] }, KeyTree.Build(basis), refused, out conflicts));
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

			WordsFile split = session.Split(source, "fr", KeyTree.Build(source), outPath);

			Assert.Equal("Main.fr", split.Name);
			Assert.Equal("en", split.DefaultLanguage); //the defaults kept for reference are still English
			Assert.Equal(["fr"], split.Languages);
			string text = File.ReadAllText(outPath);
			Assert.StartsWith("; about Main", text);
			Assert.Contains("value-fr=Français", text);
			Assert.DoesNotContain("value-en=", text);
			Assert.Contains("; the file menu", text); //comments ride along
			Assert.DoesNotContain('\r', text); //and the source's line break
			Assert.Equal("Bonjour", split.Keys["greeting"].Entries["fr"].Value);
			Assert.Equal("Hello", split.Keys["greeting"].DefaultValue); //reference kept

			//and merge takes it straight back
			WordsFile? merged = session.Merge(source, new Dictionary<string, WordsFile> { ["fr"] = split }, KeyTree.Build(source), Path.Combine(folder, "Back.ini"), out _);
			Assert.NotNull(merged);
			Assert.Equal("Ouvrir", session.FileNamed("Back").Keys["menu.file.open"].Entries["fr"].Value);
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
			Assert.Throws<ArgumentException>(() => session.Merge(basis, new Dictionary<string, WordsFile> { ["fr"] = english }, KeyTree.Build(basis), merged, out _));
			Assert.False(File.Exists(merged));

			//a split takes the language as its file labels it, and only its settings reference
			WordsFile library = session.Load(new StringReader("value-fr=!Français\nparam=all.ini\nparam-fr=french.ini\n\n[greeting]\nvalue-fr=Salut\n"), Path.Combine(folder, "Lib.ini"));
			session.Load(new StringReader("value-de=Deutsch\nparam-de=german.ini\n\n[x]\nvalue=X\n"), Path.Combine(folder, "Other.ini"));
			session.Languages.Rename(basis, "fr", new LanguageEntry("fr", "Français") { EnglishName = "French" });
			library.LanguageSettings["de"] = "german.ini"; //a stray reference the split leaves behind
			WordsFile split = session.Split(library, "fr", KeyTree.Build(library), Path.Combine(folder, "Lib.fr.ini"));
			string text = File.ReadAllText(split.Path!);
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
	public void AnotherFilesTree_TheSameKeysAndAll_IsRefused_BySaveExportMergeAndSplit() {
		// its keys match, but it would write one file's words in the other's order,
		// under the other's comments
		string folder = Path.Combine(Path.GetTempPath(), $"WordsSessionOwner-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var session = new WordsSession();
			WordsFile mine = session.Load(new StringReader(Main), Path.Combine(folder, "Mine.ini"));
			WordsFile theirs = session.Load(new StringReader(Main), Path.Combine(folder, "Theirs.ini"));
			WordsFile french = session.Load(new StringReader("value-fr=Français\n\n[greeting]\nvalue-fr=Salut\n"), Path.Combine(folder, "French.ini"));
			IKeyTreeNode their = KeyTree.Build(theirs);
			string merged = Path.Combine(folder, "Merged.ini"), split = Path.Combine(folder, "Split.ini");

			Assert.Contains("a file is written from its own tree", Assert.Throws<InvalidOperationException>(() => session.Save(mine, their, new StringWriter())).Message);
			Assert.Throws<InvalidOperationException>(() => new ExportSource(session, mine, their));
			Assert.Throws<InvalidOperationException>(() => session.Merge(mine, new Dictionary<string, WordsFile> { ["fr"] = french }, their, merged, out _));
			Assert.Throws<InvalidOperationException>(() => session.Split(mine, "fr", their, split));

			Assert.False(File.Exists(merged));
			Assert.False(File.Exists(split));
			Assert.Equal(3, session.Files.Count);
			session.Save(mine, KeyTree.Build(mine), new StringWriter()); //its own goes
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
			var partial = KeyTree.Build(session.KeysOf(source).Take(1), source.BlockComments);
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
		WordsFile main = session.Files[0], over = session.Files[1];

		//a key is its name, read as the runtime reads it, the later file winning, with no
		//prefix to probe for
		Assert.Equal("second", session.Provider([main, over]).GetValue("k"));
		Assert.Equal("first", session.Provider([over, main]).GetValue("k"));
		Assert.Equal("premier", session.Provider([main], "fr").GetValue("k"));
		Assert.Equal("first", session.Provider([main], "de").GetValue("k")); //falls back to the default
		Assert.False(session.Provider([main, over]).ContainsKey("Main.k"));

		//the key a preview shows reads its own words, though a later file has its name
		Assert.Equal("first", session.Provider([main, over], main.Keys["k"]).GetValue("k"));
		Assert.Equal("premier", session.Provider([main, over], main.Keys["k"], "fr").GetValue("k"));
	}

	//the editor's SPEC (Keys without the file's name): each file holds its own keys, so
	//two holding the same key stay apart, each saving its own, and a key moved from one
	//to the other leaves the one and joins the other
	[Fact]
	public void TwoFilesHoldingOneKey_StayApart_EachSavingItsOwn() {
		const string Base = "value-en=English\n\n[k]\nvalue=base\n\n[g.a]\nvalue=A\n\n";
		var session = new WordsSession();
		WordsFile basis = session.Load(new StringReader(Base), "Base");
		WordsFile copy = session.Load(new StringReader("value-en=English\n\n[k]\nvalue=copy\n\n"), "Copy");

		Assert.NotSame(basis.Keys["k"], copy.Keys["k"]);
		session.AddKey(copy, "fresh").DefaultValue = "new";
		Assert.False(basis.Keys.ContainsKey("fresh"));
		Assert.Equal(Base, Save(session, basis).ReplaceLineEndings("\n"));
		Assert.Equal("value-en=English\n\n[k]\nvalue=copy\n\n[fresh]\nvalue=new\n\n", Save(session, copy).ReplaceLineEndings("\n"));

		//across files: all or nothing, nothing overwritten
		Assert.False(session.TryMove(basis, "k", copy, "", out var collisions));
		Assert.Equal(["k"], collisions);
		Assert.True(session.TryMove(basis, "g.a", copy, "h", out _));
		Assert.Equal(["k"], basis.Keys.Keys);
		Assert.Equal("A", copy.Keys["h.a"].DefaultValue);
		Assert.Equal("h.a", copy.Keys["h.a"].BlockKey);
		Assert.Equal(3, session.RemoveKeysUnder(copy, "")); //an empty name is the whole file
		Assert.Empty(copy.Keys);
		Assert.Equal(["k"], basis.Keys.Keys);
	}

	//the editor's SPEC (New): a file started with no path, named as it was given and
	//never numbered, renamed in place, and given a path at its first Save
	[Fact]
	public void ANewFile_HasNoPath_UntilMovedTo_ItsKeysUntouchedByItsName() {
		var session = new WordsSession();
		WordsFile first = session.New(new StringReader("value-en=English\n\n[k]\nvalue=x\n"), "anonymous-words");
		WordsFile second = session.New(new StringReader(""), "anonymous-words");

		Assert.Null(first.Path);
		Assert.Equal("anonymous-words", first.Name);
		Assert.Equal("anonymous-words", session.ShownName(second)); //never numbered: Save tells them apart
		WordsKey k = Assert.Single(session.KeysOf(first));
		Assert.Equal("k", k.BlockKey);
		Assert.Null(session.FileAt("anonymous-words.ini")); //a file not saved is at no path
		Assert.Throws<InvalidOperationException>(() => session.Save(first, KeyTree.Build(first)));

		//a root's name is its file's alone: the key stays as it was
		session.Rename(first, "Strings.Designer");
		Assert.Equal("Strings.Designer", first.Name); //dots and all
		Assert.Same(k, Assert.Single(session.KeysOf(first)));
		Assert.Equal("k", k.BlockKey);
		session.Rename(second, "Strings.Designer"); //another's name too
		Assert.Equal("Strings.Designer", session.ShownName(second));
		Assert.Throws<ArgumentException>(() => session.Rename(first, "a/b")); //no file name
		Assert.Throws<ArgumentException>(() => session.Rename(first, " "));

		string folder = Path.Combine(Path.GetTempPath(), "WordsSessionMove");
		session.MoveTo(first, Path.Combine(folder, "Words.ini"));
		Assert.Equal(Path.Combine(folder, "Words.ini"), first.Path);
		Assert.Equal("Words", first.Name); //named as a file loaded from there
		//saved, its name is its file's: it changes as the file is saved elsewhere
		Assert.Throws<InvalidOperationException>(() => session.Rename(first, "Strings"));
		Assert.Equal(("Words", Path.Combine(folder, "Words.ini")), (first.Name, first.Path));
		Assert.Same(k, Assert.Single(session.KeysOf(first)));
		Assert.Same(first, session.FileAt(Path.Combine(folder, "Words.ini")));
		Assert.Throws<ArgumentException>(() => session.MoveTo(second, Path.Combine(folder, "Words.ini"))); //another's path

		session.MoveTo(second, Path.Combine(folder, "sub", "Words.ini"));
		Assert.Equal("Words", second.Name); //never numbered
		Assert.Equal("Words (sub)", session.ShownName(second));
		Assert.Equal("Words (WordsSessionMove)", session.ShownName(first));
		session.MoveTo(first, Path.Combine(folder, "other", "Words.ini"));
		Assert.Equal("Words (other)", session.ShownName(first));
	}

	//the editor's SPEC (Keys without the file's name): a root shows its file's true name,
	//and where another has it too, the first folder, walking up, where their paths differ
	[Fact]
	public void AFile_ShowsItsTrueName_AndATwinTheFolderTellingItApart() {
		var session = new WordsSession();
		WordsFile ui = session.Load(new StringReader(""), @"C:\app\ui\strings.ini");
		Assert.Equal("strings", session.ShownName(ui)); //a name no other has shows alone

		WordsFile core = session.Load(new StringReader(""), @"C:\app\core\strings.ini");
		Assert.Equal("strings (ui)", session.ShownName(ui));
		Assert.Equal("strings (core)", session.ShownName(core));

		//folders of one name: the nearest that differs
		WordsFile a = session.Load(new StringReader(""), @"C:\a\ui\Strings.Designer.ini");
		WordsFile b = session.Load(new StringReader(""), @"C:\b\ui\Strings.Designer.ini");
		Assert.Equal("Strings.Designer (a)", session.ShownName(a)); //dots and all
		Assert.Equal("Strings.Designer (b)", session.ShownName(b));

		//unloading one takes the brackets off the other
		session.Unload(core);
		Assert.Equal("strings", session.ShownName(ui));
	}

	//told apart together: two whose first telling folders read alike each take in the
	//next folder toward their own, then the one above
	[Fact]
	public void TwinsWhoseFoldersReadAlike_EachTakeInAFolderMore() {
		var session = new WordsSession();
		WordsFile alpha = session.Load(new StringReader(""), @"C:\alpha\ui\strings.ini");
		WordsFile beta = session.Load(new StringReader(""), @"C:\beta\ui\strings.ini");
		WordsFile other = session.Load(new StringReader(""), @"C:\other\alpha\strings.ini");

		Assert.Equal(["strings (alpha\\ui)", "strings (beta)", "strings (other\\alpha)"], session.Files.Select(session.ShownName)); //was (alpha) twice

		session.Unload(beta);
		Assert.Equal(["strings (ui)", "strings (alpha)"], session.Files.Select(session.ShownName)); //nothing alike, one folder each
	}

	//the editor's starter: a header above the table, the default's language declared
	[Fact]
	public void ANewFile_StartsWithItsHeader_AndItsDefaultsLanguage() {
		var session = new WordsSession();

		WordsFile file = session.New("anonymous-words", "An app's words.\r\n\r\nSave says where.", new LanguageEntry("en", "English"));

		Assert.Null(file.Path);
		Assert.Equal("en", file.DefaultLanguage);
		Assert.Equal(["en"], file.Languages);
		Assert.Empty(session.KeysOf(file));
		var output = new StringWriter();
		session.Save(file, KeyTree.Build(file), output);
		Assert.Equal("; An app's words.\n;\n; Save says where.\nvalue=!en\nvalue-en=English\n\n", output.ToString().ReplaceLineEndings("\n")); //in the system's breaks
	}
}
