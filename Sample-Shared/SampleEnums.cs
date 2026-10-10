using PatTech.Localization;
using System.ComponentModel;

namespace Sample_Shared;

/// <summary>
///     The coffee on the Enums page. Each member names its words with
///     <see cref="WordsAttribute"/>, and Describe finds <c>.tooltip</c>, <c>.sub</c> and
///     <c>.desc</c> beside them, and <c>.unit</c>, the samples' own slot (<see cref="SampleWords"/>);
///     Americano has only a Description, to show the fallback.
/// </summary>
public enum Brew {
	[Words("enums.brew.espresso")] Espresso,
	[Words("enums.brew.cappuccino")] Cappuccino,
	[Words("enums.brew.latte")] Latte,
	[Words("enums.brew.affogato")] Affogato,
	[Description("Americano")] Americano,
}

/// <summary>What goes on top, flag by flag; the zero member describes an order with none.</summary>
[Flags]
public enum Extras {
	[Words("enums.extras.none")] None = 0,
	[Words("enums.extras.sugar")] Sugar = 1,
	[Words("enums.extras.cocoa")] Cocoa = 2,
	[Words("enums.extras.cinnamon")] Cinnamon = 4,
	[Words("enums.extras.cream")] Cream = 8,
}
