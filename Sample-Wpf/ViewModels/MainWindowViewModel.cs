using Sample_Shared;
using Sample_Shared.ViewModels;
using System.Windows;

namespace Sample_Wpf.ViewModels {
	/// <summary>
	///     The shell (SPEC: The shell), Sample-Shared's but for the theme, which swaps
	///     Themes/Light.xaml for Themes/Dark.xaml (App.ApplyTheme).
	/// </summary>
	public class MainWindowViewModel(IReadOnlyList<KeyValuePair<string, string>> languages, string language, bool isDark, SampleConfig config, GripeLog gripes)
		: ShellViewModel(languages, language, isDark, config, gripes) {
		protected override void ApplyTheme(bool dark) => ((App)Application.Current).ApplyTheme(dark);
	}
}
