using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WordsEdit.Utils;

/// <summary>
///     An editing box whose undo is the document's (SPEC: Undo → Text boxes).
///     WPF's own stack is off, and Undo and Redo — Ctrl+Z and Ctrl+Y, or the
///     routed commands sent to the box — answer from <see cref="History"/>,
///     which steps through the document's typing as a box's own undo would and
///     then on into the entries before it. A plain <see cref="TextBox"/>, such
///     as the search box, keeps WPF's.
/// </summary>
public class WordsBox : TextBox {
	public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(
		nameof(History), typeof(ITextHistory), typeof(WordsBox), new PropertyMetadata(null));

	/// <summary>Where Undo and Redo go, and where each keystroke's selections are told.</summary>
	public ITextHistory? History {
		get => (ITextHistory?)GetValue(HistoryProperty);
		set => SetValue(HistoryProperty, value);
	}

	//a subclass's class bindings answer ahead of TextBox's own
	static WordsBox() {
		Answer(ApplicationCommands.Undo, static history => history.CanUndo, static history => history.Undo());
		Answer(ApplicationCommands.Redo, static history => history.CanRedo, static history => history.Redo());
	}

	private static void Answer(RoutedCommand command, Func<ITextHistory, bool> can, Action<ITextHistory> run)
		=> CommandManager.RegisterClassCommandBinding(typeof(WordsBox), new CommandBinding(command,
			(sender, e) => {
				if (((WordsBox)sender).History is { } history && can(history)) {
					run(history);
				}
				e.Handled = true;
			},
			(sender, e) => {
				e.CanExecute = ((WordsBox)sender).History is { } history && can(history);
				e.Handled = true;
			}));

	public WordsBox() {
		IsUndoEnabled = false;
		//TextBox's look: an implicit style matches the exact type, never a subclass
		SetResourceReference(StyleProperty, typeof(TextBox));
	}

	//the selection as it last stood: what the next keystroke finds
	private Selection selection;

	//a keystroke raises TextChanged before SelectionChanged, so the selection kept is still
	//the one it found; a text the document set is told too, and the history knows it typed none
	protected override void OnTextChanged(TextChangedEventArgs e) {
		Selection left = new(SelectionStart, SelectionLength);
		History?.Typed(Text, selection, left);
		selection = left;
		base.OnTextChanged(e);
	}

	protected override void OnSelectionChanged(RoutedEventArgs e) {
		selection = new(SelectionStart, SelectionLength);
		base.OnSelectionChanged(e);
	}
}
