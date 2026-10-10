using PatTech.Localization.Authoring;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Undo inside a text box, headless (SPEC: Undo → Text boxes): an editing
///     box answers Undo and Redo from the document's history, a step of a typing
///     run at a time with the caret put back, then on into the entries before
///     it; the Edit menu takes a run whole; a plain box keeps WPF's own.
/// </summary>
public class TextBoxUndoTests {
	//the document's history as a box sees it, each call written down
	private sealed class FakeHistory : ITextHistory {
		public bool CanUndo { get; set; }
		public bool CanRedo { get; set; }
		public int Undos, Redos;
		public readonly List<(string Text, Selection Found, Selection Left)> Typed = [];
		public void Undo() => Undos++;
		public void Redo() => Redos++;
		void ITextHistory.Typed(string text, Selection found, Selection left) => Typed.Add((text, found, left));
	}

	//a box laid out, as it is once shown
	private static void LayOut(params UIElement[] boxes) {
		var panel = new StackPanel();
		foreach (UIElement box in boxes) {
			panel.Children.Add(box);
		}
		panel.Measure(new Size(300, 200));
		panel.Arrange(new Rect(0, 0, 300, 200));
	}

	//one keystroke each, as the keyboard types it: at the caret, over the selection
	private static void Type(TextBox box, string text, FakeTime? time = null) {
		foreach (char c in text) {
			box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, box, c.ToString())) {
				RoutedEvent = TextCompositionManager.TextInputEvent,
			});
			time?.Now += TimeSpan.FromMilliseconds(100);
		}
	}

	private static void Undo(TextBox box) {
		Assert.True(ApplicationCommands.Undo.CanExecute(null, box));
		ApplicationCommands.Undo.Execute(null, box);
	}

	private static void Redo(TextBox box) {
		Assert.True(ApplicationCommands.Redo.CanExecute(null, box));
		ApplicationCommands.Redo.Execute(null, box);
	}

	//a key's context typed into a box bound to it, as the main window binds its boxes
	private sealed class Harness {
		public readonly FakeTime Time = new();
		public readonly MainWindowViewModel Vm;
		public readonly WordsKey Key;
		public readonly WordsBox Box;
		public Selection? Restored;
		public int Focused;

		public Harness(string file = "value-en=English\n\n[k]\nvalue=v\n", string label = "Ex.k", DocumentField field = DocumentField.KeyContext) {
			Vm = new MainWindowViewModel(new FakeDialogs(), time: Time);
			Vm.LoadFile(new StringReader(file), "Ex");
			Vm.Tree.Select(MainWindowViewModelTests.Node(Vm, label));
			Key = Vm.Tree.SelectedKey!;
			Box = new WordsBox { History = Vm.TextHistory };
			Box.SetBinding(TextBox.TextProperty, new Binding(field switch {
				DocumentField.KeyComment => nameof(WordsKey.Comment),
				DocumentField.DefaultValue => nameof(WordsKey.DefaultValue),
				_ => nameof(WordsKey.Context),
			}) {
				Source = Key,
				UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
			});
			LayOut(Box);
			Vm.FieldFocusRequested += (_, selection) => {
				Restored = selection;
				Focused++;
			};
		}

		public void Pause() => Time.Now += FieldEdit.Pause + TimeSpan.FromSeconds(1);
	}

	[Fact]
	public void TheBoxAnswersUndoAndRedoFromItsHistoryAndTellsWhereEachKeystrokeLeftTheSelection() {
		NavigationTests.RunSta(() => {
			var history = new FakeHistory { CanUndo = true };
			var box = new WordsBox { History = history };
			var plain = new TextBox();
			LayOut(box, plain);
			Assert.False(box.IsUndoEnabled); //WPF's own stack is off

			Undo(box);
			Assert.Equal(1, history.Undos);
			Assert.False(ApplicationCommands.Redo.CanExecute(null, box)); //the history says, not the box
			ApplicationCommands.Redo.Execute(null, box);
			Assert.Equal(0, history.Redos);

			Type(box, "ab");
			box.CaretIndex = 0;
			Type(box, "c");
			box.Select(1, 2);
			Type(box, "d");
			Assert.Equal("cd", box.Text);
			Assert.Equal([
				("a", new Selection(0, 0), new Selection(1, 0)),
				("ab", new Selection(1, 0), new Selection(2, 0)),
				("cab", new Selection(0, 0), new Selection(1, 0)),
				("cd", new Selection(1, 2), new Selection(2, 0)),
			], history.Typed);

			//a plain box beside it keeps its own
			plain.SelectedText = "typed";
			Undo(plain);
			Assert.Equal("", plain.Text);
			Assert.Equal(1, history.Undos);
		});
	}

	[Fact]
	public void TheBoxTakesTextBoxsStyle() {
		NavigationTests.RunSta(() => {
			var box = new WordsBox();
			var panel = new StackPanel { Children = { box } };
			panel.Resources.Add(typeof(TextBox), new Style(typeof(TextBox)) { Setters = { new Setter(FrameworkElement.TagProperty, "styled") } });
			panel.Measure(new Size(300, 200));
			Assert.Equal("styled", box.Tag); //an implicit style matches its exact type alone
		});
	}

	[Fact]
	public void CtrlZStepsBackAWordAtATimeWithTheCaretAndThenReachesTheEntryBefore() {
		NavigationTests.RunSta(() => {
			var h = new Harness();
			h.Vm.ToggleNeedsReviewCommand.Execute(null);
			Type(h.Box, "one two three", h.Time);
			Assert.Equal("one two three", h.Key.Context);
			Assert.Equal(2, h.Vm.UndoStack.DoneCount); //the run is one entry

			Undo(h.Box);
			Assert.Equal("one two ", h.Box.Text);
			Assert.Equal(new Selection(8, 0), h.Restored); //where "three" was started
			Assert.Equal(2, h.Vm.UndoStack.DoneCount);
			Undo(h.Box);
			Assert.Equal("one ", h.Key.Context);
			Assert.Equal(new Selection(4, 0), h.Restored);
			Undo(h.Box);
			Assert.Equal("", h.Key.Context);
			Assert.Equal(new Selection(0, 0), h.Restored);
			Assert.Equal(1, h.Vm.UndoStack.DoneCount);
			Assert.True(h.Key.NeedsReview);

			//on past the run, into the entry before it
			Undo(h.Box);
			Assert.False(h.Key.NeedsReview);
			Assert.False(ApplicationCommands.Undo.CanExecute(null, h.Box));

			//and Redo walks it forward again, the caret where each step left it
			Redo(h.Box);
			Assert.True(h.Key.NeedsReview);
			Assert.Equal("", h.Key.Context);
			Redo(h.Box);
			Assert.Equal("one ", h.Box.Text);
			Assert.Equal(new Selection(4, 0), h.Restored);
			Redo(h.Box);
			Assert.Equal("one two ", h.Key.Context);
			Assert.Equal(new Selection(8, 0), h.Restored);
			Redo(h.Box);
			Assert.Equal("one two three", h.Key.Context);
			Assert.Equal(new Selection(13, 0), h.Restored);
			Assert.False(ApplicationCommands.Redo.CanExecute(null, h.Box));
			Assert.False(h.Vm.RedoCommand.CanExecute(null));
			Assert.True(h.Vm.IsDirty);
		});
	}

	[Fact]
	public void TheEditMenusUndoTakesTheRunWholeAndItsRedoPutsBackWhatABoxSteppedBack() {
		NavigationTests.RunSta(() => {
			var h = new Harness();
			Type(h.Box, "one two three", h.Time);

			h.Vm.UndoCommand.Execute(null);
			Assert.Equal("", h.Key.Context);
			Assert.Equal(new Selection(0, 0), h.Restored);
			Assert.Equal(0, h.Vm.UndoStack.DoneCount);
			h.Vm.RedoCommand.Execute(null);
			Assert.Equal("one two three", h.Key.Context);
			Assert.Equal(new Selection(13, 0), h.Restored);

			//a box steps two back; the menu's Redo puts the rest of the run back at once
			Undo(h.Box);
			Undo(h.Box);
			Assert.Equal("one ", h.Key.Context);
			Assert.True(h.Vm.RedoCommand.CanExecute(null));
			h.Vm.RedoCommand.Execute(null);
			Assert.Equal("one two three", h.Key.Context);
			Assert.False(h.Vm.RedoCommand.CanExecute(null));

			//and from part way back, the menu's Undo takes what stands of it
			Undo(h.Box);
			h.Vm.UndoCommand.Execute(null);
			Assert.Equal("", h.Key.Context);
			Assert.Equal(0, h.Vm.UndoStack.DoneCount);
			Assert.False(h.Vm.IsDirty);
		});
	}

	[Fact]
	public void APauseADirectionAMovedCaretAndAPasteEachStartAStep() {
		NavigationTests.RunSta(() => {
			var h = new Harness();
			Type(h.Box, "abc", h.Time);
			h.Pause();
			Type(h.Box, "def", h.Time); //after a pause
			EditingCommands.Backspace.Execute(null, h.Box); //the other way
			EditingCommands.Backspace.Execute(null, h.Box);
			Type(h.Box, "e", h.Time); //and back again
			h.Box.CaretIndex = 0;
			Type(h.Box, "x", h.Time); //somewhere else, though no word starts and no time passed
			h.Box.CaretIndex = h.Box.Text.Length;
			h.Box.SelectedText = "pasted"; //more than one character
			h.Box.CaretIndex = h.Box.Text.Length;
			Type(h.Box, "y", h.Time);
			Assert.Equal("xabcdepastedy", h.Key.Context);
			FieldEdit run = Assert.IsType<FieldEdit>(h.Vm.UndoStack.NextUndo);
			Assert.Equal(1, h.Vm.UndoStack.DoneCount);
			Assert.Equal(7, run.Steps);

			string[] back = ["xabcdepasted", "xabcde", "abcde", "abcd", "abcdef", "abc", ""];
			foreach (string text in back) {
				Undo(h.Box);
				Assert.Equal(text, h.Key.Context);
			}
			Assert.Equal(0, h.Vm.UndoStack.DoneCount);
		});
	}

	[Fact]
	public void TypingAfterSteppingBackDropsTheStepsSteppedBack() {
		NavigationTests.RunSta(() => {
			var h = new Harness();
			Type(h.Box, "one two", h.Time);
			Undo(h.Box);
			Assert.Equal("one ", h.Key.Context);
			h.Box.CaretIndex = h.Box.Text.Length;
			Type(h.Box, "x", h.Time);
			Assert.Equal("one x", h.Key.Context);
			Assert.False(h.Vm.RedoCommand.CanExecute(null));
			Assert.Equal(2, h.Vm.UndoStack.DoneCount); //the new typing is an entry of its own

			h.Vm.UndoCommand.Execute(null);
			Assert.Equal("one ", h.Key.Context);
			h.Vm.UndoCommand.Execute(null);
			Assert.Equal("", h.Key.Context);
			h.Vm.RedoCommand.Execute(null);
			Assert.Equal("one ", h.Key.Context); //"two" is gone with the step it was in
		});
	}

	[Fact]
	public void ASaveBetweenStepsIsCleanThereAndNowhereElse() {
		NavigationTests.RunSta(() => {
			string folder = Path.Combine(Path.GetTempPath(), $"WordsEditBoxUndo-{Guid.NewGuid():N}");
			Directory.CreateDirectory(folder);
			try {
				string path = Path.Combine(folder, "Ex.ini");
				File.WriteAllText(path, "value-en=English\n\n[k]\nvalue=v\n");
				var h = new Harness();
				h.Vm.ResetCore();
				h.Vm.LoadFile(path);
				h.Vm.Tree.Select(MainWindowViewModelTests.Node(h.Vm, "Ex.k"));
				WordsKey key = h.Vm.Tree.SelectedKey!;
				h.Box.SetBinding(TextBox.TextProperty, new Binding(nameof(WordsKey.Context)) { Source = key, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

				Type(h.Box, "one two", h.Time);
				Undo(h.Box);
				h.Vm.Save();
				Assert.False(h.Vm.IsDirty);

				Undo(h.Box);
				Assert.True(h.Vm.IsDirty);
				Redo(h.Box);
				Assert.False(h.Vm.IsDirty);
				Redo(h.Box);
				Assert.True(h.Vm.IsDirty);
				Undo(h.Box);
				Assert.False(h.Vm.IsDirty);

				//the run whole is neither end of it
				h.Vm.UndoCommand.Execute(null);
				Assert.True(h.Vm.IsDirty);
				h.Vm.RedoCommand.Execute(null);
				Assert.True(h.Vm.IsDirty);
			}
			finally {
				Directory.Delete(folder, recursive: true);
			}
		});
	}

	[Fact]
	public void ANoteLowersTheHandOnlyOnceTheStepThatRaisedItIsSteppedBack() {
		NavigationTests.RunSta(() => {
			var h = new Harness(field: DocumentField.KeyComment);
			Type(h.Box, " ", h.Time); //nothing to read yet: the hand stays down
			Assert.False(h.Key.NeedsReview);
			h.Pause();
			Type(h.Box, "see me", h.Time);
			Assert.True(h.Key.NeedsReview);

			Undo(h.Box);
			Assert.Equal(" see ", h.Key.Comment);
			Assert.True(h.Key.NeedsReview);
			Undo(h.Box);
			Assert.Equal(" ", h.Key.Comment);
			Assert.False(h.Key.NeedsReview);
			Redo(h.Box);
			Assert.True(h.Key.NeedsReview);
			h.Vm.UndoCommand.Execute(null);
			Assert.Equal("", h.Key.Comment);
			Assert.False(h.Key.NeedsReview);
		});
	}

	[Fact]
	public void TheDefaultsStaleStampsGoWithTheStepThatMadeThem() {
		NavigationTests.RunSta(() => {
			var h = new Harness("value-en=English\nvalue-it=Italiano\n\n[k]\nvalue=\nvalue-it=Ciao\n", field: DocumentField.DefaultValue);
			Type(h.Box, "one two", h.Time);
			Assert.NotNull(h.Key.Entries["it"].Stale);

			Undo(h.Box);
			Assert.Equal("one ", h.Key.DefaultValue);
			Assert.NotNull(h.Key.Entries["it"].Stale); //the first step made them
			Undo(h.Box);
			Assert.Equal("", h.Key.DefaultValue);
			Assert.Null(h.Key.Entries["it"].Stale);
			Redo(h.Box);
			Assert.NotNull(h.Key.Entries["it"].Stale);
		});
	}

	[Fact]
	public void OnlyTheKeystrokesOwnBoxTellsWhereItLeftTheSelection() {
		var stack = new UndoStack(new FakeTime());
		stack.Type(new FieldEdit(new NodeRef("Ex.k"), null, DocumentField.KeyContext, "", "a"), wasDirty: false);
		stack.Typed("another box", new Selection(5, 0), new Selection(6, 0)); //not this keystroke's text
		stack.Typed("a", new Selection(0, 0), new Selection(1, 0));
		stack.Typed("a", new Selection(3, 0), new Selection(3, 0)); //a text handed to the box after it
		FieldEdit run = Assert.IsType<FieldEdit>(stack.NextUndo);
		Assert.Equal(new Selection(0, 0), run.SelectionAt(0, undoing: true));
		Assert.Equal(new Selection(1, 0), run.SelectionAt(1, undoing: false));
	}
}
