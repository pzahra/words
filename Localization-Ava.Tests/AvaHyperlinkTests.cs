using Avalonia.Headless.XUnit;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>The Avalonia global navigate handler: guards its argument.</summary>
public class AvaHyperlinkTests {

	[AvaloniaFact]
	public void Register_NullHandler_Throws()
		=> Assert.Throws<ArgumentNullException>(() => Hyperlink.RegisterGlobalNavigateHandler(null!));
}
