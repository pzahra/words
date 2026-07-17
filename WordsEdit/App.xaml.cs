using System.IO;
using System.Windows;

namespace WordsEdit;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
	public string[] StartupFiles = [];

	public App() {
		/*var cl = new CommandLine(0);
		try {
			List<string> files = [];
			if (!cl.Process(p => files.Add(p))) {
				Environment.Exit(2);
				return;
			}
			StartupFiles = [.. files.Where(File.Exists)];
		}
		catch (ArgumentException ex) {
			StartupFiles = [];
			MessageBox.Show(ex.Message);
			return;
		}//*/
	}
}
