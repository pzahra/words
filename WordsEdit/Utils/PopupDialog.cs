using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;

namespace WordsEdit.ViewModels;

public static class PopupDialog {
	// DialogHost.Show must run on the UI thread — that is where the loaded
	// DialogHost lives. Dispatching (not Task.Run) is the difference between the
	// dialog opening and the call quietly throwing on a threadpool thread.
	public static void Push(Control content)
		=> Application.Current.Dispatcher.InvokeAsync(() => DialogHost.Show(content));
	public static void Push(string message) => MessageBox.Show(message);
	public static MessageBoxResult ShowDialog(string message, MessageBoxButton buttons) => MessageBox.Show(message, "Wordsmith Editor", button: buttons);
	public static void Close() => DialogHost.Close(null);

	internal static bool TryFileOpen(string title, string filter, [NotNullWhen(true)] out string[]? fileNames) {
		var dlg = new OpenFileDialog {
			Title = title,
			Filter = filter,
		};
		if (dlg.ShowDialog() is not true) {
			fileNames = [];
			return false;
		}
		fileNames = dlg.FileNames;
		return true;
	}

	internal static bool TryFileSave(string title, string filter, [NotNullWhen(true)] out string? fileName) {
		var dlg = new SaveFileDialog {
			Title = title,
			Filter = filter,
		};
		if (dlg.ShowDialog() is not true) {
			fileName = null;
			return false;
		}
		fileName = dlg.FileName;
		return true;
	}
}