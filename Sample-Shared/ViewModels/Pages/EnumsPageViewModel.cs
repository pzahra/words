namespace Sample_Shared.ViewModels;

/// <summary>Enums: a brew picked from the menu, and the extras as one [Flags] value.</summary>
public class EnumsPageViewModel(SampleConfig config) : PageViewModel(SampleTopics.Enums, config) {
	/// <summary>Every brew, for the picker.</summary>
	public IReadOnlyList<Brew> Brews { get; } = Enum.GetValues<Brew>();

	private Brew selectedBrew = Brew.Cappuccino;
	public Brew SelectedBrew {
		get => selectedBrew;
		set => ChangeProperty(ref selectedBrew, value);
	}

	private Extras chosenExtras = Extras.Cocoa;
	/// <summary>The extras as one value; the toggles below set and clear its flags.</summary>
	public Extras ChosenExtras {
		get => chosenExtras;
		set {
			if (ChangeProperty(ref chosenExtras, value)) {
				AffectProperty(nameof(Sugar));
				AffectProperty(nameof(Cocoa));
				AffectProperty(nameof(Cinnamon));
				AffectProperty(nameof(Cream));
			}
		}
	}

	public bool Sugar {
		get => chosenExtras.HasFlag(Extras.Sugar);
		set => Flag(Extras.Sugar, value);
	}
	public bool Cocoa {
		get => chosenExtras.HasFlag(Extras.Cocoa);
		set => Flag(Extras.Cocoa, value);
	}
	public bool Cinnamon {
		get => chosenExtras.HasFlag(Extras.Cinnamon);
		set => Flag(Extras.Cinnamon, value);
	}
	public bool Cream {
		get => chosenExtras.HasFlag(Extras.Cream);
		set => Flag(Extras.Cream, value);
	}

	private void Flag(Extras flag, bool on) => ChosenExtras = on ? chosenExtras | flag : chosenExtras & ~flag;
}
