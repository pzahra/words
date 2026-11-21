using System.ComponentModel;

namespace PatTech.Localization.Tests;

/// <summary>
/// A member with a Description and one without; none has a [Words] key, so nothing looks
/// Words.Known up. A file of its own, since the Avalonia tests take it too.
/// </summary>
[Flags]
public enum DescribedFlags {
	None = 0,
	[Description("Two sugars")] Sugar = 1,
	Cream = 2,
}
