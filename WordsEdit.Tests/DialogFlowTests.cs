using PatTech.Localization.Authoring;
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
			var merge = new MergeControlViewModel(vm) { BaseFile = vm.Tree.KeyNodes[0] };
			merge.FilesToMerge.Add(vm.Tree.KeyNodes[0]);
			merge.FilesToMerge.Add(vm.Tree.KeyNodes[1]);
			merge.LanguageCodeFilePair["fr"] = vm.Tree.KeyNodes[1];
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
		var merge = new MergeControlViewModel(vm) { BaseFile = vm.Tree.KeyNodes[0] };
		merge.FilesToMerge.Add(vm.Tree.KeyNodes[0]);
		merge.FilesToMerge.Add(vm.Tree.KeyNodes[1]);
		//the key sets are compared against the files that supply a language, so
		//"en" must come from the other file for the disagreement to matter
		merge.LanguageCodeFilePair["en"] = vm.Tree.KeyNodes[1];
		string path = Path.Combine(Path.GetTempPath(), $"WordsEditMerge-{Guid.NewGuid():N}.ini");
		dialogs.FileToSave = path;
		try {
			merge.MergeCommand.Execute(null);

			Assert.True(merge.HasConflict);
			Assert.False(File.Exists(path));
			Assert.Equal(2, vm.Tree.KeyNodes.Count);
		}
		finally {
			File.Delete(path);
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
