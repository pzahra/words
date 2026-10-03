namespace Sample_Shared.ViewModels;

/// <summary>Hyperlinks: what the app has heard through its global link handler.</summary>
public class LinksPageViewModel(SampleConfig config) : PageViewModel(SampleTopics.Links, config) {
	private Uri? lastCommand;
	private int commandCount;

	/// <summary>Positional arguments for the report: how many app commands, and the last.</summary>
	public object[] AppCommandParams => [commandCount, lastCommand?.ToString() ?? "—"];

	/// <summary>An <c>appcmd:</c> link was clicked, on any page.</summary>
	public void TakeAppCommand(Uri uri) {
		lastCommand = uri;
		++commandCount;
		AffectProperty(nameof(AppCommandParams));
	}
}
