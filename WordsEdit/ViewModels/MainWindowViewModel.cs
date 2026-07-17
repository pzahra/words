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
public class MainWindowViewModel : LocalizationViewModelSaveBase {

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
	public DragDropKeysViewModel DragDropKeysViewModel { get; set; }

	private ObservableCollection<KeyNode> _LocalizationKeyNodes = new();
	public ObservableCollection<KeyNode> LocalizationKeyNodes {
		get => _LocalizationKeyNodes;
		private set => ChangeProperty(ref _LocalizationKeyNodes, value);
	}

	private readonly HashSet<KeyNode> _AllKeyNodes = new();

	public ObservableCollection<LocalizationKey> LocalizationKeys { get; } = new();
	private readonly Dictionary<string, LocalizationKey> _localizationKeysDictionary = new();

	private ObservableCollection<LocalizationLanguage> _LocalizationLanguages = new();
	public ObservableCollection<LocalizationLanguage> LocalizationLanguages {
		get => _LocalizationLanguages;
		set => ChangeProperty(ref _LocalizationLanguages, value);
	}

	private HashSet<string> _FileNames = new();
	public HashSet<string> FileNames {
		get => _FileNames;
		set => ChangeProperty(ref _FileNames, value);
	}


	//Selection UI
	private KeyNode? _SelectedKeyNode;
	public KeyNode? SelectedKeyNode {
		get => _SelectedKeyNode;
		set {
			if (ChangeProperty(ref _SelectedKeyNode, value)) {
				SelectedKeyNodeChanged();
			}
		}
	}

	private LocalizationKey? _SelectedLocalizationKey;
	public LocalizationKey? SelectedLocalizationKey {
		get => _SelectedLocalizationKey;
		set {
			LocalizationKey? oldValue = _SelectedLocalizationKey;
			if (ChangeProperty(ref _SelectedLocalizationKey, value)) {
				if (oldValue is not null) {
					oldValue.PropertyChanged -= OnLocalizationKeyDataChanged;
				}
				if (value is not null) {
					value.PropertyChanged += OnLocalizationKeyDataChanged;
				}
			}
		}
	}

	private LocalizationKeyLanguageData? _SelectedLocalizationKeyLanguageData;
	public LocalizationKeyLanguageData? SelectedLocalizationKeyLanguageData {
		get => _SelectedLocalizationKeyLanguageData;
		set {
			LocalizationKeyLanguageData? oldValue = _SelectedLocalizationKeyLanguageData;
			if (ChangeProperty(ref _SelectedLocalizationKeyLanguageData, value)) {
				if (oldValue is not null) {
					oldValue.PropertyChanged -= OnLocalizationKeyLanguageDataChanged;
				}
				if (value is not null) {
					value.PropertyChanged += OnLocalizationKeyLanguageDataChanged;
				}
			}
		}
	}

	private void OnLocalizationKeyDataChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedLocalizationKey is null || SelectedKeyNode is null || SelectedLocalizationKeyLanguageData is null) {
			throw new InvalidOperationException("Phantom Key Value Change");
		}
		IsDirty = true;
		if (e.PropertyName == "Comment" && SelectedLocalizationKey.Comment.Trim() != "") {
			SelectedLocalizationKey.NeedsReview = true;
			SelectedKeyNode.NeedsReview = true;
		}
		if (e.PropertyName == "DefaultValue" && SelectedLocalizationKey.DefaultValue.Trim() != "" && SelectedLocalizationKeyLanguageData.Value.Trim() != "") {
			SelectedKeyNode.EmptyValue = false;
		}
		if (e.PropertyName == "DefaultValue" && SelectedLocalizationKey.DefaultValue.Trim() == "") {
			SelectedKeyNode.EmptyValue = true;
		}
	}
	private void OnLocalizationKeyLanguageDataChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedLocalizationKeyLanguageData is null || SelectedLocalizationKey is null || SelectedKeyNode is null) {
			throw new InvalidOperationException("Phantom Key Value Change");
		}
		IsDirty = true;
		if (e.PropertyName == "LanguageComment" && SelectedLocalizationKeyLanguageData.LanguageComment.Trim() != "") {
			SelectedLocalizationKey.NeedsReview = true;
			SelectedKeyNode.NeedsReview = true;
		}
		if (e.PropertyName == "Value" && SelectedLocalizationKeyLanguageData.Value.Trim() != "" && SelectedLocalizationKey.DefaultValue.Trim() != "") {
			SelectedKeyNode.EmptyValue = false;
		}
		if (e.PropertyName == "Value" && SelectedLocalizationKeyLanguageData.Value.Trim() == "") {
			SelectedKeyNode.EmptyValue = true;
		}
	}

	private LocalizationLanguage _SelectedLocalizationLanguage;
	public LocalizationLanguage SelectedLocalizationLanguage {
		get => _SelectedLocalizationLanguage;
		set {
			if (ChangeProperty(ref _SelectedLocalizationLanguage, value)) {
				SelectedLocalizationLanguageChanged();
				ApplyFiltersAndUpdateVisibility();
			}
		}
	}


	//Filters
	private bool _IsStaleFilter = false;
	public bool IsStaleFilter {
		get => _IsStaleFilter;
		set {
			if (ChangeProperty(ref _IsStaleFilter, value)) {
				ApplyFiltersAndUpdateVisibility();
			}
		}
	}

	private bool _NeedsReviewFilter = false;
	public bool NeedsReviewFilter {
		get => _NeedsReviewFilter;
		set {
			if (ChangeProperty(ref _NeedsReviewFilter, value)) {
				ApplyFiltersAndUpdateVisibility();
			}
		}
	}

	private string _SearchFilterText = "";
	public string SearchFilterText {
		get => _SearchFilterText;
		set {
			if (ChangeProperty(ref _SearchFilterText, value)) {
				ApplyFiltersAndUpdateVisibility();
			}
		}
	}


	//Previews
	private bool _ShowDefaultPreview;
	public bool ShowDefaultPreview {
		get => _ShowDefaultPreview;
		set => ChangeProperty(ref _ShowDefaultPreview, value);
	}

	private bool _ShowLocalizationPreview;
	public bool ShowLocalizationPreview {
		get => _ShowLocalizationPreview;
		set => ChangeProperty(ref _ShowLocalizationPreview, value);
	}

	private bool usingDefaultLanguage = true;

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
	public ICommand RemoveLocalizationKeyCommand { get; }
	public ICommand StaleAllLanguagesCommand { get; }
	public ICommand ToggleStaleLanguageCommand { get; }
	public ICommand ToggleLocalizationKeyNeedsReviewCommand { get; }
	public ICommand ToggleLocalizationKeyIsConstantCommand { get; }

