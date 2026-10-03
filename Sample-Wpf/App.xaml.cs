using PatTech.Localization;
using PatTech.Localization.Wpf;
using Sample_Shared;
using Sample_Wpf.ViewModels;
using Sample_Wpf.Views;
using System.Diagnostics;
using System.Windows;

namespace Sample_Wpf;

public partial class App : Application {
	protected override void OnStartup(StartupEventArgs e) {
		base.OnStartup(e);

		// the language and theme last picked, unless `--lang=xx` or `--theme=dark|light`
		// on the command line choose for this run; Italian, to show translated words,
		// when neither says
		var config = SampleConfig.For("Sample-Wpf");
		string? lang = null, theme = null;
		foreach (var arg in e.Args) {
			if (arg.StartsWith("--lang=")) lang = arg["--lang=".Length..];
			else if (arg.StartsWith("--theme=")) theme = arg["--theme=".Length..];
		}
		lang ??= config.Language ?? "it";
		bool dark = (theme ?? config.Theme) == "dark";
		ApplyTheme(dark);
		// what Words gripes about from here on — a missing key, a loop, a lost picture —
		// is kept for the Diagnostics page to list
		var gripes = new GripeLog();
		Words.Logger = gripes;
		// the shared words, then this sample's two constants they reference. Live()
		// keeps the sources, so the language picker switches in place. The flag also
		// points FrameworkElement.Language at it, so ordinary WPF bindings (StringFormat
		// and the like) stop defaulting to en-US — once, at startup: a live switch moves
		// the thread cultures, not that default. (.UseSystemNumbers() before Digest would
		// keep the words but format numbers and dates the way this system does)
		Words.Builder()
			.LoadShared()
			.LoadResource("pack://application:,,,/Sample-Wpf;Component/Assets/framework.ini")
			.Live()
			.Digest(lang, out var languages, includeFrameworkElements: true);

		var viewModel = new MainWindowViewModel([.. languages], lang, dark, config, gripes);

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
