using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

//The kinds of entry (SPEC: Undo → One entry per action). Each holds what its action
//changed and nothing more, and finds what it changes by its file and its path there:
//entries are undone in order, so every path means what it meant when the entry was made.

/// <summary>
///     Typing into one text field: the node, the language for an entry's field,
///     the field and, for a value, the plural form typed into, and its text
///     before and after. A run of keystrokes in the field folds into the entry
///     its first keystroke made, in steps a word or a pause apart (SPEC: Undo →
///     Text boxes): an editing box steps back and forth through them, and to
///     every other caller the run is one entry.
/// </summary>
public sealed class FieldEdit : UndoEntry {
	/// <summary>Keystrokes further apart than this are steps of their own.</summary>
	public static readonly TimeSpan Pause = TimeSpan.FromSeconds(1.5);

	public FieldEdit(NodeRef node, string? language, DocumentField field, string before, string after, string? form = null) {
		Node = node;
		Language = language;
		Field = field;
		Form = form;
		Before = before;
		steps = [new Step(after, Keystroke.Of(before, after).Kind)];
	}

	public NodeRef Node { get; }
	public override string? Language { get; }
	public DocumentField Field { get; }
	/// <summary>The plural form typed into (SPEC: Plural forms), or the parameter whose description it is; null for the plain value and every other field.</summary>
	public string? Form { get; }
	public string Before { get; }
	/// <summary>The run's last text, every step standing.</summary>
	public string After => steps[^1].Text;
	/// <summary>The steps the run is made of.</summary>
	public int Steps => steps.Count;
	/// <summary>How many of them stand: all, unless a box stepped some back; none once the run is undone.</summary>
	public int Standing { get; private set; } = 1;
	/// <summary>Steps stepped back wait to be put back, and the run still stands.</summary>
	public bool Partly => Standing > 0 && Standing < steps.Count;
	/// <summary>The typing raised the key's Needs Review, as a note does; undoing it lowers the hand.</summary>
	public bool RaisedReview {
		get => raisedAt > 0;
		set => raisedAt = !value ? 0 : raisedAt > 0 ? raisedAt : Math.Max(Standing, 1);
	}
	/// <summary>A translator's note: a field whose typing raises Needs Review.</summary>
	public bool IsNote => Field is DocumentField.KeyComment or DocumentField.EntryComment;
	/// <summary>The translations typing the default stamped stale; undoing it takes the stamps back.</summary>
	public IReadOnlyList<string> Staled { get; private set; } = [];
	//typed back to where it started, a run changes nothing, though a hand it raised stays up;
	//its stale stamps go with it, as the default is what they were made against again
	internal bool ChangesNothing => Before == After && !RaisedReview;

	//one step: the text it leaves; the box's selection where its first keystroke found it
	//and where its last left it (none when no box typed it); how its keystrokes change the
	//text; when the last landed; and whether the document is dirty with it standing
	private sealed class Step(string text, Stroke kind) {
		public string Text = text;
		public readonly Stroke Kind = kind;
		public Selection? Found, Left;
		public DateTimeOffset Last;
		public bool Dirty = true;
	}

	private readonly List<Step> steps;
	//the step the hand went up in, counting from one; zero when it stayed down
	private int raisedAt;
	//the step the stale stamps went on in, likewise, and the stamp
	private int staledAt;
	private string? stamp;
	//the keystroke folded in last, until its box says where it found and left the selection:
	//the step it went into, the text that step had before it, and whether it raised the hand
	//or stamped the translations
	private (Step Step, string? Extended, bool Raised, bool Stamped)? awaiting;

	//the text and the dirtiness with so many steps standing
	private string TextAt(int standing) => standing == 0 ? Before : steps[standing - 1].Text;
	internal bool DirtyAt(int standing) => standing == 0 ? DirtyBefore : steps[standing - 1].Dirty;
	internal override bool DirtyAfter { get => steps[^1].Dirty; set => steps[^1].Dirty = value; }

	internal override void Unsaved() {
		DirtyBefore = true;
		foreach (Step step in steps) {
			step.Dirty = true;
		}
	}

