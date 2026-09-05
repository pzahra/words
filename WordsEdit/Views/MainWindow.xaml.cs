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
