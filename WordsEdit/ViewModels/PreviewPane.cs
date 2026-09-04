namespace WordsEdit.ViewModels;

/// <summary>
///     One markdown preview: the rendered text, the project rules its images
///     and links go by, and what went wrong along the way (SPEC: Markdown
///     previews → Gripes) — a reference that did not resolve, a sample that
///     would not format, an image or link the renderer could not make sense of.
/// </summary>
public sealed class PreviewPane : ViewModelBase {
	private string text = "";
	private ProjectSettings settings = ProjectSettings.Empty;
	//what the view's parser said the last time it re-parsed; kept while the
	//text and rules stand, since it is not asked again until they change
	private List<string> heard = [];

	public string Text { get => text; private set => ChangeProperty(ref text, value); }
	public ProjectSettings Settings { get => settings; private set => ChangeProperty(ref settings, value); }
	public IReadOnlyList<string> Gripes { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(GripeCount)); } = [];
	public int GripeCount => Gripes.Count;

	/// <summary>
	///     Shows <paramref name="text"/> under <paramref name="settings"/>. The view
	///     re-parses while either property changes hands, and what the parser
	///     gripes about is heard through <paramref name="collector"/> and joins
	///     <paramref name="renderGripes"/>, the rendering's own.
	/// </summary>
	internal void Show(string text, ProjectSettings settings, IEnumerable<string> renderGripes, GripeCollector collector) {
		var parsing = new List<string>();
		bool reparsed;
		using (collector.Listen(parsing)) {
			reparsed = ChangeProperty(ref this.settings, settings, nameof(Settings));
			reparsed |= ChangeProperty(ref this.text, text, nameof(Text));
		}
		if (reparsed) {
			heard = parsing;
		}
		Gripes = [.. renderGripes.Concat(heard).Distinct()];
	}

	/// <summary>Nothing to show: no key, or the preview is hidden.</summary>
	internal void Clear() {
		Text = "";
		heard = [];
		Gripes = [];
	}
}