	//the document was saved with the steps standing as they are
	internal void SavedPartly() => steps[Standing - 1].Dirty = false;

	//a new edit drops the steps stepped back, as it drops the entries undone
	internal void DropStepsBack() {
		steps.RemoveRange(Standing, steps.Count - Standing);
		if (raisedAt > Standing) {
			raisedAt = 0;
		}
		if (staledAt > Standing) {
			Unstamped();
		}
	}

	/// <summary>The keystroke stamped <paramref name="codes"/> stale with <paramref name="mark"/>, typing the default.</summary>
	internal void Stamped(IReadOnlyList<string> codes, string mark) {
		Staled = codes;
		stamp = mark;
		staledAt = Math.Max(Standing, 1);
	}

	private void Unstamped() {
		Staled = [];
		stamp = null;
		staledAt = 0;
	}

	/// <summary>
	///     Where a box's selection goes with <paramref name="standing"/> steps
	///     standing: where the step after them found it, coming back to it, and
	///     where the last of them left it, going forward; null when no box said.
	/// </summary>
	public Selection? SelectionAt(int standing, bool undoing) => undoing ? steps[standing].Found : steps[standing - 1].Left;

	//the stack took the entry: its first keystroke landed now, and its box has yet to say
	//where it found and left the selection
	internal void Began(DateTimeOffset now) {
		steps[0].Last = now;
		awaiting = (steps[0], null, false, false);
	}

	//the next keystroke, when it lands in the same field: part of the last step, unless a
	//pause, a word's start, a change of direction or a change of more than one character
	//comes between, which makes it a step of its own
	internal bool Absorb(FieldEdit next, DateTimeOffset now) {
		if (next.Node != Node || next.Language != Language || next.Field != Field || next.Form != Form) {
			return false;
		}
		Keystroke stroke = Keystroke.Of(next.Before, next.After);
		Step last = steps[^1];
		string? extended = null;
		if (stroke.Kind != Stroke.More && stroke.Kind == last.Kind && !stroke.StartsWord && now - last.Last <= Pause) {
			extended = last.Text;
			last.Text = next.After;
		}
		else {
			steps.Add(last = new Step(next.After, stroke.Kind));
			Standing = steps.Count;
		}
		last.Last = now;
		bool raised = next.RaisedReview && raisedAt == 0;
		if (raised) {
			raisedAt = Standing;
		}
		bool stamped = next.Staled.Count != 0 && staledAt == 0;
		if (stamped) {
			Stamped(next.Staled, next.stamp!);
		}
		awaiting = (last, extended, raised, stamped);
		return true;
	}

	//the keystroke folded in last told where it found the box's selection and where it left
	//it; one whose box's caret moved since the step's last keystroke is a step of its own
	internal void Typed(string text, Selection found, Selection left) {
		//another box's text, changed on the way, is not this keystroke's
		if (awaiting is not (Step step, var extended, var raised, var stamped) || step.Text != text) {
			return;
		}
		awaiting = null;
		if (extended is not null && step.Left is { } was && was != found) {
			step.Text = extended;
			steps.Add(step = new Step(text, step.Kind) { Last = step.Last });
			Standing = steps.Count;
			if (raised) {
				raisedAt = Standing;
			}
			if (stamped) {
				staledAt = Standing;
			}
		}
		step.Found ??= found;
		step.Left = left;
	}

	public override NodeRef? Site(bool undoing) => Node;

