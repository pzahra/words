namespace Sample_Shared;

/// <summary>
///     A topic of the tour (SPEC: Topics and their markers): its id, which names its
///     words, and its revision. Raise the revision in the commit that changes the
///     topic's page, and the unseen marker comes back for everyone who saw the old one.
/// </summary>
public sealed record SampleTopic(string Id, int Revision) {
	/// <summary>The key of the topic's caption in the list.</summary>
	public string CaptionKey => $"topic.{Id}";
}

/// <summary>The topics, in the order the list shows them.</summary>
public static class SampleTopics {
	public static SampleTopic Start { get; } = new("start", 2);
	public static SampleTopic Markdown { get; } = new("markdown", 2);
	public static SampleTopic References { get; } = new("references", 1);
	public static SampleTopic Links { get; } = new("links", 1);
	public static SampleTopic Images { get; } = new("images", 2);
	public static SampleTopic Parameters { get; } = new("parameters", 2);
	public static SampleTopic Enums { get; } = new("enums", 1);
	public static SampleTopic Live { get; } = new("live", 2);
	public static SampleTopic Diagnostics { get; } = new("diagnostics", 1);

	public static IReadOnlyList<SampleTopic> All { get; } = [Start, Markdown, References, Links, Images, Parameters, Enums, Live, Diagnostics];
}
