using PatTech.Localization;

namespace Sample_Shared.ViewModels;

/// <summary>
///     Live switching: a key the view model picks by the theme, which the shell tells
///     it, a count for a converted binding, and two strings it composed itself — one
///     kept, one composed again on every switch because the page knows words.
/// </summary>
public class LivePageViewModel : PageViewModel, IKnowWords {
	public LivePageViewModel(SampleConfig config) : base(SampleTopics.Live, config) {
		Kept = Words.Known["live.kept.kept"];
		watched = Words.Known["live.kept.watched"];
		Words.Watch(this);
	}

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

	/// <summary>Composed once, at startup, and kept: it stays in that language.</summary>
	public string Kept { get; }

	private string watched;
	/// <summary>Composed again whenever the language changes (<see cref="Refresh"/>).</summary>
	public string Watched {
		get => watched;
		private set => ChangeProperty(ref watched, value);
	}

	/// <summary>The language changed: compose again. Words calls it on this thread, after <see cref="Words.Watch"/>.</summary>
	public void Refresh() => Watched = Words.Known["live.kept.watched"];
}