	//undone whole, or redone whole
	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) => Apply(session, tree, undoing ? 0 : steps.Count);

	/// <summary>Puts the field back as it is with <paramref name="standing"/> of the steps standing.</summary>
	public NodeRef? Apply(WordsSession session, TreeViewModel tree, int standing) {
		string text = TextAt(standing);
		Standing = standing;
		//a comment's text is the tree's; the rest is the document's, set through the session
		if (Field == DocumentField.CommentText) {
			if (Node.Resolve(tree) is OrganizerNode organizer) {
				organizer.Text = text;
			}
			return Node;
		}
		var (file, name) = (Node.File, Node.Label);
		if (Field == DocumentField.ParameterDescription) {
			session.Describe(file, name, Form ?? "", text);
		}
		else if (Written() is { } field) {
			session.Set(file, name, field, text);
		}
		//the text first: the hand and the stamps go back to where they stood with it
		if (RaisedReview) {
			session.Set(file, name, WordsField.Hand, standing >= raisedAt ? "" : null);
		}
		foreach (string code in Staled) {
			session.Set(file, name, WordsField.StaleIn(code), standing >= staledAt ? stamp : null);
		}
		return Node;
	}

	//the field as the document names it; none for an entry's without a language
	private WordsField? Written() => (Field, Language) switch {
		(DocumentField.DefaultValue, _) => new("value", "", Form ?? ""),
		(DocumentField.KeyContext, _) => new("context", "", ""),
		(DocumentField.KeyComment, _) => new("comment", "", ""),
		(DocumentField.EntryValue, { } code) => new("value", code, Form ?? ""),
		(DocumentField.EntryContext, { } code) => new("context", code, ""),
		(DocumentField.EntryComment, { } code) => new("comment", code, ""),
		_ => null,
	};

	//what one keystroke did to the text: typed one character, removed one, or more than
	//that (a paste, a cut, a line break, a selection typed over); and whether the one it
	//typed starts a word
	private enum Stroke { Typed, Removed, More }

	private readonly record struct Keystroke(Stroke Kind, bool StartsWord) {
		public static Keystroke Of(string before, string after) {
			int prefix = 0, shorter = Math.Min(before.Length, after.Length);
			while (prefix < shorter && before[prefix] == after[prefix]) {
				prefix++;
			}
			int suffix = 0;
			while (suffix < shorter - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) {
				suffix++;
			}
			int removed = before.Length - prefix - suffix, added = after.Length - prefix - suffix;
			return (removed, added) switch {
				(0, 1) => new(Stroke.Typed, prefix > 0 && char.IsWhiteSpace(after[prefix - 1]) && !char.IsWhiteSpace(after[prefix])),
				(1, 0) => new(Stroke.Removed, false),
				_ => new(Stroke.More, false),
			};
		}
	}
}

/// <summary>A command on one key, its flag before and after; the selection stays on the key.</summary>
public abstract class KeyEdit(WordsFile file, string name) : UndoEntry {
	/// <summary>The key's file.</summary>
	protected WordsFile File { get; } = file;
	/// <summary>The key's name in it.</summary>
	protected string Name { get; } = name;

	public override NodeRef? Site(bool undoing) => new NodeRef(File, Name);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		Change(session, undoing);
		return new NodeRef(File, Name);
	}

	//puts the key's state back through the session, which finds it by its file and name
	protected virtual void Change(WordsSession session, bool undoing) {
	}
}

/// <summary>Needs Review toggled.</summary>
public sealed class ReviewEdit(WordsFile file, string name, bool raised) : KeyEdit(file, name) {
	protected override void Change(WordsSession session, bool undoing) => session.Set(File, Name, WordsField.Hand, raised != undoing ? "" : null);
}

/// <summary>Stale toggled in one language.</summary>
public sealed class StaleEdit(WordsFile file, string name, string language, string? before, string? after) : KeyEdit(file, name) {
	public override string? Language => language;

	protected override void Change(WordsSession session, bool undoing) => session.Set(File, Name, WordsField.StaleIn(language), undoing ? before : after);
}

/// <summary>Stale All Languages: one stamp on every entry, the stamps it replaced kept.</summary>
public sealed class StaleAllEdit(WordsFile file, string name, IReadOnlyDictionary<string, string?> before, string after) : KeyEdit(file, name) {
	protected override void Change(WordsSession session, bool undoing) {
		foreach (var (code, stamp) in before) {
			session.Set(File, Name, WordsField.StaleIn(code), undoing ? stamp : after);
		}
	}
}

