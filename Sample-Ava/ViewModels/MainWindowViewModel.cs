using Avalonia;
using Avalonia.Styling;
using PatTech.Localization;
using System;
using System.Collections.Generic;

namespace Sample_Ava.ViewModels {
	public partial class MainWindowViewModel(IEnumerable<KeyValuePair<string, string>> langs, string lang) : ViewModelBase {
		// starts from the variant actually in effect: "Default" in App.axaml follows the system
		private bool isDarkTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
		/// <summary>
		///     Flips the app between the Light and Dark theme variants. The images demo
		///     shows the point: a `dynres:` image of the theme icon follows the switch, a
		///     `staticres:` one keeps what it resolved at load.
		/// </summary>
		public bool IsDarkTheme {
			get => isDarkTheme;
			set {
				if (ChangeProperty(ref isDarkTheme, value)) {
					if (Application.Current is { } app) {
						app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
					}
					AffectProperty(nameof(ThemeKey));
				}
			}
		}
		/// <summary>The key the live-bindings demo shows, picked by the theme: `{l:Words {Binding ThemeKey}}` looks it up.</summary>
		public string ThemeKey => IsDarkTheme ? "demo.theme-dark" : "demo.theme-light";

		private double unread = 3;
		/// <summary>The `demo.params-positional` argument, bound as the inline's child.</summary>
		public double Unread {
			get => unread;
			set => ChangeProperty(ref unread, value);
		}
		public IEnumerable<KeyValuePair<string, string>> Languages => langs;
		public string SelectedLanguage { get; set; } = lang;

		public record ProfileInfo(string Name, DateTime Since);
		/// <summary>Named arguments for the `demo.params-named` Words, read by property name.</summary>
		public ProfileInfo Profile { get; } = new("Ada Lovelace", new DateTime(2025, 12, 10));

		private string playground = "Try your own: **bold**, *italic*, H~2~O, :sparkles: and [links](https://github.com \"with tooltips\")";
		public string Playground {
			get => playground;
			set => ChangeProperty(ref playground, value);
		}

		private Uri? lastCommand;
		private int commandCount;
		/// <summary>Positional arguments for the `demo.appcmd-report` Words.</summary>
		public object[] AppCommandParams => [commandCount, lastCommand?.ToString() ?? "—"];
		/// <summary>Called by the global hyperlink handler when an `appcmd:` link is clicked.</summary>
		public void TakeAppCommand(Uri uri) {
			lastCommand = uri;
			++commandCount;
			AffectProperty(nameof(AppCommandParams));
			if (uri.AbsolutePath == "changeLang") {
				// switch in place: the builder was kept live at startup (App.Initialize),
				// so this re-flattens for the selected language, and everything bound
				// through {l:Words} and every WordsInline follow without a relaunch
				Words.SwitchLanguage(SelectedLanguage);
			}
		}
	}
}
