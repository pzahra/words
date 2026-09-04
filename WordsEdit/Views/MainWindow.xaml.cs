using PatTech.Localization;
using PatTech.Localization.Wpf;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using WordsEdit.Views;
//the flow-document type, not the package's navigate-handler helper
using Hyperlink = System.Windows.Documents.Hyperlink;

namespace WordsEdit;
public partial class MainWindow : Window {
	//the preview parser starts schemeless on purpose: the editor is not the host
	//app, so the stock resolvers (staticres/pack/resx/assets) would resolve
	//against Wordsmith's own resources. Only the file's own scheme→folder
	//mappings are registered, rebuilt per render for the selected file; anything
	//unmapped falls back to alt text, and nothing is fetched remotely.
	private static readonly MarkdownParser markdownParser = new();

	private static void ConfigureImageSchemes(MainWindowViewModel vm) {
		markdownParser.ImageSchemes.Clear();
		foreach (var (scheme, folder) in vm.ImageSchemeFoldersFor(vm.Tree.SelectedKeyNode)) {
			markdownParser.ImageSchemes[scheme] = new FolderImageResolver(folder);
		}
	}

	public MainWindow() {

		InitializeComponent();
		var mainvm = new MainWindowViewModel(new WpfDialogs());
		DataContext = mainvm;
		((App)Application.Current).StartupFiles.ForEach(mainvm.LoadFile);
	}

	private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
		if (DataContext is not MainWindowViewModel vm) {
			return;
		}
		vm.Tree.SelectedKeyNode = (KeyNode)e.NewValue;
	}

	private void DefaultPreview_Checked(object sender, RoutedEventArgs e) {
		if (DataContext is not MainWindowViewModel vm) {
			return;
		}
		if (vm.Tree.SelectedKey is null) {
			return;
		}
		IWordsProvider wordsProvider = vm.GetWordsProvider();
		string defaultValue = Words.RenderKey(wordsProvider, vm.Tree.SelectedKey.BlockKey);
		if (vm.Tree.SelectedKey.Parameters.Count != 0) {
			try {
				defaultValue = FormatSample(vm.Tree.SelectedKey, defaultValue, null);
			}
			catch (Exception ex) {
				vm.Dialogs.Tell(ex.Message);
			}
		}
		ConfigureImageSchemes(vm);
		DefaultValuePreview.Inlines.Clear();
		DefaultValuePreview.Inlines.Add(markdownParser.ToInline(defaultValue));
		DefaultValue.Visibility = Visibility.Collapsed;
		DefaultValuePreview.Visibility = Visibility.Visible;
	}

	private void DefaultPreview_Unchecked(object sender, RoutedEventArgs e) {
		DefaultValue.Visibility = Visibility.Visible;
		DefaultValuePreview.Visibility = Visibility.Collapsed;
	}

	private void LocalizationPreview_Checked(object sender, RoutedEventArgs e) {
		if (DataContext is not MainWindowViewModel vm) {
			return;
		}
		if (vm.Tree.SelectedKey is null) {
			return;
		}
		IWordsProvider wordsProvider = vm.GetWordsProvider(vm.Tree.SelectedLanguage.Code);
		string localizationValue = Words.RenderKey(wordsProvider, vm.Tree.SelectedKey.BlockKey);
		if (vm.Tree.SelectedKey.Parameters.Count != 0) {
			try {
				localizationValue = FormatSample(vm.Tree.SelectedKey, localizationValue, vm.Tree.SelectedLanguage.Code);
			}
			catch (Exception ex) {
				vm.Dialogs.Tell(ex.Message);
			}
		}
		ConfigureImageSchemes(vm);
		LocalizationValuePreview.Inlines.Clear();
		LocalizationValuePreview.Inlines.Add(markdownParser.ToInline(localizationValue));
		LocalizationValue.Visibility = Visibility.Collapsed;
		LocalizationValuePreview.Visibility = Visibility.Visible;
	}

	private void LocalizationPreview_Unchecked(object sender, RoutedEventArgs e) {
		LocalizationValue.Visibility = Visibility.Visible;
		LocalizationValuePreview.Visibility = Visibility.Collapsed;
	}

	//the sample parameters through the same formatting a host app applies, in the
	//language's culture where there is one (the default preview is invariant)
	private static string FormatSample(WordsKey key, string text, string? languageCode) {
		CultureInfo culture = CultureInfo.InvariantCulture;
		if (languageCode is not null) {
			try {
				culture = CultureInfo.GetCultureInfo(languageCode, predefinedOnly: true);
			}
			catch (CultureNotFoundException) {
				//a made-up code renders invariant rather than not at all
			}
		}
		return WordsOperations.FormatSample(key, text, culture);
	}

	private void Preview_Clicked(object sender, MouseButtonEventArgs e) {
		if (sender is not TextBlock textBlock || DataContext is not MainWindowViewModel vm) {
			return;
		}
		var hyperlink = FindClickedHyperlink(textBlock, e);

		if (hyperlink != null) {
			var clickedHyperlink = hyperlink;
			var uriString = clickedHyperlink.CommandParameter as string;

			if (!string.IsNullOrEmpty(uriString)) {
				var completeUri = new Uri(uriString);

				var scheme = completeUri.Scheme;
				if (scheme == "http" || scheme == "https" || scheme == "mailto") {
					FollowLink(vm.Dialogs, completeUri.AbsoluteUri);
				}
				else {
					vm.Dialogs.Tell($"Internal link detected. Destination: {completeUri.AbsoluteUri}");
				}
			}
		}
	}

	private static void FollowLink(IDialogs dialogs, string uri) {
		if (dialogs.Confirm($"Do you want to follow the link?\n\nDestination: {uri}")) {
			Process.Start(new ProcessStartInfo {
				FileName = uri,
				UseShellExecute = true
			});
		}
	}

	private static Hyperlink? FindClickedHyperlink(TextBlock textBlock, MouseButtonEventArgs e) {
		var position = e.GetPosition(textBlock);
		var result = VisualTreeHelper.HitTest(textBlock, position);

		if (result?.VisualHit is TextBlock textBlockHit) {
			var inline = FindHyperlinkInline(textBlockHit);
			if (inline is Hyperlink hyperlink) {
				return hyperlink;
			}
		}

		return null;
	}

	private static Inline? FindHyperlinkInline(TextBlock textBlock) {
		foreach (var inline in textBlock.Inlines) {
			var result = FindHyperlinkInlineRecursive(inline);
			if (result != null) {
				return result;
			}
		}

		return null;
	}

	private static Inline? FindHyperlinkInlineRecursive(Inline inline) {
		if (inline is Hyperlink hyperlink && hyperlink.IsMouseOver) {
			return hyperlink;
		}

		if (inline is Span span) {
			foreach (var childInline in span.Inlines) {
				var result = FindHyperlinkInlineRecursive(childInline);
				if (result != null) {
					return result;
				}
			}
		}

		return null;
	}

	private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
		if (DataContext is not MainWindowViewModel vm || !vm.IsDirty) {
			return;
		}
		//answered here, synchronously: the close then proceeds or is cancelled,
		//so there is no Shutdown() to re-raise Closing and prompt again
		switch (vm.Dialogs.AskToSave("Do you want to save changes to this file before closing?")) {
			case CloseAnswer.Save:
				vm.Save();
				break;
			case CloseAnswer.Cancel:
				e.Cancel = true;
				break;
		}
	}
}
