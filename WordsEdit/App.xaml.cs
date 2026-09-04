using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace WordsEdit;
public partial class App : Application {
	/// <summary>The files named on the command line that exist; the main window loads them.</summary>
	public string[] StartupFiles { get; private set; } = [];

	protected override void OnStartup(StartupEventArgs e) {
		//a greyed button still says what it would do: tooltips show on disabled
		//controls everywhere, set before the first element exists
		ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
		//Startup fires before StartupUri opens the window that reads this
		StartupFiles = [.. e.Args.Where(File.Exists)];
		base.OnStartup(e);
	}
}
