namespace WordsEdit.Utils;

/// <summary>A text box's selection: where it starts and how many characters it holds; a caret holds none.</summary>
public readonly record struct Selection(int Start, int Length);

/// <summary>
///     The history a <see cref="WordsBox"/> undoes and redoes from
///     (SPEC: Undo → Text boxes): the document's, a step at a time, as a box's
///     own undo steps. The box keeps none of its own. It says where each
///     keystroke found its selection and where it left it, so that a step taken
///     back or put back can put the caret where it stood.
/// </summary>
public interface ITextHistory {
	bool CanUndo { get; }
	bool CanRedo { get; }
	void Undo();
	void Redo();
	/// <summary>A keystroke left the box holding <paramref name="text"/>: the selection it found, and the one it left.</summary>
	void Typed(string text, Selection found, Selection left);
}
