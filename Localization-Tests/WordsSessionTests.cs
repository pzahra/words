using PatTech.Localization.Authoring;
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
			WordsFile source = session.Load(new StringReader(Main), Path.Combine(folder, "Main.ini"));
			string outPath = Path.Combine(folder, "Main.fr.ini");

			WordsFile split = session.Split(source, "fr", KeyTree.Build(session, source), outPath);

			Assert.Equal("Main-fr", split.Label);
			Assert.Equal(["fr"], split.Languages);
			string text = File.ReadAllText(outPath);
			Assert.StartsWith("; about Main", text);
			Assert.Contains("value-fr=Français", text);
			Assert.DoesNotContain("value-en=", text);
			Assert.Contains("; the file menu", text); //comments ride along
			Assert.Equal("Bonjour", session.Keys["Main-fr.greeting"].Entries["fr"].Value);
			Assert.Equal("Hello", session.Keys["Main-fr.greeting"].DefaultValue); //reference kept

			//and merge takes it straight back
			WordsFile? merged = session.Merge(source, new Dictionary<string, WordsFile> { ["fr"] = split }, KeyTree.Build(session, source), Path.Combine(folder, "Back.ini"), out _);
			Assert.NotNull(merged);
			Assert.Equal("Ouvrir", session.Keys["Back.menu.file.open"].Entries["fr"].Value);
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
