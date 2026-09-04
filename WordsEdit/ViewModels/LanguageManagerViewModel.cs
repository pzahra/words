using System.Collections.ObjectModel;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The language table (SPEC: Languages): add, edit, remove, reorder. Edits
///     land in the session as they are made; the highlighted row is the
///     dialog's own and becomes the tree's language on OK, so browsing the list
///     does not re-badge the tree behind the dialog.
/// </summary>
public class LanguageManagerViewModel : DialogViewModel {
	public override string Title => Words.Known["languages.title"];
	public LanguageDrag LanguageDrag { get; }
	public MainWindowViewModel Parent { get; }
	public ObservableCollection<LanguageEntry> KnownLanguages => Parent.Tree.KnownLanguages;
	public LanguageEntry SelectedLanguage { get; set => ChangeProperty(ref field, value); }

	public DelegateCommand RemoveLanguageCommand { get; }
	public DelegateCommand AddLanguageCommand { get; }
	public DelegateCommand EditLanguageCommand { get; }
	public DelegateCommand OkayCommand { get; }
	public LanguageManagerViewModel(MainWindowViewModel parent) {
		LanguageDrag = new LanguageDrag { Vm = this };
		Parent = parent;
		SelectedLanguage = parent.Tree.SelectedLanguage;
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
		if (!Parent.Dialogs.Confirm(Words.Known.Format("ask.remove-language", SelectedLanguage.Code))) {
			return;
		}
		var remove = SelectedLanguage;
		int i = KnownLanguages.IndexOf(remove);
		SelectedLanguage = KnownLanguages[i == 0 ? 1 : i - 1];
		Parent.Session.Languages.Remove(remove.Code);
		TreeFollows();
	}

	private void DoAddLanguage() {
		Parent.Dialogs.Show(new EditLanguageViewModel(this));
	}

	public void AddLanguage(LanguageEntry lang) {
		//the table backfills every key and every file's declared languages
		Parent.Session.Languages.Add(lang);
		SelectedLanguage = lang;
		TreeFollows();
	}

	private void DoEditLanguage() {
		Parent.Dialogs.Show(new EditLanguageViewModel(this, SelectedLanguage));
	}

	public void EditLanguage(LanguageEntry lang) {
		//re-coding shifts the entries; collisions keep the target's value and
		//park the displaced one in context, in copy/paste reach of the translator.
		//Shifted onto a language that already exists, the two entries become one
		SelectedLanguage = Parent.Session.Languages.Rename(SelectedLanguage.Code, lang);
		TreeFollows();
	}

	//the table changed under the tree: its language may be gone or replaced, and
	//its badges and dropdown read the table
	private void TreeFollows() {
		Parent.Tree.FollowLanguage();
		Parent.Tree.RefreshBadges();
		Parent.MarkDirty();
	}

	private void DoOkay() {
		Parent.Tree.SelectedLanguage = SelectedLanguage;
		Close();
	}
}
