namespace Sample_Shared.ViewModels;

/// <summary>
///     Live switching: a key the view model picks by the theme, which the shell tells
///     it, and a count for a converted binding.
/// </summary>
public class LivePageViewModel(SampleConfig config) : PageViewModel(SampleTopics.Live, config) {
	private bool isDarkTheme;
	/// <summary>The window's theme, pushed down by the shell when it changes.</summary>
	public bool IsDarkTheme {
		get => isDarkTheme;
		set {
			if (ChangeProperty(ref isDarkTheme, value)) {
				AffectProperty(nameof(ThemeKey));
			}
		}
	}

	/// <summary>The key the bound-key card shows: <c>{l:Words {Binding ThemeKey}}</c> looks it up.</summary>
	public string ThemeKey => IsDarkTheme ? "live.bound-key.dark" : "live.bound-key.light";

	private double count = 7;
	/// <summary>What the converted card formats.</summary>
	public double Count {
		get => count;
		set => ChangeProperty(ref count, value);
	}
}
