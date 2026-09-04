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
	public void AddLanguage_ThroughManagerAndEditor_BackfillsEveryKey() {
		// the path the inert bindings had made unreachable: the editor opens from
		// the manager, the "user" fills it in and presses Add
		var (vm, dialogs) = Load();
		dialogs.OnShow = shown => {
			if (shown is EditLanguageViewModel editor) {
				editor.LanguageCode = "fr";
				editor.NativeName = "Français";
				editor.EnglishName = "French";
				Assert.True(editor.AddLanguageCommand.CanExecute(null));
				editor.AddLanguageCommand.Execute(null);
			}
		};
		var manager = new LanguageManagerViewModel(vm);

		manager.AddLanguageCommand.Execute(null);

		Assert.IsType<EditLanguageViewModel>(Assert.Single(dialogs.Shown));
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "fr");
		Assert.All(vm.Session.Keys.Values, key => Assert.True(key.Entries.ContainsKey("fr")));
		Assert.Equal(["en", "de", "fr"], vm.Session.Files[0].Languages);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void RemoveLanguage_AsksFirst() {
		var (vm, dialogs) = Load();
		var manager = new LanguageManagerViewModel(vm) {
			SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de"),
		};

		dialogs.ConfirmAnswer = false;
		manager.RemoveLanguageCommand.Execute(null);
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "de");

		dialogs.ConfirmAnswer = true;
		manager.RemoveLanguageCommand.Execute(null);
		Assert.DoesNotContain(vm.Tree.KnownLanguages, l => l.Code == "de");
		Assert.All(vm.Session.Keys.Values, key => Assert.False(key.Entries.ContainsKey("de")));
		Assert.Equal(["en"], vm.Session.Files[0].Languages);
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code);
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
		Assert.Same(vm.Tree.SelectedLanguage, manager.SelectedLanguage); //starts where the tree is

		manager.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de");
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code); //browsing the list moves the tree nothing

		bool closed = false;
		manager.CloseRequested += () => closed = true;
		manager.OkayCommand.Execute(null);
		Assert.True(closed);
		Assert.Equal("de", vm.Tree.SelectedLanguage.Code);
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
}
