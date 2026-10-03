namespace Sample_Shared.ViewModels;

/// <summary>References: resolved inside the words themselves, so the page needs no state.</summary>
public class ReferencesPageViewModel(SampleConfig config) : PageViewModel(SampleTopics.References, config);
