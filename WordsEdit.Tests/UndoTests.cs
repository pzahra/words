using GongSolutions.Wpf.DragDrop;
using PatTech.Localization.Authoring;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Undo, headless (SPEC: Undo): every action is an entry that takes the
///     document back to the text it had and puts the action back again; typing
///     folds into runs; an entry out of view is gone to before it is applied;
///     the boundaries clear the stack and Save does not.
/// </summary>
public class UndoTests {
	private static (MainWindowViewModel vm, FakeDialogs dialogs) Load() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(MainWindowViewModelTests.GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		return (vm, dialogs);
	}

	private static KeyNode Node(MainWindowViewModel vm, string fullLabel) => MainWindowViewModelTests.Node(vm, fullLabel);

	//what the document is: every file's saved text, then every row of the tree (an
	//empty node or a blank comment writes nothing, but is there to undo)
	private static string State(MainWindowViewModel vm) {
		var state = new StringBuilder();
		foreach (WordsFile file in vm.Session.Files) {
			var writer = new StringWriter();
			vm.Session.Save(file, vm.Tree.NodeOf(file), writer);
			state.Append(writer);
		}
		foreach (KeyNode node in vm.Tree.AllNodes) {
			state.Append('\n').Append(node.FullLabel).Append(node is OrganizerNode organizer ? $" {organizer.Text}" : "");
		}
		return state.ToString();
	}

	private static void Undo(MainWindowViewModel vm) => Step(vm.UndoCommand, () => vm.UndoStack.DoneCount);
	private static void Redo(MainWindowViewModel vm) => Step(vm.RedoCommand, () => vm.UndoStack.UndoneCount);

	//once to go there when the change is out of view, once more to apply it
	private static void Step(ICommand command, Func<int> depth) {
		int before = depth();
		Assert.True(command.CanExecute(null));
		command.Execute(null);
		if (depth() == before) {
			command.Execute(null);
		}
		Assert.Equal(before - 1, depth());
	}

