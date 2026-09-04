using PatTech.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

/// <summary>
///     The main window's intent: which node, key, entry and language are
///     selected, which filters are on, and what each button does. The document
///     is <see cref="Session"/>; the tree (<see cref="KeyNodes"/>) presents it
///     and decides write order. Processing lives in the session and
///     <see cref="WordsOperations"/>; this class only asks.
/// </summary>
public class MainWindowViewModel : ViewModelSaveBase {
	public WordsSession Session { get; } = new();
	public KeyDragDropHandler KeyDragDropHandler { get; }
	public KeyNodeCollection KeyNodes { get; } = new(null);
	public ObservableCollection<LanguageEntry> KnownLanguages => Session.Languages.Known;

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
		set {
			WordsKey? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedKeyValueChanged;
				value?.PropertyChanged += OnSelectedKeyValueChanged;
			}
		}
	}

	public WordsEntry? SelectedEntry {
		get;
		set {
			WordsEntry? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedEntryChanged;
				value?.PropertyChanged += OnSelectedEntryChanged;
			}
		}
	}

	public OrganizerNode? SelectedOrganizer {
		get;
		set {
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
				OnSelectedLanguageChanged();
				ApplyFilters();
			}
		}
	}

	//Filters
	public bool IsStaleFilter { get; set => _ = ChangeProperty(ref field, value) && ApplyFilters(); }
	public bool NeedsReviewFilter { get; set => _ = ChangeProperty(ref field, value) && ApplyFilters(); }
	public string SearchFilterText { get; set => _ = ChangeProperty(ref field, value) && ApplyFilters(); } = "";

	//Previews
	public bool ShowDefaultPreview { get; set => ChangeProperty(ref field, value); }
	public bool ShowLocalizationPreview { get; set => ChangeProperty(ref field, value); }

	//Commands
	public ICommand LoadFileCommand { get; }
	public ICommand ResetCommand { get; }
	public ICommand SaveCommand { get; }
	public ICommand MergeFilesCommand { get; }
	public ICommand ManageLanguagesCommand { get; }
	public ICommand ManageImageSchemesCommand { get; }
	public ICommand TestParametersCommand { get; }
	public ICommand RemoveLocalizationKeyAndNodeCommand { get; }
	public ICommand RenameLocalizationKeyAndNodeCommand { get; }
	public ICommand AddLocalizationKeyNodeCommand { get; }
	public ICommand AddLocalizationKeyCommand { get; }
	public ICommand AddOrganizerCommand { get; }
	public ICommand RemoveLocalizationKeyCommand { get; }
	public ICommand StaleAllLanguagesCommand { get; }
	public ICommand ToggleStaleLanguageCommand { get; }
	public ICommand ToggleLocalizationKeyNeedsReviewCommand { get; }
	public ICommand ToggleLocalizationKeyIsConstantCommand { get; }

	//how the editor asks and tells: modal windows in the app, a fake in tests
	public IDialogs Dialogs { get; }

	public MainWindowViewModel(IDialogs? dialogs = null) {
		Dialogs = dialogs ?? new WpfDialogs();
		LoadFileCommand = new DelegateCommand(DoLoadFiles);
		ResetCommand = new DelegateCommand(DoReset);
		SaveCommand = new DelegateCommand(DoSave);
		MergeFilesCommand = new DelegateCommand(DoMergeFiles);
		ManageLanguagesCommand = new DelegateCommand(DoManageLanguages);
		ManageImageSchemesCommand = new DelegateCommand(DoManageImageSchemes, CanManageImageSchemes);
		RemoveLocalizationKeyAndNodeCommand = new DelegateCommand(DoRemoveLocalizationKeyAndNode);
		RenameLocalizationKeyAndNodeCommand = new DelegateCommand(DoRenameNode);
		AddLocalizationKeyNodeCommand = new DelegateCommand(DoAddLocalizationKeyNode);
		AddLocalizationKeyCommand = new DelegateCommand(DoAddLocalizationKey);
		AddOrganizerCommand = new DelegateCommand(DoAddOrganizer);
		RemoveLocalizationKeyCommand = new DelegateCommand(DoRemoveLocalizationKey);
		StaleAllLanguagesCommand = new DelegateCommand(DoStaleAllLanguages);
		ToggleStaleLanguageCommand = new DelegateCommand<string>(DoToggleStaleLanguage);
		ToggleLocalizationKeyNeedsReviewCommand = new DelegateCommand(DoToggleKeyNeedsReview);
		ToggleLocalizationKeyIsConstantCommand = new DelegateCommand(DoToggleLocalizationKeyIsConstant);
		TestParametersCommand = new DelegateCommand<ObservableCollection<WordsParameter>>(DoTestParameters);

		Title = "Wordsmith Editor";
		SelectedLanguage = KnownLanguages[0];
		KeyDragDropHandler = new KeyDragDropHandler() { MainWindow = this };
	}

	//Selection
	private void OnSelectedLanguageChanged() {
		RefreshBadges();
		if (SelectedKeyNode is null || SelectedKey is null || SelectedKey.IsConstant) {
			SelectedEntry = null;
			return;
		}
		SelectedEntry = SelectedKey.Entries[SelectedLanguage.Code];
		ShowLocalizationPreview = false;
	}

	private void OnSelectedKeyNodeChanged() {
		SelectedOrganizer = SelectedKeyNode as OrganizerNode;
		ShowDefaultPreview = false;
		ShowLocalizationPreview = false;
		if (SelectedKeyNode is not null && Session.Keys.TryGetValue(SelectedKeyNode.FullLabel, out var key)) {
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
		IsDirty = true;
		if (e.PropertyName == nameof(SelectedKey.Comment) && SelectedKey.Comment.Trim() != "") {
			SelectedKey.NeedsReview = true;
		}
		if (e.PropertyName is nameof(SelectedKey.DefaultValue) or nameof(SelectedKey.NeedsReview)) {
			UpdateBadges(SelectedKeyNode);
		}
	}

	private void OnSelectedOrganizerChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(OrganizerNode.Text)) {
			IsDirty = true;
		}
	}

	private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedEntry is null || SelectedKey is null || SelectedKeyNode is null) {
			return; //selection and model briefly disagree while the selection is changing
		}
		IsDirty = true;
		if (e.PropertyName == nameof(SelectedEntry.Comment) && SelectedEntry.Comment.Trim() != "") {
			SelectedKey.NeedsReview = true;
		}
		if (e.PropertyName is nameof(SelectedEntry.Value) or nameof(SelectedEntry.Stale)) {
			UpdateBadges(SelectedKeyNode);
		}
	}

	//Visibility
	private IEnumerable<KeyNode> AllNodes => KeyNodes.SelectMany(root => root.SelfAndDescendants());

	public bool ApplyFilters() {
		foreach (KeyNode node in AllNodes) {
			node.IsVisible = PassesVisibilityFilters(node);
		}
		foreach (KeyNode node in AllNodes) {
			if (!node.IsVisible) {
				node.IsVisible = EnsureVisibleDescendant(node);
			}
		}
		return true;
	}

	private bool PassesVisibilityFilters(KeyNode node) {
		bool passesFilter = true;

		if (IsStaleFilter) {
			passesFilter &= node.IsStale || node.EmptyValue;
		}
		if (NeedsReviewFilter) {
			passesFilter &= node.NeedsReview;
		}
		if (!string.IsNullOrEmpty(SearchFilterText)) {
			passesFilter &= node.FullLabel.Contains(SearchFilterText, StringComparison.OrdinalIgnoreCase)
				|| (node is OrganizerNode organizer && organizer.Text.Contains(SearchFilterText, StringComparison.OrdinalIgnoreCase));
		}

		return passesFilter;
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
	internal void RefreshBadges() {
		foreach (KeyNode node in AllNodes) {
			UpdateBadges(node);
		}
	}

	private void UpdateBadges(KeyNode node) {
		if (node is OrganizerNode) {
			return;
		}
		if (!Session.Keys.TryGetValue(node.FullLabel, out var key)) {
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
		//a key wanting words in either the default or the selected language reads emphasized
		node.EmptyValue = !key.IsConstant
			&& (key.DefaultValue.Trim() == "" || (key.Entries.GetValueOrDefault(code)?.Value.Trim() ?? "") == "");
	}

	//only a leaf directly under a file may become a constant (SPEC: baseline pane)
	internal static void UpdateCanBeConstant(KeyNode fileNode) {
		foreach (KeyNode node in fileNode.Descendants()) {
			node.CanBeConstant = node is not OrganizerNode && node.Parent == fileNode && node.Children.Count == 0;
		}
	}

	//Load
	private void DoLoadFiles() {
		if (!Dialogs.TryOpenFiles("Load", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string[]? fileNames)) {
			return;
		}
		foreach (string fileName in fileNames) {
			LoadFile(fileName);
		}
	}

	public void LoadFile(string fileName) {
		try {
			using var reader = File.OpenText(fileName);
			LoadFile(reader, fileName);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Dialogs.Tell($"Could not load {fileName}:\n{ex.Message}");
		}
	}

	public void LoadFile(TextReader reader, string fileName) => Present(Session.Load(reader, fileName));

	/// <summary>Puts a loaded file in the tree; a reload takes the old node's place.</summary>
	public void Present(WordsFile file) {
		KeyNode node = KeyNode.From(KeyTree.Build(Session, file));
		node.IsLibraryFile = file.IsLibrary;
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

	//the dropdown's entry may have been replaced or pruned: follow the code, never go without
	private void FollowLanguage()
		=> SelectedLanguage = KnownLanguages.FirstOrDefault(language => language.Code == SelectedLanguage.Code) ?? KnownLanguages[0];

	/// <summary>The file a tree node belongs to.</summary>
	public WordsFile FileOf(KeyNode node)
		=> Session.FileOf(node.Root.FullLabel)
			?? throw new InvalidOperationException($"no loaded file for node {node.FullLabel}");

	//Reset
	private void DoReset() {
		if (Dialogs.Confirm("Reset the session? All unsaved changes will be lost.")) {
			ResetCore();
		}
	}

	public void ResetCore() {
		Session.Reset();
		KeyNodes.Clear();
		SelectedKeyNode = null;
		SelectedLanguage = KnownLanguages[0];
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		IsDirty = false;
	}

	//Save
	private void DoSave() => Save();

	public override void Save() {
		bool allSaved = true;
		foreach (WordsFile file in Session.Files) {
			KeyNode node = KeyNodes.FirstOrDefault(root => root.FullLabel == file.Label)
				?? throw new InvalidOperationException($"no tree for loaded file {file.Label}");
			try {
				Session.Save(file, node);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Dialogs.Tell($"Could not save {file.Path}:\n{ex.Message}");
				allSaved = false;
			}
		}
		if (allSaved) {
			IsDirty = false;
		}
	}

	//Merge
	private void DoMergeFiles() {
		Dialogs.Show(new MergeControlViewModel(this));
	}

	//Languages
	private void DoManageLanguages() {
		Dialogs.Show(new LanguageManagerViewModel(this));
	}

	//image-scheme mappings are per-file; the dialog edits the file the selection
	//sits in, so a node must be selected to know which file that is
	private bool CanManageImageSchemes() => SelectedKeyNode is not null;
	private void DoManageImageSchemes() {
		if (SelectedKeyNode is null) {
			return;
		}
		Dialogs.Show(new ImageSchemesViewModel(this, FileOf(SelectedKeyNode)));
	}

	/// <summary>
	///     Scheme → absolute folder for the file that owns <paramref name="node"/>:
	///     what the preview's resolver registry is built from.
	/// </summary>
	public IReadOnlyDictionary<string, string> ImageSchemeFoldersFor(KeyNode? node)
		=> node is null ? new Dictionary<string, string>() : FileOf(node).ImageSchemeFolders();

	//Structure edits
	private void DoRemoveLocalizationKeyAndNode() {
		if (SelectedKeyNode is null) {
			return;
		}
		if (SelectedKeyNode is OrganizerNode organizer) {
			//deleting the organizer deletes the comment it presents
			organizer.Text = "";
			RemoveKeyNode(organizer);
			return;
		}
		if (SelectedKeyNode.IsFile) {
			if (Dialogs.Confirm("Remove the selected file? All unsaved changes will be lost.")) {
				RemoveFileNodeCore(SelectedKeyNode);
			}
			return;
		}
		Session.RemoveKeysUnder(SelectedKeyNode.FullLabel);
		RemoveKeyNode(SelectedKeyNode);
	}

	public void RemoveFileNodeCore(KeyNode fileNode) {
		if (Session.FileOf(fileNode.FullLabel) is { } file) {
			Session.Unload(file);
		}
		KeyNodes.Remove(fileNode);
		SelectedKeyNode = KeyNodes.FirstOrDefault();
		FollowLanguage();
		RefreshBadges();
	}

	private void RemoveKeyNode(KeyNode node) {
		//a removed key leaves any comment above it standing; on the next load
		//the comment anchors to whatever block follows it
		KeyNode? parent = node.Parent;
		KeyNode root = node.Root;
		parent?.Children.Remove(node);
		UpdateCanBeConstant(root);
		SelectedKeyNode = parent;
		IsDirty = true;
	}

	private void DoRenameNode() {
		if (SelectedKeyNode is null or OrganizerNode || SelectedKeyNode.IsFile) {
			return;
		}
		Dialogs.Show(new KeyNameViewModel(this, SelectedKeyNode));
	}

	public void RenameLocalizationKeyAndNode(string newName) {
		if (SelectedKeyNode is null or OrganizerNode || SelectedKeyNode.Parent is not { } parent) {
			return; //files keep the name of the file
		}
		KeyNode node = SelectedKeyNode;
		if (parent.Children.Any(sibling => sibling != node && sibling.Label == newName)) {
			Dialogs.Tell($"'{parent.FullLabel}' already has a node named '{newName}'.");
			return;
		}
		//the marker is part of the key, not the name
		string marker = WordsOperations.LastSegment(node.FullLabel).StartsWith('$') ? "$" : "";
		string newFullLabel = $"{parent.FullLabel}.{marker}{newName}";
		if (!Session.TryRename(node.FullLabel, newFullLabel, out var collisions)) {
			Dialogs.Tell($"Cannot rename: {string.Join(", ", collisions)} already exist.");
			return;
		}
		node.Label = newName;
		node.Relabel(newFullLabel);
		IsDirty = true;
	}

	private void DoAddLocalizationKeyNode() {
		if (SelectedKeyNode is null or OrganizerNode) {
			return;
		}
		Dialogs.Show(new KeyNameViewModel(this, null));
	}

	public void AddLocalizationKeyNode(string newName) {
		if (SelectedKeyNode is null or OrganizerNode) {
			return;
		}
		KeyNode parent = SelectedKeyNode;
		if (parent.Children.Any(child => child.Label == newName)) {
			Dialogs.Tell($"'{parent.FullLabel}' already has a node named '{newName}'.");
			return;
		}
		KeyNode node = new(newName, $"{parent.FullLabel}.{newName}") {
			IsSelected = true,
		};
		parent.IsExpanded = true;
		parent.IsSelected = false;
		parent.Children.Add(node);
		UpdateCanBeConstant(parent.Root);
		SelectedKeyNode = node;
		IsDirty = true;
	}

	private void DoAddLocalizationKey() {
		//SPEC (The tree): a key can exist on any node except a file
		if (SelectedKeyNode is null or OrganizerNode || SelectedKeyNode.IsFile) {
			return;
		}
		WordsKey key = Session.AddKey(SelectedKeyNode.FullLabel);
		SelectedKey = key;
		SelectedEntry = key.IsConstant ? null : key.Entries[SelectedLanguage.Code];
		UpdateBadges(SelectedKeyNode);
		IsDirty = true;
	}

	private void DoAddOrganizer() {
		if (SelectedKeyNode is null or OrganizerNode || SelectedKeyNode.IsFile || SelectedKeyNode.Parent is not { } parent) {
			return;
		}
		int index = parent.Children.IndexOf(SelectedKeyNode);
		KeyNode select;
		if (index > 0 && parent.Children[index - 1] is OrganizerNode existing) {
			select = existing;
		}
		else {
			var organizer = new CommentNode($"{SelectedKeyNode.FullLabel}.;comment");
			parent.Children.Insert(index, organizer);
			select = organizer;
			IsDirty = true;
		}
		SelectedKeyNode.IsSelected = false;
		select.IsSelected = true;
		SelectedKeyNode = select;
	}

	private void DoRemoveLocalizationKey() {
		if (SelectedKeyNode is null) {
			return;
		}
		Session.RemoveKey(SelectedKeyNode.FullLabel);
		SelectedKey = null;
		SelectedEntry = null;
		UpdateBadges(SelectedKeyNode);
		IsDirty = true;
	}

	//Flags
	private void DoStaleAllLanguages() {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return;
		}
		string stamp = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
		foreach (WordsEntry entry in SelectedKey.Entries.Values) {
			entry.Stale = stamp;
		}
		UpdateBadges(SelectedKeyNode);
		IsDirty = true;
	}

	private void DoToggleStaleLanguage(string? languageCode) {
		if (languageCode is null || SelectedKey is null || SelectedKeyNode is null
				|| !SelectedKey.Entries.TryGetValue(languageCode, out var entry)) {
			return;
		}
		entry.Stale = entry.Stale is null ? DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture) : null;
		UpdateBadges(SelectedKeyNode);
		IsDirty = true;
	}

	private void DoToggleKeyNeedsReview() {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return;
		}
		SelectedKey.NeedsReview = !SelectedKey.NeedsReview;
		UpdateBadges(SelectedKeyNode);
		IsDirty = true;
	}

	private void DoToggleLocalizationKeyIsConstant() {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return;
		}
		bool makeConstant = !SelectedKey.IsConstant;
		bool clearEntries = false;
		if (makeConstant && SelectedKey.Entries.Values.Any(entry => !entry.IsEmpty())) {
			//a constant reads the same in every language: its translations go, and
			//that is the user's call to make
			if (!Dialogs.Confirm("Make this key a constant? Its translations will be removed.")) {
				return;
			}
			clearEntries = true;
		}
		string? newKey = Session.SetConstant(SelectedKey.BlockKey, makeConstant, clearEntries);
		if (newKey is null) {
			Dialogs.Tell($"A key named {WordsOperations.SetConstantMarker(SelectedKey.BlockKey, makeConstant)} already exists.");
			return;
		}
		SelectedKeyNode.Relabel(newKey);
		UpdateBadges(SelectedKeyNode);
		SelectedEntry = makeConstant ? null : SelectedKey.Entries[SelectedLanguage.Code];
		IsDirty = true;
	}

	private void DoTestParameters(ObservableCollection<WordsParameter> parameters) {
		Dialogs.Show(new TestParametersViewModel(this, parameters));
	}

	//Previews
	//file nodes in tree order; later files win bare-reference lookups, like a
	//host app stacking dictionaries
	private IEnumerable<string> FileLabels => KeyNodes.Select(node => node.FullLabel);

	public IWordsProvider GetWordsProvider() => Session.Provider(FileLabels);

	public IWordsProvider GetWordsProvider(string languageCode) => Session.Provider(FileLabels, languageCode);
}
