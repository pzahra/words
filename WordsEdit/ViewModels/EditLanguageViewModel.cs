using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

internal class EditLanguageViewModel : DataViewModelBase {
	private LanguageManagerViewModel _LanguageManagerViewModel;
	public LanguageManagerViewModel LanguageManagerViewModel {
		get => _LanguageManagerViewModel;
		set => ChangeProperty(ref _LanguageManagerViewModel, value);
	}

	private string? _LanguageCode;
	public string LanguageCode {
		get => _LanguageCode ?? "";
		set => ChangeProperty(ref _LanguageCode, value);
	}

	private string? _LanguageNativeName;
	public string LanguageNativeName {
		get => _LanguageNativeName ?? "";
		set => ChangeProperty(ref _LanguageNativeName, value);
	}

	private string? _LanguageEnglishName;
	public string LanguageEnglishName {
		get => _LanguageEnglishName ?? "";
		set => ChangeProperty(ref _LanguageEnglishName, value);
	}

	private bool _IsEdit;
	public bool IsEdit {
		get => _IsEdit;
		set => ChangeProperty(ref _IsEdit, value);
	}

	private readonly Regex LanguageCodeRegex = new Regex(
			@"^[a-z]{2}(-[a-zA-Z]+)?$",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);

	public ICommand CancelCommand { get; }
	public ICommand AddLanguageCommand { get; }
	public ICommand EditLanguageCommand { get; }

	public EditLanguageViewModel(LanguageManagerViewModel languageManagerViewModel) {
		ArgumentNullException.ThrowIfNull(languageManagerViewModel);

		_LanguageManagerViewModel = languageManagerViewModel;
		CancelCommand = new DelegateCommand(DoCancel);
		AddLanguageCommand = new DelegateCommand(DoAddLanguage);
		EditLanguageCommand = new DelegateCommand(DoEditLanguage);
	}

	public EditLanguageViewModel(LanguageManagerViewModel languageManagerViewModel, LocalizationLanguage selectedLocalizationLanguage) {
		_LanguageManagerViewModel = languageManagerViewModel;
		_LanguageCode = selectedLocalizationLanguage.Code;
		_LanguageNativeName = selectedLocalizationLanguage.NativeName;
		_LanguageEnglishName = selectedLocalizationLanguage.EnglishName;
		_IsEdit = true;
		CancelCommand = new DelegateCommand(DoCancel);
		AddLanguageCommand = new DelegateCommand(DoAddLanguage);
		EditLanguageCommand = new DelegateCommand(DoEditLanguage);
	}

	private void DoAddLanguage() {
		ClearAllErrors();
		LanguageEnglishName = LanguageEnglishName.Trim();
		LanguageNativeName = LanguageNativeName.Trim();
		LanguageCode = LanguageCode.Trim();
		if (!LanguageCodeRegex.IsMatch(LanguageCode)) {
			SetError("Invalid Language Code", nameof(LanguageCode));
			return;
		}
		if (string.IsNullOrWhiteSpace(LanguageNativeName)) {
			SetError("Invalid Language Name", nameof(LanguageNativeName));
			return;
		}
		if (string.IsNullOrWhiteSpace(LanguageEnglishName)) {
			SetError("Invalid Language Name", nameof(LanguageEnglishName));
			return;
		}
		foreach (LocalizationLanguage language in LanguageManagerViewModel.LocalizationLanguages) {
			if (LanguageCode == language.Code) {
				SetError("Already exists.", nameof(LanguageCode));
				return;
			}
			else if (LanguageNativeName == language.NativeName) {
				SetError("Already exists.", nameof(LanguageNativeName));
				return;
			}
			else if (LanguageEnglishName == language.EnglishName) {
				SetError("Already exists.", nameof(LanguageEnglishName));
				return;
			}
		}
		LocalizationLanguage languageToAdd = new(LanguageCode, LanguageNativeName) { EnglishName = LanguageEnglishName, };
		LanguageManagerViewModel.AddLocalizationLanguage(languageToAdd);
		PopupDialog.Push(new LanguageManagerView() { DataContext = LanguageManagerViewModel });
	}

	private void DoEditLanguage() {
		ClearAllErrors();
		LanguageEnglishName = LanguageEnglishName.Trim();
		LanguageNativeName = LanguageNativeName.Trim();
		LanguageCode = LanguageCode.Trim();
		if (!LanguageCodeRegex.IsMatch(LanguageCode)) {
			SetError("Invalid Language Code", nameof(LanguageCode));
			return;
		}
		if (string.IsNullOrWhiteSpace(LanguageNativeName)) {
			SetError("Invalid Native Language Name", nameof(LanguageNativeName));
			return;
		}
		if (string.IsNullOrWhiteSpace(LanguageEnglishName)) {
			SetError("Invalid English Language Name", nameof(LanguageEnglishName));
			return;
		}
		if (LanguageCode == LanguageManagerViewModel.SelectedLocalizationLanguage.Code && LanguageNativeName == LanguageManagerViewModel.SelectedLocalizationLanguage.NativeName && LanguageEnglishName == LanguageManagerViewModel.SelectedLocalizationLanguage.EnglishName) {
			PopupDialog.Push(new LanguageManagerView() { DataContext = LanguageManagerViewModel });
			return;
		}
		foreach (LocalizationLanguage language in LanguageManagerViewModel.LocalizationLanguages) {
			if (LanguageCode == language.Code || LanguageNativeName == language.NativeName || LanguageEnglishName == language.EnglishName) {
				if (LanguageCode == LanguageManagerViewModel.SelectedLocalizationLanguage.Code || LanguageNativeName == LanguageManagerViewModel.SelectedLocalizationLanguage.NativeName || LanguageEnglishName == LanguageManagerViewModel.SelectedLocalizationLanguage.EnglishName) {
					continue;
				}
				if (LanguageCode == language.Code) {
					SetError("Already exists.", nameof(LanguageCode));
					return;
				}
				else if (LanguageNativeName == language.NativeName) {
					SetError("Already exists.", nameof(LanguageNativeName));
					return;
				}
				else if (LanguageEnglishName == language.EnglishName) {
					SetError("Already exists.", nameof(LanguageEnglishName));
					return;
				}
			}
		}
		LocalizationLanguage languageToEdit = new(LanguageCode, LanguageNativeName) { EnglishName = LanguageEnglishName };
		LanguageManagerViewModel.EditLocalizationLanguage(languageToEdit);
		PopupDialog.Push(new LanguageManagerView() { DataContext = LanguageManagerViewModel });
	}

	private void DoCancel() {
		PopupDialog.Push(new LanguageManagerView() { DataContext = LanguageManagerViewModel });
	}

	protected override bool Validate([AllowNull, CallerMemberName] string propertyName = null) => throw new NotImplementedException();
}
