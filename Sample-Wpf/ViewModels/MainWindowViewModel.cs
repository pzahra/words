using PatTech.Localization;
using Sample_Shared;
using Sample_Shared.ViewModels;
using System.Windows;

namespace Sample_Wpf.ViewModels {
	/// <summary>
	///     The shell (SPEC: The shell): the languages and the one in use, the theme, and
	///     the topics' pages with the one open. Every pick is remembered in the config.
	///     The pages are Sample-Shared's; the theme is this sample's own.
	/// </summary>
	public class MainWindowViewModel : ViewModelBase {
		private readonly SampleConfig config;
		private readonly LinksPageViewModel links;
		private readonly LivePageViewModel live;

		public MainWindowViewModel(IReadOnlyList<KeyValuePair<string, string>> languages, string language, bool isDark, SampleConfig config) {
			this.config = config;
			Languages = languages;
			selectedLanguage = language;
			isDarkTheme = isDark;
			links = new LinksPageViewModel(config);
			live = new LivePageViewModel(config) { IsDarkTheme = isDark };
			//in the order of SampleTopics.All, which the twin follows too
			Pages = [new StartPageViewModel(config), new MarkdownPageViewModel(config), links,
				new ImagesPageViewModel(config), new ParametersPageViewModel(config), live];
			SelectedPage = Pages.FirstOrDefault(page => page.Topic.Id == config.Topic) ?? Pages[0];
		}

		/// <summary>The languages the words label at the top of the file: code and native name.</summary>
		public IReadOnlyList<KeyValuePair<string, string>> Languages { get; }

		private string selectedLanguage;
		/// <summary>
		///     The language the window speaks. The builder was kept live at startup
		///     (App.OnStartup), so a pick switches every {l:Words}, bound or not, and every
		///     WordsInline in place.
		/// </summary>
		public string SelectedLanguage {
			get => selectedLanguage;
			set {
				if (value is not null && ChangeProperty(ref selectedLanguage, value)) {
					Words.SwitchLanguage(value);
					config.Language = value;
				}
			}
		}

		private bool isDarkTheme;
		/// <summary>
		///     Flips the app between Themes/Light.xaml and Themes/Dark.xaml; the images page
		///     shows a dynres: icon following it, and the live page a key picked by it.
		/// </summary>
		public bool IsDarkTheme {
			get => isDarkTheme;
			set {
				if (ChangeProperty(ref isDarkTheme, value)) {
					((App)Application.Current).ApplyTheme(value);
					config.Theme = value ? "dark" : "light";
					live.IsDarkTheme = value;
				}
			}
		}

		/// <summary>The topics, in order, each a page.</summary>
		public IReadOnlyList<PageViewModel> Pages { get; }

		private PageViewModel? selectedPage;
		/// <summary>The page open on the right; opening it marks it seen and remembers it.</summary>
		public PageViewModel? SelectedPage {
			get => selectedPage;
			set {
				if (ChangeProperty(ref selectedPage, value) && value is not null) {
					value.Visit();
					config.Topic = value.Topic.Id;
				}
			}
		}

		/// <summary>Called by the global hyperlink handler when an <c>appcmd:</c> link is clicked.</summary>
		public void TakeAppCommand(Uri uri) => links.TakeAppCommand(uri);
	}
}