/*
Constructor
**/
	public MainWindowViewModel() {
		LocalizationLanguage defaultLanguage = new("en", "!English (common)");
		_LocalizationLanguages.Add(defaultLanguage);
		_SelectedLocalizationLanguage = defaultLanguage;
		DragDropKeysViewModel = new DragDropKeysViewModel() { MainWindow = this };
		LoadFileCommand = new DelegateCommand(DoLoadFiles);
		ResetCommand = new DelegateCommand(DoReset);
		SaveCommand = new DelegateCommand(DoSave);
		MergeFilesCommand = new DelegateCommand(DoMergeFiles);
		ManageLanguagesCommand = new DelegateCommand(DoManageLanguages);
		RemoveLocalizationKeyAndNodeCommand = new DelegateCommand(DoRemoveLocalizationKeyAndNode);
		RenameLocalizationKeyAndNodeCommand = new DelegateCommand(DoRenameLocalizationKeyAndNode);
		AddLocalizationKeyNodeCommand = new DelegateCommand(DoAddLocalizationKeyNode);
		AddLocalizationKeyCommand = new DelegateCommand(DoAddLocalizationKey);
		RemoveLocalizationKeyCommand = new DelegateCommand(DoRemoveLocalizationKey);
		StaleAllLanguagesCommand = new DelegateCommand(DoStaleAllLanguages);
		ToggleStaleLanguageCommand = new DelegateCommand<string>(DoToggleStaleLanguage);
		ToggleLocalizationKeyNeedsReviewCommand = new DelegateCommand(DoToggleKeyNeedsReview);
		ToggleLocalizationKeyIsConstantCommand = new DelegateCommand(DoToggleLocalizationKeyIsConstant);
		TestParametersCommand = new DelegateCommand<ObservableCollection<LocalizationParameter>>(DoTestParameters);
	}

