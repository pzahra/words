using Avalonia;
using Avalonia.Headless;
using PatTech.Localization.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace PatTech.Localization.Tests;

/// <summary>
/// The headless Avalonia application the [AvaloniaFact] tests run in. Skia
/// replaces the headless drawing stub so bitmaps decode and encode for real.
/// </summary>
public class TestAppBuilder {
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
		.UseSkia()
		.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
