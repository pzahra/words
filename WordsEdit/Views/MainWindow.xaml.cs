using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using WordsEdit.ViewModels;

namespace WordsEdit;
public partial class MainWindow : Window {
	//the view model is the app's to make and hand over
	public MainWindow() {
		InitializeComponent();
		DataContextChanged += (_, e) => BindShortcuts(e.NewValue as MainWindowViewModel);
	}

	//the command table's keys, bound once (SPEC: Menu and toolbars): a gesture is
	//the one thing a row cannot carry into the window by binding
	private readonly List<KeyBinding> shortcuts = [];

	private void BindShortcuts(MainWindowViewModel? vm) {
		foreach (KeyBinding binding in shortcuts) {
			InputBindings.Remove(binding);
		}
		shortcuts.Clear();
		foreach (CommandItem item in vm?.Commands.Shortcuts ?? []) {
			var binding = new KeyBinding(item.Command, item.Gesture!);
			shortcuts.Add(binding);
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

	//the filter popup closes on a mouse-down anywhere outside it, before that mouse-down
	//reaches what is under it; when that is the popup's own button, the click would
	//reopen it, so the button swallows the one click that closed the popup
	private bool filterClosedUnderItsButton;

	private void FilterPopup_Closed(object sender, EventArgs e)
		=> filterClosedUnderItsButton = new Rect(FilterToggle.RenderSize).Contains(Mouse.GetPosition(FilterToggle));

	private void FilterToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
		if (filterClosedUnderItsButton) {
			filterClosedUnderItsButton = false;
			e.Handled = true;
		}
	}

	//answered synchronously: the close then proceeds or is cancelled, so there
	//is no Shutdown() to re-raise Closing and prompt again
	private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (!retiring && DataContext is MainWindowViewModel vm) {
			e.Cancel = !vm.TryClose();
		}
	}
}
