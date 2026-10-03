using Avalonia;
using Avalonia.Styling;
using Sample_Shared;
using Sample_Shared.ViewModels;
using System.Collections.Generic;

namespace Sample_Ava.ViewModels {
	/// <summary>
	///     The shell (SPEC: The shell), Sample-Shared's but for the theme, which moves the
	///     application between the Light and Dark theme variants.
	/// </summary>
	public class MainWindowViewModel(IReadOnlyList<KeyValuePair<string, string>> languages, string language, bool isDark, SampleConfig config, GripeLog gripes, CardSources sources)
		: ShellViewModel(languages, language, isDark, config, gripes, sources) {
		protected override void ApplyTheme(bool dark) {
			if (Application.Current is { } app) {
				app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
			}
		}
	}
}
