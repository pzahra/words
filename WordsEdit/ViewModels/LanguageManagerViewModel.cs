using System.Collections.ObjectModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     One file's language table (SPEC: Languages), edited on a working copy:
///     the rows are the file's languages as <see cref="LanguageRow"/>s, labelled
///     as the file labels them, its <c>!</c> and all; the pane edits the
///     highlighted one live, + adds a row, the trash drops one, drag reorders and
///     one row may be the default's language. No other file's table changes, so a
///     library beside its host keeps its own. Nothing reaches the session until
///     OK, which hands the lot to the table (<see cref="LanguageTable.Apply"/>) —
///     removals, re-codes and renames, additions, the default's language, the
///     order — as one undoable commit and makes the highlighted row the tree's
///     language; Cancel or Escape forgets it all.
/// </summary>
public class LanguageManagerViewModel : DialogViewModel {
	public override string Title => Words.Known.Format("languages.title", Parent.Session.ShownName(File));
	public LanguageDrag LanguageDrag { get; }
	public MainWindowViewModel Parent { get; }
	/// <summary>The file whose table the manager edits.</summary>
	public WordsFile File { get; }
	public ObservableCollection<LanguageRow> Rows { get; } = [];
	/// <summary>The highlighted row: the pane's, and the tree's language on OK. The list pushes null while its items turn over.</summary>
	public LanguageRow? Selected {
		get;
		set {
			if (value is not null) {
				ChangeProperty(ref field, value);
			}
		}
	}

	/// <summary>
	///     The row whose language the default is written in, or null when the
	///     file does not say (SPEC: Languages); ticking a row moves it there. The
	///     file declares it on OK, when the tick moved.
	/// </summary>
	public LanguageRow? DefaultRow {
		get;
		set {
			LanguageRow? was = field;
			if (ChangeProperty(ref field, value)) {
				was?.DefaultMoved();
				value?.DefaultMoved();
				AffectProperty(nameof(ExonymHeader));
			}
		}
	}

	/// <summary>
	///     The header over the names in the default's language: what the file's
	///     <c>comment-xx</c> labels are written in, English while no row says.
	/// </summary>
	public string ExonymHeader => DefaultRow?.NativeName.TrimStart('!').Trim() is { Length: > 0 } name
		? Words.Known.Format("language.name-in", name)
		: Words.Known["language.english-name"];

	public ICommand AddCommand { get; }
	public ICommand OkCommand { get; }
	public ICommand CancelCommand { get; }

	/// <summary>The manager for the file the selection sits in, or the only one loaded (<see cref="MainWindowViewModel.LanguagesFile"/>).</summary>
	public LanguageManagerViewModel(MainWindowViewModel parent)
		: this(parent, parent.LanguagesFile ?? throw new InvalidOperationException("no file to manage the languages of")) { }

	public LanguageManagerViewModel(MainWindowViewModel parent, WordsFile file) {
		Parent = parent;
		File = file;
		LanguageDrag = new LanguageDrag { Vm = this };
		foreach (LanguageEntry label in parent.Session.Languages.For(file)) {
			Rows.Add(new LanguageRow(this, label));
		}
		DefaultRow = Rows.FirstOrDefault(row => row.Code == file.DefaultLanguage);
		Revalidate();
		Selected = Rows.FirstOrDefault(row => row.Code == parent.Tree.SelectedLanguage.Code) ?? Rows.FirstOrDefault();
		AddCommand = new DelegateCommand(DoAdd);
		OkCommand = new DelegateCommand(DoOk, () => Rows.All(row => !row.HasErrors));
		CancelCommand = new DelegateCommand(Close);
	}

	/// <summary>Every row checks itself against the others, and the header reads the default row's name again. Always true, to chain.</summary>
	internal bool Revalidate() {
		foreach (LanguageRow row in Rows) {
			row.Check([.. Rows.Where(other => other != row)]);
		}
		AffectProperty(nameof(ExonymHeader));
		return true;
	}

	//+: a blank row, highlighted, for the pane to fill in
	private void DoAdd() {
		var row = new LanguageRow(this, null);
		Rows.Add(row);
		Revalidate();
		Selected = row;
	}

	//the trash: the row leaves the copy after confirmation (SPEC: Languages); the
	//session loses the language and its entries on OK. The last row stays
	internal void Remove(LanguageRow row) {
		if (Rows.Count <= 1 || !Rows.Contains(row)) {
			return;
		}
		if (!Parent.Dialogs.Confirm(Words.Known.Format("ask.remove-language", row.Code))) {
			return;
		}
		int i = Rows.IndexOf(row);
		Rows.Remove(row);
		if (DefaultRow == row) {
			DefaultRow = null;
		}
		if (Selected == row) {
			Selected = Rows[Math.Min(i, Rows.Count - 1)];
		}
		Revalidate();
	}

	/// <summary>A drag dropped a row elsewhere: the copy's order, the session's on OK.</summary>
	public void Reorder(int from, int to) => Rows.Move(from, to);

	private void DoOk() {
		Apply();
		Close();
	}

	//the copy reaches the file, as one undoable commit: the table plans the order of
	//its operations and makes them through the edit, which records them. Whatever
	//the table refused is told, never skipped in silence
	private void Apply() {
		LanguageTable table = Parent.Session.Languages;
		List<PlannedLanguage> rows = [.. Rows.Select(row => new PlannedLanguage(row.Origin, row.ToEntry(), row == DefaultRow))];
		IReadOnlyList<string> refused = [];
		Parent.ChangeLanguages(File, edit => refused = table.Apply(File, rows, edit));
		Parent.Tree.SelectedLanguage = (Selected is { } selected ? table.Find(selected.NormalCode) : null) ?? table.Known[0];
		if (refused.Count > 0) {
			Parent.Dialogs.Tell(Words.Known.Format("tell.languages-refused", string.Join(", ", refused)));
		}
	}
}
