using System.Windows;
using System.Windows.Input;

namespace WordsEdit.Utils;

/// <summary>
///     Hands a window's Undo and Redo to the document (SPEC: Undo → Text boxes).
///     A text box turns Ctrl+Z and Ctrl+Y into <see cref="ApplicationCommands.Undo"/>
///     and <see cref="ApplicationCommands.Redo"/> before any window key binding
///     sees them; the window catches the commands on their way down, ahead of the
///     box's own handling, and runs the document's instead — except for a box that
///     keeps its own, such as a search box.
/// </summary>
public static class DocumentUndo {
	/// <param name="scope">The window whose boxes give up their undo.</param>
	/// <param name="undo">The document's undo, read when it is asked for.</param>
	/// <param name="redo">The document's redo, likewise.</param>
	/// <param name="keepsOwn">True for a command's source that keeps the box's own undo.</param>
	public static void Route(UIElement scope, Func<ICommand?> undo, Func<ICommand?> redo, Func<object, bool> keepsOwn) {
		ICommand? Document(ICommand routed) => routed == ApplicationCommands.Undo ? undo() : routed == ApplicationCommands.Redo ? redo() : null;
		bool Takes(ICommand routed, object source)
			=> (routed == ApplicationCommands.Undo || routed == ApplicationCommands.Redo) && !keepsOwn(source);

		//the preview events tunnel from the window to the box: handled here, the box
		//never hears. Plain handlers, since a CommandBinding marks whatever it sees handled
		CommandManager.AddPreviewCanExecuteHandler(scope, (_, e) => {
			if (Takes(e.Command, e.OriginalSource)) {
				e.CanExecute = Document(e.Command)?.CanExecute(null) == true;
				e.Handled = true;
			}
		});
		CommandManager.AddPreviewExecutedHandler(scope, (_, e) => {
			if (Takes(e.Command, e.OriginalSource)) {
				if (Document(e.Command) is { } command && command.CanExecute(null)) {
					command.Execute(null);
				}
				e.Handled = true;
			}
		});
	}
}
