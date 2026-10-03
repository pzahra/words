namespace Sample_Shared.ViewModels;

/// <summary>Markdown: the playground's text, rendered as the reader types.</summary>
public class MarkdownPageViewModel(SampleConfig config) : PageViewModel(SampleTopics.Markdown, config) {
	private string playground = "Try your own: **bold**, *italic*, H~2~O, :sparkles: and [links](https://github.com \"with tooltips\")";
	public string Playground {
		get => playground;
		set => ChangeProperty(ref playground, value);
	}
}
