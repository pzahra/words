namespace WordsEdit.ViewModels;

/// <summary>
///     What a view model needs to live in its own modal window: a title, and a
///     way to say it is done. <see cref="DialogViewModel"/> is the plain one; a
///     view model that needs another base (<see cref="KeyNameViewModel"/>)
///     implements this itself.
/// </summary>
public interface IDialogViewModel {
	/// <summary>The window title.</summary>
	[Localized]
	string Title { get; }

	/// <summary>Raised when the view model is done; the hosting window closes.</summary>
	event Action? CloseRequested;
}
