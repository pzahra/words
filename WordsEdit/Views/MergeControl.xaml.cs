using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WordsEdit.ViewModels;

namespace WordsEdit.Views;
public partial class MergeControlView : UserControl {
	public MergeControlView() {
		InitializeComponent();
	}

	private void FileListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs args) {
		if (DataContext is not MergeControlViewModel vm) {
			throw new InvalidOperationException("Bad View Model");
		}
		foreach (KeyNode keyNode in args.AddedItems) {
			vm.FilesToMerge.Add(keyNode);
			if (vm.FilesToMerge.Count == 1) {
				if (vm.BaseFile is not null) {
					vm.BaseFile.IsBaseFile = false;
				}
				keyNode.IsBaseFile = true;
				vm.BaseFile = keyNode;

			}
		}
		foreach (KeyNode keyNode in args.RemovedItems) {
			vm.FilesToMerge.Remove(keyNode);
			keyNode.IsBaseFile = false;
			if (vm.BaseFile == keyNode) {
				if (vm.FilesToMerge.Count == 0) {
					vm.BaseFile = null;
				}
				else {
					vm.BaseFile = vm.FilesToMerge[0];
					vm.BaseFile.IsBaseFile = true;
				}
			}
		}
		vm.FilesChanged();
	}

	private void LanguageListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs args) {
		if (DataContext is not MergeControlViewModel vm) {
			throw new InvalidOperationException("Bad View Model");
		}
		ListBox currentListBox = (ListBox)sender;
		KeyNode keyNode = (KeyNode)currentListBox.DataContext;
		ItemsControl? parentItemsControl = FindVisualParent<ItemsControl>(currentListBox);
		foreach (LocalizationLanguage language in args.AddedItems) {
			string languageCode = language.Code;
			if (vm.LanguageCodeFilePairDictionary.ContainsKey(languageCode)) {
				vm.LanguageCodeFilePairDictionary.Remove(languageCode);
			}
			vm.LanguageCodeFilePairDictionary.Add(languageCode, keyNode);
			foreach (ListBox listBox in FindVisualChildren<ListBox>(parentItemsControl)) {
				if (listBox != currentListBox) {
					listBox.SelectedItems.Remove(language);
				}
			}
		}
	}

	private static T? FindVisualParent<T>(DependencyObject dependencyObject) where T : DependencyObject {
		DependencyObject parent = VisualTreeHelper.GetParent(dependencyObject);

		while (parent != null && parent is not T) {
			parent = VisualTreeHelper.GetParent(parent);
		}

		return parent as T;
	}

	private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? dependencyObject) where T : DependencyObject {
		if (dependencyObject != null) {
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dependencyObject); i++) {
				DependencyObject child = VisualTreeHelper.GetChild(dependencyObject, i);

				if (child is T typedChild) {
					yield return typedChild;
				}

				foreach (T childOfChild in FindVisualChildren<T>(child)) {
					yield return childOfChild;
				}
			}
		}
	}

	private void FilesToMergeListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
		if (e.Delta < 0) {
			FilesToMergeScrollViewer.ScrollToVerticalOffset(FilesToMergeScrollViewer.VerticalOffset - (double)e.Delta);
		}
		else {
			FilesToMergeScrollViewer.ScrollToVerticalOffset(FilesToMergeScrollViewer.VerticalOffset - (double)e.Delta);
		}
	}

	private void AvailableFilesListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
		if (e.Delta < 0) {
			AvailableFilesScrollViewer.ScrollToVerticalOffset(AvailableFilesScrollViewer.VerticalOffset - (double)e.Delta);
		}
		else {
			AvailableFilesScrollViewer.ScrollToVerticalOffset(AvailableFilesScrollViewer.VerticalOffset - (double)e.Delta);
		}
	}
}