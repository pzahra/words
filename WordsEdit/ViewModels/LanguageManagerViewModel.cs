using System.Collections.ObjectModel;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

public class LanguageManagerViewModel : ViewModelBase {
	public DragDropLanguagesViewModel DragDropLanguagesViewModel { get; set; }
	private MainWindowViewModel _MainWindowViewModel;
	public MainWindowViewModel MainWindowViewModel {
		get => _MainWindowViewModel;
		set => ChangeProperty(ref _MainWindowViewModel, value);
	}
	private ObservableCollection<LocalizationLanguage> _LocalizationLanguages = new();
	public ObservableCollection<LocalizationLanguage> LocalizationLanguages {
		get => _LocalizationLanguages;
		set => ChangeProperty(ref _LocalizationLanguages, value);
	}
	private LocalizationLanguage _SelectedLocalizationLanguage;
	public LocalizationLanguage SelectedLocalizationLanguage {
		get => _SelectedLocalizationLanguage;
		set {
			ChangeProperty(ref _SelectedLocalizationLanguage, value);
		}
	}


	public DelegateCommand RemoveLocalizationLanguageCommand { get; }
	public DelegateCommand AddLocalizationLanguageCommand { get; }
	public DelegateCommand EditLocalizationLanguageCommand { get; }
	public DelegateCommand OkayCommand { get; }
	public LanguageManagerViewModel(MainWindowViewModel mainWindowViewModel) {
		DragDropLanguagesViewModel = new DragDropLanguagesViewModel() { LanguageManager = this };
		_MainWindowViewModel = mainWindowViewModel;
		LocalizationLanguages = mainWindowViewModel.LocalizationLanguages;
		_SelectedLocalizationLanguage = mainWindowViewModel.SelectedLocalizationLanguage;
		OkayCommand = new DelegateCommand(DoOkay);
		RemoveLocalizationLanguageCommand = new DelegateCommand(DoRemoveLocalizationLanguage, CanRemoveLocalizationLanguage);
		AddLocalizationLanguageCommand = new DelegateCommand(DoAddLocalizationLanguage);
		EditLocalizationLanguageCommand = new DelegateCommand(DoEditLocalizationLanguage);
	}

	private bool CanRemoveLocalizationLanguage() {
		return LocalizationLanguages.Count != 1;
	}
	private void DoRemoveLocalizationLanguage() {
		foreach (LocalizationKey localizationKey in MainWindowViewModel.LocalizationKeys) {
			localizationKey.LanguageData.Remove(SelectedLocalizationLanguage.Code);
		}
		if (LocalizationLanguages.Count <= 1) {
			throw new InvalidOperationException("Must have at least two languages to remove one.");
		}
		LocalizationLanguage languageToRemove = SelectedLocalizationLanguage;
		int indexToRemove = LocalizationLanguages.IndexOf(languageToRemove);
		if(indexToRemove == 0) {
			SelectedLocalizationLanguage = LocalizationLanguages[1];
		}
		else {
			SelectedLocalizationLanguage = LocalizationLanguages[indexToRemove - 1];
		}
		if (MainWindowViewModel.SelectedLocalizationLanguage == languageToRemove) {
			MainWindowViewModel.SelectedLocalizationLanguage = SelectedLocalizationLanguage;
		}
		LocalizationLanguages.Remove(languageToRemove);
		MainWindowViewModel.IsDirty = true;
	}

	private void DoAddLocalizationLanguage() {
		PopupDialog.Push(new EditLanguageView() { DataContext = new EditLanguageViewModel(this) });
	}

	public void AddLocalizationLanguage(LocalizationLanguage languageToAdd) {
		foreach (LocalizationKey localizationKey in MainWindowViewModel.LocalizationKeys) {
			localizationKey.LanguageData[languageToAdd.Code] = new LocalizationKeyLanguageData();
		}
		LocalizationLanguages.Add(languageToAdd);
		SelectedLocalizationLanguage = languageToAdd;
		MainWindowViewModel.IsDirty = true;
	}

	private void DoEditLocalizationLanguage() {
		PopupDialog.Push(new EditLanguageView() { DataContext = new EditLanguageViewModel(this, SelectedLocalizationLanguage) });
	}

	public void EditLocalizationLanguage(LocalizationLanguage languageToEdit) {
		foreach (LocalizationKey localizationKey in MainWindowViewModel.LocalizationKeys) {
			localizationKey.LanguageData[languageToEdit.Code] = localizationKey.LanguageData[SelectedLocalizationLanguage.Code];
		}
		LocalizationLanguages.Insert(LocalizationLanguages.IndexOf(SelectedLocalizationLanguage), languageToEdit);
		for (int i = 0; i < LocalizationLanguages.Count; i++) {
			if (LocalizationLanguages[i].Equals(SelectedLocalizationLanguage)) {
				SelectedLocalizationLanguage = languageToEdit;
				if (MainWindowViewModel.SelectedLocalizationLanguage == LocalizationLanguages[i]) {
					MainWindowViewModel.SelectedLocalizationLanguage = SelectedLocalizationLanguage;
				}
				LocalizationLanguages.RemoveAt(i);
				break;
			}
		}
		MainWindowViewModel.IsDirty = true;
	}

	private void DoOkay() {
		_MainWindowViewModel.LocalizationLanguages = _LocalizationLanguages;
		_MainWindowViewModel.SelectedLocalizationLanguage = _SelectedLocalizationLanguage;
		PopupDialog.Close();
	}
}