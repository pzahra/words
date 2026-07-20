using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PatTech.Localization;
using PatTech.Localization.Avalonia;
using Sample_Ava.ViewModels;
using Sample_Ava.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sample_Ava {
	public partial class App : Application {
		IEnumerable<KeyValuePair<string, string>> langs = [];
		string lang = "it";
		public override void Initialize() {
			// honor `--lang=xx` from a changeLang relaunch (see MainWindowViewModel.TakeAppCommand)
			foreach (var arg in Environment.GetCommandLineArgs()) {
				if (arg.StartsWith("--lang=")) lang = arg["--lang=".Length..];
			}
			var wb = Words.Builder()
				.LoadResource("avares://Sample-Ava/Assets/sample.ini");
			langs = [.. wb.GetLanguages()];
			Words.Known = wb.ToWords(lang);
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted() {
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

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				desktop.MainWindow = new MainWindow {
					DataContext = viewModel,
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}