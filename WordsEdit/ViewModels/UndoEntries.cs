namespace WordsEdit.ViewModels;

//The kinds of entry (SPEC: Undo → One entry per action). Each holds what its action
//changed and nothing more, and finds what it changes by label: entries are undone in
//order, so every label means what it meant when the entry was made.

/// <summary>
///     Typing into one text field: the node, the language for an entry's field,
///     the field, and its text before and after. A run of keystrokes in the
///     field folds into the entry its first keystroke made.
/// </summary>
public sealed class FieldEdit(NodeRef node, string? language, DocumentField field, string before, string after) : UndoEntry {
	public NodeRef Node { get; } = node;
	public override string? Language { get; } = language;
	public DocumentField Field { get; } = field;
	public string Before { get; } = before;
	public string After { get; private set; } = after;
	/// <summary>The typing raised the key's Needs Review, as a note does; undoing it lowers the hand.</summary>
	public bool RaisedReview { get; set; }
	/// <summary>A translator's note: a field whose typing raises Needs Review.</summary>
	public bool IsNote => Field is DocumentField.KeyComment or DocumentField.EntryComment;
	internal bool ChangesNothing => Before == After && !RaisedReview;

	//the next keystroke, when it lands in the same field: the run's last text is its
	internal bool Absorb(FieldEdit next) {
		if (next.Node != Node || next.Language != Language || next.Field != Field) {
			return false;
		}
		After = next.After;
		RaisedReview |= next.RaisedReview;
		return true;
	}

	public override NodeRef? Site(bool undoing) => Node;

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		string text = undoing ? Before : After;
		if (Field == DocumentField.CommentText) {
			if (Node.Resolve(tree) is OrganizerNode organizer) {
				organizer.Text = text;
			}
			return Node;
		}
		if (!session.Keys.TryGetValue(Node.Label, out WordsKey? key)) {
			return Node;
		}
		WordsEntry? entry = Language is null ? null : key.Entries.GetValueOrDefault(Language);
		switch (Field) {
			case DocumentField.DefaultValue: key.DefaultValue = text; break;
			case DocumentField.KeyContext: key.Context = text; break;
			case DocumentField.KeyComment: key.Comment = text; break;
			case DocumentField.EntryValue when entry is not null: entry.Value = text; break;
			case DocumentField.EntryContext when entry is not null: entry.Context = text; break;
			case DocumentField.EntryComment when entry is not null: entry.Comment = text; break;
		}
		//the text first: the hand goes back to where it stood with it
		if (RaisedReview) {
			key.NeedsReview = !undoing;
		}
		return Node;
	}
}

/// <summary>A command on one key, its flag before and after; the selection stays on the key.</summary>
public abstract class KeyEdit(string label) : UndoEntry {
	/// <summary>The key's label.</summary>
	protected string Label { get; } = label;

	public override NodeRef? Site(bool undoing) => new NodeRef(Label);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (session.Keys.TryGetValue(Label, out WordsKey? key)) {
			Change(key, undoing);
		}
		return new NodeRef(Label);
	}

	protected virtual void Change(WordsKey key, bool undoing) {
	}
}

/// <summary>Needs Review toggled.</summary>
public sealed class ReviewEdit(string label, bool raised) : KeyEdit(label) {
	protected override void Change(WordsKey key, bool undoing) => key.NeedsReview = raised != undoing;
}

/// <summary>Stale toggled in one language.</summary>
public sealed class StaleEdit(string label, string language, string? before, string? after) : KeyEdit(label) {
	public override string? Language => language;

	protected override void Change(WordsKey key, bool undoing) {
		if (key.Entries.TryGetValue(language, out WordsEntry? entry)) {
			entry.Stale = undoing ? before : after;
		}
	}
}

/// <summary>Stale All Languages: one stamp on every entry, the stamps it replaced kept.</summary>
public sealed class StaleAllEdit(string label, IReadOnlyDictionary<string, string?> before, string after) : KeyEdit(label) {
	protected override void Change(WordsKey key, bool undoing) {
		foreach (var (code, stamp) in before) {
			if (key.Entries.TryGetValue(code, out WordsEntry? entry)) {
				entry.Stale = undoing ? stamp : after;
			}
		}
	}
}

