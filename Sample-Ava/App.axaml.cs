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
			// one call loads, installs Words.Known (which syncs the thread cultures)
			// and hands back the language menu
			Words.Builder()
				.LoadResource("avares://Sample-Ava/Assets/sample.ini")
				.Digest(lang, out var languages);
			langs = [.. languages];
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted() {
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

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				desktop.MainWindow = new MainWindow {
					DataContext = viewModel,
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}