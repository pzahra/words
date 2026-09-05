using PatTech.Localization.Wpf;
using System.Diagnostics;
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
		//the command line, else the saved setting, else the OS language; what the
		//parser gripes about goes where every runtime gripe goes
		EditorWords.Load(EditorWords.StartupLanguage(e.Args), MainWindowViewModel.Gripes);
		base.OnStartup(e);

		var viewModel = new MainWindowViewModel(new WpfDialogs());
		//every hyperlink the previews render lands here, whichever pane it is in
		Hyperlink.RegisterGlobalNavigateHandler(viewModel.FollowLink);
		foreach (string file in e.Args.Where(File.Exists)) {
			viewModel.LoadFile(file);
		}
		viewModel.UiLanguageRequested += code => Restart(viewModel, code);
		new MainWindow { DataContext = viewModel }.Show();
	}

	//{l:Words} resolves when a window loads, so a change of language is a new
	//process: unsaved changes are asked about first, the choice is saved, and
	//the same files are opened again. The window has had its question answered
	//and retires without asking twice
	private void Restart(MainWindowViewModel viewModel, string languageCode) {
		if (!viewModel.TryClose() || Environment.ProcessPath is not { } exe) {
			return;
		}
		EditorConfig.Language = languageCode;
		var start = new ProcessStartInfo(exe) { UseShellExecute = false };
		foreach (WordsFile file in viewModel.Session.Files) {
			start.ArgumentList.Add(file.Path);
		}
		Process.Start(start);
		(MainWindow as MainWindow)?.Retire();
		Shutdown();
	}
}
