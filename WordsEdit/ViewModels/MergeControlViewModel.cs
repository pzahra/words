using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

internal class MergeControlViewModel : ViewModelBase {
	public MainWindowViewModel Parent { get; }

	public IReadOnlyCollection<KeyNode> AvailableFiles { get; }

	public ObservableCollection<KeyNode> FilesToMerge { get; } = [];

	public KeyNode? BaseFile { get; set => ChangeProperty(ref field, value); }

	public IReadOnlyCollection<LanguageEntry> Languages { get; }

	public Dictionary<string, KeyNode> LanguageCodeFilePair { get; } = [];

	public string ConflictMessage { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasConflict)); }

	public bool HasConflict => ConflictMessage.Length > 0;
	public bool CanMerge => !HasConflict && FilesToMerge.Count > 0;

	public ICommand MergeCommand { get; }
	public ICommand SetBaseFileCommand { get; }
	public ICommand CancelCommand { get; }

	public MergeControlViewModel(MainWindowViewModel parent) {
		ArgumentNullException.ThrowIfNull(parent);
		
		MergeCommand = new DelegateCommand(DoMerge);
		SetBaseFileCommand = new DelegateCommand<KeyNode>(DoSetBaseFile);
		CancelCommand = new DelegateCommand(DoCancel);

		ConflictMessage = "";
		Parent = parent;
		AvailableFiles = parent.KeyNodes;
		Languages = parent.KnownLanguages;
	}

	private void DoMerge() {
		ArgumentNullException.ThrowIfNull(BaseFile);
		if (!PopupDialog.TryFileSave("Merge Location", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string? mergedFileName)) {
			return;
		}
		KeyNode mergedFile = Parent.GetMergedKeyNode(BaseFile, LanguageCodeFilePair, Path.GetFileNameWithoutExtension(mergedFileName), out var mergedKeys) ?? throw new InvalidOperationException("Merge Failed");
		IniWriter.WriteFile(mergedFile, mergedFileName, mergedKeys, Parent.KnownLanguages);
		Parent.LoadFile(mergedFileName);
		PopupDialog.Close();
	}

	private void DoSetBaseFile(KeyNode file) {
		BaseFile?.IsBaseFile = false;
		BaseFile = file;
		file.IsBaseFile = true;
	}

	private void DoCancel() {
		PopupDialog.Close();
	}

	public void FilesChanged() {
		if (Parent.FilesHaveConflict(FilesToMerge, out HashSet<string> conflicts)) {
			var conflict = new StringBuilder("Files do not share Keys:");
			conflicts.ForEach(c => conflict.Append("\n" + c));
			ConflictMessage = conflict.ToString();
		}
		else {
			ConflictMessage = "";
		}
		AffectProperty(nameof(CanMerge));
	}
}
