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
		// one call loads, installs Words.Known (which syncs the thread cultures) and
		// hands back the language menu; the flag also points FrameworkElement.Language
		// at it, so ordinary WPF bindings (StringFormat and the like) stop defaulting to en-US
		Words.Builder()
			.LoadResource("pack://application:,,,/Sample-Wpf;Component/Assets/sample.ini")
			.Digest(lang, out var languages, includeFrameworkElements: true);
		KeyValuePair<string, string>[] langs = [.. languages];

		var viewModel = new MainWindowViewModel(langs, lang);

		Hyperlink.RegisterGlobalNavigateHandler(uri => {
			if (uri.Scheme is "appcmd") {
				// application-command links stay inside the app
				viewModel.TakeAppCommand(uri);
			}
			else if (uri.Scheme is "http" or "https" or "mailto") {
				// only shell-open the schemes we trust; never an arbitrary one
				Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
			}
		});

		new MainWindow { DataContext = viewModel }.Show();
	}
}
