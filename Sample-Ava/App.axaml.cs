using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PatTech.Localization;
using PatTech.Localization.Avalonia;
using Sample_Ava.ViewModels;
using Sample_Ava.Views;
using System.Diagnostics;

namespace Sample_Ava {
	public partial class App : Application {
		public override void Initialize() {
			Words.Known = Words.Builder()
				.LoadResource("avares://Sample-Ava/Assets/sample.ini")
				.ToWords("it");
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted() {
			var viewModel = new MainWindowViewModel();

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