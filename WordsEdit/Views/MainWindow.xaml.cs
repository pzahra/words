using PatTech.Localization.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using WordsEdit.Views;

namespace WordsEdit;
public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();
		var mainvm = new MainWindowViewModel(new WpfDialogs());
		DataContext = mainvm;
		//every hyperlink the previews render lands here, whichever pane it is in
		Hyperlink.RegisterGlobalNavigateHandler(mainvm.FollowLink);
		((App)Application.Current).StartupFiles.ForEach(mainvm.LoadFile);
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
		if (DataContext is MainWindowViewModel vm) {
			e.Cancel = !vm.TryClose();
		}
	}
}
