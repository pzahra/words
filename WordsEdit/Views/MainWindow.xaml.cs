using PatTech.Localization.Wpf;
using System.Windows;
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

	private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (DataContext is not MainWindowViewModel vm || !vm.IsDirty) {
			return;
		}
		//answered here, synchronously: the close then proceeds or is cancelled,
		//so there is no Shutdown() to re-raise Closing and prompt again
		switch (vm.Dialogs.AskToSave("Do you want to save changes to this file before closing?")) {
			case CloseAnswer.Save:
				vm.Save();
				break;
			case CloseAnswer.Cancel:
				e.Cancel = true;
				break;
		}
	}
}
