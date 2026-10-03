namespace Sample_Shared.ViewModels;

/// <summary>
///     One topic's page (SPEC: Topics and their markers): the topic it shows, and
///     whether the reader has yet to see this revision of it. The list shows a dot
///     while <see cref="IsUnseen"/>; <see cref="Visit"/> remembers the revision.
/// </summary>
public abstract class PageViewModel(SampleTopic topic, SampleConfig config) : ViewModelBase {
	public SampleTopic Topic { get; } = topic;
	/// <summary>The key of the topic's caption, bound as <c>{l:Words {Binding CaptionKey}}</c>.</summary>
	public string CaptionKey => Topic.CaptionKey;
	public bool IsUnseen => config.Seen(Topic.Id) < Topic.Revision;

	/// <summary>The reader opened the page: this revision is seen.</summary>
	public void Visit() {
		if (IsUnseen) {
			config.MarkSeen(Topic.Id, Topic.Revision);
			AffectProperty(nameof(IsUnseen));
		}
	}
}
