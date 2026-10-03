using PatTech.Localization;

namespace Sample_Shared.ViewModels;

/// <summary>
///     Format parameters: a count for the positional placeholder, an object for the
///     named ones, and a number and a date for the culture card, whose box flips the
///     live builder's UseSystemNumbers.
/// </summary>
public class ParametersPageViewModel(SampleConfig config, Action relocalize) : PageViewModel(SampleTopics.Parameters, config) {
	private double unread = 3;
	/// <summary>The positional argument, bound as the inline's child.</summary>
	public double Unread {
		get => unread;
		set => ChangeProperty(ref unread, value);
	}

	public record ProfileInfo(string Name, DateTime Since);
	/// <summary>Named arguments, read by property name.</summary>
	public ProfileInfo Profile { get; } = new("Ada Lovelace", new DateTime(2025, 12, 10));

	public record CultureSample(double Amount, DateTime When);
	/// <summary>What the culture card formats: separators and a long date show the culture.</summary>
	public CultureSample Sample { get; } = new(1234567.891, new DateTime(2026, 3, 14));

	/// <summary>The culture this system formats in, named on the card's box.</summary>
	public string SystemCulture => Words.SystemCulture.NativeName;

	private bool useSystemNumbers;
	/// <summary>
	///     The builder's UseSystemNumbers: on, numbers and dates keep the system's format
	///     while the words follow the language. Flipping it digests the language again.
	/// </summary>
	public bool UseSystemNumbers {
		get => useSystemNumbers;
		set {
			if (ChangeProperty(ref useSystemNumbers, value)) {
				Words.Live?.UseSystemNumbers(value);
				relocalize();
			}
		}
	}
}
