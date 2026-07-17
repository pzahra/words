using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PatTech.Localization;
using PatTech.Localization.Ava;
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
			Hyperlink.RegisterGlobalNavigateHandler(uri => {
				if (uri.Scheme is "appcmd") {

				}
				else {
					Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
				}
			});

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				desktop.MainWindow = new MainWindow {
					DataContext = new MainWindowViewModel(),
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}