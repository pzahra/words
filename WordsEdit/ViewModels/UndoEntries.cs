using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

//The kinds of entry (SPEC: Undo → One entry per action). Each holds what its action
//changed and nothing more, and finds what it changes by label: entries are undone in
//order, so every label means what it meant when the entry was made.

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
	/// <summary>The plural form typed into (SPEC: Plural forms); null for the plain value and every other field.</summary>
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
			case DocumentField.DefaultValue when Form is not null: FormPane.Write(key.Forms, Form, text); break;
			case DocumentField.EntryValue when entry is not null && Form is not null: FormPane.Write(entry.Forms, Form, text); break;
			case DocumentField.DefaultValue: key.DefaultValue = text; break;
			case DocumentField.KeyContext: key.Context = text; break;
			case DocumentField.KeyComment: key.Comment = text; break;
			case DocumentField.EntryValue when entry is not null: entry.Value = text; break;
			case DocumentField.EntryContext when entry is not null: entry.Context = text; break;
			case DocumentField.EntryComment when entry is not null: entry.Comment = text; break;
		}
		//the text first: the hand and the stamps go back to where they stood with it
		if (RaisedReview) {
			key.NeedsReview = standing >= raisedAt;
		}
		foreach (string code in Staled) {
			if (key.Entries.TryGetValue(code, out WordsEntry? staled)) {
				staled.Stale = standing >= staledAt ? stamp : null;
			}
		}
		return Node;
	}

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
///     A Language Manager commit: the table operations on one file, made through
///     here so the commit is put back whole either way: every file's table, with
///     the language its default is written in, the session's language list, and
///     every key's entries in each code the commit touched, as they stood before
///     or after it. A recode onto a code the file declares merges two languages:
///     <see cref="Merged"/> says the commit made one, and the commit is a
///     boundary instead. It shows nowhere in the tree.
/// </summary>
public sealed class LanguagesEdit : UndoEntry {
	private readonly WordsSession session;
	private readonly Dictionary<WordsFile, FileTable> tablesBefore;
	private Dictionary<WordsFile, FileTable> tablesAfter = [];
	private readonly KnownList knownBefore;
	private KnownList? knownAfter;
	//every key's languages in its order, which its fields are written in, and each
	//touched code's entries, block key to entry, as they stood before the commit
	//first touched it, and after the commit
	private readonly Dictionary<string, string[]> ordersBefore;
	private Dictionary<string, string[]> ordersAfter = [];
	private readonly Dictionary<string, Dictionary<string, WordsEntry>> entriesBefore = [];
	private Dictionary<string, Dictionary<string, WordsEntry>> entriesAfter = [];

	//the session's list as it stands: its entries in order, each with its names
	private sealed record KnownList((LanguageEntry Entry, string NativeName, string EnglishName)[] Languages) {
		public static KnownList Of(LanguageTable table) => new([.. table.Known.Select(known => (known, known.NativeName, known.EnglishName))]);

		public void PutBack(LanguageTable table) {
			if (!table.Known.SequenceEqual(Languages.Select(language => language.Entry))) {
				table.Known.Clear();
				foreach (var (entry, _, _) in Languages) {
					table.Known.Add(entry);
				}
			}
			foreach (var (entry, nativeName, englishName) in Languages) {
				entry.NativeName = nativeName;
				entry.EnglishName = englishName;
			}
		}
	}

	//what a file's table is: its codes, its labels, its settings references and its
	//default's language, copied so later edits leave the copy alone
	private sealed record FileTable(string[] Codes, (string Code, LanguageEntry Label)[] Labels, (string Code, string Path)[] Settings, string? Default) {
		public static FileTable Of(WordsFile file) => new(
			[.. file.Languages],
			[.. file.Labels.Select(pair => (pair.Key, new LanguageEntry(pair.Value)))],
			[.. file.LanguageSettings.Select(pair => (pair.Key, pair.Value))],
			file.DefaultLanguage);

		public void PutBack(WordsFile file) {
			file.Languages.Clear();
			file.Languages.AddRange(Codes);
			file.Labels.Clear();
			foreach (var (code, label) in Labels) {
				file.Labels[code] = new LanguageEntry(label);
			}
			file.LanguageSettings.Clear();
			foreach (var (code, path) in Settings) {
				file.LanguageSettings[code] = path;
			}
			file.DefaultLanguage = Default;
		}
	}

