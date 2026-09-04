namespace WordsEdit.ViewModels;

/// <summary>A view model over a document: a title that stars while dirty, and a Save.</summary>
public abstract class ViewModelSaveBase : ViewModelBase {
	public string TitleMarked => IsDirty ? Title + " *" : Title;
	public string Title { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); } = "";
	public bool IsDirty { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); }

	public abstract void Save();
}
