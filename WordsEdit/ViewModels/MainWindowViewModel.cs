using PatTech.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;
//TODO: Rename MainWindow, if to LanguageManager then rename other LanguageManager
/*
MainWindowViewModel
Sections
	State
	Constructor
	UI Logic
	Command Logic
	Miscellaneous Logic
**/
public class MainWindowViewModel : ViewModelSaveBase {

	/*
	State
	Subsections: 
		Collections
		Selection UI
		Filters
		Previews
		Commands
	**/

	//Collections
	public KeyDragDropHandler KeyDragDropHandler { get; set; }

	public ObservableCollection<KeyNode> KeyNodes { get; } = [];

	private readonly HashSet<KeyNode> AllKeyNodes = [];

	public ObservableCollection<WordsKey> Keys { get; } = [];
	internal readonly Dictionary<string, WordsKey> allKeys = [];

	//file preamble comment runs, keyed by the file node's label. The preamble is
	//the only comment written outside the tree walk (it precedes the language
	//table); every other comment is a standalone CommentNode in the tree
	internal readonly Dictionary<string, string> filePreambles = [];

	//each file's own declared language codes in declaration order — files never
	//gain each other's languages on save; KnownLanguages is the session union
	internal readonly Dictionary<string, List<string>> fileLanguages = [];

	public ObservableCollection<LanguageEntry> KnownLanguages { get; set => ChangeProperty(ref field, value); } = [];

	public HashSet<string> FileNames { get; set => ChangeProperty(ref field, value); } = [];


