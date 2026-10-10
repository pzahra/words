using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>A text field the panes edit (SPEC: Undo → Fields): what a <see cref="FieldEdit"/> names and the window focuses.</summary>
public enum DocumentField {
	DefaultValue,
	KeyContext,
	KeyComment,
	EntryValue,
	EntryContext,
	EntryComment,
	CommentText,
	/// <summary>A definition's description, its parameter named as a value names its form (SPEC: Parameters → Undo).</summary>
	ParameterDescription,
}

/// <summary>
///     Where a node stands, as an entry remembers it: a key's node by its label,
///     which is unique, and a comment by its parent's label and its place there,
///     since comments share labels. Entries are undone in order, so the tree a
///     reference meets is the tree it was taken from.
/// </summary>
/// <param name="Label">The node's label, or its parent's when <paramref name="Index"/> places it.</param>
/// <param name="Index">The node's place among its parent's children; -1 for a node found by its label.</param>
public readonly record struct NodeRef(string Label, int Index = -1) {
	public static NodeRef Of(KeyNode node)
		=> node is OrganizerNode && node.Parent is { } parent ? new(parent.FullLabel, parent.Children.IndexOf(node)) : new(node.FullLabel);

	/// <summary>The node in <paramref name="tree"/>, if it stands there.</summary>
	public KeyNode? Resolve(TreeViewModel tree) {
		KeyNode? node = tree.NodeAt(Label);
		return Index < 0 ? node : node?.Children.ElementAtOrDefault(Index);
	}
}

/// <summary>
///     One action on the document (SPEC: Undo): what it changed, where that
///     shows, and its own inverse. Applying it undoing takes the action back;
///     applying it otherwise puts it back.
/// </summary>
public abstract class UndoEntry {
	/// <summary>The node the change shows on before it is undone (or redone); null when it shows nowhere in the tree.</summary>
	public abstract NodeRef? Site(bool undoing);
	/// <summary>The translation language the change is in, for an entry tied to one.</summary>
	public virtual string? Language => null;
	/// <summary>Takes the action back, or puts it back, and says where the selection goes.</summary>
	public abstract NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing);

	//the dirtiness undoing and redoing return to; a save moves them (UndoStack.Saved)
	internal bool DirtyBefore { get; set; }
	internal virtual bool DirtyAfter { get; set; }

	//a save left the disk at none of the states the entry passes through
	internal virtual void Unsaved() {
		DirtyBefore = true;
		DirtyAfter = true;
	}
}

/// <summary>
///     The edits made and the edits undone (SPEC: Undo): undo moves the latest
///     across, redo moves it back, and a new edit drops what waited to be redone.
///     Typing folds into the entry its run started, until something ends the run;
///     an editing box steps back through a run and forward again, and the run
///     stays the latest entry until the last of its steps is undone.
/// </summary>
public sealed class UndoStack(TimeProvider? time = null) {
	private readonly TimeProvider time = time ?? TimeProvider.System;
	private readonly Stack<UndoEntry> done = new();
	private readonly Stack<UndoEntry> undone = new();
	//the typing run still taking keystrokes: always the top of done while set
	private FieldEdit? run;

	public int DoneCount => done.Count;
	public int UndoneCount => undone.Count;
	public UndoEntry? NextUndo => done.TryPeek(out UndoEntry? entry) ? entry : null;
	public UndoEntry? NextRedo => undone.TryPeek(out UndoEntry? entry) ? entry : null;
	/// <summary>What a redo puts back: the steps a box stepped back of the latest run, then the next entry undone.</summary>
	public UndoEntry? NextToPutBack => NextUndo is FieldEdit { Partly: true } edit ? edit : NextRedo;
	public bool CanRedo => NextToPutBack is not null;

	/// <summary>An action changed the document; <paramref name="wasDirty"/> is what undoing it returns to.</summary>
	public void Push(UndoEntry entry, bool wasDirty) {
		(NextUndo as FieldEdit)?.DropStepsBack();
		undone.Clear();
		entry.DirtyBefore = wasDirty;
		entry.DirtyAfter = true;
		done.Push(entry);
		run = entry as FieldEdit;
		run?.Began(time.GetUtcNow());
	}

	/// <summary>
	///     A keystroke: it folds into the open run when it edits the same field,
	///     and starts an entry otherwise.
	/// </summary>
	/// <returns>
	///     The dirtiness the document goes back to when the keystroke took its run
	///     back to where it started, leaving nothing to undo; null otherwise.
	/// </returns>
	public bool? Type(FieldEdit edit, bool wasDirty) {
		if (run is null || !run.Absorb(edit, time.GetUtcNow())) {
			Push(edit, wasDirty);
			return null;
		}
		if (!run.ChangesNothing) {
			return null;
		}
		//typed and taken back: nothing left to undo, and the document is as it was
		done.Pop();
		bool before = run.DirtyBefore;
		run = null;
		return before;
	}

	/// <summary>
	///     The keystroke just typed, its box holding <paramref name="text"/>, found
	///     the box's selection at <paramref name="found"/> and left it at
	///     <paramref name="left"/>. A text the box was handed is no keystroke and
	///     is passed over.
	/// </summary>
	public void Typed(string text, Selection found, Selection left) => run?.Typed(text, found, left);

	/// <summary>The next keystroke starts an entry of its own.</summary>
	public void EndRun() => run = null;

	/// <summary>Moves the next entry to undo (or redo) across, once it has been applied.</summary>
	public UndoEntry Take(bool undoing) {
		run = null;
		UndoEntry entry = (undoing ? done : undone).Pop();
		(undoing ? undone : done).Push(entry);
		return entry;
	}

	/// <summary>
	///     The document was saved: every step away from here dirties it, and the
	///     two steps that come back to it — redoing the entry just undone, undoing
	///     the one just redone — leave it clean. Saved in part, the disk holds no
	///     state the history passes through, and every step dirties. Either way
	///     the typing run ends, so the next keystroke is a step of its own.
	/// </summary>
	/// <param name="partly">Some files were written and some were not.</param>
	public void Saved(bool partly = false) {
		run = null;
		foreach (UndoEntry entry in done.Concat(undone)) {
			entry.Unsaved();
		}
		if (partly) {
			return;
		}
		//a run a box stepped part way back was saved between two of its steps
		if (NextUndo is FieldEdit { Partly: true } edit) {
			edit.SavedPartly();
			return;
		}
		NextUndo?.DirtyAfter = false;
		NextRedo?.DirtyBefore = false;
	}

	/// <summary>A boundary: what the entries refer to is gone.</summary>
	public void Clear() {
		done.Clear();
		undone.Clear();
		run = null;
	}
}
