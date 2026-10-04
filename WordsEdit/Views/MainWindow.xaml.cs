using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WordsEdit.Utils;
using WordsEdit.ViewModels;

namespace WordsEdit;
public partial class MainWindow : Window {
	//the view model is the app's to make and hand over
	public MainWindow() {
		InitializeComponent();
		DataContextChanged += (_, e) => {
			BindShortcuts(e.NewValue as MainWindowViewModel);
			(e.OldValue as MainWindowViewModel)?.FieldFocusRequested -= FocusField;
			(e.NewValue as MainWindowViewModel)?.FieldFocusRequested += FocusField;
		};
		//Ctrl+Z and Ctrl+Y in the editing boxes are the document's; the search box keeps its own (SPEC: Undo)
		DocumentUndo.Route(this,
			() => (DataContext as MainWindowViewModel)?.UndoCommand,
			() => (DataContext as MainWindowViewModel)?.RedoCommand,
			source => source == SearchBox);
	}

	//an undone or redone field edit: its box takes the focus once the panes have
	//caught up with the selection, the caret at the end
	private void FocusField(DocumentField field) {
		TextBox box = field switch {
			DocumentField.DefaultValue => DefaultValueBox,
			DocumentField.KeyContext => KeyContextBox,
			DocumentField.KeyComment => KeyCommentBox,
			DocumentField.EntryValue => EntryValueBox,
			DocumentField.EntryContext => EntryContextBox,
			DocumentField.EntryComment => EntryCommentBox,
			_ => CommentTextBox,
		};
		Dispatcher.BeginInvoke(() => {
			if (box.Focus()) {
				box.CaretIndex = box.Text.Length;
			}
		}, DispatcherPriority.Input);
	}

	//the command table's keys and mouse buttons, bound once (SPEC: Menu and toolbars):
	//a gesture is the one thing a row cannot carry into the window by binding
	private readonly List<InputBinding> shortcuts = [];

	private void BindShortcuts(MainWindowViewModel? vm) {
		foreach (InputBinding binding in shortcuts) {
			InputBindings.Remove(binding);
		}
		shortcuts.Clear();
		foreach (CommandItem item in vm?.Commands.Shortcuts ?? []) {
			shortcuts.Add(new KeyBinding(item.Command, item.Gesture!));
		}
		foreach (CommandItem item in vm?.Commands.MouseShortcuts ?? []) {
			shortcuts.Add(new InputBinding(item.Command, new MouseButtonGesture(item.Button!.Value)));
		}
		foreach (InputBinding binding in shortcuts) {
			InputBindings.Add(binding);
		}
	}

	private bool retiring;

	/// <summary>The app already asked about unsaved changes (a restart): close without asking again.</summary>
	public void Retire() {
		retiring = true;
		Close();
	}

	//TreeView.SelectedItem is read-only: the one gesture WPF will not bind
	private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
		if (DataContext is MainWindowViewModel vm) {
			vm.Tree.SelectedKeyNode = e.NewValue as KeyNode;
		}
	}

	//a right-click selects the row under the mouse, so the context menu acts on it
	private void TreeView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
		DependencyObject? source = e.OriginalSource as DependencyObject;
		while (source is not null and not TreeViewItem) {
			source = source is Visual or Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
		}
		if (source is TreeViewItem item) {
			item.IsSelected = true;
			item.Focus();
		}
	}

	//Ctrl+F (ApplicationCommands.Find): the search box
	private void FocusSearch(object sender, ExecutedRoutedEventArgs e) {
		SearchBox.Focus();
		SearchBox.SelectAll();
	}

	//answered synchronously: the close then proceeds or is cancelled, so there
	//is no Shutdown() to re-raise Closing and prompt again
	private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (!retiring && DataContext is MainWindowViewModel vm) {
			e.Cancel = !vm.TryClose();
		}
	}
}