	[Fact]
	public void EveryActionUndoesToTheDocumentBeforeAndRedoesToTheDocumentAfter() {
		var (vm, dialogs) = Load();
		string loaded = State(vm);

		void Twin(string what, Action act) {
			vm.IsDirty = false;
			string before = State(vm);
			int depth = vm.UndoStack.DoneCount;
			act();
			string after = State(vm);
			Assert.True(before != after, $"{what} should change the document");
			Assert.True(depth + 1 == vm.UndoStack.DoneCount, $"{what} should be one entry");
			Undo(vm);
			Assert.True(before == State(vm), $"undoing {what} should give back the document before it");
			Assert.False(vm.IsDirty, $"undoing {what} should give back the clean title");
			Redo(vm);
			Assert.True(after == State(vm), $"redoing {what} should give back the document after it");
			Assert.True(vm.IsDirty, $"redoing {what} should star the title");
		}

		LanguageManagerViewModel Manager() => new(vm);

		Twin("add node", () => { vm.Tree.Select(Node(vm, "Example.main")); vm.AddNode("fresh"); });
		Twin("add key", () => vm.AddKeyCommand.Execute(null));
		Twin("typing the default", () => { vm.Tree.SelectedKey!.DefaultValue = "w"; vm.Tree.SelectedKey!.DefaultValue = "words"; });
		Twin("typing a translation", () => vm.Tree.SelectedEntry!.Value = "words too");
		Twin("a note raising the hand", () => vm.Tree.SelectedKey!.Comment = "a note");
		Twin("rename", () => vm.RenameNode("renamed"));
		Twin("needs review", () => vm.ToggleNeedsReviewCommand.Execute(null));
		Twin("stale", () => vm.ToggleStaleLanguageCommand.Execute(null));
		Twin("stale all", () => vm.StaleAllLanguagesCommand.Execute(null));
		Twin("typing a translation of a constant-to-be", () => { vm.Tree.Select(Node(vm, "Example.prefix-whitespace")); vm.Tree.SelectedEntry!.Value = "translated"; });
		Twin("constant, clearing the translation", () => vm.ToggleConstantCommand.Execute(null));
		Twin("typing a comment", () => { vm.Tree.Select(Node(vm, "Example.main").Children.OfType<OrganizerNode>().First()); vm.Tree.SelectedOrganizer!.Text = " edited"; });
		Twin("add comment", () => { vm.Tree.Select(Node(vm, "Example.main.circle-1")); vm.AddOrganizerCommand.Execute(null); });
		Twin("typing the new comment", () => vm.Tree.SelectedOrganizer!.Text = " fresh note");
		Twin("remove the preamble", () => { vm.Tree.Select(vm.Tree.KeyNodes[0].Children[0]); vm.RemoveNodeCommand.Execute(null); });
		Twin("remove key", () => { vm.Tree.Select(Node(vm, "Example.enum.none")); vm.RemoveKeyCommand.Execute(null); });
		Twin("parameters", () => {
			vm.Tree.Select(Node(vm, "Example.view.section-name.key"));
			dialogs.OnShow = shown => {
				var parameters = (TestParametersViewModel)shown;
				parameters.Parameters[0].Value = "1";
				parameters.AddParameterCommand.Execute(null);
			};
			vm.TestParametersCommand.Execute(null);
			dialogs.OnShow = null;
		});
		Twin("drag under another parent", () => vm.KeyDrag.Drop(new FakeDropInfo(Node(vm, "Example.enum.two"), Node(vm, "Example.format"), RelativeInsertPosition.TargetItemCenter)));
		Twin("drag among siblings", () => vm.KeyDrag.Drop(new FakeDropInfo(Node(vm, "Example.format.named"), Node(vm, "Example.format.object"), RelativeInsertPosition.BeforeTargetItem)));
		Twin("drag a comment", () => vm.KeyDrag.Drop(new FakeDropInfo(Node(vm, "Example.main").Children.OfType<CommentNode>().First(), Node(vm, "Example.enum.none"), RelativeInsertPosition.AfterTargetItem)));
		Twin("settings slots", () => {
			vm.Tree.Select(Node(vm, "Example.enum"));
			dialogs.OnShow = shown => {
				var settings = (SettingsViewModel)shown;
				settings.SettingsFile = "wordsmith.ini";
				settings.OkayCommand.Execute(null);
			};
			vm.SettingsCommand.Execute(null);
			dialogs.OnShow = null;
		});
		Twin("remove a node with keys and comments", () => { vm.Tree.Select(Node(vm, "Example.main")); vm.RemoveNodeCommand.Execute(null); });
		Twin("add a language", () => {
			var manager = Manager();
			manager.AddCommand.Execute(null);
			manager.Selected!.Code = "fr";
			manager.Selected.NativeName = "Français";
			manager.Selected.EnglishName = "French";
			manager.OkCommand.Execute(null);
		});
		Twin("remove a language", () => {
			var manager = Manager();
			manager.Rows.Single(row => row.Code == "zh-HK").RemoveCommand.Execute(null);
			manager.OkCommand.Execute(null);
		});
		Twin("relabel a language", () => {
			var manager = Manager();
			manager.Rows.Single(row => row.Code == "en-GB").NativeName = "English (British)";
			manager.OkCommand.Execute(null);
		});
		Twin("recode a language", () => {
			var manager = Manager();
			manager.Rows.Single(row => row.Code == "en-CA").Code = "en-AU";
			manager.OkCommand.Execute(null);
		});
		Twin("reorder the languages", () => {
			var manager = Manager();
			manager.Reorder(0, 2);
			manager.OkCommand.Execute(null);
		});
		Twin("swap two codes", () => {
			var manager = Manager();
			LanguageRow british = manager.Rows.Single(row => row.Code == "en-GB"), american = manager.Rows.Single(row => row.Code == "en-US");
			british.Code = "en-US";
			american.Code = "en-GB";
			Assert.True(manager.OkCommand.CanExecute(null));
			manager.OkCommand.Execute(null);
		});
		Assert.Empty(dialogs.Notices);

		//the whole run backwards is the file as loaded, and forwards again the last of it
		string last = State(vm);
		while (vm.UndoStack.DoneCount > 0) {
			Undo(vm);
		}
		Assert.Equal(loaded, State(vm));
		Assert.False(vm.IsDirty);
		while (vm.UndoStack.UndoneCount > 0) {
			Redo(vm);
		}
		Assert.Equal(last, State(vm));
	}

