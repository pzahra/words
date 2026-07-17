using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
internal class KeyNameViewModel : DataViewModelBase {
	private MainWindowViewModel _MainWindowViewModel;
	public MainWindowViewModel MainWindowViewModel {
		get => _MainWindowViewModel;
		set => ChangeProperty(ref _MainWindowViewModel, value);
	}

	private bool _IsAddKey;
	public bool IsAddKey {
		get => _IsAddKey;
		set => ChangeProperty(ref _IsAddKey, value);
	}

	private bool _IsRenameKey;
	public bool IsRenameKey {
		get => _IsRenameKey;
		set => ChangeProperty(ref _IsRenameKey, value);
	}

	private string? _KeyName;
	public string KeyName {
		get => _KeyName ?? "";
		set => ChangeProperty(ref _KeyName, value);
	}

	public ICommand CancelCommand { get; }
	public ICommand AddKeyCommand { get; }
	public ICommand RenameKeyCommand { get; }

	public KeyNameViewModel(MainWindowViewModel mainWindowViewModel) {
		ArgumentNullException.ThrowIfNull(mainWindowViewModel);
		_IsRenameKey = true;
		if (mainWindowViewModel.SelectedKeyNode is null || mainWindowViewModel.SelectedKeyNode.Label is null) {
			throw new InvalidDataException("Error: Node has no name");
		}
		_KeyName = mainWindowViewModel.SelectedKeyNode.Label;
		_MainWindowViewModel = mainWindowViewModel;
		CancelCommand = new DelegateCommand(DoCancel);
		AddKeyCommand = new DelegateCommand(DoAddKey);
		RenameKeyCommand = new DelegateCommand(DoRenameKey);
	}
	public KeyNameViewModel(MainWindowViewModel mainWindowViewModel, bool addKey) {
		ArgumentNullException.ThrowIfNull(mainWindowViewModel);
		if (addKey) {
			_IsAddKey = true;
		}
		_MainWindowViewModel = mainWindowViewModel;
		CancelCommand = new DelegateCommand(DoCancel);
		AddKeyCommand = new DelegateCommand(DoAddKey);
		RenameKeyCommand = new DelegateCommand(DoRenameKey);
	}

	private void DoAddKey() {
		KeyName = KeyName.ToLower();
		ClearAllErrors();
		if (string.IsNullOrWhiteSpace(KeyName)) {
			SetError("Required", nameof(KeyName));
			return;
		}
		KeyNode? SelectedKeyNode = MainWindowViewModel.SelectedKeyNode ?? throw new InvalidDataException("Selected Key Node is null");
		if (SelectedKeyNode.FullLabel is null || SelectedKeyNode.Label is null) {
			throw new InvalidDataException("Error: Node has no name");
		}
		if (SelectedKeyNode.Children.Any(k => k.Label.ToLower() == KeyName)) {
			SetError("Already Exists", nameof(KeyName));
			return;
		}
		MainWindowViewModel.AddLocalizationKeyNode(KeyName);
		PopupDialog.Close();
	}

	private void DoRenameKey() {
		KeyName = KeyName.ToLower();
		ClearAllErrors();
		if (string.IsNullOrWhiteSpace(KeyName)) {
			SetError("Required", nameof(KeyName));
			return;
		}
		KeyNode? SelectedKeyNode = MainWindowViewModel.SelectedKeyNode;
		if (SelectedKeyNode is null || SelectedKeyNode.FullLabel is null || SelectedKeyNode.Label is null) {
			throw new InvalidDataException("Error: Node has no name");
		}
		if (KeyName == SelectedKeyNode.Label) {
			PopupDialog.Close();
			return;
		}
		KeyNode? parentNode = SelectedKeyNode.GetParentNode(MainWindowViewModel.LocalizationKeyNodes);
		if (parentNode is null) {
			if (MainWindowViewModel.LocalizationKeyNodes.Any(k => k.Label.ToLower() == KeyName)) {
				SetError("Already Exists", nameof(KeyName));
				return;
			}
		}
		else {
			if (parentNode.Children.Any(k => k.Label.ToLower() == KeyName)) {
				SetError("Already Exists", nameof(KeyName));
				return;
			}
		}
		MainWindowViewModel.RenameLocalizationKeyAndNode(KeyName);
		PopupDialog.Close();
	}

	private void DoCancel() {
		PopupDialog.Close();
	}

	protected override bool Validate([CallerMemberName] string? propertyName = null) => throw new NotImplementedException();
}

public static class PopupDialog {
	public static void Push(Control content) => Task.Run(() => DialogHost.Show(content));
	public static void Push(string message) => MessageBox.Show(message);
	public static MessageBoxResult ShowDialog(string message, MessageBoxButton buttons) => MessageBox.Show(message, "Wordsmith Editor", button: buttons);
	public static void Close() => DialogHost.Close(null);

	internal static bool TryFileOpen(string title, string filter, [NotNullWhen(true)] out string[]? fileNames) {
		var dlg = new OpenFileDialog {
			Title = title,
			Filter = filter,
		};
		if (dlg.ShowDialog() is not true) {
			fileNames = [];
			return false;
		}
		fileNames = dlg.FileNames;
		return true;
	}

	internal static bool TryFileSave(string title, string filter, [NotNullWhen(true)] out string? fileName) {
		var dlg = new SaveFileDialog {
			Title = title,
			Filter = filter,
		};
		if (dlg.ShowDialog() is not true) {
			fileName = null;
			return false;
		}
		fileName = dlg.FileName;
		return true;
	}
}