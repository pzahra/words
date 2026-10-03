using System.Globalization;

namespace PatTech.Localization.Tests;

/// <summary>
/// Saves the process-wide state a test moves — the dictionary, live mode, the logger,
/// the thread cultures — and puts it back on dispose. The dictionary goes back first,
/// while live mode is still what the test left it, so the restore itself refreshes
/// whatever the test left watching and no stale text survives into the next test.
/// </summary>
internal sealed class WordsGlobals : IDisposable {
	private readonly IWords known = Words.Known;
	private readonly WordsBuilder? live = Words.Live;
	private readonly ITakeException logger = Words.Logger;
	private readonly CultureInfo culture = CultureInfo.CurrentCulture;
	private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
	private readonly CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
	private readonly CultureInfo? defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

	public void Dispose() {
		Words.Known = known;
		Words.Live = live;
		Words.Logger = logger;
		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = uiCulture;
		CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
		CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
	}
}
