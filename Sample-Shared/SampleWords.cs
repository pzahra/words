using PatTech.Localization;

namespace Sample_Shared;

/// <summary>
///     The samples' words (SPEC: The words): one shared file, embedded here, which
///     each sample follows with its own <c>framework.ini</c> of the constants the shared
///     values reference — <c>{$framework}</c> and <c>{$embedded}</c>.
/// </summary>
public static class SampleWords {
	/// <summary>The manifest name of the shared file.</summary>
	public const string ResourceName = "Sample_Shared.sample.ini";

	/// <summary>Loads the words both samples share; chain the sample's <c>framework.ini</c> after it.</summary>
	public static WordsBuilder LoadShared(this WordsBuilder builder)
		=> builder.LoadResource(ResourceName, typeof(SampleWords).Assembly);
}
