using PatTech.Localization;

namespace Sample_Shared;

/// <summary>
///     The samples' words (SPEC: The words): one shared file, embedded here, which
///     each sample follows with its own <c>framework-words.ini</c> of the constants the shared
///     values reference — <c>{$framework}</c> and <c>{$embedded}</c>.
/// </summary>
public static class SampleWords {
	/// <summary>The manifest name of the shared file.</summary>
	public const string ResourceName = "Sample_Shared.sample-words.ini";

	//a slot of the samples' own, read beside each brew's key by U (the Enums page);
	//once per process, however often the words load
	static SampleWords() => Describable.Slot('U', ".unit");

	/// <summary>Loads the words both samples share; chain the sample's <c>framework-words.ini</c> after it.</summary>
	public static WordsBuilder LoadShared(this WordsBuilder builder)
		=> builder.LoadResource(ResourceName, typeof(SampleWords).Assembly);

	/// <summary>The shared file as written, for the cards' How (SPEC: How each card is made).</summary>
	public static CardSource ReadShared()
		=> CardSource.Read("sample-words.ini", typeof(SampleWords).Assembly.GetManifestResourceStream(ResourceName)!);
}
