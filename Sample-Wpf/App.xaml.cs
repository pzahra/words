using PatTech.Localization;
using PatTech.Localization.Wpf;
using System.Windows;

namespace Sample_Wpf; 
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
	public App() {
		Words.Known = WordsBuilder.Create()
			.LoadResource("pack://application:,,,/Sample-Wpf;Component/Assets/words.ini")
			.ToWords("en");
	}

	private void Application_Startup(object sender, StartupEventArgs e) {

	}
}
