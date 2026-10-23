using PatTech.Localization.Wpf;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using WordsEdit.Views;

namespace WordsEdit;
public partial class App : Application {
	//the language the window closes to restart in; null for any other close
	private string? restartIn;

	protected override void OnStartup(StartupEventArgs e) {
		//a greyed button still says what it would do: tooltips show on disabled
		//controls everywhere, set before the first element exists
		ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
		//the formats the editor trades with: the built-ins, and whatever a
		//third-party assembly's entry point registers here, at startup
		WordsFormats formats = WordsFormats.BuiltIn();
		//Wordsmith's own words, before the first {l:Words} resolves: --lang=xx on
		//the command line, else the saved setting, else the OS language; what the
		//parser gripes about goes where every runtime gripe goes
		EditorWords.Load(EditorWords.StartupLanguage(e.Args), MainWindowViewModel.Gripes, formats);
		base.OnStartup(e);

		var viewModel = new MainWindowViewModel(new WpfDialogs(), formats, help: new ConfigHelpMemory(), toolbars: new ConfigToolbarMemory());
		//a help bubble's links are the editor's own: their handler goes ahead of the library's,
		//so it takes them first, and every hyperlink the previews render lands in the second
		Bubble.HandleLinks(viewModel.FollowHelpLink);
		Hyperlink.RegisterGlobalNavigateHandler(viewModel.FollowLink);
		Bubble.Bubbles = viewModel.Help;
		foreach (string file in e.Args.Where(File.Exists)) {
			viewModel.LoadFile(file);
		}
		viewModel.UiLanguageRequested += code => Restart(viewModel, code);
		var window = new MainWindow { DataContext = viewModel };
		//Exit is the menu's: the window closes as by its own button, asking first
		viewModel.ExitRequested += window.Close;
		//the window opens as it last closed; a close the save question cancelled is no close
		EditorConfig.Window?.ApplyTo(window);
		Func<WindowPlace> place = WindowPlace.Track(window);
		window.Closing += (_, e) => {
			if (!e.Cancel) {
				EditorConfig.KeepClosing(place(), restartIn);
			}
		};
		window.Show();
	}

	//{l:Words} resolves when a window loads, so a change of language is a new
	//process: unsaved changes are asked about first, and the same files are opened
	//again. The window has had its question answered and retires without asking
	//twice, keeping its place and the choice in one write, before the new process
	//reads them. The new process is told the language too, in case the config won't
	//keep it
	private void Restart(MainWindowViewModel viewModel, string languageCode) {
		if (!viewModel.TryClose() || Environment.ProcessPath is not { } exe) {
			return;
		}
		restartIn = languageCode;
		var start = new ProcessStartInfo(exe) { UseShellExecute = false };
		start.ArgumentList.Add(EditorWords.LanguageSwitch + languageCode);
		//a file never saved was saved by now, or let go
		foreach (WordsFile file in viewModel.Session.Files) {
			if (file.Path is { } path) {
				start.ArgumentList.Add(path);
			}
		}
		(MainWindow as MainWindow)?.Retire();
		Process.Start(start);
		Shutdown();
	}
}
