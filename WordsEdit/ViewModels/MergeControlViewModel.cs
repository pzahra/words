using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
internal class MergeControlViewModel : DataViewModelBase {
	private MainWindowViewModel _MainWindowViewModel;
	public MainWindowViewModel MainWindowViewModel {
		get => _MainWindowViewModel;
		set => ChangeProperty(ref _MainWindowViewModel, value);
	}

	private ObservableCollection<KeyNode> _AvailableFiles;
	public ObservableCollection<KeyNode> AvailableFiles {
		get => _AvailableFiles;
		set => ChangeProperty(ref _AvailableFiles, value);
	}

	private ObservableCollection<KeyNode> _FilesToMerge = [];
	public ObservableCollection<KeyNode> FilesToMerge {
		get => _FilesToMerge;
		set => ChangeProperty(ref _FilesToMerge, value);
	}

	private KeyNode? _BaseFile;
	public KeyNode? BaseFile {
		get => _BaseFile;
		set => ChangeProperty(ref _BaseFile, value);
	}

	private ObservableCollection<LocalizationLanguage> _LocalizationLanguages = [];
	public ObservableCollection<LocalizationLanguage> LocalizationLanguages {
		get => _LocalizationLanguages;
		set => ChangeProperty(ref _LocalizationLanguages, value);
	}

	private Dictionary<string, KeyNode> _LanguageCodeFilePairDictionary = [];
	public Dictionary<string, KeyNode> LanguageCodeFilePairDictionary {
		get => _LanguageCodeFilePairDictionary;
		set => ChangeProperty(ref _LanguageCodeFilePairDictionary, value);
	}

	private bool _HasConflict = false;
	public bool HasConflict {
		get => _HasConflict;
		set => ChangeProperty(ref _HasConflict, value);
	}

	private string _ConflictMessage = "";
	public string ConflictMessage {
		get => _ConflictMessage;
		set => ChangeProperty(ref _ConflictMessage, value);
	}

	public bool CanMerge => !HasConflict && FilesToMerge.Count > 0;

	public ICommand MergeCommand { get; }
	public ICommand SetBaseFileCommand { get; }
	public ICommand CancelCommand { get; }

	public MergeControlViewModel(MainWindowViewModel mainWindowViewModel) {
		ArgumentNullException.ThrowIfNull(mainWindowViewModel);
		_MainWindowViewModel = mainWindowViewModel;
		_AvailableFiles = mainWindowViewModel.LocalizationKeyNodes;
		_LocalizationLanguages = mainWindowViewModel.LocalizationLanguages;
		MergeCommand = new DelegateCommand(DoMerge);
		SetBaseFileCommand = new DelegateCommand<KeyNode>(DoSetBaseFile);
		CancelCommand = new DelegateCommand(DoCancel);
	}

	private void DoMerge() {
		ArgumentNullException.ThrowIfNull(BaseFile);
		if (!PopupDialog.TryFileSave("Merge Location", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string? mergedFileName)) {
			return;
		}
		KeyNode mergedFile = MainWindowViewModel.GetMergedKeyNode(BaseFile, LanguageCodeFilePairDictionary, Path.GetFileNameWithoutExtension(mergedFileName), out var mergedKeys) ?? throw new InvalidOperationException("Merge Failed");
		MainWindowViewModel.WriteMergedToINIFile(mergedFile, mergedFileName, mergedKeys);
		MainWindowViewModel.LoadFile(mergedFileName);
		PopupDialog.Close();
	}

	private void DoSetBaseFile(KeyNode file) {
		if (BaseFile is not null) {
			BaseFile.IsBaseFile = false;
		}
		BaseFile = file;
		file.IsBaseFile = true;
	}

	private void DoCancel() {
		PopupDialog.Close();
	}

	public void FilesChanged() {
		HasConflict = MainWindowViewModel.FilesHaveConflict(_FilesToMerge, out HashSet<string> conflicts);
		if (HasConflict) {
			ConflictMessage = "Files do not share Keys:";
			foreach (string conflict in conflicts) {
				ConflictMessage += "\n" + conflict;
			}
		}
		else {
			ConflictMessage = "";
		}
		AffectProperty(nameof(CanMerge));
	}

	protected override bool Validate([AllowNull, CallerMemberName] string propertyName = null) => throw new NotImplementedException();
}
