namespace Sample_Shared.ViewModels;

/// <summary>Format parameters: a count for the positional placeholder, an object for the named ones.</summary>
public class ParametersPageViewModel(SampleConfig config) : PageViewModel(SampleTopics.Parameters, config) {
	private double unread = 3;
	/// <summary>The positional argument, bound as the inline's child.</summary>
	public double Unread {
		get => unread;
		set => ChangeProperty(ref unread, value);
	}

	public record ProfileInfo(string Name, DateTime Since);
	/// <summary>Named arguments, read by property name.</summary>
	public ProfileInfo Profile { get; } = new("Ada Lovelace", new DateTime(2025, 12, 10));
}