/// <summary>Constant toggled: the key renamed with its marker, and the translations making it constant cleared.</summary>
/// <param name="file">The key's file.</param>
/// <param name="before">The key's name before the toggle.</param>
/// <param name="after">Its name after, the marker added or dropped.</param>
public sealed class ConstantEdit(WordsFile file, string before, string after, bool madeConstant, IReadOnlyDictionary<string, WordsEntry>? cleared) : KeyEdit(file, before) {
	public override NodeRef? Site(bool undoing) => new NodeRef(File, undoing ? after : Name);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		(string from, string to) = undoing ? (after, Name) : (Name, after);
		KeyNode? node = tree.NodeAt(File, from);
		if (session.SetConstant(File, from, madeConstant != undoing, clearEntries: !undoing && cleared is not null) is null) {
			return new NodeRef(File, from);
		}
		if (undoing && cleared is not null) {
			session.PutBackEntries(File, to, cleared);
		}
		node?.Relabel(to);
		return new NodeRef(File, to);
	}
}

/// <summary>
///     A definition added, adopted, renamed, retyped or taken away (SPEC: Parameters →
///     Undo): the key's parameters before and after.
/// </summary>
public sealed class ParametersEdit(WordsFile file, string name, IReadOnlyList<WordsParameter> before, IReadOnlyList<WordsParameter> after) : KeyEdit(file, name) {
	protected override void Change(WordsSession session, bool undoing) => session.PutBackParameters(File, Name, undoing ? before : after);

	/// <summary>
	///     Carries a renamed definition's input (<see cref="ParameterInputs"/>) back to
	///     its old name on undo, and on to the new one on redo, as the rename carried
	///     it: the inputs are the session's, so the history holds none, and only follows
	///     the names. A rename keeps its definition's place, so the names that differ
	///     place by place are the ones renamed.
	/// </summary>
	public void Carry(WordsSession session, ParameterInputs inputs, bool undoing) {
		if (before.Count != after.Count || !File.Keys.TryGetValue(Name, out WordsKey? key)) {
			return;
		}
		foreach (var (was, now) in before.Zip(after)) {
			var (from, to) = undoing ? (now.Key, was.Key) : (was.Key, now.Key);
			inputs.Carry(key, from, to);
		}
	}
}

/// <summary>Key information added to a node.</summary>
public sealed class KeyAdded(WordsFile file, string name) : UndoEntry {
	public override NodeRef? Site(bool undoing) => new NodeRef(file, name);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (undoing) {
			session.RemoveKey(file, name);
		}
		else {
			session.AddKey(file, name);
		}
		return new NodeRef(file, name);
	}
}

/// <summary>Key information removed from a node, a copy of the key kept.</summary>
public sealed class KeyRemoved(WordsFile file, WordsKey key) : UndoEntry {
	private readonly WordsKey kept = new(key);

	public override NodeRef? Site(bool undoing) => new NodeRef(file, kept.BlockKey);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (undoing) {
			session.Restore(file, kept);
		}
		else {
			session.RemoveKey(file, kept.BlockKey);
		}
		return new NodeRef(file, kept.BlockKey);
	}
}

/// <summary>
///     A node in or out of the tree: its parent, its place, the node itself
///     (its subtree, comments and all), copies of the keys beneath it, and a
///     comment's text — the preamble's lives in its file.
/// </summary>
public abstract class NodeEdit : UndoEntry {
	private readonly NodeRef parent;
	private readonly int index;
	private readonly KeyNode node;
	private readonly IReadOnlyList<WordsKey> keys;
	private readonly string text;

	protected NodeEdit(WordsSession session, KeyNode node) {
		KeyNode at = node.Parent ?? throw new ArgumentException("a file node is not added or removed this way", nameof(node));
		parent = NodeRef.Of(at);
		index = at.Children.IndexOf(node);
		this.node = node;
		keys = node is OrganizerNode ? [] : [.. session.KeysUnder(parent.File, node.FullLabel).Select(key => new WordsKey(key))];
		text = (node as OrganizerNode)?.Text ?? "";
	}

	/// <summary>The node, where it stands in the tree.</summary>
	protected NodeRef Here => node is OrganizerNode ? parent with { Index = index } : new NodeRef(parent.File, node.FullLabel);
	protected NodeRef Parent => parent;

	protected NodeRef? Restore(WordsSession session, TreeViewModel tree) {
		if (parent.Resolve(tree) is not { } at) {
			return null;
		}
		at.Children.Insert(index, node);
		foreach (WordsKey key in keys) {
			session.Restore(parent.File, key);
		}
		if (node is OrganizerNode organizer) {
			organizer.Text = text;
		}
		return Here;
	}

