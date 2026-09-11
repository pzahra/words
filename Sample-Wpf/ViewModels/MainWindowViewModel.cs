using System.Diagnostics;
using System.Windows;

namespace Sample_Wpf.ViewModels {
	public class MainWindowViewModel(IEnumerable<KeyValuePair<string, string>> langs, string lang, bool isDark) : ViewModelBase {
		private bool isDarkTheme = isDark;
		/// <summary>
		///     Flips the app between Themes/Light.xaml and Themes/Dark.xaml. The images
		///     demo shows the point: a `dynres:` image of the theme icon follows the swap,
		///     a `staticres:` one keeps what it resolved at load.
		/// </summary>
		public bool IsDarkTheme {
			get => isDarkTheme;
			set {
				if (ChangeProperty(ref isDarkTheme, value)) ((App)Application.Current).ApplyTheme(value);
			}
		}

		private double unread = 3;
		public double Unread {
			get => unread;
			set {
				if (ChangeProperty(ref unread, value)) AffectProperty(nameof(UnreadParams));
			}
		}
		/// <summary>Positional arguments for the `demo.params-positional` Words.</summary>
		public object[] UnreadParams => [(int)unread];
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
		/// <summary>Called by the hyperlink class handler when an `appcmd:` link is clicked.</summary>
		public void TakeAppCommand(Uri uri) {
			lastCommand = uri;
			++commandCount;
			AffectProperty(nameof(AppCommandParams));
			if (uri.AbsolutePath == "changeLang" && Environment.ProcessPath is { } exe) {
				// relaunch with the selected language, and the current theme, on the
				// command line (App.OnStartup reads them back before loading the Words)
				Process.Start(new ProcessStartInfo(exe, $"--lang={SelectedLanguage} --theme={(IsDarkTheme ? "dark" : "light")}"));
				Application.Current.Shutdown();
			}
		}
	}
}
