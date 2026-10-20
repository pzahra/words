namespace WordsEdit.ViewModels;

/// <summary>
///     A view model over saveable files: a title that stars while any of them
///     is dirty, a Save, and one door for dirtiness, each file's own (SPEC: Dirty
///     by file) — <see cref="MarkDirty(WordsFile)"/> for an edit to a file. Save
///     and Reset are the only cleaners (<see cref="MarkClean(WordsFile)"/>), but
///     for the history putting back the marks it recorded (<see cref="PutBack"/>).
///     The marks are the base's own, so nothing else can set them.
/// </summary>
public abstract class ViewModelSaveBase : ViewModelBase {
	private readonly HashSet<WordsFile> unsaved = [];

	public string TitleMarked => IsDirty ? Title + " *" : Title;
	[Localized]
	public string Title { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); } = "";
	/// <summary>Whether any file has unsaved work.</summary>
	public bool IsDirty => unsaved.Count > 0;
	/// <summary>The files with unsaved work.</summary>
	public IReadOnlySet<WordsFile> Unsaved => unsaved;

	/// <summary>Whether <paramref name="file"/> has unsaved work.</summary>
	public bool IsUnsaved(WordsFile file) => unsaved.Contains(file);

	/// <summary>An edit reached <paramref name="file"/>.</summary>
	public void MarkDirty(WordsFile file) => MarkDirty([file]);

	/// <summary>An edit reached <paramref name="files"/>.</summary>
	public void MarkDirty(IEnumerable<WordsFile> files) {
		bool changed = false;
		foreach (WordsFile file in files) {
			changed |= unsaved.Add(file);
		}
		Marked(changed);
	}

	/// <summary>Save: <paramref name="file"/> is as the disk has it, or gone from the session.</summary>
	protected internal void MarkClean(WordsFile file) => Marked(unsaved.Remove(file));

	/// <summary>Reset: every file is gone. Tests start clean through it too.</summary>
	protected internal void MarkClean() {
		bool changed = unsaved.Count > 0;
		unsaved.Clear();
		Marked(changed);
	}

	/// <summary>
	///     Undo or Redo, or a keystroke typing its run back to where it started: the
	///     files are as dirty as the history recorded them there.
	/// </summary>
	protected void PutBack(IReadOnlySet<WordsFile> dirty) {
		if (unsaved.SetEquals(dirty)) {
			return;
		}
		unsaved.Clear();
		unsaved.UnionWith(dirty);
		Marked(true);
	}

	//the marks changed: the title, and whatever shows them
	private void Marked(bool changed) {
		if (!changed) {
			return;
		}
		AffectProperty(nameof(IsDirty));
		AffectProperty(nameof(TitleMarked));
		OnUnsavedChanged();
	}

	/// <summary>The files with unsaved work changed.</summary>
	protected virtual void OnUnsavedChanged() { }

	public abstract void Save();
}
