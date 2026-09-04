using PatTech.Localization.Wpf;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using WordsEdit.Views;

namespace WordsEdit;
public partial class App : Application {
	protected override void OnStartup(StartupEventArgs e) {
		//a greyed button still says what it would do: tooltips show on disabled
		//controls everywhere, set before the first element exists
		ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
		//Wordsmith's own words, before the first {l:Words} resolves: --lang=xx on
		//the command line, else the OS language; what the parser gripes about goes
		//where every runtime gripe goes
		EditorWords.Load(EditorWords.LanguageFrom(e.Args), MainWindowViewModel.Gripes);
		base.OnStartup(e);

		var viewModel = new MainWindowViewModel(new WpfDialogs());
		//every hyperlink the previews render lands here, whichever pane it is in
		Hyperlink.RegisterGlobalNavigateHandler(viewModel.FollowLink);
		foreach (string file in e.Args.Where(File.Exists)) {
			viewModel.LoadFile(file);
		}
		new MainWindow { DataContext = viewModel }.Show();
	}
}
