using System.Collections.ObjectModel;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

public class LanguageManagerViewModel : ViewModelBase {
	public LanguageDragDropHandler LanguageDragDropHandler { get; }
	public MainWindowViewModel Parent { get; }
	public ObservableCollection<LanguageEntry> KnownLanguages => Parent.KnownLanguages;
	public LanguageEntry SelectedLanguage {
		get => Parent.SelectedLanguage;
		set {
			if (value == Parent.SelectedLanguage) return;

			Parent.SelectedLanguage = value;
			AffectProperty(nameof(SelectedLanguage));
		}
	}


	public DelegateCommand RemoveLanguageCommand { get; }
	public DelegateCommand AddLanguageCommand { get; }
	public DelegateCommand EditLanguageCommand { get; }
	public DelegateCommand OkayCommand { get; }
	public LanguageManagerViewModel(MainWindowViewModel parent) {
		LanguageDragDropHandler = new LanguageDragDropHandler() { LanguageManager = this };
		Parent = parent;
		OkayCommand = new DelegateCommand(DoOkay);
		RemoveLanguageCommand = new DelegateCommand(DoRemoveLanguage, CanRemoveLanguage);
		AddLanguageCommand = new DelegateCommand(DoAddLanguage);
		EditLanguageCommand = new DelegateCommand(DoEditLanguage);
	}

	private bool CanRemoveLanguage() => KnownLanguages.Count > 1;
	private void DoRemoveLanguage() {
		if (KnownLanguages.Count <= 1) {
			throw new InvalidOperationException("Must have at least two languages to remove one.");
		}
		foreach (var key in Parent.Keys) {
			key.Entries.Remove(SelectedLanguage.Code);
		}
		var remove = SelectedLanguage;
		int i = KnownLanguages.IndexOf(remove);
		if (i == 0) {
			SelectedLanguage = KnownLanguages[1];
		}
		else {
			SelectedLanguage = KnownLanguages[i - 1];
		}
		KnownLanguages.Remove(remove);
		Parent.RemoveLanguageCode(remove.Code);
		Parent.IsDirty = true;
	}

	private void DoAddLanguage() {
		PopupDialog.Push(new EditLanguageView() { DataContext = new EditLanguageViewModel(this) });
	}

	public void AddLanguage(LanguageEntry lang) {
		foreach (var key in Parent.Keys) {
			key.Entries[lang.Code] = new WordsEntry();
		}
		KnownLanguages.Add(lang);
		Parent.AddLanguageCode(lang.Code);
		SelectedLanguage = lang;
		Parent.IsDirty = true;
	}

	private void DoEditLanguage() {
		PopupDialog.Push(new EditLanguageView() { DataContext = new EditLanguageViewModel(this, SelectedLanguage) });
	}

	public void EditLanguage(LanguageEntry lang) {
		foreach (var key in Parent.Keys) {
			key.Entries[lang.Code] = key.Entries[SelectedLanguage.Code];
			if (lang.Code != SelectedLanguage.Code) {
				//re-coding: the entries moved, don't leave orphans under the old code
				key.Entries.Remove(SelectedLanguage.Code);
			}
		}
		if (lang.Code != SelectedLanguage.Code) {
			Parent.ReplaceLanguageCode(SelectedLanguage.Code, lang.Code);
		}
		KnownLanguages.Insert(KnownLanguages.IndexOf(SelectedLanguage), lang);
		for (int i = 0; i < KnownLanguages.Count; i++) {
			if (KnownLanguages[i].Equals(SelectedLanguage)) {
				SelectedLanguage = lang;
				if (Parent.SelectedLanguage == KnownLanguages[i]) {
					Parent.SelectedLanguage = SelectedLanguage;
				}
				KnownLanguages.RemoveAt(i);
				break;
			}
		}
		Parent.IsDirty = true;
	}

	private void DoOkay() {
		Parent.KnownLanguages = KnownLanguages;
		Parent.SelectedLanguage = SelectedLanguage;
		PopupDialog.Close();
	}
}