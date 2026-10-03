using PatTech.Localization;

namespace Sample_Shared.ViewModels;

/// <summary>
///     The shell (SPEC: The shell): the languages and the one in use, the theme, and
///     the topics' pages with the one open. Every pick is remembered in the config.
///     Each sample's window view model derives from it and supplies the one part that
///     is framework code, <see cref="ApplyTheme"/>.
/// </summary>
public abstract class ShellViewModel : ViewModelBase {
	private readonly SampleConfig config;
	private readonly LinksPageViewModel links;
	private readonly LivePageViewModel live;

	protected ShellViewModel(IReadOnlyList<KeyValuePair<string, string>> languages, string language, bool isDark, SampleConfig config, GripeLog gripes) {
		this.config = config;
		Languages = languages;
		selectedLanguage = language;
		isDarkTheme = isDark;
		links = new LinksPageViewModel(config);
		live = new LivePageViewModel(config) { IsDarkTheme = isDark };
		//in the order of SampleTopics.All
		Pages = [
			new StartPageViewModel(config),
			new MarkdownPageViewModel(config),
			new ReferencesPageViewModel(config),
			links,
			new ImagesPageViewModel(config),
			new ParametersPageViewModel(config, Relocalize),
			new EnumsPageViewModel(config),
			live,
			new DiagnosticsPageViewModel(config, gripes, Relocalize),
		];
		SelectedPage = Pages.FirstOrDefault(page => page.Topic.Id == config.Topic) ?? Pages[0];
	}

	/// <summary>The languages the words label at the top of the file: code and native name.</summary>
	public IReadOnlyList<KeyValuePair<string, string>> Languages { get; }

	private string selectedLanguage;
	/// <summary>
	///     The language the window speaks. The builder was kept live at startup, so a
	///     pick switches every {l:Words}, bound or not, and every WordsInline in place.
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
	///     Flips the app between its light and dark themes; the images page shows a
	///     dynres: icon following it, and the live page a key picked by it.
	/// </summary>
	public bool IsDarkTheme {
		get => isDarkTheme;
		set {
			if (ChangeProperty(ref isDarkTheme, value)) {
				ApplyTheme(value);
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

	/// <summary>Switches the application to its light or dark theme.</summary>
	protected abstract void ApplyTheme(bool dark);

	//a page changed a setting of the live builder (Debug, UseSystemNumbers): digest the
	//language showing again, and the window follows as it does a switch
	private void Relocalize() => Words.SwitchLanguage(selectedLanguage);
}
