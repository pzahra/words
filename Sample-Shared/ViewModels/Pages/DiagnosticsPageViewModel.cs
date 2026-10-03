using PatTech.Localization;
using System.Collections.ObjectModel;

namespace Sample_Shared.ViewModels;

/// <summary>
///     Diagnostics: the live builder's fallback brands, switched in place, and what
///     Words has griped about so far.
/// </summary>
public class DiagnosticsPageViewModel(SampleConfig config, GripeLog gripes, Action relocalize) : PageViewModel(SampleTopics.Diagnostics, config) {
	/// <summary>What <see cref="Words.Logger"/> heard, newest first.</summary>
	public ObservableCollection<string> Gripes => gripes.Entries;

	private bool showFallbacks;
	/// <summary>
	///     The builder's Debug: on, every value that fell back from the language showing
	///     is branded. Flipping it digests the language again, so the whole window shows it.
	/// </summary>
	public bool ShowFallbacks {
		get => showFallbacks;
		set {
			if (ChangeProperty(ref showFallbacks, value)) {
				Words.Live?.Debug(value);
				relocalize();
			}
		}
	}
}
