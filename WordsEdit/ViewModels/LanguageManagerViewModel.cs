using System.Collections.ObjectModel;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

public class LanguageManagerViewModel : DialogViewModel {
	public override string Title => "Languages";
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
			return;
		}
		//SPEC (Languages): a removal deletes the entries only after confirmation
		if (!Parent.Dialogs.Confirm($"Remove language '{SelectedLanguage.Code}' and delete its entries from every key?")) {
			return;
		}
		var remove = SelectedLanguage;
		int i = KnownLanguages.IndexOf(remove);
		SelectedLanguage = KnownLanguages[i == 0 ? 1 : i - 1];
		Parent.Session.Languages.Remove(remove.Code);
		Parent.RefreshBadges();
		Parent.IsDirty = true;
	}

	private void DoAddLanguage() {
		Parent.Dialogs.Show(new EditLanguageViewModel(this));
	}

	public void AddLanguage(LanguageEntry lang) {
		//the table backfills every key and every file's declared languages
		Parent.Session.Languages.Add(lang);
		SelectedLanguage = lang;
		Parent.IsDirty = true;
	}

	private void DoEditLanguage() {
		Parent.Dialogs.Show(new EditLanguageViewModel(this, SelectedLanguage));
	}

	public void EditLanguage(LanguageEntry lang) {
		//re-coding shifts the entries; collisions keep the target's value and
		//park the displaced one in context, in copy/paste reach of the translator.
		//Shifted onto a language that already exists, the two entries become one
		SelectedLanguage = Parent.Session.Languages.Rename(SelectedLanguage.Code, lang);
		Parent.RefreshBadges();
		Parent.IsDirty = true;
	}

	private void DoOkay() => Close();
}