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
///     OK, which applies the lot — removals, re-codes and renames, additions, the
///     default's language, the order — as one undoable commit and makes the
///     highlighted row the tree's language; Cancel or Escape forgets it all.
/// </summary>
public class LanguageManagerViewModel : DialogViewModel {
	public override string Title => Words.Known.Format("languages.title", File.Label);
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

	//the tick as the file had it, so OK declares the default's language only when it moved
	private readonly LanguageRow? declaredRow;

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
		DefaultRow = declaredRow = Rows.FirstOrDefault(row => row.Code == file.DefaultLanguage);
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

	//the copy reaches the file, as one undoable commit. Additions whose code is
	//free go first, so the last-language rule never refuses a removal; removals
	//next, freeing codes — one whose code a new row takes parks on a throwaway
	//code first, so the new row comes in before it goes; then the renames, one
	//whose new code is still taken waiting for the rename that frees it, a cycle
	//of swaps parking one language on a throwaway code; then the additions that
	//waited for a code; then the default's language, every code being final; then
	//the order. Whatever the table refused is told, never skipped in silence
	private void Apply() {
		LanguageTable table = Parent.Session.Languages;
		WordsFile file = File;
		List<string> refused = [];
		Parent.ChangeLanguages(edit => {
			List<LanguageEntry> gone = [.. table.For(file).Where(label => Rows.All(row => row.Origin != label))];
			List<LanguageRow> additions = [.. Rows.Where(row => row.Origin is null)];
			AddFree(edit, file, additions);
			foreach (LanguageEntry entry in gone) {
				string code = entry.Code;
				if (additions.Any(row => row.NormalCode == code)) {
					code = $"zz-{Guid.NewGuid():N}";
					edit.Rename(file, entry.Code, new LanguageEntry(code, entry.NativeName) { EnglishName = entry.EnglishName });
					AddFree(edit, file, additions);
				}
				if (!edit.Remove(file, code)) {
					refused.Add(entry.Code);
				}
			}
			List<LanguageRow> renames = [.. Rows.Where(row => row.Origin is not null && row.IsChanged)];
			Dictionary<LanguageRow, string> current = renames.ToDictionary(row => row, row => row.Origin!.Code);
			while (renames.Count > 0) {
				LanguageRow next = renames.FirstOrDefault(row => row.NormalCode == current[row] || !file.Languages.Contains(row.NormalCode)) ?? renames[0];
				if (next.NormalCode != current[next] && file.Languages.Contains(next.NormalCode)) {
					LanguageEntry blocking = table.For(file).First(label => label.Code == next.NormalCode);
					LanguageRow blocked = renames.First(row => current[row] == blocking.Code);
					current[blocked] = $"zz-{Guid.NewGuid():N}";
					edit.Rename(file, blocking.Code, new LanguageEntry(current[blocked], blocking.NativeName) { EnglishName = blocking.EnglishName });
				}
				edit.Rename(file, current[next], next.ToEntry());
				renames.Remove(next);
			}
			AddFree(edit, file, additions);
			refused.AddRange(additions.Select(row => row.NormalCode));
			if (DefaultRow != declaredRow) {
				edit.Declare(file, DefaultRow?.NormalCode);
			}
			int place = 0;
			foreach (LanguageRow row in Rows) {
				int at = file.Languages.IndexOf(row.NormalCode);
				if (at >= 0) {
					edit.Reorder(file, at, place++);
				}
			}
		});
		Parent.Tree.SelectedLanguage = (Selected is { } selected ? table.Find(selected.NormalCode) : null) ?? table.Known[0];
		if (refused.Count > 0) {
			Parent.Dialogs.Tell(Words.Known.Format("tell.languages-refused", string.Join(", ", refused)));
		}
	}

	//adds the rows whose code the file does not declare yet, and drops them from the list
	private static void AddFree(LanguagesEdit edit, WordsFile file, List<LanguageRow> additions) {
		foreach (LanguageRow row in additions.Where(row => !file.Languages.Contains(row.NormalCode)).ToList()) {
			if (edit.Add(file, row.ToEntry())) {
				additions.Remove(row);
			}
		}
	}
}