	public LanguagesEdit(WordsSession session) {
		this.session = session;
		tablesBefore = Tables();
		knownBefore = KnownList.Of(Table);
		ordersBefore = Orders();
	}

	private LanguageTable Table => session.Languages;
	/// <summary>The commit changed the table.</summary>
	public bool Changed { get; private set; }
	/// <summary>A recode merged two languages' entries.</summary>
	public bool Merged { get; private set; }

	/// <inheritdoc cref="LanguageTable.Add"/>
	public bool Add(WordsFile file, LanguageEntry language) {
		Keep(language.Code);
		return Note(Table.Add(file, language));
	}

	/// <inheritdoc cref="LanguageTable.Remove"/>
	public bool Remove(WordsFile file, string code) {
		Keep(code);
		return Note(Table.Remove(file, code));
	}

	/// <inheritdoc cref="LanguageTable.Rename"/>
	public LanguageEntry Rename(WordsFile file, string code, LanguageEntry replacement) {
		Keep(code);
		Keep(replacement.Code);
		Merged |= replacement.Code != code && file.Languages.Contains(replacement.Code);
		Note(true);
		return Table.Rename(file, code, replacement);
	}

	/// <inheritdoc cref="LanguageTable.Reorder"/>
	public void Reorder(WordsFile file, int from, int to) {
		if (from != to) {
			Table.Reorder(file, from, to);
			Note(true);
		}
	}

	/// <summary>Declares the language <paramref name="file"/>'s default is written in (SPEC: Languages); nothing happens when the file says so already.</summary>
	public void Declare(WordsFile file, string? code) {
		if (file.DefaultLanguage != code) {
			file.DefaultLanguage = code;
			Note(true);
		}
	}

	private bool Note(bool changed) => Changed |= changed;

	//a code's entries as they stand, the first time the commit touches it
	private void Keep(string code) => entriesBefore.TryAdd(code, Entries(code));

	private Dictionary<string, WordsEntry> Entries(string code) => session.Keys.Values
		.Where(key => key.Entries.ContainsKey(code))
		.ToDictionary(key => key.BlockKey, key => new WordsEntry(key.Entries[code]));

	private Dictionary<string, string[]> Orders() => session.Keys.Values.ToDictionary(key => key.BlockKey, key => key.Entries.Keys.ToArray());

	/// <summary>The commit is done: every file's table, the session's list and the touched entries as they now stand are what a redo puts back.</summary>
	internal void Close() {
		tablesAfter = Tables();
		knownAfter = KnownList.Of(Table);
		ordersAfter = Orders();
		entriesAfter = entriesBefore.Keys.ToDictionary(code => code, Entries);
	}

	private Dictionary<WordsFile, FileTable> Tables() => session.Files.ToDictionary(file => file, FileTable.Of);

	public override NodeRef? Site(bool undoing) => null;

	public override NodeRef? Apply(WordsSession session, TreeViewModel tree, bool undoing) {
		foreach (var (file, table) in undoing ? tablesBefore : tablesAfter) {
			table.PutBack(file);
		}
		(undoing ? knownBefore : knownAfter!).PutBack(Table);
		//each key's entries rebuilt in its order then: a touched code's from the copy,
		//any other the one it holds, which the commit left alone
		var (orders, kept) = undoing ? (ordersBefore, entriesBefore) : (ordersAfter, entriesAfter);
		foreach (WordsKey key in session.Keys.Values) {
			if (!orders.TryGetValue(key.BlockKey, out string[]? codes)) {
				continue;
			}
			List<(string Code, WordsEntry Entry)> entries = [.. codes.Select(code =>
				(code, kept.TryGetValue(code, out var copies) && copies.TryGetValue(key.BlockKey, out WordsEntry? copy) ? new WordsEntry(copy) : key.Entries[code]))];
			key.Entries.Clear();
			foreach (var (code, entry) in entries) {
				key.Entries[code] = entry;
			}
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
		key.Forms.Clear();
		foreach (var (form, text) in kept.Forms) {
			key.Forms[form] = text;
		}
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
