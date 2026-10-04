using System.Collections.ObjectModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The language table (SPEC: Languages), edited on a working copy: the rows
///     are the session's languages as <see cref="LanguageRow"/>s, the pane edits
///     the highlighted one live, + adds a row, the trash drops one, drag
///     reorders and one row may be the default's language. Nothing reaches the
///     session until OK, which applies the lot — removals, re-codes and renames,
///     additions, the default's language, the order — as one undoable
///     commit and makes the highlighted row the tree's language; Cancel or
///     Escape forgets it all.
/// </summary>
public class LanguageManagerViewModel : DialogViewModel {
	public override string Title => Words.Known["languages.title"];
	public LanguageDrag LanguageDrag { get; }
	public MainWindowViewModel Parent { get; }
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
	///     files do not say (SPEC: Languages); ticking a row moves it there. Every
	///     file declares it on OK.
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

	public LanguageManagerViewModel(MainWindowViewModel parent) {
		Parent = parent;
		LanguageDrag = new LanguageDrag { Vm = this };
		foreach (LanguageEntry known in parent.Tree.KnownLanguages) {
			Rows.Add(new LanguageRow(this, known));
		}
		DefaultRow = Rows.FirstOrDefault(row => row.Code == parent.Session.Languages.DefaultLanguage);
		Revalidate();
		Selected = Rows.FirstOrDefault(row => row.Origin == parent.Tree.SelectedLanguage) ?? Rows[0];
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

	//the copy reaches the session, as one undoable commit. Additions whose code is
	//free go first, so the last-language rule never refuses a removal; removals
	//next, freeing codes; then the renames, one whose new code is still taken
	//waiting for the rename that frees it, a cycle of swaps parking one language on
	//a throwaway code; then the additions that waited for a code; then the
	//default's language, every code being final; then the order
	private void Apply() {
		LanguageTable table = Parent.Session.Languages;
		Parent.ChangeLanguages(edit => {
			List<LanguageEntry> gone = [.. table.Known.Where(known => Rows.All(row => row.Origin != known))];
			List<LanguageRow> additions = [.. Rows.Where(row => row.Origin is null)];
			AddFree(edit, table, additions);
			foreach (LanguageEntry entry in gone) {
				edit.Remove(entry.Code);
			}
			List<LanguageRow> renames = [.. Rows.Where(row => row.Origin is not null && row.IsChanged)];
			Dictionary<LanguageRow, string> current = renames.ToDictionary(row => row, row => row.Origin!.Code);
			while (renames.Count > 0) {
				LanguageRow next = renames.FirstOrDefault(row => row.Code == current[row] || table.Find(row.Code) is null) ?? renames[0];
				if (next.Code != current[next] && table.Find(next.Code) is { } blocking) {
					LanguageRow blocked = renames.First(row => current[row] == blocking.Code);
					current[blocked] = $"zz-{Guid.NewGuid():N}";
					edit.Rename(blocking.Code, new LanguageEntry(current[blocked], blocking.NativeName) { EnglishName = blocking.EnglishName });
				}
				edit.Rename(current[next], next.ToEntry());
				renames.Remove(next);
			}
			AddFree(edit, table, additions);
			edit.Declare(DefaultRow?.Code);
			for (int i = 0; i < Rows.Count; i++) {
				int at = table.Known.ToList().FindIndex(known => known.Code == Rows[i].Code);
				if (at >= 0) {
					edit.Reorder(at, i);
				}
			}
		});
		Parent.Tree.SelectedLanguage = (Selected is { } selected ? table.Find(selected.Code) : null) ?? table.Known[0];
	}

	//adds the rows whose code the table does not hold yet, and drops them from the list
	private static void AddFree(LanguagesEdit edit, LanguageTable table, List<LanguageRow> additions) {
		foreach (LanguageRow row in additions.Where(row => table.Find(row.Code) is null).ToList()) {
			edit.Add(row.ToEntry());
			additions.Remove(row);
		}
	}
}