/// <summary>Constant toggled: the key relabelled with its marker, and the translations making it constant cleared.</summary>
/// <param name="before">The key's label before the toggle.</param>
/// <param name="after">Its label after, the marker added or dropped.</param>
public sealed class ConstantEdit(string before, string after, bool madeConstant, IReadOnlyDictionary<string, WordsEntry>? cleared) : KeyEdit(before) {
	public override NodeRef? Site(bool undoing) => new NodeRef(undoing ? after : Label);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		(string from, string to) = undoing ? (after, Label) : (Label, after);
		KeyNode? node = tree.NodeAt(from);
		if (session.SetConstant(from, madeConstant != undoing, clearEntries: !undoing && cleared is not null) is null) {
			return new NodeRef(from);
		}
		if (undoing && cleared is not null && session.Keys.TryGetValue(to, out WordsKey? key)) {
			foreach (var (code, entry) in cleared) {
				key.Entries[code] = new WordsEntry(entry);
			}
		}
		node?.Relabel(to);
		return new NodeRef(to);
	}
}

/// <summary>A Test Parameters session: the key's parameters before and after.</summary>
public sealed class ParametersEdit(string label, IReadOnlyList<WordsParameter> before, IReadOnlyList<WordsParameter> after) : KeyEdit(label) {
	/// <summary>The key's parameters as they stand, copied.</summary>
	public static IReadOnlyList<WordsParameter> Copy(WordsKey key) => [.. key.Parameters.Select(parameter => new WordsParameter(parameter))];

	public static bool Same(IReadOnlyList<WordsParameter> a, IReadOnlyList<WordsParameter> b)
		=> a.Count == b.Count && a.Zip(b).All(pair => pair.First.Key == pair.Second.Key
			&& pair.First.Value == pair.Second.Value
			&& Equals(pair.First.DataType, pair.Second.DataType));

	protected override void Change(WordsKey key, bool undoing) {
		key.Parameters.Clear();
		foreach (WordsParameter parameter in undoing ? before : after) {
			key.Parameters.Add(new WordsParameter(parameter));
		}
	}
}

/// <summary>Key information added to a node.</summary>
public sealed class KeyAdded(string label) : UndoEntry {
	public override NodeRef? Site(bool undoing) => new NodeRef(label);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (undoing) {
			session.RemoveKey(label);
		}
		else {
			session.AddKey(label);
		}
		return new NodeRef(label);
	}
}

/// <summary>Key information removed from a node, a copy of the key kept.</summary>
public sealed class KeyRemoved(WordsKey key) : UndoEntry {
	private readonly WordsKey kept = new(key);

	public override NodeRef? Site(bool undoing) => new NodeRef(kept.BlockKey);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (undoing) {
			KeptKeys.PutBack(session, kept);
		}
		else {
			session.RemoveKey(kept.BlockKey);
		}
		return new NodeRef(kept.BlockKey);
	}
}

/// <summary>
///     A node in or out of the tree: its parent, its place, the node itself
///     (its subtree, comments and all), copies of the keys beneath it, and a
///     comment's text — the preamble's lives in its file.
/// </summary>
public abstract class NodeEdit : UndoEntry {
	private readonly string parent;
	private readonly int index;
	private readonly KeyNode node;
	private readonly IReadOnlyList<WordsKey> keys;
	private readonly string text;

	protected NodeEdit(WordsSession session, KeyNode node) {
		KeyNode at = node.Parent ?? throw new ArgumentException("a file node is not added or removed this way", nameof(node));
		parent = at.FullLabel;
		index = at.Children.IndexOf(node);
		this.node = node;
		keys = node is OrganizerNode ? [] : [.. KeptKeys.Under(session, node.FullLabel).Select(key => new WordsKey(key))];
		text = (node as OrganizerNode)?.Text ?? "";
	}

