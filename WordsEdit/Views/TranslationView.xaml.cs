using System.Windows;
using System.Windows.Controls;
using WordsEdit.ViewModels;

namespace WordsEdit.Views;

public partial class TranslationView : UserControl {
	public TranslationView() {
		InitializeComponent();
		DataContextChanged += (_, e) => {
			if (e.OldValue is TranslationViewModel old) {
				old.AnswerFaulted -= ShowFault;
			}
			if (e.NewValue is TranslationViewModel now) {
				now.AnswerFaulted += ShowFault;
			}
		};
		Loaded += (_, _) => {
			if (Window.GetWindow(this) is { } window) {
				window.ContentRendered += Resizable;
			}
		};
	}

	//the window sized to the first page once, then the window's own size: the view fills it, so
	//dragging its edge grows the answer box, the grid and the grid's columns
	private void Resizable(object? sender, EventArgs e) {
		var window = (Window)sender!;
		window.ContentRendered -= Resizable;
		window.SizeToContent = SizeToContent.Manual;
		Root.MinWidth = Root.ActualWidth;
		Root.Width = double.NaN;
	}

	//the answer from where it stops reading as JSON to the end of that line, highlighted and in view
	private void ShowFault(int at) {
		string text = AnswerBox.Text;
		if (at > text.Length) {
			return;
		}
		int end = text.IndexOfAny(['\r', '\n'], at);
		end = end < 0 ? text.Length : end;
		AnswerBox.Focus();
		AnswerBox.Select(at, Math.Max(end - at, 1));
		AnswerBox.ScrollToLine(AnswerBox.GetLineIndexFromCharacterIndex(at));
	}
}
