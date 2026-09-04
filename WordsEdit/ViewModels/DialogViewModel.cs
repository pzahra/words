namespace WordsEdit.ViewModels;

/// <summary>
///     A view model that lives in its own modal window. The window shows it,
///     picks a view for it by type, and closes when it calls <see cref="Close"/>.
/// </summary>
public abstract class DialogViewModel : ViewModelBase {
	/// <summary>The window title.</summary>
	public virtual string Title => "Wordsmith";

	/// <summary>Raised when the view model is done; the hosting window closes.</summary>
	public event Action? CloseRequested;

	/// <summary>Done: ask the hosting window to close.</summary>
	protected void Close() => CloseRequested?.Invoke();
}
