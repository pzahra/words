using System.IO;
using System.Windows;

namespace WordsEdit;
public partial class App : Application {
	/// <summary>The files named on the command line that exist; the main window loads them.</summary>
	public string[] StartupFiles { get; private set; } = [];

	protected override void OnStartup(StartupEventArgs e) {
		//Startup fires before StartupUri opens the window that reads this
		StartupFiles = [.. e.Args.Where(File.Exists)];
		base.OnStartup(e);
	}
}