	/// <summary>The node, where it stands in the tree.</summary>
	protected NodeRef Here => node is OrganizerNode ? new NodeRef(parent, index) : new NodeRef(node.FullLabel);
	protected NodeRef Parent => new(parent);

	protected NodeRef? Restore(WordsSession session, TreeViewModel tree) {
		if (tree.NodeAt(parent) is not { } at) {
			return null;
		}
		at.Children.Insert(index, node);
		foreach (WordsKey key in keys) {
			KeptKeys.PutBack(session, key);
		}
		if (node is OrganizerNode organizer) {
			organizer.Text = text;
		}
		return Here;
	}

	protected NodeRef? Take(WordsSession session, TreeViewModel tree) {
		if (tree.NodeAt(parent) is not { } at) {
			return null;
		}
		if (node is OrganizerNode organizer) {
			organizer.Text = "";
		}
		else {
			session.RemoveKeysUnder(node.FullLabel);
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

/// <summary>Where a node stood, for a move: its parent, its place there, its label and its name.</summary>
public readonly record struct Place(string Parent, int Index, string Label, string Name) {
	public static Place Of(KeyNode node) {
		KeyNode parent = node.Parent ?? throw new ArgumentException("a file node has no place to move from", nameof(node));
		return new(parent.FullLabel, parent.Children.IndexOf(node), node.FullLabel, node.Label);
	}
}

/// <summary>
///     A rename or a drag: the node from one place to another, its keys renamed
///     in the session when its label changes. A comment's label is nobody's key.
/// </summary>
public sealed class Move(Place from, Place to, bool comment) : UndoEntry {
	public override NodeRef? Site(bool undoing) => At(undoing ? to : from);

	private NodeRef At(Place place) => comment ? new NodeRef(place.Parent, place.Index) : new NodeRef(place.Label);

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		(Place a, Place b) = undoing ? (to, from) : (from, to);
		if (tree.NodeAt(a.Parent) is not { } oldParent || oldParent.Children.ElementAtOrDefault(a.Index) is not { } node
				|| tree.NodeAt(b.Parent) is not { } newParent) {
			return null;
		}
		if (!comment && a.Label != b.Label && !session.TryRename(a.Label, b.Label, out _)) {
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
}

/// <summary>A Settings Okay that changed a file's <c>param=</c> and <c>param-xx=</c> slots. It shows nowhere in the tree.</summary>
public sealed class FileSettingsEdit(string file, FileSettingsEdit.Slots before, FileSettingsEdit.Slots after) : UndoEntry {
	/// <summary>A file's slots, as written.</summary>
	public sealed record Slots(string Settings, IReadOnlyList<KeyValuePair<string, string>> Languages) {
		public static Slots Of(WordsFile file) => new(file.Settings, [.. file.LanguageSettings]);
		public bool Matches(Slots other) => Settings == other.Settings && Languages.SequenceEqual(other.Languages);
	}

	public override NodeRef? Site(bool undoing) => null;

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (session.FileOf(file) is { } target) {
			Slots slots = undoing ? before : after;
			target.Settings = slots.Settings;
			target.LanguageSettings.Clear();
			foreach (var (code, path) in slots.Languages) {
				target.LanguageSettings[code] = path;
			}
		}
		return null;
	}
}

/// <summary>
///     A Language Manager commit: the table operations, made through here so
///     each keeps its inverse, undone in reverse order; every file's table is
///     put back whole. A recode onto a code the table holds merges two
///     languages and has no inverse: <see cref="Merged"/> says the commit made
///     one, and the commit is a boundary instead. It shows nowhere in the tree.
/// </summary>
public sealed class LanguagesEdit : UndoEntry {
	private readonly WordsSession session;
	private readonly List<(Action Undo, Action Redo)> steps = [];
	private readonly Dictionary<WordsFile, string[]> tablesBefore;
	private Dictionary<WordsFile, string[]> tablesAfter = [];

	public LanguagesEdit(WordsSession session) {
		this.session = session;
		tablesBefore = Tables();
	}

	private LanguageTable Table => session.Languages;
	/// <summary>The commit changed the table.</summary>
	public bool Changed => steps.Count > 0;
	/// <summary>A recode merged two languages' entries.</summary>
	public bool Merged { get; private set; }

	/// <inheritdoc cref="LanguageTable.Add"/>
	public bool Add(LanguageEntry language) {
		if (!Table.Add(language)) {
			return false;
		}
		steps.Add((() => Table.Remove(language.Code), () => Table.Add(language)));
		return true;
	}

	/// <inheritdoc cref="LanguageTable.Remove"/>
	public bool Remove(string code) {
		if (Table.Find(code) is not { } known) {
			return false;
		}
		int at = Table.Known.IndexOf(known);
		List<(string Label, WordsEntry Entry)> dropped = [.. session.Keys.Values
			.Where(key => key.Entries.ContainsKey(code))
			.Select(key => (key.BlockKey, key.Entries[code]))];
		if (!Table.Remove(code)) {
			return false;
		}
		steps.Add((() => {
			Table.Known.Insert(at, known);
			foreach (var (label, entry) in dropped) {
				if (session.Keys.TryGetValue(label, out WordsKey? key)) {
					key.Entries[code] = new WordsEntry(entry);
				}
			}
		}, () => Table.Remove(code)));
		return true;
	}

	/// <inheritdoc cref="LanguageTable.Rename"/>
	public LanguageEntry Rename(string code, LanguageEntry replacement) {
		LanguageEntry edited = Table.Find(code) ?? throw new ArgumentException($"no language '{code}'", nameof(code));
		Merged |= replacement.Code != code && Table.Find(replacement.Code) is not null;
		LanguageEntry standing = Table.Rename(code, replacement);
		//a recode onto a free code moves each entry whole, so recoding back is exact
		steps.Add((() => Table.Rename(replacement.Code, edited), () => Table.Rename(code, replacement)));
		return standing;
	}

	/// <inheritdoc cref="LanguageTable.Reorder"/>
	public void Reorder(int from, int to) {
		if (from == to) {
			return;
		}
		Table.Reorder(from, to);
		steps.Add((() => Table.Reorder(to, from), () => Table.Reorder(from, to)));
	}

	/// <summary>The commit is done: every file's table as it now stands is what a redo puts back.</summary>
	internal void Close() => tablesAfter = Tables();

	private Dictionary<WordsFile, string[]> Tables() => session.Files.ToDictionary(file => file, file => file.Languages.ToArray());

	public override NodeRef? Site(bool undoing) => null;

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		if (undoing) {
			for (int i = steps.Count - 1; i >= 0; i--) {
				steps[i].Undo();
			}
		}
		else {
			foreach (var (_, redo) in steps) {
				redo();
			}
		}
		foreach (var (file, codes) in undoing ? tablesBefore : tablesAfter) {
			file.Languages.Clear();
			file.Languages.AddRange(codes);
		}
		return null;
	}
}

/// <summary>Keys an entry keeps and puts back.</summary>
internal static class KeptKeys {
	/// <summary>The key at <paramref name="label"/> and every key below it.</summary>
	public static IEnumerable<WordsKey> Under(WordsSession session, string label)
		=> session.Keys.Values.Where(key => key.BlockKey == label || key.BlockKey.StartsWith(label + ".", StringComparison.Ordinal));

	/// <summary>A kept key back in the session as it was: the session makes the key, the copy fills it.</summary>
	public static void PutBack(WordsSession session, WordsKey kept) {
		WordsKey key = session.AddKey(kept.BlockKey);
		key.IsConstant = kept.IsConstant;
		key.DefaultValue = kept.DefaultValue;
		key.Context = kept.Context;
		key.Comment = kept.Comment;
		key.NeedsReview = kept.NeedsReview;
		key.Parameters.Clear();
		foreach (WordsParameter parameter in kept.Parameters) {
			key.Parameters.Add(new WordsParameter(parameter));
		}
		key.Entries.Clear();
		foreach (var (code, entry) in kept.Entries) {
			key.Entries[code] = new WordsEntry(entry);
		}
	}
}