/*
UI Logic
Subsections: 
	Selection
	Visibility
	Markers
**/

	//Selection
	public void SelectedLocalizationLanguageChanged() {
		foreach (KeyNode keyNode in _AllKeyNodes) {
			if (_localizationKeysDictionary.ContainsKey(keyNode.FullLabel) && !keyNode.IsConstant) {
				LocalizationKey localizationKey = _localizationKeysDictionary[keyNode.FullLabel];
				if (localizationKey.DefaultValue.Trim() == "" || localizationKey.LanguageData[SelectedLocalizationLanguage.Code].Value.Trim() == "") {
					keyNode.EmptyValue = true;
				}
				else {
					keyNode.EmptyValue = false;
				}
			}
		}
		if (SelectedKeyNode is null || SelectedLocalizationKey is null || SelectedKeyNode.IsConstant) {
			SelectedLocalizationKeyLanguageData = null;
			foreach (KeyNode node in LocalizationKeyNodes) {
				MarkStaleNodes(node);
				MarkOverwrittenNodes(node);
			}
			return;
		}
		SelectedLocalizationKeyLanguageData = SelectedLocalizationKey.LanguageData[SelectedLocalizationLanguage.Code];
		ShowLocalizationPreview = false;
		foreach (KeyNode node in LocalizationKeyNodes) {
			MarkStaleNodes(node);
			MarkOverwrittenNodes(node);
		}
	}

	private void SelectedKeyNodeChanged() {
		if (SelectedKeyNode is null) {
			SelectedLocalizationKey = null;
			SelectedLocalizationKeyLanguageData = null;
			return;
		}
		string fullLabel = SelectedKeyNode.FullLabel;
		string label = SelectedKeyNode.Label;
		ShowDefaultPreview = false;
		ShowLocalizationPreview = false;
		if (_localizationKeysDictionary.ContainsKey(fullLabel)) {
			SelectedLocalizationKey = _localizationKeysDictionary[fullLabel];
			if (SelectedLocalizationKey.IsConstant) {
				SelectedLocalizationKeyLanguageData = null;
			}
			else {
				SelectedLocalizationKeyLanguageData = SelectedLocalizationKey.LanguageData[SelectedLocalizationLanguage.Code];
			}
		}
		else {
			SelectedLocalizationKey = null;
			SelectedLocalizationKeyLanguageData = null;
		}
	}


	//Visibility
	public void ApplyFiltersAndUpdateVisibility() {
		foreach (var node in _AllKeyNodes) {
			node.IsVisible = PassesVisibilityFilters(node);
		}
		UpdateVisibilityBasedOnDescendants(LocalizationKeyNodes);
	}

	public bool PassesVisibilityFilters(KeyNode node) {
		bool passesFilter = true;

		if (IsStaleFilter) {
			passesFilter &= node.IsStale || node.EmptyValue;
		}
		if (NeedsReviewFilter) {
			passesFilter &= node.NeedsReview;
		}
		if (!string.IsNullOrEmpty(SearchFilterText)) {
			passesFilter &= node.FullLabel.Contains(SearchFilterText, StringComparison.OrdinalIgnoreCase);
		}

		return passesFilter;
	}

	public static void UpdateVisibilityBasedOnDescendants(ObservableCollection<KeyNode> keyNodes) {
		foreach (var keyNode in keyNodes) {
			UpdateVisibilityBasedOnDescendantsRecursive(keyNode);
		}
	}

	private static bool UpdateVisibilityBasedOnDescendantsRecursive(KeyNode keyNode) {
		if (keyNode.Children.Count == 0) {
			return keyNode.IsVisible;
		}

		foreach (var childKeyNode in keyNode.Children) {
			bool isVisible = UpdateVisibilityBasedOnDescendantsRecursive(childKeyNode);
			keyNode.IsVisible |= isVisible;
		}

		return keyNode.IsVisible;
	}


	//Markers
	private void MarkStaleNodes(KeyNode node) {
		if (_localizationKeysDictionary.ContainsKey(node.FullLabel)
				&& _localizationKeysDictionary[node.FullLabel].LanguageData[SelectedLocalizationLanguage.Code].StaleComment != null) {
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
		if (_localizationKeysDictionary.ContainsKey(node.FullLabel)) {
			LocalizationKey localizationKey = _localizationKeysDictionary[node.FullLabel];
			IEnumerable<string> regionalLanguages = localizationKey.LanguageData.Keys
				.Where(language => language.Contains(SelectedLocalizationLanguage.Code) && language != SelectedLocalizationLanguage.Code);
			foreach (string language in regionalLanguages) {
				if (!string.IsNullOrEmpty(localizationKey.LanguageData[language].Value)) {
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
		if (!FileNames.Contains(fileName)) {
			FileNames.Add(fileName);
		}
		fileName = Path.GetFileNameWithoutExtension(fileName);
		WordsParserToLocalizationProvider consumer = new();
		WordsParser parser = new(consumer);
		parser.Load(reader);
		if (usingDefaultLanguage && consumer.LocalizationKeysDictionary.Count > 0) {
			LocalizationLanguages.Clear();
		}
		foreach (LocalizationLanguage language in consumer.LocalizationLanguagesDictionary.Values) {
			if (!LocalizationLanguages.Any(languageInFile => languageInFile.Code == language.Code)
					&& !LocalizationLanguages.Any(languageInFile => languageInFile.NativeName == language.NativeName)
					&& !LocalizationLanguages.Any(languageInFile => languageInFile.EnglishName == language.EnglishName)) {
				LocalizationLanguages.Add(language);
			}
			else if (!language.NativeName.StartsWith("MISSING") && LocalizationLanguages
					.Any(languageInFile => languageInFile.Code == language.Code && languageInFile.NativeName
						.StartsWith("MISSING"))) {
				LocalizationLanguage languageToEdit = LocalizationLanguages
					.Where(languageInFile => languageInFile.Code == language.Code).First();
				languageToEdit.NativeName = language.NativeName;
				languageToEdit.EnglishName = language.EnglishName;
			}
			else if (!(language.EnglishName == language.NativeName) && LocalizationLanguages
					.Any(languageInFile => languageInFile.Code == language.Code 
						&& languageInFile.EnglishName == languageInFile.NativeName)) {
				LocalizationLanguage languageToEdit = LocalizationLanguages
					.Where(languageInFile => languageInFile.Code == language.Code).First();
				languageToEdit.EnglishName = language.EnglishName;
			}
		}
		SelectedLocalizationLanguage ??= LocalizationLanguages[0];
		var localizationKeys = consumer.LocalizationKeys;
		if (localizationKeys.Count == 0) {
			KeyNode fileNode = new KeyNode(fileName, fileName) {
				IsFile = true
			};
			LocalizationKeyNodes.Add(fileNode);
			_AllKeyNodes.Add(fileNode);
		}
		else {
			foreach (LocalizationKey localizationKey in localizationKeys) {
				localizationKey.BlockKey = $"{fileName}.{localizationKey.BlockKey}";
				if (_localizationKeysDictionary.ContainsKey(localizationKey.BlockKey)) {
					_localizationKeysDictionary.Remove(localizationKey.BlockKey);
					for (int i = LocalizationKeys.Count - 1; i >= 0; i--) {
						if (LocalizationKeys[i].BlockKey == localizationKey.BlockKey) {
							LocalizationKeys.RemoveAt(i);
						}
					}
				}
				if (!localizationKey.IsEmpty()) {
					LocalizationKeys.Add(localizationKey);
					_localizationKeysDictionary.Add(localizationKey.BlockKey, localizationKey);
				}
			}
			foreach (LocalizationKey localizationKey in LocalizationKeys) {
				foreach (LocalizationLanguage localizationLanguage in LocalizationLanguages) {
					if (!localizationKey.LanguageData.ContainsKey(localizationLanguage.Code)) {
						localizationKey.LanguageData[localizationLanguage.Code] = new();
					}
				}
			}
			KeyNode fileToAdd = GetFileNode(localizationKeys);
			if (!consumer.LocalizationLanguagesDictionary.Values
					.Any(localizationLanguage => !localizationLanguage.NativeName.StartsWith("MISSING NAME"))) {
				fileToAdd.IsLibraryFile = true;
			}
			if (LocalizationKeyNodes.Any(file => file.FullLabel == fileToAdd.FullLabel)) {
				int indexOfFileToRemove = LocalizationKeyNodes.FindIndex(file => file.FullLabel == fileToAdd.FullLabel);
				LocalizationKeyNodes.RemoveAt(indexOfFileToRemove);
				_AllKeyNodes.RemoveWhere(keyNode => keyNode.FullLabel.StartsWith(fileToAdd.Label + '.') || keyNode.FullLabel == fileToAdd.FullLabel);
			}
			LocalizationKeyNodes.Add(fileToAdd);
			_AllKeyNodes.Add(fileToAdd);
		}
		usingDefaultLanguage = false;
		foreach (LocalizationKey localizationKey in LocalizationKeys) {
			foreach (LocalizationLanguage localizationLanguage in LocalizationLanguages) {
				if (!localizationKey.LanguageData.ContainsKey(localizationLanguage.Code)) {
					localizationKey.LanguageData[localizationLanguage.Code] = new();
				}
			}
		}
		foreach (KeyNode keyNode in _AllKeyNodes) {
			if (_localizationKeysDictionary.ContainsKey(keyNode.FullLabel) && !keyNode.IsConstant) {
				LocalizationKey localizationKey = _localizationKeysDictionary[keyNode.FullLabel];
				if (localizationKey.DefaultValue.Trim() == "" || localizationKey.LanguageData[SelectedLocalizationLanguage.Code].Value.Trim() == "") {
					keyNode.EmptyValue = true;
				}
				else {
					keyNode.EmptyValue = false;
				}
			}
		}
	}

	private KeyNode GetFileNode(IEnumerable<LocalizationKey> localizationKeys) {
		List<string> localizationKeyLabels = new List<string>();
		foreach (LocalizationKey localizationKey in localizationKeys) {
			localizationKeyLabels.Add(localizationKey.BlockKey);
		}
		Dictionary<string, Tuple<KeyNode, string>> nodes = new Dictionary<string, Tuple<KeyNode, string>>();

		foreach (string keyName in localizationKeyLabels) {
			string[] parts = keyName.Split('.');
			for (int i = 1; i <= parts.Length; i++) {
				string[] subparts = parts.Take(i).ToArray();
				string name = string.Join(".", subparts);
				string label = subparts[^1];
				if (label.Length > 0 && label[0] == '$') {
					label = label[1..];
				}
				string fullLabel = string.Join(".", subparts);
				if (!nodes.ContainsKey(name)) {
					KeyNode node = new KeyNode(label, fullLabel);
					if (_localizationKeysDictionary.ContainsKey(fullLabel)) {
						LocalizationKey localizationKey = _localizationKeysDictionary[fullLabel];
						if (localizationKey.IsConstant) {
							node.IsConstant = true;
						}
						if (localizationKey.NeedsReview) {
							node.NeedsReview = true;
						}
						if (localizationKey.LanguageData[SelectedLocalizationLanguage.Code].StaleComment != null) {
							node.IsStale = true;
						}
						IEnumerable<string> regionalLanguages = localizationKey.LanguageData.Keys
							.Where(language => language.Contains(SelectedLocalizationLanguage.Code) && language != SelectedLocalizationLanguage.Code);
						foreach (string language in regionalLanguages) {
							if (!string.IsNullOrEmpty(localizationKey.LanguageData[language].Value)) {
								node.IsOverwritten = true;
							}
						}
					}
					string parentName = string.Join(".", subparts.Take(i - 1));
					nodes[name] = new Tuple<KeyNode, string>(node, parentName);
				}
			}
		}
		KeyNode fileStarter = new();
		foreach (var node in nodes.Values) {
			if (node.Item2 == null || !nodes.ContainsKey(node.Item2)) {
				fileStarter.Children.Add(node.Item1);
				_AllKeyNodes.Add(node.Item1);
			}
			else {
				nodes[node.Item2].Item1.Children.Add(node.Item1);
				_AllKeyNodes.Add(node.Item1);
			}
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
		LocalizationKeys.Clear();
		LocalizationKeyNodes.Clear();
		_AllKeyNodes.Clear();
		LocalizationLanguages.Clear();
		_localizationKeysDictionary.Clear();
		LocalizationLanguage defaultLanguage = new("en", "!English (common)");
		LocalizationLanguages.Add(defaultLanguage);
		usingDefaultLanguage = true;
		SelectedLocalizationLanguage = LocalizationLanguages.FirstOrDefault()
			?? throw new InvalidOperationException("Failed to add default language.");
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		SelectedKeyNode = null;
		FileNames.Clear();
		IsDirty = false;
	}


	//Save
	private void DoSave() {
		Save();
	}
	public override void Save() {
		IsDirty = false;
		foreach (string fileName in FileNames) {
			using StreamWriter writer = new StreamWriter(fileName);
			WriteToINIFile(writer, fileName);
		}
	}

	public void WriteToINIFile(string fileName) {
		using StreamWriter writer = new StreamWriter(fileName);
		WriteToINIFile(writer, fileName);
	}

	public void WriteToINIFile(TextWriter textWriter, string fileName) {
		IniWriter writer = new(textWriter);
		fileName = Path.GetFileNameWithoutExtension(fileName);
		KeyNode fileToWrite = LocalizationKeyNodes.Where(k => k.FullLabel == fileName).FirstOrDefault()
			?? throw new InvalidDataException($"Cannot find node with file name: {fileName}");
		if (fileToWrite.IsLibraryFile) {
			WriteKeyNodesToFile(fileToWrite, writer);
			return;
		}
		foreach (LocalizationLanguage language in LocalizationLanguages) {
			writer.WritePair($"value-{language.Code}", language.NativeName);
			writer.WritePair($"comment-{language.Code}", language.EnglishName);
		}
		writer.WriteLine();
		WriteKeyNodesToFile(fileToWrite, writer);
	}

	private void WriteKeyNodesToFile(KeyNode keyNode, IniWriter writer) {
		if (_localizationKeysDictionary.ContainsKey(keyNode.FullLabel)) {
			LocalizationKey localizationKey = _localizationKeysDictionary[keyNode.FullLabel];
			WriteBlockToFile(localizationKey, writer);
		}
		foreach (KeyNode childNode in keyNode.Children) {
			WriteKeyNodesToFile(childNode, writer);
		}
	}

	private static void WriteBlockToFile(LocalizationKey localizationKey, IniWriter writer) {
		string blockKey = localizationKey.BlockKey[(localizationKey.BlockKey.IndexOf('.') + 1)..];
		writer.WriteBlockHeader(blockKey);


		if (localizationKey.Context != "") {
			writer.WritePair("context", localizationKey.Context);
		}

		if (localizationKey.Comment != "") {
			writer.WritePair("comment", localizationKey.Comment);
		}

		if (localizationKey.DefaultValue != "") {
			writer.WritePair("value", localizationKey.DefaultValue);
		}

		if (localizationKey.Parameters.Count != 0) {
			foreach (LocalizationParameter parameter in localizationKey.Parameters) {
				writer.WritePair(
					$"param-{parameter.Key}",
					$"{parameter.DataType.Name}:{parameter.Value}");
			}
		}

		if (localizationKey.NeedsReview) {
			writer.WritePair("stale", "");
		}

		foreach (KeyValuePair<string, LocalizationKeyLanguageData> localizationLanguageDataEntry in localizationKey.LanguageData) {
			string languageCode = localizationLanguageDataEntry.Key;
			LocalizationKeyLanguageData languageData = localizationLanguageDataEntry.Value;

			if (languageData.Value != "") {
				writer.WritePair($"value-{languageCode}", languageData.Value);
			}

			if (languageData.StaleComment is not null) {
				writer.WritePair($"stale-{languageCode}", $"{languageData.StaleComment?.ToString(CultureInfo.InvariantCulture)}");
			}

			if (languageData.LanguageContext != "") {
				writer.WritePair($"context-{languageCode}", languageData.LanguageContext);
			}

			if (languageData.LanguageComment != "") {
				writer.WritePair($"comment-{languageCode}", languageData.LanguageComment);
			}
		}
		if (localizationKey.DefaultValue != "") {
			writer.WriteLine();
		}
	}

	//Merge
	private void DoMergeFiles() {
		PopupDialog.Push(new MergeControlView() { DataContext = new MergeControlViewModel(this) });
	}

	public KeyNode? GetMergedKeyNode(
		KeyNode baseFile,
		Dictionary<string, KeyNode> languageCodeFilePairDictionary,
		string mergedFileName,
		out Dictionary<string,
		LocalizationKey> keysToMerge) {
		KeyNode fileToMerge = new(baseFile) {
			Label = mergedFileName,
			FullLabel = mergedFileName
		};
		UpdateChildFullLabelsWithoutKeys(fileToMerge.Children, fileToMerge.FullLabel);
		Dictionary<string, Dictionary<string, LocalizationKey>> selectedDictionaries = new();
		var baseKeys = _localizationKeysDictionary.Where(pair => pair.Key.StartsWith(baseFile.FullLabel + "."))
			.ToDictionary(pair => pair.Key, pair => pair.Value);
		keysToMerge = new Dictionary<string, LocalizationKey>();
		foreach (var kvp in baseKeys) {
			string newBlockKey = mergedFileName + kvp.Value.BlockKey[kvp.Value.BlockKey.IndexOf('.')..];
			LocalizationKey localizationKey = new LocalizationKey(kvp.Value) {
				BlockKey = newBlockKey
			};
			keysToMerge.Add(localizationKey.BlockKey, localizationKey);
		}
		foreach (var (languageCode, sourceFile) in languageCodeFilePairDictionary) {
			var localizationKeyDictionary = _localizationKeysDictionary.Where(pair => pair.Key.StartsWith(sourceFile.FullLabel + "."))
				.ToDictionary(pair => pair.Key, pair => pair.Value);
			selectedDictionaries.Add(languageCode, localizationKeyDictionary);
		}
		selectedDictionaries.Add("default", keysToMerge);
		if (!DictionariesHaveTheSameLocalizationKeys(selectedDictionaries.Values)) {
			return null;
		}
		selectedDictionaries.Remove("default");
		foreach (var (languageCode, localizationKeyDictionary) in selectedDictionaries) {
			foreach (LocalizationKey localizationKey in localizationKeyDictionary.Values) {
				string sharedBlockKey = localizationKey.BlockKey[(localizationKey.BlockKey.IndexOf('.') + 1)..];
				keysToMerge[$"{mergedFileName}.{sharedBlockKey}"].LanguageData[languageCode] = localizationKey.LanguageData[languageCode];
			}
		}
		return fileToMerge;
	}

	public static bool DictionariesHaveTheSameLocalizationKeys(IEnumerable<Dictionary<string, LocalizationKey>> localizationKeyDictionaries) {
		HashSet<string>? firstKeySet = null;

		foreach (var dictionary in localizationKeyDictionaries) {
			HashSet<string> currentKeySet = new HashSet<string>();

			foreach (var key in dictionary.Keys) {
				int dotIndex = key.IndexOf('.');
				string sharedBlockKey = key[(dotIndex + 1)..];
				currentKeySet.Add(sharedBlockKey);
			}

			if (firstKeySet == null) {
				firstKeySet = currentKeySet;
			}
			else if (!firstKeySet.SetEquals(currentKeySet)) {
				return false;
			}
		}

		return true;
	}

	public static bool DictionariesHaveTheSameLocalizationKeys(
			IEnumerable<Dictionary<string, LocalizationKey>> localizationKeyDictionaries,
			out HashSet<string> conflicts) {
		HashSet<string>? firstKeySet = null;
		bool haveTheSameKeys = true;
		conflicts = new HashSet<string>();

		foreach (var dictionary in localizationKeyDictionaries) {
			HashSet<string> currentKeySet = new HashSet<string>();

			foreach (var key in dictionary.Keys) {
				int dotIndex = key.IndexOf('.');
				string sharedBlockKey = key[(dotIndex + 1)..];
				currentKeySet.Add(sharedBlockKey);
			}

			if (firstKeySet == null) {
				firstKeySet = currentKeySet;
			}
			else if (!firstKeySet.SetEquals(currentKeySet)) {
				haveTheSameKeys = false;
				currentKeySet.SymmetricExceptWith(firstKeySet);
				foreach (string conflict in currentKeySet) {
					conflicts.Add(conflict);
				}
			}
		}
		return haveTheSameKeys;
	}

	public bool FilesHaveConflict(IEnumerable<KeyNode> files, out HashSet<string> conflictingKeys) {
		List<Dictionary<string, LocalizationKey>> localizationKeyDictionaries = new();
		foreach (var file in files) {
			var localizationKeyDictionary = _localizationKeysDictionary.Where(pair => pair.Key.StartsWith(file.FullLabel + "."))
				.ToDictionary(pair => pair.Key, pair => pair.Value);
			localizationKeyDictionaries.Add(localizationKeyDictionary);
		}
		bool hasConflict = !DictionariesHaveTheSameLocalizationKeys(localizationKeyDictionaries, out var conflicts);
		conflictingKeys = conflicts;
		return hasConflict;
	}

	public void WriteMergedToINIFile(KeyNode mergedFile, string fileName, Dictionary<string, LocalizationKey> mergedKeys) {
		using IniWriter writer = new(new StreamWriter(fileName));
		if (mergedFile.IsLibraryFile) {
			WriteMergedKeyNodesToFile(mergedFile, writer, mergedKeys);
			return;
		}
		foreach (LocalizationLanguage language in LocalizationLanguages) {
			writer.WritePair($"value-{language.Code}", language.NativeName);
			writer.WritePair($"comment-{language.Code}", language.EnglishName);
		}
		writer.WriteLine();
		WriteMergedKeyNodesToFile(mergedFile, writer, mergedKeys);
	}

	private void WriteMergedKeyNodesToFile(KeyNode keyNode, IniWriter writer, Dictionary<string, LocalizationKey> mergedKeys) {
		if (mergedKeys.ContainsKey(keyNode.FullLabel)) {
			LocalizationKey localizationKey = mergedKeys[keyNode.FullLabel];
			WriteBlockToFile(localizationKey, writer);
		}
		foreach (KeyNode childNode in keyNode.Children) {
			WriteMergedKeyNodesToFile(childNode, writer, mergedKeys);
		}
	}

	//Data Alteration
	private void DoManageLanguages() {
		PopupDialog.Push(new LanguageManagerView() { DataContext = new LanguageManagerViewModel(this) });
	}

	private void DoRemoveLocalizationKeyAndNode() {
		if (SelectedKeyNode is null || SelectedKeyNode.FullLabel is null) {
			return;
		}
		if (SelectedKeyNode.IsFile) {
			RemoveFileNodePopup(SelectedKeyNode);
			return;
		}
		string blockKeyToRemove = SelectedKeyNode.FullLabel;
		for (int i = LocalizationKeys.Count - 1; i >= 0; i--) {
			if (LocalizationKeys[i].BlockKey.Contains(blockKeyToRemove)) {
				LocalizationKeys.RemoveAt(i);
				_localizationKeysDictionary.Remove(blockKeyToRemove);
			}
		}
		RemoveKeyNode(SelectedKeyNode);
	}

	private void RemoveFileNodePopup(KeyNode fileNodeToRemove) {
		var result2 = PopupDialog.ShowDialog("Are you sure you want to remove the selected file? All unsaved changes will be lost", MessageBoxButton.YesNo);
		if (!result2.IsAffirmative()) {
			return;
		}
		RemoveFileNodeCore(fileNodeToRemove);
	}

	public void RemoveFileNodeCore(KeyNode fileNodeToRemove) {
		if (SelectedKeyNode is null) {
			throw new InvalidDataException("SelectedKeyNode is null");
		}
		string blockKeyToRemove = SelectedKeyNode.FullLabel;
		for (int i = LocalizationKeys.Count - 1; i >= 0; i--) {
			if (LocalizationKeys[i].BlockKey.Contains(blockKeyToRemove)) {
				LocalizationKeys.RemoveAt(i);
				_localizationKeysDictionary.Remove(blockKeyToRemove);
			}
		}
		FileNames.RemoveWhere(fileName => Path.GetFileNameWithoutExtension(fileName) == fileNodeToRemove.FullLabel);
		LocalizationKeyNodes.Remove(fileNodeToRemove);
		_AllKeyNodes.RemoveWhere(keyNode => keyNode.FullLabel.StartsWith(fileNodeToRemove.Label + '.') || keyNode.FullLabel == fileNodeToRemove.FullLabel);
		if (!LocalizationKeyNodes.IsNullOrEmpty()) {
			SelectedKeyNode = LocalizationKeyNodes[0];
		}
		else {
			SelectedKeyNode = null;
		}
		return;
	}

	private void RemoveKeyNode(KeyNode keyNodeToRemove) {
		if (keyNodeToRemove.FullLabel is null) {
			return;
		}
		KeyNode? parentNode = keyNodeToRemove.GetParentNode(LocalizationKeyNodes);
		KeyNode? grandParentNode = parentNode?.GetParentNode(LocalizationKeyNodes);
		parentNode?.Children.Remove(keyNodeToRemove);
		_AllKeyNodes.RemoveWhere(keyNode => keyNode.FullLabel.StartsWith(keyNodeToRemove.FullLabel + '.') || keyNode.FullLabel == keyNodeToRemove.FullLabel);
		if (parentNode is not null && parentNode.Children.IsNullOrEmpty() && grandParentNode is not null && grandParentNode.IsFile) {
			parentNode.CanBeConstant = true;
		}
		SelectedKeyNode = parentNode;
		IsDirty = true;
	}


	private void DoRenameLocalizationKeyAndNode() {
		PopupDialog.Push(new KeyNameView() { DataContext = new KeyNameViewModel(this) });
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
		if (LocalizationKeyNodes.Any(k => k.FullLabel == SelectedKeyNode.FullLabel)) {
			SelectedKeyNode.FullLabel = newName;
		}
		else {
			string[] fullLabelParts = SelectedKeyNode.FullLabel.Split('.');
			fullLabelParts[^1] = newName;
			string blockKey = string.Join('.', fullLabelParts);
			SelectedKeyNode.FullLabel = blockKey;
		}
		UpdateChildFullLabels(SelectedKeyNode.Children, SelectedKeyNode.FullLabel);
		if (SelectedLocalizationKey is not null) {
			_localizationKeysDictionary.Remove(SelectedLocalizationKey.BlockKey);
			SelectedLocalizationKey.BlockKey = SelectedKeyNode.FullLabel;
			_localizationKeysDictionary.Add(SelectedLocalizationKey.BlockKey, SelectedLocalizationKey);
		}
	}

	private void DoAddLocalizationKeyNode() {
		PopupDialog.Push(new KeyNameView() { DataContext = new KeyNameViewModel(this, true) });
	}

	public void AddLocalizationKeyNode(string newName) {
		if (SelectedKeyNode is null) {
			throw new InvalidDataException("Selected Key Node is null");
		}
		string blockKey = SelectedKeyNode.FullLabel + $".{newName}";
		KeyNode nodeToAdd = new(newName, blockKey) {
			IsSelected = true
		};
		SelectedKeyNode.Children.Add(nodeToAdd);
		_AllKeyNodes.Add(nodeToAdd);
		SelectedKeyNode.CanBeConstant = false;
		SelectedKeyNode.IsExpanded = true;
		SelectedKeyNode.IsSelected = false;
		SelectedKeyNode = nodeToAdd;
		KeyNode? parentNode = nodeToAdd.GetParentNode(LocalizationKeyNodes);
		if (parentNode is not null && parentNode.IsFile && nodeToAdd.Children.Count == 0) {
			nodeToAdd.CanBeConstant = true;
		}
		else {
			nodeToAdd.CanBeConstant = false;
		}
		if (parentNode is not null) {
			parentNode.CanBeConstant = false;
		}
		IsDirty = true;
	}


	private void DoAddLocalizationKey() {
		if (SelectedKeyNode is null) {
			throw new InvalidDataException("Selected Node is null.");
		}
		LocalizationKey keyToAdd = new(SelectedKeyNode.FullLabel);
		foreach (LocalizationLanguage language in LocalizationLanguages) {
			keyToAdd.LanguageData[language.Code] = new();
		}
		LocalizationKeys.Add(keyToAdd);
		_localizationKeysDictionary.Add(keyToAdd.BlockKey, keyToAdd);
		SelectedLocalizationKey = keyToAdd;
		SelectedLocalizationKeyLanguageData = keyToAdd.LanguageData[SelectedLocalizationLanguage.Code];
		IsDirty = true;
	}

	private void DoRemoveLocalizationKey() {
		if (SelectedKeyNode is null || SelectedKeyNode.FullLabel is null) {
			return;
		}
		string blockKeyToRemove = SelectedKeyNode.FullLabel;
		for (int i = LocalizationKeys.Count - 1; i >= 0; i--) {
			if (LocalizationKeys[i].BlockKey == blockKeyToRemove) {
				LocalizationKeys.RemoveAt(i);
				_localizationKeysDictionary.Remove(blockKeyToRemove);
			}
		}
		SelectedKeyNode.IsConstant = false;
		SelectedKeyNode.IsStale = false;
		SelectedKeyNode.NeedsReview = false;
		SelectedKeyNode.IsOverwritten = false;
		SelectedLocalizationKey = null;
		SelectedLocalizationKeyLanguageData = null;
		IsDirty = true;
	}


	private void DoStaleAllLanguages() {
		if (SelectedKeyNode is not null) {
			string? selectedKeyLabel = SelectedKeyNode.FullLabel;
			LocalizationKey? selectedLocalizationKey = LocalizationKeys.FirstOrDefault(key => key.BlockKey == selectedKeyLabel);
			if (selectedLocalizationKey != null) {
				foreach (var languageData in selectedLocalizationKey.LanguageData.Values) {
					languageData.StaleComment = DateTimeOffset.Now.ToString();
				}
				AffectProperty(nameof(SelectedLocalizationLanguage));
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

			LocalizationKey? selectedLocalizationKey = LocalizationKeys.FirstOrDefault(key => key.BlockKey == selectedKeyLabel);

			if (selectedLocalizationKey != null) {
				if (selectedLocalizationKey.LanguageData[languageCode].StaleComment is null) {
					selectedLocalizationKey.LanguageData[languageCode].StaleComment = DateTimeOffset.Now.ToString();
					SelectedKeyNode.IsStale = true;
				}
				else {
					selectedLocalizationKey.LanguageData[languageCode].StaleComment = null;
					SelectedKeyNode.IsStale = false;
				}
			}
			AffectProperty(nameof(SelectedLocalizationLanguage));
			IsDirty = true;
		}
	}

	private void DoToggleKeyNeedsReview() {
		if (SelectedLocalizationKey is null || SelectedKeyNode is null) {
			return;
		}
		if (SelectedLocalizationKey.NeedsReview) {
			SelectedLocalizationKey.NeedsReview = false;
			SelectedKeyNode.NeedsReview = false;
		}
		else {
			SelectedLocalizationKey.NeedsReview = true;
			SelectedKeyNode.NeedsReview = true;
		}
	}

	private void DoToggleLocalizationKeyIsConstant() {
		if (SelectedLocalizationKey is null || SelectedKeyNode is null) {
			return;
		}
		IsDirty = true;
		if (SelectedLocalizationKey.IsConstant) {
			SelectedKeyNode.IsConstant = false;
			SelectedLocalizationKey.IsConstant = false;
			_localizationKeysDictionary.Remove(SelectedLocalizationKey.BlockKey);
			SelectedLocalizationKey.BlockKey = SelectedLocalizationKey.BlockKey.Replace(".$", ".");
			_localizationKeysDictionary.Add(SelectedLocalizationKey.BlockKey, SelectedLocalizationKey);
			SelectedKeyNode.FullLabel = SelectedKeyNode.FullLabel.Replace(".$", ".");
			SelectedLocalizationKeyLanguageData = SelectedLocalizationKey.LanguageData[SelectedLocalizationLanguage.Code];
		}
		else {
			SelectedKeyNode.IsConstant = true;
			SelectedKeyNode.IsStale = false;
			SelectedKeyNode.IsOverwritten = false;
			SelectedLocalizationKey.IsConstant = true;
			_localizationKeysDictionary.Remove(SelectedLocalizationKey.BlockKey);
			SelectedLocalizationKey.BlockKey = SelectedLocalizationKey.BlockKey.Replace(".", ".$");
			_localizationKeysDictionary.Add(SelectedLocalizationKey.BlockKey, SelectedLocalizationKey);
			SelectedKeyNode.FullLabel = SelectedKeyNode.FullLabel.Replace(".", ".$");
			SelectedLocalizationKeyLanguageData = null;
			foreach (string key in _localizationKeysDictionary[SelectedKeyNode.FullLabel].LanguageData.Keys) {
				_localizationKeysDictionary[SelectedKeyNode.FullLabel].LanguageData[key] = new LocalizationKeyLanguageData();
			}
		}
	}

	private void DoTestParameters(ObservableCollection<LocalizationParameter> parameters) {
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
			if (_localizationKeysDictionary.ContainsKey(childNode.FullLabel)) {
				LocalizationKey keyToUpdate = _localizationKeysDictionary[childNode.FullLabel];
				_localizationKeysDictionary.Remove(keyToUpdate.BlockKey);
				if (_localizationKeysDictionary.ContainsKey(newFullLabel)) {
					_localizationKeysDictionary.Remove(newFullLabel);
					int indexToRemove = LocalizationKeys.FindIndex(localizationKey => localizationKey.BlockKey == newFullLabel);
					LocalizationKeys.RemoveAt(indexToRemove);
				}
				keyToUpdate.BlockKey = newFullLabel;
				_localizationKeysDictionary.Add(newFullLabel, keyToUpdate);
			}
			childNode.FullLabel = newFullLabel;
			if (childNode.Children.Count > 0) {
				UpdateChildFullLabels(childNode.Children, childNode.FullLabel);
			}
		}
	}

	public void UpdateChildFullLabelsWithoutKeys(IEnumerable<KeyNode> childNodes, string parentFullLabel) {
		foreach (KeyNode childNode in childNodes) {
			string newFullLabel = parentFullLabel + $".{childNode.Label}";
			childNode.FullLabel = newFullLabel;
			if (childNode.Children.Count > 0) {
				UpdateChildFullLabelsWithoutKeys(childNode.Children, childNode.FullLabel);
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
		if (!_localizationKeysDictionary.TryGetValue(oldKey, out var keyToUpdate)) {
			return;
		}
		_localizationKeysDictionary.Remove(oldKey);
		if (_localizationKeysDictionary.ContainsKey(newKey)) {
			_localizationKeysDictionary.Remove(newKey);
			int indexToRemove = LocalizationKeys.FindIndex(localizationKey => localizationKey.BlockKey == newKey);
			LocalizationKeys.RemoveAt(indexToRemove);
		}
		keyToUpdate.BlockKey = newKey;
		_localizationKeysDictionary.Add(keyToUpdate.BlockKey, keyToUpdate);
		IsDirty = true;
	}


	//WordsProvider
	public IWordsProvider GetWordsProvider(string fileName) {
		return new LocalizationDefaultWordsProvider(_localizationKeysDictionary, fileName);
	}

	public IWordsProvider GetWordsProvider(string languageCode, string fileName) {
		return new LocalizationLanguageWordsProvider(_localizationKeysDictionary, languageCode, fileName);
	}
}