	//Selection UI
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
			if (ChangeProperty(ref field, value)) {
				OnSelectedLanguageChanged();
				ApplyFilters();
			}
		}
	}

	private bool usingDefaultLanguage = true;

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

	/*
	Constructor
	**/
	public MainWindowViewModel() {
		LoadFileCommand = new DelegateCommand(DoLoadFiles);
		ResetCommand = new DelegateCommand(DoReset);
		SaveCommand = new DelegateCommand(DoSave);
		MergeFilesCommand = new DelegateCommand(DoMergeFiles);
		ManageLanguagesCommand = new DelegateCommand(DoManageLanguages);
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
		LanguageEntry defaultLanguage = new("en", "!English (common)");
		KnownLanguages.Add(defaultLanguage);
		SelectedLanguage = defaultLanguage;
		KeyDragDropHandler = new KeyDragDropHandler() { MainWindow = this };
	}

	/*
	UI Logic
	Subsections: 
		Selection
		Visibility
		Markers
	**/

	//Selection
	public void OnSelectedLanguageChanged() {
		foreach (KeyNode keyNode in AllKeyNodes) {
			if (allKeys.TryGetValue(keyNode.FullLabel, out var key) && !keyNode.IsConstant) {
				if (key.DefaultValue.Trim() == "" || key.Entries[SelectedLanguage.Code].Value.Trim() == "") {
					keyNode.EmptyValue = true;
				}
				else {
					keyNode.EmptyValue = false;
				}
			}
		}
		if (SelectedKeyNode is null || SelectedKey is null || SelectedKeyNode.IsConstant) {
			SelectedEntry = null;
			foreach (KeyNode node in KeyNodes) {
				MarkStaleNodes(node);
				MarkOverwrittenNodes(node);
			}
			return;
		}
		SelectedEntry = SelectedKey.Entries[SelectedLanguage.Code];
		ShowLocalizationPreview = false;
		foreach (KeyNode node in KeyNodes) {
			MarkStaleNodes(node);
			MarkOverwrittenNodes(node);
		}
	}

	private void OnSelectedKeyNodeChanged() {
		SelectedOrganizer = SelectedKeyNode as OrganizerNode;
		if (SelectedKeyNode is null) {
			SelectedKey = null;
			SelectedEntry = null;
			return;
		}
		string fullLabel = SelectedKeyNode.FullLabel;
		ShowDefaultPreview = false;
		ShowLocalizationPreview = false;
		if (allKeys.TryGetValue(fullLabel, out var key)) {
			SelectedKey = key;
			if (SelectedKey.IsConstant) {
				SelectedEntry = null;
			}
			else {
				SelectedEntry = SelectedKey.Entries[SelectedLanguage.Code];
			}
		}
		else {
			SelectedKey = null;
			SelectedEntry = null;
		}
	}

	private void OnSelectedKeyValueChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedKey is null || SelectedKeyNode is null) {
			throw new InvalidOperationException("Phantom Key Value Change");
		}
		IsDirty = true;
		if (e.PropertyName == nameof(SelectedKey.Comment) && SelectedKey.Comment.Trim() != "") {
			SelectedKey.NeedsReview = true;
			SelectedKeyNode.NeedsReview = true;
		}
		if (e.PropertyName == nameof(SelectedKey.DefaultValue)) {
			//SelectedEntry is legitimately null while a constant is selected
			SelectedKeyNode.EmptyValue = SelectedKey.DefaultValue.Trim() == ""
				&& (SelectedEntry?.Value.Trim() ?? "") == "";
		}
	}

	private void OnSelectedOrganizerChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(OrganizerNode.Text)) {
			IsDirty = true;
		}
	}

	private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedEntry is null || SelectedKey is null || SelectedKeyNode is null) {
			throw new InvalidOperationException("Phantom Key Value Change");
		}
		IsDirty = true;
		if (e.PropertyName == nameof(SelectedEntry.Comment) && SelectedEntry.Comment.Trim() != "") {
			SelectedKey.NeedsReview = true;
			SelectedKeyNode.NeedsReview = true;
		}
		if (e.PropertyName == nameof(SelectedEntry.Value)) {
			SelectedKeyNode.EmptyValue = SelectedEntry.Value.Trim() == "" && SelectedKey.DefaultValue.Trim() == "";
		}
	}


	//Visibility
	public bool ApplyFilters() {
		foreach (var node in AllKeyNodes) {
			node.IsVisible = PassesVisibilityFilters(node);
		}
		foreach (var node in AllKeyNodes) {
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


	//Markers
	private void MarkStaleNodes(KeyNode node) {
		if (allKeys.TryGetValue(node.FullLabel, out var key) && key.Entries[SelectedLanguage.Code].Stale != null) {
			node.IsStale = true;
		}
		else {
			node.IsStale = false;
		}

		foreach (KeyNode childNode in node.Children) {
			MarkStaleNodes(childNode);
		}
	}

	private void MarkOverwrittenNodes(KeyNode node) {
		node.IsOverwritten = false;
		if (allKeys.TryGetValue(node.FullLabel, out var key)) {
			IEnumerable<string> regionalLanguages = key.Entries.Keys
				.Where(language => language.StartsWith(SelectedLanguage.Code + '-', StringComparison.Ordinal));
			foreach (string language in regionalLanguages) {
				if (!string.IsNullOrEmpty(key.Entries[language].Value)) {
					node.IsOverwritten = true;
				}
			}
		}
		foreach (KeyNode childNode in node.Children) {
			MarkOverwrittenNodes(childNode);
		}
	}


	/*
	Command Logic
	Subsections:
		Load
		Reset
		Save
		Merge
		Data Alteration
	**/

	//Load
	private void DoLoadFiles() {
		if (!PopupDialog.TryFileOpen("Load", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string[]? fileNames)) {
			return;
		}
		foreach (string fileName in fileNames) {
			LoadFile(fileName);
		}
	}

	public void LoadFile(string fileName) {
		using var reader = File.OpenText(fileName);
		LoadFile(reader, fileName);
	}

	public void LoadFile(TextReader reader, string fileName) {
		FileNames.Add(fileName);
		fileName = Path.GetFileNameWithoutExtension(fileName);
		WordsParserToLocalizationProvider consumer = new();
		WordsParser parser = new(consumer);
		parser.Load(reader);
		filePreambles[fileName] = consumer.Preamble;
		fileLanguages[fileName] = [.. consumer.DeclaredLanguages];
		// TODO: explain?
		if (usingDefaultLanguage && consumer.WordKeys.Count > 0) {
			KnownLanguages.Clear();
		}
		foreach (var language in consumer.KnownLanguages.Values) {
			if (!KnownLanguages.Any(l
				=> l.Code == language.Code
			)) {
				KnownLanguages.Add(language);
			}
			else if (!language.IsPlaceholder
				&& KnownLanguages.Any(l => l.Code == language.Code && l.IsPlaceholder)
			) {
				LanguageEntry edit = KnownLanguages.First(l => l.Code == language.Code);
				edit.NativeName = language.NativeName;
				edit.EnglishName = language.EnglishName;
			}
			else if (language.EnglishName != language.NativeName
				&& KnownLanguages.Any(l => l.Code == language.Code && l.EnglishName == l.NativeName)
			) {
				LanguageEntry edit = KnownLanguages.First(l => l.Code == language.Code);
				edit.EnglishName = language.EnglishName;
			}
		}
		SelectedLanguage ??= KnownLanguages[0];
		var keys = consumer.WordKeys;
		if (keys.Count == 0) {
			KeyNode fileNode = new KeyNode(fileName, fileName) {
				IsFile = true
			};
			KeyNodes.Add(fileNode);
			AllKeyNodes.Add(fileNode);
			AddFileOrganizers(fileNode, consumer.Trailer);
		}
		else {
			foreach (var (_, key) in keys) {
				key.BlockKey = $"{fileName}.{key.BlockKey}";
				if (allKeys.Remove(key.BlockKey, out var gone)) {
					Keys.Remove(gone);
				}
				if (!key.IsEmpty()) {
					Keys.Add(key);
					allKeys.Add(key.BlockKey, key);
				}
			}
			foreach (var key in Keys) {
				foreach (var lang in KnownLanguages) {
					if (!key.Entries.ContainsKey(lang.Code)) {
						key.Entries[lang.Code] = new();
					}
				}
			}
			var comments = consumer.BlockComments.ToDictionary(
				pair => $"{fileName}.{pair.Key}",
				pair => pair.Value);
			var add = GetFileNode(keys.Values, comments);
			//a library file lists nothing: every declared label is a !Label
			//(or there are no labels at all)
			if (!consumer.DeclaredLanguages.Any(code => !consumer.KnownLanguages[code].NativeName.StartsWith('!'))) {
				add.IsLibraryFile = true;
			}
			int remove = KeyNodes.FindIndex(file => file.FullLabel == add.FullLabel);
			if (remove > -1) {
				KeyNodes.RemoveAt(remove);
				AllKeyNodes.RemoveWhere(k => k.FullLabel.StartsWith(add.Label + '.') || k.FullLabel == add.FullLabel);
			}
			KeyNodes.Add(add);
			AllKeyNodes.Add(add);
			AddFileOrganizers(add, consumer.Trailer);
		}
		usingDefaultLanguage = false;
		foreach (var key in Keys) {
			foreach (var lang in KnownLanguages) {
				if (!key.Entries.ContainsKey(lang.Code)) {
					key.Entries[lang.Code] = new();
				}
			}
		}
		foreach (var keyNode in AllKeyNodes) {
			if (allKeys.TryGetValue(keyNode.FullLabel, out var key) && !keyNode.IsConstant) {
				keyNode.EmptyValue = key.DefaultValue.Trim() == ""
					|| key.Entries[SelectedLanguage.Code].Value.Trim() == "";
			}
		}
		ApplyFilters();
	}

	//the preamble shows as an organizer pinned to the file's start; its text
	//lives in filePreambles, which Save writes above the language table. The
	//trailer is just a standalone comment at the end of the walk.
	private void AddFileOrganizers(KeyNode fileNode, string trailer) {
		string label = fileNode.FullLabel;
		if (filePreambles.GetValueOrDefault(label, "") != "") {
			var node = new OrganizerNode($"{label}.;preamble",
				() => filePreambles.GetValueOrDefault(label, ""),
				text => filePreambles[label] = text);
			fileNode.Children.Insert(0, node);
			AllKeyNodes.Add(node);
		}
		if (trailer != "") {
			var node = new CommentNode($"{label}.;trailer", trailer);
			fileNode.Children.Add(node);
			AllKeyNodes.Add(node);
		}
	}

	private KeyNode GetFileNode(IEnumerable<WordsKey> keys, IReadOnlyDictionary<string, string> comments) {
		List<string> labels = [];
		foreach (var key in keys) {
			labels.Add(key.BlockKey);
		}
		Dictionary<string, (KeyNode node, string parentName)> nodes = [];

		foreach (string keyName in labels) {
			string[] parts = keyName.Split('.');
			for (int i = 1; i <= parts.Length; i++) {
				string[] subparts = [.. parts.Take(i)];
				string name = string.Join(".", subparts);
				string label = subparts[^1];
				if (label.Length > 0 && label[0] == '$') {
					label = label[1..];
				}
				string fullLabel = string.Join(".", subparts);
				if (!nodes.ContainsKey(name)) {
					KeyNode node = new KeyNode(label, fullLabel);
					if (allKeys.TryGetValue(fullLabel, out var key)) {
						node.IsConstant = key.IsConstant;
						node.NeedsReview = key.NeedsReview;
						node.IsStale = key.Entries[SelectedLanguage.Code].Stale != null;
						node.IsOverwritten = KnownLanguages
							.Where(lang => lang.Code.StartsWith(SelectedLanguage.Code + "-"))
							.Any(lang => !string.IsNullOrEmpty(key.Entries[lang.Code].Value));
						
					}
					string parentName = string.Join(".", subparts.Take(i - 1));
					nodes[name] = (node, parentName);
				}
			}
		}
		KeyNode fileStarter = new();
		foreach (var (node, parentName) in nodes.Values) {
			KeyNode target = parentName == null || !nodes.TryGetValue(parentName, out var parent)
				? fileStarter
				: parent.node;
			if (comments.TryGetValue(node.FullLabel, out var text)) {
				//the run that sat above this block becomes a standalone comment
				//node in front of it; from here on, position is the anchor
				var organizer = new CommentNode($"{node.FullLabel}.;comment", text);
				target.Children.Add(organizer);
				AllKeyNodes.Add(organizer);
			}
			target.Children.Add(node);
			AllKeyNodes.Add(node);
		}
		KeyNode file = fileStarter.Children[0];
		file.IsFile = true;
		foreach (KeyNode child in file.Children) {
			if (child.Children.IsNullOrEmpty()) {
				child.CanBeConstant = true;
			}
		}
		return file;
	}

	//Reset
	private void DoReset() {
		//ResetPopup().SafeFireAndForget(x => PopupDialog.Push(x.ToString()));
		ResetPopup();
	}

	private void ResetPopup() {
		var result2 = PopupDialog.ShowDialog("Are you sure you want to reset the Language Manager?", MessageBoxButton.YesNo);
		if (!result2.IsAffirmative()) {
			return;
		}
		ResetCore();
	}

	public void ResetCore() {
		Keys.Clear();
		KeyNodes.Clear();
		AllKeyNodes.Clear();
		KnownLanguages.Clear();
		allKeys.Clear();
		LanguageEntry defaultLanguage = new("en", "!English (common)");
		KnownLanguages.Add(defaultLanguage);
		usingDefaultLanguage = true;
		SelectedLanguage = KnownLanguages.FirstOrDefault()
			?? throw new InvalidOperationException("Failed to add default language.");
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		SelectedKeyNode = null;
		FileNames.Clear();
		filePreambles.Clear();
		fileLanguages.Clear();
		IsDirty = false;
	}


	//Save
	private void DoSave() {
		Save();
	}
	public override void Save() {
		foreach (string fileName in FileNames) {
			//FileNames holds the paths given to LoadFile; nodes go by the bare name
			string label = Path.GetFileNameWithoutExtension(fileName);
			KeyNode fileNode = KeyNodes.FirstOrDefault(k => k.FullLabel == label)
				?? throw new InvalidDataException($"Cannot find node with file name: {fileName}");
			//comments in the tree write themselves in place; only the preamble
			//needs passing, since it precedes the language table
			IniWriter.WriteFile(fileNode, fileName, allKeys, LanguagesFor(label), preamble: filePreambles.GetValueOrDefault(label, ""));
		}
		IsDirty = false;
	}

	//the file's own language table: its declared codes carrying the session's
	//current labels; files we did not load (e.g. brand new) get the session union
	internal IReadOnlyCollection<LanguageEntry> LanguagesFor(string label) {
		if (!fileLanguages.TryGetValue(label, out var codes)) {
			return KnownLanguages;
		}
		return [.. codes
			.Select(code => KnownLanguages.FirstOrDefault(language => language.Code == code))
			.OfType<LanguageEntry>()];
	}


	//Merge
	private void DoMergeFiles() {
		PopupDialog.Push(new MergeControlView() { DataContext = new MergeControlViewModel(this) });
	}

	public KeyNode? GetMergedKeyNode(
		KeyNode baseFile,
		Dictionary<string, KeyNode> files,
		string mergedFileName,
		out Dictionary<string, WordsKey> keysToMerge) {
		var languageSources = files.ToDictionary(pair => pair.Key, pair => pair.Value.FullLabel);
		var merged = WordsOperations.Merge(allKeys, baseFile.FullLabel, languageSources, mergedFileName, out _);
		keysToMerge = merged ?? [];
		if (merged is null) {
			return null;
		}
		KeyNode fileToMerge = new(baseFile) {
			Label = mergedFileName,
			FullLabel = mergedFileName
		};
		UpdateChildFullLabelsWithoutKeys(fileToMerge.Children, fileToMerge.FullLabel);
		return fileToMerge;
	}

	public bool FilesHaveConflict(IEnumerable<KeyNode> files, out HashSet<string> conflictingKeys) {
		var keySets = files.Select(file => WordsOperations.KeysOf(allKeys, file.FullLabel));
		return !WordsOperations.HaveSameKeys(keySets, out conflictingKeys);
	}

	//Data Alteration
	private void DoManageLanguages() {
		PopupDialog.Push(new LanguageManagerView() { DataContext = new LanguageManagerViewModel(this) });
	}

	private void DoRemoveLocalizationKeyAndNode() {
		if (SelectedKeyNode is null || SelectedKeyNode.FullLabel is null) {
			return;
		}
		if (SelectedKeyNode is OrganizerNode organizer) {
			//deleting the organizer deletes the comment it presents
			organizer.Text = "";
			RemoveKeyNode(organizer);
			return;
		}
		if (SelectedKeyNode.IsFile) {
			RemoveFileNodePopup(SelectedKeyNode);
			return;
		}
		RemoveKeysUnder(SelectedKeyNode.FullLabel);
		RemoveKeyNode(SelectedKeyNode);
	}

	//removes the key at blockKey and every descendant key, from both
	//collections; exact-or-prefix so `view` never catches `viewer`
	private void RemoveKeysUnder(string blockKey) {
		for (int i = Keys.Count - 1; i >= 0; i--) {
			string candidate = Keys[i].BlockKey;
			if (candidate == blockKey || candidate.StartsWith(blockKey + '.', StringComparison.Ordinal)) {
				Keys.RemoveAt(i);
				allKeys.Remove(candidate);
			}
		}
	}

	private void RemoveFileNodePopup(KeyNode fileNodeToRemove) {
		var result2 = PopupDialog.ShowDialog("Are you sure you want to remove the selected file? All unsaved changes will be lost", MessageBoxButton.YesNo);
		if (!result2.IsAffirmative()) {
			return;
		}
		RemoveFileNodeCore(fileNodeToRemove);
	}

	public void RemoveFileNodeCore(KeyNode fileNodeToRemove) {
		RemoveKeysUnder(fileNodeToRemove.FullLabel);
		FileNames.RemoveWhere(fileName => Path.GetFileNameWithoutExtension(fileName) == fileNodeToRemove.FullLabel);
		filePreambles.Remove(fileNodeToRemove.FullLabel);
		fileLanguages.Remove(fileNodeToRemove.FullLabel);
		KeyNodes.Remove(fileNodeToRemove);
		AllKeyNodes.RemoveWhere(keyNode => keyNode.FullLabel.StartsWith(fileNodeToRemove.Label + '.') || keyNode.FullLabel == fileNodeToRemove.FullLabel);
		if (!KeyNodes.IsNullOrEmpty()) {
			SelectedKeyNode = KeyNodes[0];
		}
		else {
			SelectedKeyNode = null;
		}
	}

	private void RemoveKeyNode(KeyNode keyNodeToRemove) {
		if (keyNodeToRemove.FullLabel is null) {
			return;
		}
		KeyNode? parentNode = keyNodeToRemove.GetParentNode(KeyNodes);
		KeyNode? grandParentNode = parentNode?.GetParentNode(KeyNodes);
		//a removed key leaves any comment above it standing; on the next load
		//the comment anchors to whatever block follows it
		parentNode?.Children.Remove(keyNodeToRemove);
		AllKeyNodes.RemoveWhere(keyNode => keyNode.FullLabel.StartsWith(keyNodeToRemove.FullLabel + '.') || keyNode.FullLabel == keyNodeToRemove.FullLabel);
		if (parentNode is not null && parentNode.Children.IsNullOrEmpty() && grandParentNode is not null && grandParentNode.IsFile) {
			parentNode.CanBeConstant = true;
		}
		SelectedKeyNode = parentNode;
		IsDirty = true;
	}


	private void DoRenameNode() {
		if (SelectedKeyNode is OrganizerNode) {
			return;
		}
		PopupDialog.Push(new KeyNameView() { DataContext = new KeyNameViewModel(this, SelectedKeyNode) });
	}

	public void RenameLocalizationKeyAndNode(string newName) {
		if (SelectedKeyNode is null || SelectedKeyNode.FullLabel is null || SelectedKeyNode.Label is null) {
			throw new InvalidDataException("Error: Node has no name");
		}
		IsDirty = true;
		SelectedKeyNode.Label = newName;
		if (SelectedKeyNode.IsConstant) {
			newName = "$" + newName;
		}
		if (KeyNodes.Any(k => k.FullLabel == SelectedKeyNode.FullLabel)) {
			SelectedKeyNode.FullLabel = newName;
		}
		else {
			string[] fullLabelParts = SelectedKeyNode.FullLabel.Split('.');
			fullLabelParts[^1] = newName;
			string blockKey = string.Join('.', fullLabelParts);
			SelectedKeyNode.FullLabel = blockKey;
		}
		UpdateChildFullLabels(SelectedKeyNode.Children, SelectedKeyNode.FullLabel);
		if (SelectedKey is not null) {
			allKeys.Remove(SelectedKey.BlockKey);
			SelectedKey.BlockKey = SelectedKeyNode.FullLabel;
			allKeys.Add(SelectedKey.BlockKey, SelectedKey);
		}
	}

	private void DoAddLocalizationKeyNode() {
		if (SelectedKeyNode is OrganizerNode) {
			return;
		}
		PopupDialog.Push(new KeyNameView() { DataContext = new KeyNameViewModel(this, null) });
	}

	public void AddLocalizationKeyNode(string newName) {
		if (SelectedKeyNode is null) {
			throw new InvalidDataException("Selected Key Node is null");
		}
		string blockKey = SelectedKeyNode.FullLabel + $".{newName}";
		KeyNode nodeToAdd = new(newName, blockKey) {
			IsSelected = true,
			CanBeConstant = SelectedKeyNode.IsFile
		};
		SelectedKeyNode.CanBeConstant = false;
		SelectedKeyNode.IsExpanded = true;
		SelectedKeyNode.IsSelected = false;
		AllKeyNodes.Add(nodeToAdd);
		SelectedKeyNode.Children.Add(nodeToAdd);
		SelectedKeyNode = nodeToAdd;
		
		IsDirty = true;
	}


	private void DoAddLocalizationKey() {
		if (SelectedKeyNode is null) {
			throw new InvalidDataException("Selected Node is null.");
		}
		if (SelectedKeyNode is OrganizerNode) {
			return;
		}
		WordsKey keyToAdd = new(SelectedKeyNode.FullLabel);
		foreach (LanguageEntry language in KnownLanguages) {
			keyToAdd.Entries[language.Code] = new();
		}
		Keys.Add(keyToAdd);
		allKeys.Add(keyToAdd.BlockKey, keyToAdd);
		SelectedKey = keyToAdd;
		SelectedEntry = keyToAdd.Entries[SelectedLanguage.Code];
		IsDirty = true;
	}

	private void DoAddOrganizer() {
		if (SelectedKeyNode is null or OrganizerNode || SelectedKeyNode.IsFile) {
			return;
		}
		KeyNode? parent = SelectedKeyNode.GetParentNode(KeyNodes);
		if (parent is null) {
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
			AllKeyNodes.Add(organizer);
			select = organizer;
			IsDirty = true;
		}
		SelectedKeyNode.IsSelected = false;
		select.IsSelected = true;
		SelectedKeyNode = select;
	}

	private void DoRemoveLocalizationKey() {
		if (SelectedKeyNode is null || SelectedKeyNode.FullLabel is null) {
			return;
		}
		string blockKeyToRemove = SelectedKeyNode.FullLabel;
		for (int i = Keys.Count - 1; i >= 0; i--) {
			if (Keys[i].BlockKey == blockKeyToRemove) {
				Keys.RemoveAt(i);
				allKeys.Remove(blockKeyToRemove);
			}
		}
		SelectedKeyNode.IsConstant = false;
		SelectedKeyNode.IsStale = false;
		SelectedKeyNode.NeedsReview = false;
		SelectedKeyNode.IsOverwritten = false;
		SelectedKey = null;
		SelectedEntry = null;
		IsDirty = true;
	}


	private void DoStaleAllLanguages() {
		if (SelectedKeyNode is not null) {
			string? selectedKeyLabel = SelectedKeyNode.FullLabel;
			WordsKey? selectedLocalizationKey = Keys.FirstOrDefault(key => key.BlockKey == selectedKeyLabel);
			if (selectedLocalizationKey != null) {
				foreach (var languageData in selectedLocalizationKey.Entries.Values) {
					languageData.Stale = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
				}
				AffectProperty(nameof(SelectedLanguage));
			}
			SelectedKeyNode.IsStale = true;
			IsDirty = true;
		}
	}

	private void DoToggleStaleLanguage(string? languageCode) {
		if (languageCode is null) {
			return;
		}
		if (SelectedKeyNode is not null) {
			string? selectedKeyLabel = SelectedKeyNode.FullLabel;

			WordsKey? selectedLocalizationKey = Keys.FirstOrDefault(key => key.BlockKey == selectedKeyLabel);

			if (selectedLocalizationKey != null) {
				if (selectedLocalizationKey.Entries[languageCode].Stale is null) {
					selectedLocalizationKey.Entries[languageCode].Stale = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
					SelectedKeyNode.IsStale = true;
				}
				else {
					selectedLocalizationKey.Entries[languageCode].Stale = null;
					SelectedKeyNode.IsStale = false;
				}
			}
			AffectProperty(nameof(SelectedLanguage));
			IsDirty = true;
		}
	}

	private void DoToggleKeyNeedsReview() {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return;
		}
		if (SelectedKey.NeedsReview) {
			SelectedKey.NeedsReview = false;
			SelectedKeyNode.NeedsReview = false;
		}
		else {
			SelectedKey.NeedsReview = true;
			SelectedKeyNode.NeedsReview = true;
		}
	}

	//adds or strips the constant marker on the LAST segment only:
	//"Example.view.key" <-> "Example.view.$key"
	private static string SetConstantMarker(string key, bool isConstant) {
		int start = key.LastIndexOf('.') + 1;
		string name = key[start..].TrimStart('$');
		return key[..start] + (isConstant ? "$" + name : name);
	}

	private void DoToggleLocalizationKeyIsConstant() {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return;
		}
		IsDirty = true;
		bool makeConstant = !SelectedKey.IsConstant;
		SelectedKey.IsConstant = makeConstant;
		SelectedKeyNode.IsConstant = makeConstant;
		allKeys.Remove(SelectedKey.BlockKey);
		SelectedKey.BlockKey = SetConstantMarker(SelectedKey.BlockKey, makeConstant);
		allKeys.Add(SelectedKey.BlockKey, SelectedKey);
		SelectedKeyNode.FullLabel = SetConstantMarker(SelectedKeyNode.FullLabel, makeConstant);
		if (makeConstant) {
			SelectedKeyNode.IsStale = false;
			SelectedKeyNode.IsOverwritten = false;
			SelectedEntry = null;
			foreach (string language in SelectedKey.Entries.Keys) {
				SelectedKey.Entries[language] = new WordsEntry();
			}
		}
		else {
			SelectedEntry = SelectedKey.Entries[SelectedLanguage.Code];
		}
	}

	private void DoTestParameters(ObservableCollection<WordsParameter> parameters) {
		PopupDialog.Push(new TestParametersView() { DataContext = new TestParametersViewModel(this, parameters) });
	}



	/*
	Miscellaneous Logic
	Subsections:
		KeyNode
		DragDrop
		WordsProvider
	**/


	//KeyNode 
	public void UpdateChildFullLabels(IEnumerable<KeyNode> childNodes, string parentFullLabel) {
		IsDirty = true;
		foreach (KeyNode childNode in childNodes) {
			string newFullLabel = parentFullLabel + $".{childNode.Label}";
			if (allKeys.TryGetValue(childNode.FullLabel, out var keyToUpdate)) {
				allKeys.Remove(keyToUpdate.BlockKey);
				if (allKeys.Remove(newFullLabel)) {
					int indexToRemove = Keys.FindIndex(localizationKey => localizationKey.BlockKey == newFullLabel);
					Keys.RemoveAt(indexToRemove);
				}
				keyToUpdate.BlockKey = newFullLabel;
				allKeys.Add(newFullLabel, keyToUpdate);
			}
			childNode.FullLabel = newFullLabel;
			if (childNode.Children.Count > 0) {
				UpdateChildFullLabels(childNode.Children, childNode.FullLabel);
			}
		}
	}

	public static void UpdateChildFullLabelsWithoutKeys(IEnumerable<KeyNode> childNodes, string parentFullLabel) {
		foreach (KeyNode childNode in childNodes) {
			string newFullLabel = parentFullLabel + $".{childNode.Label}";
			childNode.FullLabel = newFullLabel;
			if (childNode.Children.Count > 0) {
				UpdateChildFullLabelsWithoutKeys(childNode.Children, childNode.FullLabel);
			}
		}
	}

	//Language table upkeep, used by the language manager: the manager operates
	//session-wide, so its changes apply to every file's declared table
	internal void AddLanguageCode(string code) {
		foreach (var codes in fileLanguages.Values) {
			if (!codes.Contains(code)) {
				codes.Add(code);
			}
		}
	}

	internal void RemoveLanguageCode(string code) {
		foreach (var codes in fileLanguages.Values) {
			codes.Remove(code);
		}
	}

	internal void ReplaceLanguageCode(string oldCode, string newCode) {
		foreach (var codes in fileLanguages.Values) {
			int i = codes.IndexOf(oldCode);
			if (i < 0) {
				continue;
			}
			if (codes.Contains(newCode)) {
				codes.RemoveAt(i);
			}
			else {
				codes[i] = newCode;
			}
		}
	}


	//DragDrop
	internal void MoveKey(string oldKey, string newKey) {
		ArgumentNullException.ThrowIfNull(oldKey);
		ArgumentNullException.ThrowIfNull(newKey);

		if (oldKey == newKey) {
			return;
		}
		if (!allKeys.TryGetValue(oldKey, out var keyToUpdate)) {
			return;
		}
		allKeys.Remove(oldKey);
		if (allKeys.Remove(newKey)) {
			int indexToRemove = Keys.FindIndex(localizationKey => localizationKey.BlockKey == newKey);
			Keys.RemoveAt(indexToRemove);
		}
		keyToUpdate.BlockKey = newKey;
		allKeys.Add(keyToUpdate.BlockKey, keyToUpdate);
		IsDirty = true;
	}


	//WordsProvider
	//file nodes in load order; later files win bare-reference lookups,
	//like a host app stacking dictionaries
	private IEnumerable<string> FileLabels => KeyNodes.Select(node => node.FullLabel);

	public IWordsProvider GetWordsProvider()
		=> new DefaultWordsProvider(allKeys, FileLabels);

	public IWordsProvider GetWordsProvider(string languageCode)
		=> new LanguageWordsProvider(allKeys, languageCode, FileLabels);
}
