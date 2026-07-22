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
		var edited = SelectedLanguage;
		if (lang.Code != edited.Code) {
			//re-coding shifts the entries; collisions keep the target's value and
			//park the displaced one in context, in copy/paste reach of the translator
			WordsOperations.Shift(Parent.Keys, edited.Code, lang.Code);
			Parent.ReplaceLanguageCode(edited.Code, lang.Code);
		}
		LanguageEntry? absorbedInto = KnownLanguages.FirstOrDefault(known => known.Code == lang.Code && known != edited);
		if (absorbedInto is not null) {
			//shifted onto a language that already exists: no new list entry
			SelectedLanguage = absorbedInto;
			KnownLanguages.Remove(edited);
		}
		else {
			KnownLanguages[KnownLanguages.IndexOf(edited)] = lang;
			SelectedLanguage = lang;
		}
		Parent.IsDirty = true;
	}

	private void DoOkay() {
		Parent.KnownLanguages = KnownLanguages;
		Parent.SelectedLanguage = SelectedLanguage;
		PopupDialog.Close();
	}
}