using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using PatTech.Localization;
using PatTech.Localization.Avalonia;
using Sample_Ava.ViewModels;
using Sample_Ava.Views;
using Sample_Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sample_Ava {
	public partial class App : Application {
		private const string FrameworkWords = "avares://Sample-Ava/Assets/framework.ini";
		private readonly SampleConfig config = SampleConfig.For("Sample-Ava");
		private IReadOnlyList<KeyValuePair<string, string>> langs = [];
		private string lang = "it";

		public override void Initialize() {
			// the language and theme last picked, unless `--lang=xx` or `--theme=dark|light`
			// on the command line choose for this run; Italian, to show translated words,
			// when neither says, and the system's theme ("Default" in App.axaml)
			string? argLang = null, theme = null;
			foreach (var arg in Environment.GetCommandLineArgs()) {
				if (arg.StartsWith("--lang=")) argLang = arg["--lang=".Length..];
				else if (arg.StartsWith("--theme=")) theme = arg["--theme=".Length..];
			}
			lang = argLang ?? config.Language ?? "it";
			theme ??= config.Theme;
			// the shared words, then this sample's two constants they reference. Live()
			// keeps the sources, so the language picker switches in place. (.UseSystemNumbers()
			// before Digest would keep the words but format numbers and dates the way this
			// system does)
			Words.Builder()
				.LoadShared()
				.LoadResource(FrameworkWords)
				.Live()
				.Digest(lang, out var languages);
			langs = [.. languages];
			AvaloniaXamlLoader.Load(this);
			if (theme is "dark") RequestedThemeVariant = ThemeVariant.Dark;
			else if (theme is "light") RequestedThemeVariant = ThemeVariant.Light;
		}

		public override void OnFrameworkInitializationCompleted() {
			// what Words gripes about from here on — a missing key, a loop, a lost picture —
			// is kept for the Diagnostics page to list
			var gripes = new GripeLog();
			Words.Logger = gripes;
			var viewModel = new MainWindowViewModel(langs, lang, ActualThemeVariant == ThemeVariant.Dark, config, gripes, CardSources());

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

		/// <summary>
		///     The real files the cards' How cuts from (SPEC: How each card is made): the pages'
		///     markup, embedded under pages/ by the project file, and the words this sample loads.
		/// </summary>
		public static CardSources CardSources() => new(
			CardSource.ReadAll(typeof(App).Assembly, "pages/"),
			[SampleWords.ReadShared(), CardSource.Read("framework.ini", AssetLoader.Open(new Uri(FrameworkWords)))]);
	}
}