	[Fact]
	public void ARunOfTypingIsOneEntryUntilSomethingEndsIt() {
		var (vm, _) = Load();
		KeyNode title = Node(vm, "Example.main.title");
		vm.Tree.Select(title);
		WordsKey key = vm.Tree.SelectedKey!;
		string value = key.DefaultValue, context = key.Context;

		key.DefaultValue = value + "a";
		key.DefaultValue = value + "ab";
		key.DefaultValue = value + "abc";
		Assert.Equal(1, vm.UndoStack.DoneCount);
		key.Context = "x"; //another field
		Assert.Equal(2, vm.UndoStack.DoneCount);
		key.DefaultValue = value + "abcd"; //back in the first: a run of its own
		Assert.Equal(3, vm.UndoStack.DoneCount);
		vm.Tree.Select(Node(vm, "Example.main.circle-1"));
		vm.Tree.Select(title);
		key.DefaultValue = value + "abcde"; //another node came between
		Assert.Equal(4, vm.UndoStack.DoneCount);
		Undo(vm);
		key.DefaultValue = value + "abcdf"; //an undo came between
		Assert.Equal(4, vm.UndoStack.DoneCount);
		key.DefaultValue = value + "abcd"; //typed and taken back: nothing left of the run
		Assert.Equal(3, vm.UndoStack.DoneCount);

		Undo(vm);
		Assert.Equal(value + "abc", key.DefaultValue);
		Undo(vm);
		Assert.Equal(context, key.Context);
		Undo(vm);
		Assert.Equal(value, key.DefaultValue);
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void ANoteThatRaisedTheHandUndoesBoth() {
		var (vm, _) = Load();
		vm.Tree.Select(Node(vm, "Example.main.circle-1"));
		WordsKey key = vm.Tree.SelectedKey!;
		Assert.False(key.NeedsReview);
		Assert.Equal("", key.Comment);

		key.Comment = "a";
		key.Comment = "a note";
		Assert.True(key.NeedsReview);
		Assert.Equal(1, vm.UndoStack.DoneCount);

		Undo(vm);
		Assert.Equal("", key.Comment);
		Assert.False(key.NeedsReview);
		Redo(vm);
		Assert.Equal("a note", key.Comment);
		Assert.True(key.NeedsReview);

		//the hand lowered by itself, then the note cleared: undoing the clearing leaves it down
		vm.ToggleNeedsReviewCommand.Execute(null);
		key.Comment = "";
		Undo(vm);
		Assert.Equal("a note", key.Comment);
		Assert.False(key.NeedsReview);
	}

	[Fact]
	public void AnUndoOutOfViewGoesThereFirst() {
		var (vm, _) = Load();
		KeyNode target = Node(vm, "Example.format.object"), elsewhere = Node(vm, "Example.enum.none");
		LanguageEntry chinese = vm.Tree.KnownLanguages.Single(language => language.Code == "zh");
		vm.Tree.Select(target);
		vm.Tree.SelectedLanguage = chinese;
		WordsEntry entry = vm.Tree.SelectedEntry!;
		string value = entry.Value;
		entry.Value = "changed";
		string changed = State(vm);
		DocumentField? focused = null;
		vm.FieldFocusRequested += field => focused = field;

		//away: another node, the default language, and a search that hides the change
		vm.Tree.Select(Node(vm, "Example.main.title"));
		vm.Tree.Select(elsewhere);
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages[0];
		vm.Tree.SearchFilterText = "Selection";
		Assert.False(target.IsVisible);

		vm.UndoCommand.Execute(null);
		Assert.Same(target, vm.Tree.SelectedKeyNode);
		Assert.True(target.IsVisible);
		Assert.Equal("zh", vm.Tree.SelectedLanguage.Code);
		Assert.Equal(changed, State(vm)); //shown, not yet undone
		Assert.Equal(1, vm.UndoStack.DoneCount);
		Assert.Null(focused);

		vm.UndoCommand.Execute(null);
		Assert.Equal(value, entry.Value);
		Assert.Equal(0, vm.UndoStack.DoneCount);
		Assert.Equal(DocumentField.EntryValue, focused);
		Assert.True(target.IsVisible); //what was undone stays in view

		//the way there was a move like any other
		vm.Tree.BackCommand.Execute(null);
		Assert.Same(elsewhere, vm.Tree.SelectedKeyNode);
		Assert.False(target.IsVisible);
	}

	[Fact]
	public void AnUndoInViewAppliesAtOnceAndFocusesTheField() {
		var (vm, _) = Load();
		vm.Tree.Select(Node(vm, "Example.format.object"));
		WordsKey key = vm.Tree.SelectedKey!;
		string context = key.Context;
		DocumentField? focused = null;
		vm.FieldFocusRequested += field => focused = field;
		key.Context = "typed";

		vm.UndoCommand.Execute(null);
		Assert.Equal(context, key.Context);
		Assert.Equal(DocumentField.KeyContext, focused);
		focused = null;
		vm.RedoCommand.Execute(null);
		Assert.Equal("typed", key.Context);
		Assert.Equal(DocumentField.KeyContext, focused);
	}

	[Fact]
	public void SaveKeepsTheStackAndFilesInOrOutClearIt() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditUndo-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			string example = Path.Combine(folder, "Example.ini"), other = Path.Combine(folder, "Other.ini");
			using (StreamReader reader = MainWindowViewModelTests.GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini")) {
				File.WriteAllText(example, reader.ReadToEnd());
			}
			File.WriteAllText(other, "value-en=English\n\n[a]\nvalue=1\n");
			vm.LoadFile(example);

			void Edit() {
				vm.Tree.Select(vm.Tree.NodeAt("Example.enum.none")!);
				vm.ToggleNeedsReviewCommand.Execute(null);
				Assert.True(vm.UndoStack.DoneCount > 0);
			}

			//a save keeps the stack: undoing past it stars the title, coming back clears it
			Edit();
			vm.Save();
			Assert.Equal(1, vm.UndoStack.DoneCount);
			Assert.False(vm.IsDirty);
			Undo(vm);
			Assert.True(vm.IsDirty);
			Redo(vm);
			Assert.False(vm.IsDirty);
			Edit();
			Assert.True(vm.IsDirty);
			Undo(vm);
			Assert.False(vm.IsDirty);

			Edit();
			vm.LoadFile(other);
			Assert.Equal(0, vm.UndoStack.DoneCount);
			Assert.Equal(0, vm.UndoStack.UndoneCount);
			Assert.False(vm.UndoCommand.CanExecute(null));

			Edit();
			vm.ImportFile(other); //an ini set imports as a load
			Assert.Equal(0, vm.UndoStack.DoneCount);

			Edit();
			vm.RemoveFileNodeCore(vm.Tree.NodeAt("Other")!);
			Assert.Equal(0, vm.UndoStack.DoneCount);

			Edit();
			var merge = new MergeControlViewModel(vm);
			merge.SplitFile = merge.Files[0];
			dialogs.FileToSave = Path.Combine(folder, "Split.ini");
			merge.SplitCommand.Execute(null);
			Assert.Equal(0, vm.UndoStack.DoneCount);

			Edit();
			merge = new MergeControlViewModel(vm);
			merge.Files[0].IsSelected = true;
			merge.Files[1].IsSelected = true;
			dialogs.FileToSave = Path.Combine(folder, "Merged.ini");
			merge.MergeCommand.Execute(null);
			Assert.Equal(0, vm.UndoStack.DoneCount);

			//a file reordered is precedence, not a boundary
			Edit();
			vm.KeyDrag.Drop(new FakeDropInfo(vm.Tree.KeyNodes[0], vm.Tree.KeyNodes[1], RelativeInsertPosition.AfterTargetItem));
			Assert.Equal(1, vm.UndoStack.DoneCount);

			vm.ResetCore();
			Assert.Equal(0, vm.UndoStack.DoneCount);
			Assert.Empty(dialogs.Notices);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void AMergingRecodeIsABoundary() {
		var (vm, _) = Load();
		vm.Tree.Select(Node(vm, "Example.enum.none"));
		vm.ToggleNeedsReviewCommand.Execute(null);
		Assert.Equal(1, vm.UndoStack.DoneCount);

		vm.ChangeLanguages(edit => { }); //nothing changed, nothing recorded
		Assert.Equal(1, vm.UndoStack.DoneCount);

		vm.IsDirty = false;
		vm.ChangeLanguages(edit => edit.Rename("en-GB", new LanguageEntry("en", "English")));
		Assert.Equal(0, vm.UndoStack.DoneCount);
		Assert.True(vm.IsDirty);
		Assert.DoesNotContain(vm.Tree.KnownLanguages, language => language.Code == "en-GB");
	}

	[Fact]
	public void TheEditingBoxesGiveTheirUndoToTheDocumentAndTheSearchBoxKeepsItsOwn() {
		NavigationTests.RunSta(() => {
			int undos = 0, redos = 0;
			var undo = new DelegateCommand(() => undos++);
			var redo = new DelegateCommand(() => redos++, () => false);
			var editing = new TextBox();
			var search = new TextBox();
			var window = new StackPanel { Children = { editing, search } };
			//a box keeps an undo of its own once it has been laid out
			window.Measure(new Size(200, 100));
			window.Arrange(new Rect(0, 0, 200, 100));
			DocumentUndo.Route(window, () => undo, () => redo, source => source == search);

			Assert.True(ApplicationCommands.Undo.CanExecute(null, editing));
			ApplicationCommands.Undo.Execute(null, editing);
			Assert.Equal(1, undos);
			Assert.False(ApplicationCommands.Redo.CanExecute(null, editing)); //the document says, not the box
			ApplicationCommands.Redo.Execute(null, editing);
			Assert.Equal(0, redos);

			search.SelectedText = "typed";
			Assert.Equal("typed", search.Text);
			Assert.True(ApplicationCommands.Undo.CanExecute(null, search));
			ApplicationCommands.Undo.Execute(null, search);
			Assert.Equal(1, undos);
			Assert.Equal("", search.Text); //the box's own undo
		});
	}
}
