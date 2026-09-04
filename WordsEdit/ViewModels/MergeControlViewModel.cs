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
		AvailableFiles = parent.KeyNodes;
		Languages = parent.KnownLanguages;
	}

	private void DoMerge() {
		if (BaseFile is null) {
			return;
		}
		if (!Parent.Dialogs.TrySaveFile("Merge Location", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string? mergedFileName)) {
			return;
		}
		KeyNode? mergedFile = Parent.GetMergedKeyNode(BaseFile, LanguageCodeFilePair, Path.GetFileNameWithoutExtension(mergedFileName), out var mergedKeys);
		if (mergedFile is null) {
			//the files disagree on their keys: say so rather than fail
			FilesChanged();
			return;
		}
		//the merged file declares the base file's languages plus the ones merged
		//in, and keeps the base file's preamble and image schemes — the round
		//trip SPEC guarantees, not the session union
		string baseLabel = BaseFile.FullLabel;
		var codes = Parent.LanguagesFor(baseLabel).Select(l => l.Code)
			.Concat(LanguageCodeFilePair.Keys)
			.Distinct();
		List<LanguageEntry> languages = [.. codes
			.Select(code => Parent.KnownLanguages.FirstOrDefault(l => l.Code == code))
			.OfType<LanguageEntry>()];
		IniWriter.WriteFile(mergedFile, mergedFileName, mergedKeys, languages,
			preamble: Parent.filePreambles.GetValueOrDefault(baseLabel, ""),
			imageSchemes: Parent.fileImageSchemes.GetValueOrDefault(baseLabel));
		Parent.LoadFile(mergedFileName);
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
