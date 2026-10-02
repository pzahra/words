using System.Windows;
using System.Windows.Input;
using WordsEdit.ViewModels;

namespace WordsEdit.Views;

/// <summary>
///     Hosts one <see cref="IDialogViewModel"/> modally. Escape closes it, and
///     so does the view model raising its <c>CloseRequested</c>.
/// </summary>
public partial class DialogWindow : Window {
	private readonly IDialogViewModel dialog;

	public DialogWindow(IDialogViewModel dialog) {
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