	protected NodeRef? Take(WordsSession session, TreeViewModel tree) {
		if (parent.Resolve(tree) is not { } at) {
			return null;
		}
		if (node is OrganizerNode organizer) {
			organizer.Text = "";
		}
		else {
			session.RemoveKeysUnder(parent.File, node.FullLabel);
		}
		node.IsSelected = false;
		at.Children.Remove(node);
		return Parent;
	}
}

/// <summary>A node or a comment added; undone, the selection goes to its parent.</summary>
public sealed class NodeAdded(WordsSession session, KeyNode node) : NodeEdit(session, node) {
	public override NodeRef? Site(bool undoing) => undoing ? Here : Parent;
	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) => undoing ? Take(session, tree) : Restore(session, tree);
}

/// <summary>A node removed, however much it took with it; undone, the selection goes to the node.</summary>
public sealed class NodeRemoved(WordsSession session, KeyNode node) : NodeEdit(session, node) {
	public override NodeRef? Site(bool undoing) => undoing ? Parent : Here;
	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) => undoing ? Restore(session, tree) : Take(session, tree);
}

/// <summary>Where a node stood, for a move: its file, its parent's path, its place there, its path and its name.</summary>
public readonly record struct Place(WordsFile File, string Parent, int Index, string Label, string Name) {
	public static Place Of(KeyNode node) {
		KeyNode parent = node.Parent ?? throw new ArgumentException("a file node has no place to move from", nameof(node));
		return new(NodeRef.Of(parent).File, parent.FullLabel, parent.Children.IndexOf(node), node.FullLabel, node.Label);
	}
}

/// <summary>
///     A rename or a drag: the node from one place to another, its keys renamed
///     in the session when its file or its path changes. A comment's path is nobody's key.
/// </summary>
public sealed class Move(Place from, Place to, bool comment) : UndoEntry {
	public override NodeRef? Site(bool undoing) => At(undoing ? to : from);

	private NodeRef At(Place place) => comment ? new NodeRef(place.File, place.Parent, place.Index) : new NodeRef(place.File, place.Label);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		(Place a, Place b) = undoing ? (to, from) : (from, to);
		if (tree.NodeAt(a.File, a.Parent) is not { } oldParent || oldParent.Children.ElementAtOrDefault(a.Index) is not { } node
				|| tree.NodeAt(b.File, b.Parent) is not { } newParent) {
			return null;
		}
		if (!comment && (a.File, a.Label) != (b.File, b.Label) && !Rename(session, a, b)) {
			return At(a);
		}
		if (oldParent != newParent || a.Index != b.Index) {
			oldParent.Children.RemoveAt(a.Index);
			newParent.Children.Insert(b.Index, node);
		}
		node.Label = b.Name;
		node.Relabel(b.Label);
		return At(b);
	}

	//a rename stays in its file; a drag into another keeps its last segment
	private static bool Rename(WordsSession session, Place a, Place b)
		=> a.File == b.File ? session.TryRename(a.File, a.Label, b.Label, out _) : session.TryMove(a.File, a.Label, b.File, b.Parent, out _);
}

/// <summary>A Settings Okay that changed a file's <c>param=</c> and <c>param-xx=</c> slots. It shows nowhere in the tree.</summary>
public sealed class FileSettingsEdit(WordsFile file, SettingsSlots before, SettingsSlots after) : UndoEntry {
	public override NodeRef? Site(bool undoing) => null;

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		(undoing ? before : after).PutBack(file);
		return null;
	}
}

/// <summary>
///     A Language Manager commit: its table operations, kept as they stood before
///     the commit and after it (<see cref="LanguageTableChange"/>), and put back
///     whole either way. A commit that merged two languages is a boundary
///     instead, and makes none. It shows nowhere in the tree.
/// </summary>
public sealed class LanguagesEdit(LanguageTableChange change) : UndoEntry {
	public override NodeRef? Site(bool undoing) => null;

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		change.PutBack(before: undoing);
		return null;
	}
}
