using PatTech.Localization;
using PatTech.Localization.Wpf;
using Sample_Wpf.ViewModels;
using Sample_Wpf.Views;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

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

		// WPF's take on a global navigate handler: one class handler catches
		// every hyperlink click the markdown renders
		EventManager.RegisterClassHandler(typeof(Hyperlink), Hyperlink.RequestNavigateEvent,
			new RequestNavigateEventHandler((_, args) => {
				if (args.Uri.Scheme is "appcmd") {
					// application-command links stay inside the app
					viewModel.TakeAppCommand(args.Uri);
				}
				else {
					Process.Start(new ProcessStartInfo(args.Uri.ToString()) { UseShellExecute = true });
				}
				args.Handled = true;
			}));

		new MainWindow { DataContext = viewModel }.Show();
	}
}
