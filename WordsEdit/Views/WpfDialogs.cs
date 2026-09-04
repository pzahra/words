using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using WordsEdit.Utils;
using WordsEdit.ViewModels;

namespace WordsEdit.Views;

/// <summary>
///     <see cref="IDialogs"/> for the running app: each dialog is a
///     <see cref="DialogWindow"/> owned by the main window and shown modally, so
///     dialogs nest freely (the language manager can open the language editor);
///     questions and notices are message boxes; files go through the shell's.
/// </summary>
public sealed class WpfDialogs : IDialogs {
	private static string Caption => Words.Known["app.name"];

	//the main window once it is up; a dialog raised during startup has no owner
	private static Window? MainWindow
		=> Application.Current?.MainWindow is { IsLoaded: true } main ? main : null;

	public void Show(DialogViewModel dialog)
		=> new DialogWindow(dialog) { Owner = MainWindow }.ShowDialog();

	public bool Confirm(string message)
		=> Box(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

	public CloseAnswer AskToSave(string message)
		=> Box(message, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch {
			MessageBoxResult.Yes => CloseAnswer.Save,
			MessageBoxResult.No => CloseAnswer.Discard,
			_ => CloseAnswer.Cancel,
		};

	public void Tell(string message)
		=> Box(message, MessageBoxButton.OK, MessageBoxImage.Information);

	private static MessageBoxResult Box(string message, MessageBoxButton buttons, MessageBoxImage image)
		=> MainWindow is { } owner
			? MessageBox.Show(owner, message, Caption, buttons, image)
			: MessageBox.Show(message, Caption, buttons, image);

	public bool TryOpenFiles([Localized] string title, [Localized] string filter, [NotNullWhen(true)] out string[]? fileNames) {
		var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
		if (Run(dialog) is not true) {
			fileNames = null;
			return false;
		}
		fileNames = dialog.FileNames;
		return true;
	}

	public bool TrySaveFile([Localized] string title, [Localized] string filter, [NotNullWhen(true)] out string? fileName) {
		var dialog = new SaveFileDialog { Title = title, Filter = filter };
		if (Run(dialog) is not true) {
			fileName = null;
			return false;
		}
		fileName = dialog.FileName;
		return true;
	}

	private static bool? Run(CommonDialog dialog)
		=> MainWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
}
