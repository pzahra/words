using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The tree pane: one row per node of every loaded file, the selection (and
///     what the panes show for it), the language the badges are computed for,
///     and the filters. It presents <see cref="WordsSession"/> and never writes
///     to it — edits made through the selection are announced by
///     <see cref="Edited"/> for the owner to mark the session dirty.
/// </summary>
public class TreeViewModel : ViewModelBase {
	private readonly WordsSession session;

	public KeyNodeCollection KeyNodes { get; } = new(null);
	public ObservableCollection<LanguageEntry> KnownLanguages => session.Languages.Known;
	/// <summary>
	///     The dropdown: what the selected key's file speaks — its language table,
	///     plus codes found on its fields (the ! placeholders) — and the language
	///     selected, so the choice always shows. The session union when nothing is
	///     selected. (SPEC: translation pane)
	/// </summary>
	public ObservableCollection<LanguageEntry> FileLanguages { get; } = [];

	/// <summary>Raised when an edit reached the document through the selection.</summary>
	public event Action? Edited;

	public TreeViewModel(WordsSession session) {
		this.session = session;
		SelectedLanguage = KnownLanguages[0];
	}

	//Selection
	public KeyNode? SelectedKeyNode {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				OnSelectedKeyNodeChanged();
			}
		}
	}

	public WordsKey? SelectedKey {
		get;
		private set {
			WordsKey? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedKeyValueChanged;
				value?.PropertyChanged += OnSelectedKeyValueChanged;
			}
		}
	}

	public WordsEntry? SelectedEntry {
		get;
		private set {
			WordsEntry? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedEntryChanged;
				value?.PropertyChanged += OnSelectedEntryChanged;
			}
		}
	}

	public OrganizerNode? SelectedOrganizer {
		get;
		private set {
			OrganizerNode? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedOrganizerChanged;
				value?.PropertyChanged += OnSelectedOrganizerChanged;
			}
		}
	}

	public LanguageEntry SelectedLanguage {
		get;
		set {
			//the ComboBox pushes null while its items turn over; the session always
			//has a language, so the selection never goes without one
			if (value is null) {
				return;
			}
			if (ChangeProperty(ref field, value)) {
				RefreshBadges();
				FollowSelectedKey();
				ApplyFilters();
			}
		}
	}

	/// <summary>The file the selected node belongs to, if any.</summary>
	public WordsFile? SelectedFile => SelectedKeyNode is null ? null : session.FileOf(SelectedKeyNode.Root.FullLabel);

	private void OnSelectedKeyNodeChanged() {
		SelectedOrganizer = SelectedKeyNode as OrganizerNode;
		FollowSelectedKey();
		RefreshFileLanguages();
	}

	private void RefreshFileLanguages() {
		IEnumerable<LanguageEntry> wanted = KnownLanguages;
		if (SelectedFile is { } file) {
			HashSet<string> codes = [.. file.Languages, SelectedLanguage.Code];
			foreach (WordsKey key in session.KeysOf(file)) {
				foreach (var (code, entry) in key.Entries) {
					if (entry.Value.Trim() != "") {
						codes.Add(code);
					}
				}
			}
			wanted = KnownLanguages.Where(language => codes.Contains(language.Code));
		}
		if (wanted.SequenceEqual(FileLanguages)) {
			return;
		}
		FileLanguages.Clear();
		foreach (LanguageEntry language in wanted) {
			FileLanguages.Add(language);
		}
		//the ComboBox dropped its selection while the items turned over: hand it back
		AffectProperty(nameof(SelectedLanguage));
	}

	/// <summary>Re-reads the selected node's key and entry from the document.</summary>
	public void FollowSelectedKey() {
		if (SelectedKeyNode is not null && session.Keys.TryGetValue(SelectedKeyNode.FullLabel, out var key)) {
			SelectedKey = key;
			SelectedEntry = key.IsConstant ? null : key.Entries[SelectedLanguage.Code];
		}
		else {
			SelectedKey = null;
			SelectedEntry = null;
		}
	}

	private void OnSelectedKeyValueChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return; //selection and model briefly disagree while the selection is changing
		}
		if (e.PropertyName == nameof(SelectedKey.Comment) && SelectedKey.Comment.Trim() != "") {
			SelectedKey.NeedsReview = true;
		}
		if (e.PropertyName is nameof(SelectedKey.DefaultValue) or nameof(SelectedKey.NeedsReview)) {
			RefreshBadges(SelectedKeyNode);
		}
		Edited?.Invoke();
	}

	private void OnSelectedOrganizerChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(OrganizerNode.Text)) {
			Edited?.Invoke();
		}
	}

	private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedEntry is null || SelectedKey is null || SelectedKeyNode is null) {
			return; //selection and model briefly disagree while the selection is changing
		}
		if (e.PropertyName == nameof(SelectedEntry.Comment) && SelectedEntry.Comment.Trim() != "") {
			SelectedKey.NeedsReview = true;
		}
		if (e.PropertyName is nameof(SelectedEntry.Value) or nameof(SelectedEntry.Stale)) {
			RefreshBadges(SelectedKeyNode);
		}
		Edited?.Invoke();
	}

	//Filters
	/// <summary>The translator's work queue: keys stale in the selected language.</summary>
	public bool IsStaleFilter { get; set => Filter(ref field, value); }
	/// <summary>The programmer's: keys a translator raised a hand on.</summary>
	public bool NeedsReviewFilter { get; set => Filter(ref field, value); }
	/// <summary>Keys wanting words in the default, or in the selected language where their file registers it.</summary>
	public bool MissingFilter { get; set => Filter(ref field, value); }
	public string SearchFilterText { get; set => Filter(ref field, value); } = "";

	//a filter that changed re-runs the pass
	private void Filter<T>(ref T field, T value, [CallerMemberName] string propertyName = "") {
		if (ChangeProperty(ref field, value, propertyName)) {
			ApplyFilters();
		}
	}
	/// <summary>True while any filter narrows the tree.</summary>
	public bool IsFiltering => IsStaleFilter || NeedsReviewFilter || MissingFilter || SearchFilterText != "";
	/// <summary>How many rows the filters hide.</summary>
	public int HiddenCount { get; private set => ChangeProperty(ref field, value); }

	public void ClearFilters() {
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		MissingFilter = false;
	}

	public IEnumerable<KeyNode> AllNodes => KeyNodes.SelectMany(root => root.SelfAndDescendants());

	public void ApplyFilters() {
		foreach (KeyNode node in AllNodes) {
			node.IsVisible = PassesVisibilityFilters(node);
		}
		foreach (KeyNode node in AllNodes) {
			if (!node.IsVisible) {
				node.IsVisible = EnsureVisibleDescendant(node);
			}
		}
		HiddenCount = AllNodes.Count(node => !node.IsVisible);
		AffectProperty(nameof(IsFiltering));
		//a selection the filter hid moves up to the nearest row still showing
		if (SelectedKeyNode is { IsVisible: false } hidden) {
			KeyNode? shown = hidden.Parent;
			while (shown is { IsVisible: false }) {
				shown = shown.Parent;
			}
			Select(shown);
		}
	}

	/// <summary>Selects <paramref name="node"/> (or nothing), the way a click would.</summary>
	public void Select(KeyNode? node) {
		if (SelectedKeyNode is { } previous && previous != node) {
			previous.IsSelected = false;
		}
		node?.IsSelected = true;
		SelectedKeyNode = node;
	}

	private bool PassesVisibilityFilters(KeyNode node) {
		bool passesFilter = true;

		if (IsStaleFilter) {
			passesFilter &= node.IsStale;
		}
		if (NeedsReviewFilter) {
			passesFilter &= node.NeedsReview;
		}
		if (MissingFilter) {
			passesFilter &= node.EmptyValue;
		}
		if (!string.IsNullOrEmpty(SearchFilterText)) {
			passesFilter &= Matches(node, SearchFilterText);
		}

		return passesFilter;
	}

	//what a translator searches for: a name, the words in the default and the
	//selected language, and the notes around them; for a comment row, its text
	private bool Matches(KeyNode node, string text) {
		const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
		if (node.FullLabel.Contains(text, ignoreCase)) {
			return true;
		}
		if (node is OrganizerNode organizer) {
			return organizer.Text.Contains(text, ignoreCase);
		}
		if (!session.Keys.TryGetValue(node.FullLabel, out var key)) {
			return false;
		}
		WordsEntry? entry = key.Entries.GetValueOrDefault(SelectedLanguage.Code);
		return key.DefaultValue.Contains(text, ignoreCase)
			|| key.Context.Contains(text, ignoreCase)
			|| key.Comment.Contains(text, ignoreCase)
			|| (entry is not null && (entry.Value.Contains(text, ignoreCase) || entry.Context.Contains(text, ignoreCase) || entry.Comment.Contains(text, ignoreCase)));
	}

	private static bool EnsureVisibleDescendant(KeyNode node) {
		if (node.IsVisible) return true;
		foreach (var child in node.Children) {
			if (EnsureVisibleDescendant(child)) {
				node.IsVisible = true;
				return true;
			}
		}
		return false;
	}

	//Badges: computed from the document, for the selected language, in one pass
	public void RefreshBadges() {
		foreach (KeyNode root in KeyNodes) {
			WordsFile? file = session.FileOf(root.FullLabel);
			foreach (KeyNode node in root.SelfAndDescendants()) {
				RefreshBadges(node, file);
			}
		}
		//every path that changes the language table passes here
		RefreshFileLanguages();
	}

	public void RefreshBadges(KeyNode node) => RefreshBadges(node, session.FileOf(node.Root.FullLabel));

	private void RefreshBadges(KeyNode node, WordsFile? file) {
		if (node is OrganizerNode) {
			return;
		}
		if (!session.Keys.TryGetValue(node.FullLabel, out var key)) {
			node.IsConstant = false;
			node.NeedsReview = false;
			node.IsStale = false;
			node.IsOverwritten = false;
			node.EmptyValue = false;
			return;
		}
		string code = SelectedLanguage.Code;
		node.IsConstant = key.IsConstant;
		node.NeedsReview = key.NeedsReview;
		node.IsStale = key.HasStaleValue(code);
		node.IsOverwritten = key.HasRegionalOverride(code);
		//a key wanting words in the default, or in the selected language where its
		//file registers that language (listed or !-hidden), reads emphasized; a file
		//that never declared the language has no gap to show (SPEC: Badges)
		bool registers = file?.Languages.Contains(code) == true;
		node.EmptyValue = !key.IsConstant
			&& (key.DefaultValue.Trim() == "" || (registers && (key.Entries.GetValueOrDefault(code)?.Value.Trim() ?? "") == ""));
	}

	//only a leaf directly under a file may become a constant (SPEC: baseline pane)
	public static void UpdateCanBeConstant(KeyNode fileNode) {
		foreach (KeyNode node in fileNode.Descendants()) {
			node.CanBeConstant = node is not OrganizerNode && node.Parent == fileNode && node.Children.Count == 0;
		}
	}

	//Files
	/// <summary>Puts a loaded file in the tree; a reload takes the old node's place.</summary>
	public void Present(WordsFile file) {
		KeyNode node = KeyNode.From(KeyTree.Build(session, file));
		node.IsLibraryFile = file.IsLibrary;
		node.GripeCount = file.Errors.Count;
		if (file.Preamble != "") {
			//the preamble shows as an organizer pinned to the file's start; its text
			//is the file's own, which Save writes above the language table
			node.Children.Insert(0, new OrganizerNode($"{file.Label}.;preamble",
				() => file.Preamble,
				text => file.Preamble = text));
		}
		int existing = KeyNodes.FindIndex(root => root.FullLabel == file.Label);
		if (existing >= 0) {
			KeyNodes[existing] = node;
		}
		else {
			KeyNodes.Add(node);
		}
		if (SelectedKeyNode is not null && !KeyNodes.Contains(SelectedKeyNode.Root)) {
			SelectedKeyNode = null;
		}
		UpdateCanBeConstant(node);
		FollowLanguage();
		RefreshBadges();
		ApplyFilters();
	}

	/// <summary>Takes a file's node out of the tree, after the session let the file go.</summary>
	public void RemoveFile(KeyNode fileNode) {
		KeyNodes.Remove(fileNode);
		SelectedKeyNode = KeyNodes.FirstOrDefault();
		FollowLanguage();
		RefreshBadges();
	}

	/// <summary>The empty tree over the reset session.</summary>
	public void Clear() {
		KeyNodes.Clear();
		SelectedKeyNode = null;
		SelectedLanguage = KnownLanguages[0];
		RefreshFileLanguages();
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		MissingFilter = false;
	}

	//the dropdown's entry may have been replaced or pruned: follow the code, never go without
	public void FollowLanguage()
		=> SelectedLanguage = KnownLanguages.FirstOrDefault(language => language.Code == SelectedLanguage.Code) ?? KnownLanguages[0];

	/// <summary>The file a tree node belongs to.</summary>
	public WordsFile FileOf(KeyNode node)
		=> session.FileOf(node.Root.FullLabel)
			?? throw new InvalidOperationException($"no loaded file for node {node.FullLabel}");

	/// <summary>The file node for a loaded file.</summary>
	public KeyNode NodeOf(WordsFile file)
		=> KeyNodes.FirstOrDefault(root => root.FullLabel == file.Label)
			?? throw new InvalidOperationException($"no tree for loaded file {file.Label}");

	//file nodes in tree order; later files win bare-reference lookups, like a
	//host app stacking dictionaries
	public IEnumerable<string> FileLabels => KeyNodes.Select(node => node.FullLabel);

	//Structure
	/// <summary>Adds an empty node under <paramref name="parent"/> and selects it.</summary>
	public KeyNode Add(KeyNode parent, string label) {
		KeyNode node = new(label, $"{parent.FullLabel}.{label}") {
			IsSelected = true,
		};
		parent.IsExpanded = true;
		parent.IsSelected = false;
		parent.Children.Add(node);
		UpdateCanBeConstant(parent.Root);
		SelectedKeyNode = node;
		return node;
	}

	/// <summary>
	///     Takes a node out of the tree and selects its parent. A removed key leaves
	///     any comment above it standing; on the next load the comment anchors to
	///     whatever block follows it.
	/// </summary>
	public void Remove(KeyNode node) {
		KeyNode? parent = node.Parent;
		KeyNode root = node.Root;
		parent?.Children.Remove(node);
		UpdateCanBeConstant(root);
		SelectedKeyNode = parent;
	}

	/// <summary>
	///     Selects the comment standing ahead of <paramref name="node"/>, inserting a
	///     blank one if there is none. Returns true when one was inserted.
	/// </summary>
	public bool CommentAhead(KeyNode node) {
		if (node.Parent is not { } parent) {
			return false;
		}
		int index = parent.Children.IndexOf(node);
		bool inserted = false;
		KeyNode select;
		if (index > 0 && parent.Children[index - 1] is OrganizerNode existing) {
			select = existing;
		}
		else {
			select = new CommentNode($"{node.FullLabel}.;comment");
			parent.Children.Insert(index, select);
			inserted = true;
		}
		node.IsSelected = false;
		select.IsSelected = true;
		SelectedKeyNode = select;
		return inserted;
	}
}
