using System.Windows;
using System.Windows.Input;
using WordsEdit.ViewModels;

namespace WordsEdit.Views;

/// <summary>
///     Hosts one <see cref="DialogViewModel"/> modally. Escape closes it, and so
///     does the view model calling its <c>Close()</c>.
/// </summary>
public partial class DialogWindow : Window {
	private readonly DialogViewModel dialog;

	public DialogWindow(DialogViewModel dialog) {
		InitializeComponent();
		this.dialog = dialog;
		DataContext = dialog;
		dialog.CloseRequested += Close;
	}

	protected override void OnClosed(EventArgs e) {
		dialog.CloseRequested -= Close;
		base.OnClosed(e);
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape) {
			e.Handled = true;
			Close();
		}
		base.OnPreviewKeyDown(e);
	}
}
