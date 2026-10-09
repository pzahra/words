using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The flows that go through a dialog, driven headless with
///     <see cref="FakeDialogs"/>. Until the dialogs were injectable none of this
///     could be exercised without a window.
/// </summary>
public class DialogFlowTests {
	private const string Ini = @"
value-en=English
value-de=Deutsch

[k]
value=x
value-de=y
";

	private static (MainWindowViewModel vm, FakeDialogs dialogs) Load() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader(Ini), "Example");
		return (vm, dialogs);
	}

	[Fact]
	public void Reset_AsksFirst() {
		var (vm, dialogs) = Load();

		dialogs.ConfirmAnswer = false;
		vm.ResetCommand.Execute(null);
		Assert.Single(dialogs.Confirmations);
		Assert.NotEmpty(vm.Tree.KeyNodes);

		dialogs.ConfirmAnswer = true;
		vm.ResetCommand.Execute(null);
		Assert.Empty(vm.Tree.KeyNodes);
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void RemoveFile_AsksFirst() {
		var (vm, dialogs) = Load();
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];

		dialogs.ConfirmAnswer = false;
		vm.RemoveNodeCommand.Execute(null);
		Assert.Single(vm.Tree.KeyNodes);

		dialogs.ConfirmAnswer = true;
		vm.RemoveNodeCommand.Execute(null);
		Assert.Empty(vm.Tree.KeyNodes);
		Assert.Equal(2, dialogs.Confirmations.Count);
	}

	[Fact]
	public void LoadFiles_GoesThroughTheDialog() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		string path = Path.Combine(Path.GetTempPath(), $"WordsEditDialog-{Guid.NewGuid():N}.ini");
		File.WriteAllText(path, Ini);
		try {
			//cancelled: nothing happens
			vm.LoadFileCommand.Execute(null);
			Assert.Empty(vm.Tree.KeyNodes);

			dialogs.FilesToOpen = [path];
			vm.LoadFileCommand.Execute(null);
			Assert.Contains(vm.Session.Files, file => file.Path == path);
			Assert.Single(vm.Tree.KeyNodes);

			//a path that isn't there is told, not thrown
			dialogs.FilesToOpen = [Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.ini")];
			vm.LoadFileCommand.Execute(null);
			Assert.Single(dialogs.Notices);
			Assert.Single(vm.Tree.KeyNodes);
		}
		finally {
			File.Delete(path);
		}
	}

	[Fact]
	public void ManageLanguages_ShowsTheManager() {
		var (vm, dialogs) = Load();

		vm.ManageLanguagesCommand.Execute(null);

		Assert.IsType<LanguageManagerViewModel>(Assert.Single(dialogs.Shown));
	}

	[Fact]
	public void AddLanguage_OnOk_BackfillsEveryKey() {
		// + adds a blank row the pane fills in; nothing reaches the session until OK
		var (vm, dialogs) = Load();
		var manager = new LanguageManagerViewModel(vm);
		Assert.Equal("en", manager.Selected!.Code); //the pane starts on the tree's language

		manager.AddCommand.Execute(null);
		LanguageRow row = manager.Selected;
		Assert.Null(row.Origin);
		Assert.True(row.HasErrors); //blank: OK waits
		Assert.False(manager.OkCommand.CanExecute(null));
		Assert.Empty(row.GetErrors(null)); //but nothing is flagged until a field is typed in
		row.Code = "f";
		Assert.NotEmpty(row.GetErrors(nameof(LanguageRow.Code)));
		Assert.Empty(row.GetErrors(nameof(LanguageRow.NativeName)));
		row.Code = "fr";
		row.NativeName = "Français";
		Assert.False(row.HasErrors); //the name in the default's language may stay blank
		row.EnglishName = "French";
		Assert.False(row.HasErrors);
		Assert.True(manager.OkCommand.CanExecute(null));
		Assert.DoesNotContain(vm.Tree.KnownLanguages, l => l.Code == "fr"); //not yet

		bool closed = false;
		manager.CloseRequested += () => closed = true;
		manager.OkCommand.Execute(null);

		Assert.True(closed);
		Assert.Empty(dialogs.Shown); //nothing opened over the manager
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "fr");
		Assert.All(vm.Session.Keys.Values, key => Assert.True(key.Entries.ContainsKey("fr")));
		Assert.Equal(["en", "de", "fr"], vm.Session.Files[0].Languages);
		Assert.True(vm.IsDirty);
		Assert.Equal("fr", vm.Tree.SelectedLanguage.Code); //the highlighted row became the tree's language
	}

	//1.4.0's manager took two letters and one more subtag, so OK never lit for
	//these, and it told codes apart by case, so en-us beside en-US saved twice
	[Fact]
	public void LanguageManager_TakesAnyCode_CasedByKind_AndKnowsOneInAnotherCase() {
		var (vm, _) = Load();
		var manager = new LanguageManagerViewModel(vm);
		foreach (var (typed, name) in ((string, string)[])[("ceb", "Sinugboanon"), ("es-419", "Español"), ("zh-hans-cn", "简体中文")]) {
			manager.AddCommand.Execute(null);
			manager.Selected!.Code = typed;
			manager.Selected.NativeName = name;
			Assert.False(manager.Selected.HasErrors, typed);
		}
		manager.AddCommand.Execute(null);
		LanguageRow twin = manager.Selected!;
		twin.Code = "DE";
		twin.NativeName = "Deutsch (bis)";
		Assert.True(twin.HasErrors); //de is taken, whatever the case
		Assert.False(manager.OkCommand.CanExecute(null));
		twin.Code = "de-at";
		Assert.False(twin.HasErrors);

		manager.OkCommand.Execute(null);

		Assert.Equal(["en", "de", "ceb", "es-419", "zh-Hans-CN", "de-AT"], vm.Session.Files[0].Languages);
	}

	[Fact]
	public void LanguageManager_EditsACopyUntilOk() {
		var (vm, dialogs) = Load();
		var manager = new LanguageManagerViewModel(vm);
		LanguageRow german = manager.Rows.Single(row => row.Code == "de");
		german.NativeName = "Deutsch (DE)";
		german.Code = "en"; //taken: the row and OK say so
		Assert.True(german.HasErrors);
		Assert.False(manager.OkCommand.CanExecute(null));
		german.Code = "de-DE";
		Assert.False(german.HasErrors);
		Assert.True(manager.OkCommand.CanExecute(null));
		Assert.Equal("Deutsch", vm.Tree.KnownLanguages.Single(l => l.Code == "de").NativeName); //the session has not heard

		manager.CancelCommand.Execute(null); //forgotten
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "de" && l.NativeName == "Deutsch");
		Assert.False(vm.IsDirty);

		manager = new LanguageManagerViewModel(vm);
		german = manager.Rows.Single(row => row.Code == "de");
		german.Code = "de-DE";
		german.NativeName = "Deutsch (DE)";
		manager.OkCommand.Execute(null); //applied: the re-code shifts the entries
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "de-DE" && l.NativeName == "Deutsch (DE)");
		Assert.DoesNotContain(vm.Tree.KnownLanguages, l => l.Code == "de");
		Assert.Equal("y", vm.Session.Keys["Example.k"].Entries["de-DE"].Value);
		Assert.True(vm.IsDirty);
		Assert.Empty(dialogs.Shown);
	}

	//one row may be the default's language: a tick moves it, the names written in it
	//head their field by it, OK declares it in every file, and the trash takes it along
	[Fact]
	public void LanguageManager_TheDefaultsLanguageIsOneRowsTick() {
		var (vm, dialogs) = Load();
		var manager = new LanguageManagerViewModel(vm);
		LanguageRow english = manager.Rows.Single(row => row.Code == "en");
		LanguageRow german = manager.Rows.Single(row => row.Code == "de");
		Assert.Null(manager.DefaultRow); //the file does not say
		Assert.Equal(Words.Known["language.english-name"], manager.ExonymHeader);

		english.IsDefault = true;
		Assert.Same(english, manager.DefaultRow);
		Assert.Equal(Words.Known.Format("language.name-in", "English"), manager.ExonymHeader);
		german.IsDefault = true;
		Assert.False(english.IsDefault);
		german.NativeName = "Deutsch (DE)";
		Assert.Equal(Words.Known.Format("language.name-in", "Deutsch (DE)"), manager.ExonymHeader);
		german.IsDefault = false;
		Assert.Null(manager.DefaultRow);
		Assert.Null(vm.Session.Files[0].DefaultLanguage); //not until OK

		english.IsDefault = true;
		manager.OkCommand.Execute(null);
		Assert.Equal("en", vm.Session.Files[0].DefaultLanguage);
		Assert.True(vm.IsDirty);

		manager = new LanguageManagerViewModel(vm);
		Assert.Equal("en", manager.DefaultRow!.Code); //read back from the files
		dialogs.ConfirmAnswer = true;
		manager.DefaultRow.RemoveCommand.Execute(null);
		Assert.Null(manager.DefaultRow);
		manager.OkCommand.Execute(null);
		Assert.Null(vm.Session.Files[0].DefaultLanguage);
	}

	[Fact]
	public void RemoveLanguage_AsksAtTheTrashAndDeletesOnOk() {
		var (vm, dialogs) = Load();
		var manager = new LanguageManagerViewModel(vm);
		LanguageRow german = manager.Rows.Single(row => row.Code == "de");

		dialogs.ConfirmAnswer = false;
		german.RemoveCommand.Execute(null);
		Assert.Contains(german, manager.Rows);
		Assert.Single(dialogs.Confirmations);

		dialogs.ConfirmAnswer = true;
		german.RemoveCommand.Execute(null);
		Assert.DoesNotContain(german, manager.Rows);
		Assert.False(manager.Rows.Single().RemoveCommand.CanExecute(null)); //the last row stays
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "de"); //until OK

		manager.OkCommand.Execute(null);
		Assert.DoesNotContain(vm.Tree.KnownLanguages, l => l.Code == "de");
		Assert.All(vm.Session.Keys.Values, key => Assert.False(key.Entries.ContainsKey("de")));
		Assert.Equal(["en"], vm.Session.Files[0].Languages);
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void Merge_WritesAndLoadsTheMergedFile() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditMerge-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			vm.LoadFile(new StringReader("value-en=English\n\n[a]\nvalue=1\nvalue-en=one\n"), Path.Combine(folder, "Base.ini"));
			vm.LoadFile(new StringReader("value-fr=Français\n\n[a]\nvalue-fr=un\n"), Path.Combine(folder, "French.ini"));
			var merge = new MergeControlViewModel(vm);
			merge.Files[0].IsSelected = true;
			merge.Files[1].IsSelected = true;
			merge.Files[1].Languages.First(l => l.Code == "fr").IsSelected = true;
			Assert.Same(merge.Files[0], merge.BaseFile); //the first file ticked is the base until told otherwise
			string outPath = Path.Combine(folder, "Merged.ini");
			dialogs.FileToSave = outPath;
			bool closed = false;
			merge.CloseRequested += () => closed = true;

			merge.MergeCommand.Execute(null);

			Assert.True(closed);
			Assert.True(File.Exists(outPath));
			Assert.Equal(["Base", "French", "Merged"], vm.Tree.KeyNodes.Select(n => n.FullLabel));
			Assert.Equal("un", vm.Session.Keys["Merged.a"].Entries["fr"].Value);
			Assert.Equal("one", vm.Session.Keys["Merged.a"].Entries["en"].Value);
			Assert.Equal(["en", "fr"], vm.Session.FileOf("Merged")!.Languages);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Merge_WithConflictingKeys_ReportsInsteadOfThrowing() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader("value-en=English\n\n[a]\nvalue=1\n"), "One");
		vm.LoadFile(new StringReader("value-en=English\n\n[b]\nvalue=2\n"), "Two");
		var merge = new MergeControlViewModel(vm);
		string path = Path.Combine(Path.GetTempPath(), $"WordsEditMerge-{Guid.NewGuid():N}.ini");
		dialogs.FileToSave = path;
		try {
			merge.Files[0].IsSelected = true;
			Assert.False(merge.HasConflict);
			Assert.True(merge.CanMerge);

			merge.Files[1].IsSelected = true;
			Assert.True(merge.HasConflict);
			Assert.Contains("\nb", merge.ConflictMessage);
			Assert.False(merge.CanMerge);

			merge.MergeCommand.Execute(null);
			Assert.False(File.Exists(path));
			Assert.Equal(2, vm.Tree.KeyNodes.Count);
		}
		finally {
			File.Delete(path);
		}
	}

	[Fact]
	public void Merge_KeepsOneBaseAndOneFilePerLanguage() {
		// the rules live in the view model, whatever the view's buttons do: a
		// language chosen on one file leaves every other file, the base is always
		// one of the selected files, and an unticked file takes its choices with it
		var (vm, _) = Load();
		vm.LoadFile(new StringReader(Ini), "Second");
		var merge = new MergeControlViewModel(vm);
		MergeFileRow first = merge.Files[0], second = merge.Files[1];
		first.IsSelected = true;
		second.IsSelected = true;

		first.Languages.First(l => l.Code == "de").IsSelected = true;
		second.Languages.First(l => l.Code == "de").IsSelected = true;
		Assert.False(first.Languages.First(l => l.Code == "de").IsSelected);
		Assert.Equal(["de"], merge.Sources.Keys);
		Assert.Same(second.File, merge.Sources["de"]);

		second.IsBase = true;
		Assert.False(first.IsBase);
		Assert.Same(second, merge.BaseFile);

		second.IsSelected = false;
		Assert.Same(first, merge.BaseFile);
		Assert.Equal([first], merge.Selected);
		Assert.Empty(merge.Sources); //the unticked file's languages went with it

		first.IsSelected = false;
		Assert.Null(merge.BaseFile);
		Assert.False(merge.CanMerge);
	}

	[Fact]
	public void Merge_OffersOnlyTheLanguagesEachFileDeclares() {
		// the union's other languages are empty backfill: picking one as a source
		// would empty the base's words for it
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader("value-en=English\nvalue-fr=Français\n\n[a]\nvalue=1\nvalue-fr=un\n"), "Base");
		vm.LoadFile(new StringReader("value-en=English\n\n[a]\nvalue-en=one\n"), "English");

		var merge = new MergeControlViewModel(vm);

		Assert.Equal(["en", "fr"], merge.Files[0].Languages.Select(language => language.Code));
		Assert.Equal(["en"], merge.Files[1].Languages.Select(language => language.Code));
		merge.SplitFile = merge.Files[1];
		Assert.Equal(["en"], merge.SplitLanguages.Select(language => language.Code));
	}

	[Fact]
	public void LanguageManager_TheDefaultsLanguageIsTheFilesOwn_DeclaredOnlyWhenTheTickMoved() {
		var (vm, _) = Load();
		vm.LoadFile(new StringReader("value=!en\nvalue-de=Deutsch\n\n[a]\nvalue=1\n"), "One");
		vm.LoadFile(new StringReader("value=!de\nvalue-en=English\nvalue-de=Deutsch\n\n[b]\nvalue=2\n"), "Two");
		WordsFile one = vm.Session.FileOf("One")!, two = vm.Session.FileOf("Two")!;
		var manager = new LanguageManagerViewModel(vm, two);
		Assert.Equal("de", manager.DefaultRow!.Code); //its own, not the first file's
		manager.Rows.Single(row => row.Code == "en").IsDefault = true;

		manager.OkCommand.Execute(null);

		Assert.Equal("en", two.DefaultLanguage);
		Assert.Equal("en", one.DefaultLanguage);
		Assert.Null(vm.Session.FileOf("Example")!.DefaultLanguage);
		vm.UndoCommand.Execute(null);
		Assert.Equal("de", two.DefaultLanguage);

		//One's default is English, which its table doesn't declare: no row has the tick,
		//and an OK that never moved it leaves the declaration be
		manager = new LanguageManagerViewModel(vm, one);
		Assert.Null(manager.DefaultRow);
		manager.Rows.Single().NativeName = "Deutsch (DE)";
		manager.OkCommand.Execute(null);
		Assert.Equal("en", one.DefaultLanguage);
	}

	//a library declares what its hosts need of it, unlisted: the manager edits one
	//file's table, the selection's, so neither file's ! is the other's to change
	[Fact]
	public void LanguageManager_EditsTheSelectionsFile_ALibrarysBangAndAHostsListingStayTheirOwn() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader("value-en=!English\nvalue-de=!Deutsch\n\n[j]\nvalue=y\n"), "Lib");
		vm.LoadFile(new StringReader("value-en=English\nvalue-de=Deutsch\n\n[k]\nvalue=x\n"), "Host");
		WordsFile library = vm.Session.FileOf("Lib")!, host = vm.Session.FileOf("Host")!;
		vm.Tree.SelectedKeyNode = null;
		Assert.Null(vm.LanguagesFile); //two files and no selection: no table to edit
		Assert.False(vm.ManageLanguagesCommand.CanExecute(null));

		vm.Tree.Select(Find(vm, "Host.k"));
		Assert.Same(host, vm.LanguagesFile);
		var manager = new LanguageManagerViewModel(vm);
		Assert.Equal(Words.Known.Format("languages.title", "Host"), manager.Title);
		LanguageRow german = manager.Rows.Single(row => row.Code == "de");
		Assert.Equal("Deutsch", german.NativeName); //the host's label, though the library loaded first
		german.NativeName = "Deutsch (DE)";
		manager.OkCommand.Execute(null);
		Assert.Equal("Deutsch (DE)", host.Labels["de"].NativeName);
		Assert.Equal("!Deutsch", library.Labels["de"].NativeName);

		vm.Tree.Select(Find(vm, "Lib.j"));
		manager = new LanguageManagerViewModel(vm);
		german = manager.Rows.Single(row => row.Code == "de");
		Assert.Equal("!Deutsch", german.NativeName); //the library's own, ! and all
		german.NativeName = "Deutsch";
		manager.OkCommand.Execute(null);
		manager = new LanguageManagerViewModel(vm);
		manager.Rows.Single(row => row.Code == "de").NativeName = "!Deutsch"; //and back
		manager.OkCommand.Execute(null);
		Assert.Equal("!Deutsch", library.Labels["de"].NativeName);
		Assert.Equal("Deutsch (DE)", host.Labels["de"].NativeName); //never hidden along with it
	}

	//a host offering a language its library has no words for reads the library's
	//default there: the library's node says so, until it declares the language too
	[Fact]
	public void ALibraryLackingWhatAHostLists_IsFlagged_UntilItDeclaresItToo() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\n"), "Host");
		vm.LoadFile(new StringReader("value-en=!English\n\n[j]\nvalue=y\n"), "Lib");
		KeyNode lib = vm.Tree.KeyNodes.Single(node => node.FullLabel == "Lib");
		Assert.True(lib.IsLibraryFile);
		Assert.False(lib.HasLacks);

		vm.Tree.Select(Find(vm, "Host.k"));
		var manager = new LanguageManagerViewModel(vm);
		manager.AddCommand.Execute(null);
		manager.Selected!.Code = "fr";
		manager.Selected.NativeName = "Français";
		manager.Selected.EnglishName = "French";
		manager.OkCommand.Execute(null);
		Assert.DoesNotContain("fr", vm.Session.FileOf("Lib")!.Languages); //the host's addition is the host's
		Assert.True(lib.HasLacks);
		Assert.Equal(Words.Known.Format("main.library-lacks", "French (Host)"), lib.LacksLanguages);

		vm.Tree.Select(Find(vm, "Lib.j"));
		manager = new LanguageManagerViewModel(vm);
		manager.AddCommand.Execute(null);
		manager.Selected!.Code = "fr";
		manager.Selected.NativeName = "!Français";
		manager.OkCommand.Execute(null);
		Assert.False(lib.HasLacks);
		Assert.True(lib.IsLibraryFile);
		vm.UndoCommand.Execute(null);
		Assert.True(lib.HasLacks);
	}

	[Fact]
	public void LanguageManager_ANewRowTakingTheOnlyLanguagesCodeReplacesIt() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\nvalue-en=ex\n"), "Example");
		var manager = new LanguageManagerViewModel(vm);
		LanguageRow old = manager.Rows.Single();
		manager.AddCommand.Execute(null);
		manager.Selected!.Code = "en";
		manager.Selected.NativeName = "English (new)";
		dialogs.ConfirmAnswer = true;
		old.RemoveCommand.Execute(null);
		Assert.True(manager.OkCommand.CanExecute(null));

		manager.OkCommand.Execute(null);

		Assert.Empty(dialogs.Notices);
		Assert.Equal(["en"], vm.Session.Languages.Known.Select(language => language.Code));
		Assert.Equal("English (new)", vm.Session.Languages.Find("en")!.NativeName);
		Assert.Equal("", vm.Session.Keys["Example.k"].Entries["en"].Value); //the removed language's words went with it
		Assert.True(vm.IsDirty);
		Assert.Contains("value-en=English (new)", Saved(vm));
		vm.UndoCommand.Execute(null);
		Assert.Equal("English", vm.Session.Languages.Find("en")!.NativeName);
		Assert.Equal("ex", vm.Session.Keys["Example.k"].Entries["en"].Value);
		Assert.Contains("value-en=English\n", Saved(vm).ReplaceLineEndings("\n"));
	}

	private static string Saved(MainWindowViewModel vm) {
		var writer = new StringWriter();
		vm.Session.Save(vm.Session.Files[0], vm.Tree.NodeOf(vm.Session.Files[0]), writer);
		return writer.ToString();
	}

	[Fact]
	public void LanguageManager_ARecodeOrASwapCarriesTheSettingsReferences() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader("value-en=English\nvalue-de=Deutsch\nparam-en=english.ini\nparam-de=german.ini\n\n[k]\nvalue=x\n"), "Example");
		WordsFile file = vm.Session.Files[0];
		var manager = new LanguageManagerViewModel(vm);
		LanguageRow english = manager.Rows.Single(row => row.Code == "en"), german = manager.Rows.Single(row => row.Code == "de");
		english.Code = "de";
		german.Code = "en";

		manager.OkCommand.Execute(null);

		Assert.Equal("german.ini", file.LanguageSettings["en"]);
		Assert.Equal("english.ini", file.LanguageSettings["de"]);
		Assert.Equal(2, file.LanguageSettings.Count); //nothing left on the throwaway code
		vm.UndoCommand.Execute(null);
		Assert.Equal("english.ini", file.LanguageSettings["en"]);
		Assert.Equal("german.ini", file.LanguageSettings["de"]);
	}

	[Fact]
	public void TryClose_CleanGoes_DirtyAsks() {
		var (vm, dialogs) = Load();
		Assert.True(vm.TryClose());

		vm.IsDirty = true;
		dialogs.SaveAnswer = CloseAnswer.Cancel;
		Assert.False(vm.TryClose());
		Assert.True(vm.IsDirty);

		dialogs.SaveAnswer = CloseAnswer.Discard;
		Assert.True(vm.TryClose());
	}

	[Fact]
	public void TryClose_SaveWaitsOnTheFilesBeingWritten() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditClose-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs { SaveAnswer = CloseAnswer.Save };
			var vm = new MainWindowViewModel(dialogs);
			string path = Path.Combine(folder, "Example.ini");
			vm.LoadFile(new StringReader(Ini), path);
			vm.IsDirty = true;

			Assert.True(vm.TryClose());
			Assert.True(File.Exists(path));
			Assert.False(vm.IsDirty);

			//a file that cannot be written keeps the window open
			vm.LoadFile(new StringReader(Ini), Path.Combine(folder, "missing", "Nowhere.ini"));
			vm.IsDirty = true;
			Assert.False(vm.TryClose());
			Assert.Single(dialogs.Notices);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Settings_NamesTheFilesAndWritesTheTables() {
		// the dialog fills the file's param slots (a document change) and writes
		// the tables of whichever named file is picked, to that file
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSettingsDialog-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			vm.LoadFile(new StringReader(Ini), Path.Combine(folder, "strings.ini"));
			WordsFile file = vm.Session.Files[0];
			vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0].Children[0]; //strings.k
			vm.ShowDefaultPreview = true;
			vm.ShowLocalizationPreview = true;
			dialogs.OnShow = shown => {
				var settings = Assert.IsType<SettingsViewModel>(shown);
				Assert.Empty(settings.Targets);
				Assert.Null(settings.Document);

				settings.SettingsFile = "wordsmith.ini";
				Assert.Equal(Path.Combine(folder, "wordsmith.ini"), Assert.Single(settings.Targets).Path);
				SettingsDocument document = Assert.IsType<SettingsDocument>(settings.Document);
				document.AddImageCommand.Execute(null);
				document.Images[0].Scheme = "shot";
				document.Images[0].Folder = "shots";
				Assert.Contains(document.Errors, error => error.Contains("shot-decode")); //seen before it is saved
				document.Images[0].Decode = @"/^shot:(\w+)$//$1.png";
				Assert.Empty(document.Errors);
				document.AddImageCommand.Execute(null); //a blank row is dropped
				document.AddLinkCommand.Execute(null);
				document.Links[0].Scheme = "help";
				document.Links[0].Mode = "shellexec";

				settings.Languages.First(language => language.Code == "de").Path = "de/wordsmith.ini";
				Assert.Equal(2, settings.Targets.Count);
				Assert.Same(settings.Targets[0], settings.Target); //the pick survives the list turning over
				settings.Target = settings.Targets[1];
				Assert.NotSame(document, settings.Document);
				settings.Document!.AddImageCommand.Execute(null);
				settings.Document.Images[0].Scheme = "shot";
				settings.Document.Images[0].Folder = "shots-de";

				settings.OkayCommand.Execute(null);
			};

			vm.SettingsCommand.Execute(null);

			Assert.Single(dialogs.Shown);
			Assert.Empty(dialogs.Notices);
			Assert.Equal("wordsmith.ini", file.Settings);
			Assert.Equal("de/wordsmith.ini", file.LanguageSettings["de"]);
			Assert.True(vm.IsDirty);
			ProjectSettings written = ProjectSettings.Load(Path.Combine(folder, "wordsmith.ini"));
			Assert.Empty(written.Errors);
			Assert.Equal("shots", Assert.Single(written.Images).Folder);
			Assert.Equal(LinkMode.ShellExec, Assert.Single(written.Links).Mode);
			Assert.True(File.Exists(Path.Combine(folder, "de", "wordsmith.ini"))); //its folder was made
			//the previews see the new rules at once
			Assert.True(vm.DefaultPreview.Settings.TryLocate(new Uri("shot:Login"), out string root, out _));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots")), root);
			vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(language => language.Code == "de");
			Assert.True(vm.TranslationPreview.Settings.TryLocate(new Uri("shot:Login"), out root, out string path));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "de", "shots-de")), root); //relative to the language's own file
			Assert.Equal("Login.png", path); //the decode from the dictionary's file
			//and the slots save with the dictionary
			var saved = new StringWriter();
			vm.Session.Save(file, vm.Tree.NodeOf(file), saved);
			Assert.Contains("param=wordsmith.ini", saved.ToString());
			Assert.Contains("param-de=de/wordsmith.ini", saved.ToString());
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Settings_AFailedWriteKeepsTheDialogAndItsEditsForAnotherTry() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSettingsLocked-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		string settingsPath = Path.Combine(folder, "wordsmith.ini");
		try {
			File.WriteAllText(settingsPath, "[images]\nshot=old\nshot-decode=/^shot:(\\w+)$//$1.png\n");
			File.SetAttributes(settingsPath, FileAttributes.ReadOnly);
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			vm.LoadFile(new StringReader("value-en=English\nparam=wordsmith.ini\n\n[k]\nvalue=x\n"), Path.Combine(folder, "strings.ini"));
			WordsFile file = vm.Session.Files[0];
			var settings = new SettingsViewModel(vm, file);
			bool closed = false;
			settings.CloseRequested += () => closed = true;
			settings.Document!.Images[0].Folder = "new";
			settings.Languages.Single().Path = "english.ini";

			settings.OkayCommand.Execute(null);

			Assert.False(closed);
			Assert.Single(dialogs.Notices);
			Assert.Equal("new", settings.Document.Images[0].Folder); //the edit is still there
			Assert.Empty(file.LanguageSettings); //and the slots wait for it

			File.SetAttributes(settingsPath, FileAttributes.Normal);
			settings.OkayCommand.Execute(null);

			Assert.True(closed);
			Assert.Equal("new", Assert.Single(ProjectSettings.Load(settingsPath).Images).Folder);
			Assert.Equal("english.ini", file.LanguageSettings["en"]);
		}
		finally {
			if (File.Exists(settingsPath)) {
				File.SetAttributes(settingsPath, FileAttributes.Normal);
			}
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Settings_CancelLeavesFileAndDiskAlone() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSettingsCancel-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var (vm, _) = Load();
			vm.LoadFile(new StringReader(Ini), Path.Combine(folder, "strings.ini"));
			WordsFile file = vm.Session.Files[1];
			var settings = new SettingsViewModel(vm, file) { SettingsFile = "wordsmith.ini" };
			settings.Document!.AddImageCommand.Execute(null);
			settings.Document.Images[0].Scheme = "pack";
			settings.Document.Images[0].Folder = "pics";
			bool closed = false;
			settings.CloseRequested += () => closed = true;

			settings.CancelCommand.Execute(null);

			Assert.True(closed);
			Assert.Equal("", file.Settings);
			Assert.False(vm.IsDirty);
			Assert.False(File.Exists(Path.Combine(folder, "wordsmith.ini")));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void KeyNameDialog_CancelIsAlwaysAvailable_AndRequestsClose() {
		// opens with an empty (invalid) name; Cancel must still work
		var (vm, _) = Load();
		var dialog = new KeyNameViewModel(vm, null);
		bool closed = false;
		dialog.CloseRequested += () => closed = true;

		Assert.True(dialog.HasErrors);
		Assert.True(dialog.CancelCommand.CanExecute(null));
		dialog.CancelCommand.Execute(null);

		Assert.True(closed);
	}

	//the name starts empty, so its first check fails: a good name typed after clears
	//that failure, or Add never comes back
	[Fact]
	public void KeyNameDialog_AddComesBack_OnceTheNameIsGood() {
		var (vm, _) = Load();
		var dialog = new KeyNameViewModel(vm, null);
		Assert.False(dialog.AddKeyCommand.CanExecute(null));

		dialog.KeyName = "abc";
		Assert.False(dialog.HasErrors);
		Assert.True(dialog.AddKeyCommand.CanExecute(null));
		dialog.KeyName = "a b";
		Assert.False(dialog.AddKeyCommand.CanExecute(null));
	}

	//a name is one segment of the runtime's grammar: any script's letters, UAX #31's
	[Theory]
	[InlineData("हिंदी", true)]
	[InlineData("می‌خواهم", true)]
	[InlineData("a😀", false)]
	[InlineData("café", false)]
	public void KeyNameDialog_TakesAnyScriptsLetters_InNfc(string name, bool valid) {
		var (vm, _) = Load();
		var dialog = new KeyNameViewModel(vm, null) { KeyName = name };

		Assert.Equal(valid, !dialog.HasErrors);
		Assert.Equal(valid, dialog.AddKeyCommand.CanExecute(null));
	}

	[Fact]
	public void RemoveKeyData_AsksFirst() {
		var dialogs = new FakeDialogs { ConfirmAnswer = false };
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader("value-en=English\n\n[a]\nvalue=A\n"), "T");
		vm.Tree.SelectedKeyNode = Find(vm, "T.a");

		vm.RemoveKeyCommand.Execute(null);
		Assert.Contains("key information", dialogs.Confirmations.Single());
		Assert.NotNull(vm.Tree.SelectedKey);
		Assert.False(vm.IsDirty);

		dialogs.ConfirmAnswer = true;
		vm.RemoveKeyCommand.Execute(null);
		Assert.Null(vm.Tree.SelectedKey);
		Assert.True(vm.IsDirty);
		Assert.NotNull(Find(vm, "T.a")); //the node stays
	}

	[Fact]
	public void RemoveNode_AsksFirstWhenKeysGoWithIt() {
		var dialogs = new FakeDialogs { ConfirmAnswer = false };
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader("value-en=English\n\n[a.b]\nvalue=B\n\n[a.c]\nvalue=C\n"), "T");
		vm.Tree.SelectedKeyNode = Find(vm, "T.a");

		vm.RemoveNodeCommand.Execute(null);
		Assert.Contains("2 keys", dialogs.Confirmations.Single());
		Assert.Equal(2, vm.Session.Keys.Count);

		//a node carrying nothing goes quietly
		vm.Tree.Add(Find(vm, "T"), "empty");
		vm.RemoveNodeCommand.Execute(null);
		Assert.Single(dialogs.Confirmations);
		Assert.DoesNotContain(MainWindowViewModelTests.GetAllKeyNodes(vm.Tree.KeyNodes), node => node.FullLabel == "T.empty");

		dialogs.ConfirmAnswer = true;
		vm.Tree.SelectedKeyNode = Find(vm, "T.a");
		vm.RemoveNodeCommand.Execute(null);
		Assert.Empty(vm.Session.Keys);
	}

	private static KeyNode Find(MainWindowViewModel vm, string fullLabel)
		=> MainWindowViewModelTests.GetAllKeyNodes(vm.Tree.KeyNodes).First(node => node.FullLabel == fullLabel);

	[Fact]
	public void LanguageManager_CommitsItsSelectionOnOk() {
		var (vm, _) = Load();
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code);
		var manager = new LanguageManagerViewModel(vm);
		Assert.Equal(vm.Tree.SelectedLanguage.Code, manager.Selected!.Code); //starts where the tree is

		manager.Selected = manager.Rows.Single(row => row.Code == "de");
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code); //browsing the list moves the tree nothing

		bool closed = false;
		manager.CloseRequested += () => closed = true;
		manager.OkCommand.Execute(null);
		Assert.True(closed);
		Assert.Equal("de", vm.Tree.SelectedLanguage.Code);
		Assert.False(vm.IsDirty); //the table itself did not change
	}

	[Fact]
	public void Split_WritesOneLanguageAndLoadsIt() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSplit-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			vm.LoadFile(new StringReader("value-en=English\nvalue-de=Deutsch\n\n[a]\nvalue=1\nvalue-en=one\nvalue-de=eins\n"), Path.Combine(folder, "Main.ini"));
			var merge = new MergeControlViewModel(vm);
			Assert.False(merge.SplitCommand.CanExecute(null));

			merge.SplitFile = merge.Files[0];
			Assert.Equal(["en", "de"], merge.SplitLanguages.Select(l => l.Code)); //the file's own table
			merge.SplitLanguage = merge.SplitLanguages[1];
			Assert.True(merge.SplitCommand.CanExecute(null));
			string outPath = Path.Combine(folder, "German.ini");
			dialogs.FileToSave = outPath;
			bool closed = false;
			merge.CloseRequested += () => closed = true;

			merge.SplitCommand.Execute(null);

			Assert.True(closed);
			Assert.True(File.Exists(outPath));
			Assert.Equal(["Main", "German"], vm.Tree.KeyNodes.Select(n => n.FullLabel));
			Assert.Equal(["de"], vm.Session.FileOf("German")!.Languages);
			Assert.Equal("eins", vm.Session.Keys["German.a"].Entries["de"].Value);
			Assert.Equal("1", vm.Session.Keys["German.a"].DefaultValue); //the reference the translator reads
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void SettingsDocument_DropsBlankRowsAndLetsTheLaterDuplicateWin() {
		// rows as typed become rules: blank schemes are nothing, whitespace goes,
		// and a scheme written twice — in any case — is the later row
		var document = new SettingsDocument(Path.Combine(Path.GetTempPath(), $"WordsEditSettings-{Guid.NewGuid():N}", "wordsmith.ini"));
		Assert.Empty(document.Images); //a file not there yet is empty, not an error
		Assert.False(document.IsEdited);

		document.Images.Add(new ImageRuleRow { Scheme = "  ", Folder = "nowhere" });
		document.Images.Add(new ImageRuleRow { Scheme = "avares", Folder = "first" });
		document.Images.Add(new ImageRuleRow { Scheme = " Avares ", Folder = " second " });
		document.Links.Add(new LinkRuleRow { Scheme = "" });
		document.Links.Add(new LinkRuleRow { Scheme = "help", Mode = "popup" });
		document.Links.Add(new LinkRuleRow { Scheme = "HELP", Mode = "shellexec" });
		Assert.True(document.IsEdited);

		ProjectSettings settings = document.ToSettings();
		ImageRule image = Assert.Single(settings.Images);
		Assert.Equal("second", image.Folder);
		Assert.True(settings.TryLocate(new Uri("AVARES://Assembly/img/x.png"), out string root, out string path));
		Assert.EndsWith("second", root);
		Assert.Equal("img/x.png", path);
		LinkRule link = Assert.Single(settings.Links);
		Assert.Equal(LinkMode.ShellExec, link.Mode);
		Assert.Equal("help:x", settings.Link(new Uri("help:x"), out LinkMode mode));
		Assert.Equal(LinkMode.ShellExec, mode);
		Assert.Empty(settings.Errors);
		Assert.Empty(document.Errors);
	}

	[Fact]
	public void Settings_TablesGoToTheirOwnFileAndSaveThreadsEveryDictionarysSlots() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSettings-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			string a = Path.Combine(folder, "A.ini");
			string b = Path.Combine(folder, "B.ini");
			File.WriteAllText(a, "value-en=English\nparam=wordsmith.ini\n\n[a]\nvalue=A\n");
			File.WriteAllText(b, "value-en=English\nvalue-de=Deutsch\nparam=wordsmith.ini\nparam-de=de/wordsmith.ini\n\n[b]\nvalue=B\n");
			vm.LoadFile(a);
			vm.LoadFile(b);
			vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0].Children[0]; //A.a
			dialogs.OnShow = shown => {
				var settings = (SettingsViewModel)shown;
				Assert.Equal(Path.Combine(folder, "wordsmith.ini"), settings.Target!.Path);
				settings.Document!.Images.Add(new ImageRuleRow { Scheme = "assets", Folder = "img" });
				settings.OkayCommand.Execute(null);
			};

			vm.SettingsCommand.Execute(null);

			//the table went to its own file; the dictionary is as it was
			Assert.False(vm.IsDirty);
			Assert.Contains("assets=img", File.ReadAllText(Path.Combine(folder, "wordsmith.ini")));

			vm.Tree.SelectedKey!.DefaultValue = "A!";
			Assert.True(vm.IsDirty);
			vm.Save();
			Assert.False(vm.IsDirty);
			Assert.Empty(dialogs.Notices);
			Assert.Contains("param=wordsmith.ini", File.ReadAllText(a));
			string savedB = File.ReadAllText(b);
			Assert.Contains("param=wordsmith.ini", savedB);
			Assert.Contains("param-de=de/wordsmith.ini", savedB);
			Assert.Contains("value=B", savedB);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
