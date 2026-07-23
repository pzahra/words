using PatTech.Localization;
using PatTech.Localization.Wpf;
using Sample_Wpf.ViewModels;
using Sample_Wpf.Views;
using System.Diagnostics;
using System.Windows;

namespace Sample_Wpf;

public partial class App : Application {
	protected override void OnStartup(StartupEventArgs e) {
		base.OnStartup(e);

		// honor `--lang=xx` from a changeLang relaunch (see MainWindowViewModel.TakeAppCommand)
		string lang = "it";
		foreach (var arg in e.Args) {
			if (arg.StartsWith("--lang=")) lang = arg["--lang=".Length..];
		}
		var wb = Words.Builder()
			.LoadResource("pack://application:,,,/Sample-Wpf;Component/Assets/sample.ini");
		KeyValuePair<string, string>[] langs = [.. wb.GetLanguages()];
		Words.Known = wb.ToWords(lang);

		var viewModel = new MainWindowViewModel(langs, lang);

		Hyperlink.RegisterGlobalNavigateHandler(uri => {
			if (uri.Scheme is "appcmd") {
				// application-command links stay inside the app
				viewModel.TakeAppCommand(uri);
			}
			else {
				Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
			}
		});

		new MainWindow { DataContext = viewModel }.Show();
	}
}
