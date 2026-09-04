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
		vm.RemoveLocalizationKeyAndNodeCommand.Execute(null);
		Assert.Single(vm.Tree.KeyNodes);

		dialogs.ConfirmAnswer = true;
		vm.RemoveLocalizationKeyAndNodeCommand.Execute(null);
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
}
