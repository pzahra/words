using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace WordsEdit.ViewModels;

/// <summary>
///     A view model over a saveable document: a title that stars while dirty,
///     a Save, and one door for dirtiness — <see cref="MarkDirty"/> for an edit
///     a command made, the <c>dirty</c> overload of <c>ChangeProperty</c> for a
///     property that is document state. Save and Reset are the only cleaners
///     (<see cref="MarkClean"/>), but for the history putting back the dirtiness
///     it recorded (<see cref="PutBack"/>). The flag's setter is private, so
///     nothing else can assign it.
/// </summary>
public abstract class ViewModelSaveBase : ViewModelBase {
	public string TitleMarked => IsDirty ? Title + " *" : Title;
	[Localized]
	public string Title { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); } = "";
	public bool IsDirty { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); }

	/// <summary>An edit reached the document.</summary>
	public void MarkDirty() => IsDirty = true;

	/// <summary>Save or Reset: the document is as the disk has it, or gone. Tests start clean through it too.</summary>
	protected internal void MarkClean() => IsDirty = false;

	/// <summary>
	///     Undo or Redo, or a keystroke typing its run back to where it started: the
	///     document is as dirty as the history recorded it there.
	/// </summary>
	protected void PutBack(bool dirty) => IsDirty = dirty;

	public abstract void Save();

	/// <summary>
	///     Sets a property; when <paramref name="dirty"/>, the property is document
	///     state and its change marks the document dirty.
	/// </summary>
	protected bool ChangeProperty<T>(
		[NotNullIfNotNull(nameof(newValue))] ref T field,
		T newValue,
		bool dirty,
		[CallerMemberName] string propertyName = ""
	) {
		if (!ChangeProperty(ref field, newValue, propertyName)) {
			return false;
		}
		if (dirty) {
			MarkDirty();
		}
		return true;
	}
}
