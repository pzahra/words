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

		// honor `--lang=xx` and `--theme=dark|light` from a changeLang relaunch
		// (see MainWindowViewModel.TakeAppCommand)
		string lang = "it";
		bool dark = false;
		foreach (var arg in e.Args) {
			if (arg.StartsWith("--lang=")) lang = arg["--lang=".Length..];
			else if (arg.StartsWith("--theme=")) dark = arg["--theme=".Length..] == "dark";
		}
		ApplyTheme(dark);
		// one call loads, installs Words.Known (which syncs the thread cultures) and
		// hands back the language menu; the flag also points FrameworkElement.Language
		// at it, so ordinary WPF bindings (StringFormat and the like) stop defaulting to en-US
		Words.Builder()
			.LoadResource("pack://application:,,,/Sample-Wpf;Component/Assets/sample.ini")
			.Digest(lang, out var languages, includeFrameworkElements: true);
		KeyValuePair<string, string>[] langs = [.. languages];

		var viewModel = new MainWindowViewModel(langs, lang, dark);

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

	/// <summary>
	///     Swaps the theme dictionary merged in App.xaml for Themes/Light.xaml or
	///     Themes/Dark.xaml. Replacing a merged dictionary is what makes WPF re-evaluate
	///     every {DynamicResource} — and every dynres: image — that points into it.
	/// </summary>
	public void ApplyTheme(bool dark) {
		var uri = new Uri($"pack://application:,,,/Sample-Wpf;component/Themes/{(dark ? "Dark" : "Light")}.xaml");
		var dictionaries = Resources.MergedDictionaries;
		var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Themes/") == true);
		if (current is not null) {
			if (current.Source == uri) return;
			dictionaries.Remove(current);
		}
		dictionaries.Add(new ResourceDictionary { Source = uri });
	}
}
