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
///     Where a node stands, as an entry or Back's history remembers it: its file and
///     its path there, which is a key's node's alone, or for a comment its parent's
///     path and its place there, since comments share paths. The file is the
///     session's: a file in or out empties the undo history, and Back drops an entry
///     whose file went. Entries are undone in order, so the tree a reference meets
///     is the tree it was taken from.
/// </summary>
/// <param name="File">The file the node is in.</param>
/// <param name="Label">The node's path in it, or its parent's when <paramref name="Index"/> places it; empty for the root.</param>
/// <param name="Index">The node's place among its parent's children; -1 for a node found by its path.</param>
public sealed record NodeRef(WordsFile File, string Label, int Index = -1) {
	/// <summary>Where <paramref name="node"/> stands in the tree.</summary>
	/// <exception cref="ArgumentException">The node is out of the tree.</exception>
	public static NodeRef Of(KeyNode node) {
		WordsFile file = node.Root.File ?? throw new ArgumentException($"'{node.FullLabel}' is in no file's tree", nameof(node));
		return node is OrganizerNode && node.Parent is { } parent ? new(file, parent.FullLabel, parent.Children.IndexOf(node)) : new(file, node.FullLabel);
	}

	/// <summary>The node in <paramref name="tree"/>, if it stands there.</summary>
	public KeyNode? Resolve(TreeViewModel tree) {
		KeyNode? node = tree.NodeAt(File, Label);
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
	/// <summary>The files the action changes (SPEC: Dirty by file).</summary>
	public abstract IReadOnlyCollection<WordsFile> Files { get; }

	//the files dirty with the history standing before the entry and after it, which
	//undoing and redoing return to; a save moves them (UndoStack.Saved)
	internal HashSet<WordsFile> DirtyBefore { get; private set; } = [];
	internal virtual IReadOnlySet<WordsFile> DirtyAfter => after;
	private HashSet<WordsFile> after = [];

	//the stack took the entry, dirty the files dirty before it: after it, its own are too
	internal virtual void Recorded(IReadOnlySet<WordsFile> dirty) {
		DirtyBefore = [.. dirty];
		after = [.. dirty, .. Files];
	}

	//file was written, and the history reaches the entry from where it stands, going back
	//(or ahead): the file is clean on the entry's near side when clean, and on its far
	//side too when the entry does not change it. Says whether it is clean on the far side
	internal virtual bool Saved(WordsFile file, bool clean, bool ahead) {
		bool far = clean && !Files.Contains(file);
		Mark(ahead ? DirtyBefore : after, file, clean);
		Mark(ahead ? after : DirtyBefore, file, far);
		return far;
	}

	private protected static void Mark(HashSet<WordsFile> dirty, WordsFile file, bool clean) {
		if (clean) {
			dirty.Remove(file);
		}
		else {
			dirty.Add(file);
		}
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

	/// <summary>An action changed the document; <paramref name="dirty"/>, the files dirty before it, is what undoing it returns to.</summary>
	public void Push(UndoEntry entry, IReadOnlySet<WordsFile> dirty) {
		(NextUndo as FieldEdit)?.DropStepsBack();
		undone.Clear();
		entry.Recorded(dirty);
		done.Push(entry);
		run = entry as FieldEdit;
		run?.Began(time.GetUtcNow());
	}

	/// <summary>
	///     A keystroke: it folds into the open run when it edits the same field,
	///     and starts an entry otherwise.
	/// </summary>
	/// <returns>
	///     The files dirty when the keystroke took its run back to where it
	///     started, leaving nothing to undo: those dirty before the run; null
	///     otherwise.
	/// </returns>
	public IReadOnlySet<WordsFile>? Type(FieldEdit edit, IReadOnlySet<WordsFile> dirty) {
		if (run is null || !run.Absorb(edit, time.GetUtcNow())) {
			Push(edit, dirty);
			return null;
		}
		if (!run.ChangesNothing) {
			return null;
		}
		//typed and taken back: nothing left to undo, and the document is as it was
		done.Pop();
		IReadOnlySet<WordsFile> before = run.DirtyBefore;
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
	///     <paramref name="file"/> was written (SPEC: Dirty by file): it is clean
	///     wherever the history reaches from here, back or ahead, without
	///     crossing an entry that changes it, and dirty past the first that does.
	///     Every other file keeps its marks. The typing run ends, so the next
	///     keystroke is a step of its own.
	/// </summary>
	public void Saved(WordsFile file) {
		run = null;
		bool back = true, ahead = true;
		IEnumerable<UndoEntry> below = done;
		//a run a box stepped part way back was saved between two of its steps, and the
		//steps it stepped back lie ahead before the entries undone
		if (NextUndo is FieldEdit { Partly: true } edit) {
			back = ahead = edit.SavedPartly(file);
			below = done.Skip(1);
		}
		foreach (UndoEntry entry in below) {
			back = entry.Saved(file, back, ahead: false);
		}
		foreach (UndoEntry entry in undone) {
			ahead = entry.Saved(file, ahead, ahead: true);
		}
	}

	/// <summary>A boundary: what the entries refer to is gone.</summary>
	public void Clear() {
		done.Clear();
		undone.Clear();
		run = null;
	}
}
