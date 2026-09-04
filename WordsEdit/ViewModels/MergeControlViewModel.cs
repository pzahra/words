using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>One language a file can supply to the merge.</summary>
public class MergeLanguageRow(MergeFileRow file, LanguageEntry language) : ViewModelBase {
	public string Code => language.Code;
	public string Name => language.EnglishName;

	/// <summary>Chosen: the merged file takes this language from <see cref="file"/>, and from no other.</summary>
	public bool IsSelected {
		get;
		set {
			if (ChangeProperty(ref field, value) && value) {
				file.Owner.LanguageChosen(file, this);
			}
		}
	}
}

/// <summary>One loaded file as the merge dialog sees it.</summary>
public class MergeFileRow : ViewModelBase {
	internal MergeControlViewModel Owner { get; }
	public KeyNode Node { get; }
	public WordsFile File { get; }
	public string Label => Node.FullLabel;
	public IReadOnlyList<MergeLanguageRow> Languages { get; }

	internal MergeFileRow(MergeControlViewModel owner, KeyNode node, WordsFile file, IEnumerable<LanguageEntry> languages) {
		Owner = owner;
		Node = node;
		File = file;
		Languages = [.. languages.Select(language => new MergeLanguageRow(this, language))];
	}

	/// <summary>Part of the merge.</summary>
	public bool IsSelected {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				Owner.FileToggled(this);
			}
		}
	}

	/// <summary>The file whose defaults, preamble and tree shape the merged file takes; exactly one of the selected.</summary>
	public bool IsBase {
		get;
		set {
			if (ChangeProperty(ref field, value) && value) {
				Owner.BaseChosen(this);
			}
		}
	}
}

/// <summary>
///     The translator round trip in bulk (SPEC: Merge, Split): pick the files, a
///     base among them, and which file supplies each language; the merged file
///     is written and loaded. The rules — one base, one file per language, the
///     files agreeing on their keys — are kept here, whatever the view does.
///     Split, the other direction, shares the dialog: one file, one of its
///     languages, written on its own and loaded.
/// </summary>
public class MergeControlViewModel : DialogViewModel {
	public override string Title => "Merge and Split";
	public MainWindowViewModel Parent { get; }

	/// <summary>Every loaded file, in tree order.</summary>
	public IReadOnlyList<MergeFileRow> Files { get; }
	/// <summary>The files taking part, in the same order.</summary>
	public ObservableCollection<MergeFileRow> Selected { get; } = [];
	public MergeFileRow? BaseFile => Selected.FirstOrDefault(file => file.IsBase);

	public string ConflictMessage { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasConflict)); } = "";
	public bool HasConflict => ConflictMessage.Length > 0;
	public bool CanMerge => !HasConflict && Selected.Count > 0;

	public ICommand MergeCommand { get; }
	public ICommand SplitCommand { get; }
	public ICommand CancelCommand { get; }

	//Split: the file to take one language out of, and which of its languages
	public MergeFileRow? SplitFile {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				SplitLanguages = value is null ? [] : Parent.Session.Languages.For(value.File);
				SplitLanguage = SplitLanguages.FirstOrDefault();
			}
		}
	}
	/// <summary>The languages <see cref="SplitFile"/> declares.</summary>
	public IReadOnlyList<LanguageEntry> SplitLanguages { get; private set => ChangeProperty(ref field, value); } = [];
	public LanguageEntry? SplitLanguage { get; set => ChangeProperty(ref field, value); }

	public MergeControlViewModel(MainWindowViewModel parent) {
		ArgumentNullException.ThrowIfNull(parent);
		Parent = parent;
		MergeCommand = new DelegateCommand(DoMerge);
		SplitCommand = new DelegateCommand(DoSplit, () => SplitFile is not null && SplitLanguage is not null);
		CancelCommand = new DelegateCommand(Close);
		Files = [.. parent.Tree.KeyNodes.Select(node => new MergeFileRow(this, node, parent.Tree.FileOf(node), parent.Tree.KnownLanguages))];
	}

	/// <summary>Language code → the file it comes from, as chosen.</summary>
	public IReadOnlyDictionary<string, WordsFile> Sources
		=> Selected.SelectMany(file => file.Languages.Where(language => language.IsSelected).Select(language => (language.Code, file.File)))
			.ToDictionary(pair => pair.Code, pair => pair.File);

	internal void FileToggled(MergeFileRow file) {
		if (file.IsSelected) {
			//keep Files order so the list reads the same on both sides
			int index = Files.TakeWhile(other => other != file).Count(Selected.Contains);
			Selected.Insert(index, file);
			if (Selected.Count == 1) {
				file.IsBase = true;
			}
		}
		else {
			Selected.Remove(file);
			foreach (MergeLanguageRow language in file.Languages) {
				language.IsSelected = false;
			}
			if (file.IsBase) {
				file.IsBase = false;
				Selected.FirstOrDefault()?.IsBase = true;
			}
		}
		FilesChanged();
	}

	internal void BaseChosen(MergeFileRow file) {
		foreach (MergeFileRow other in Files) {
			if (other != file) {
				other.IsBase = false;
			}
		}
		AffectProperty(nameof(BaseFile));
	}

	internal void LanguageChosen(MergeFileRow file, MergeLanguageRow language) {
		foreach (MergeFileRow other in Files) {
			if (other != file) {
				foreach (MergeLanguageRow candidate in other.Languages) {
					if (candidate.Code == language.Code) {
						candidate.IsSelected = false;
					}
				}
			}
		}
	}

	private void FilesChanged() {
		if (!Parent.Session.HaveSameKeys(Selected.Select(file => file.File), out HashSet<string> conflicts)) {
			var conflict = new StringBuilder("Files do not share Keys:");
			conflicts.ForEach(c => conflict.Append("\n" + c));
			ConflictMessage = conflict.ToString();
		}
		else {
			ConflictMessage = "";
		}
		AffectProperty(nameof(CanMerge));
		AffectProperty(nameof(BaseFile));
	}

	private void DoMerge() {
		if (!CanMerge || BaseFile is not { } baseFile) {
			return;
		}
		if (!Parent.Dialogs.TrySaveFile("Merge Location", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string? mergedFileName)) {
			return;
		}
		WordsFile? merged;
		try {
			//the merged file declares the base file's languages plus the ones merged
			//in, and keeps the base file's preamble and image schemes — the round
			//trip SPEC guarantees, not the session union
			merged = Parent.Session.Merge(baseFile.File, Sources, baseFile.Node, mergedFileName, out _);
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

	private void DoSplit() {
		if (SplitFile is not { } file || SplitLanguage is not { } language) {
			return;
		}
		if (!Parent.Dialogs.TrySaveFile("Split Location", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string? splitFileName)) {
			return;
		}
		WordsFile split;
		try {
			//that language's entries with the defaults for reference, the source's
			//shape, preamble and settings references: what Merge takes back
			split = Parent.Session.Split(file.File, language.Code, file.Node, splitFileName);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Parent.Dialogs.Tell($"Could not write {splitFileName}:\n{ex.Message}");
			return;
		}
		Parent.Tree.Present(split);
		Close();
	}
}
