using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

public class MergeControlViewModel : DialogViewModel {
	public override string Title => "Merge Files";
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
		AvailableFiles = parent.Tree.KeyNodes;
		Languages = parent.Tree.KnownLanguages;
	}

	private void DoMerge() {
		if (BaseFile is null) {
			return;
		}
		if (!Parent.Dialogs.TrySaveFile("Merge Location", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string? mergedFileName)) {
			return;
		}
		var sources = LanguageCodeFilePair.ToDictionary(pair => pair.Key, pair => Parent.Tree.FileOf(pair.Value));
		WordsFile? merged;
		try {
			//the merged file declares the base file's languages plus the ones merged
			//in, and keeps the base file's preamble and image schemes — the round
			//trip SPEC guarantees, not the session union
			merged = Parent.Session.Merge(Parent.Tree.FileOf(BaseFile), sources, BaseFile, mergedFileName, out _);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Parent.Dialogs.Tell($"Could not write {mergedFileName}:\n{ex.Message}");
			return;
		}
		if (merged is null) {
			//the files disagree on their keys: say so rather than fail
			FilesChanged();
			return;
		}
		Parent.Tree.Present(merged);
		Close();
	}

	private void DoSetBaseFile(KeyNode file) {
		BaseFile?.IsBaseFile = false;
		BaseFile = file;
		file.IsBaseFile = true;
	}

	private void DoCancel() {
		Close();
	}

	public void FilesChanged() {
		if (!Parent.Session.HaveSameKeys(FilesToMerge.Select(Parent.Tree.FileOf), out HashSet<string> conflicts)) {
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
