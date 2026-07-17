using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

internal class EditLanguageViewModel : DataViewModelBase {
	public LanguageManagerViewModel Parent { get; }
	public string LanguageCode { get; set => _ = ChangeProperty(ref field, value) && Validate(); } = "";
	public string NativeName { get; set => _ = ChangeProperty(ref field, value) && Validate(); } = "";
	public string EnglishName { get; set => _ = ChangeProperty(ref field, value) && Validate(); } = "";

	[MemberNotNullWhen(true, nameof(editing))]
	public bool IsEdit => editing is not null;

	private readonly Regex rxLangCode = new Regex(
			@"^[a-z]{2}(-[a-zA-Z]+)?$",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
	private LanguageEntry? editing;

	public ICommand CancelCommand { get; }
	public ICommand AddLanguageCommand { get; }
	public ICommand EditLanguageCommand { get; }

	public EditLanguageViewModel(LanguageManagerViewModel parent) {
		ArgumentNullException.ThrowIfNull(parent);

		CancelCommand = new DelegateCommand(DoCancel);
		AddLanguageCommand = new DelegateCommand(DoAddLanguage, CanAddLanguage);
		EditLanguageCommand = new DelegateCommand(DoEditLanguage, CanEditLanguage);

		Parent = parent;
	}

	public EditLanguageViewModel(LanguageManagerViewModel parent, LanguageEntry language) : this(parent) {
		editing = language;
		LanguageCode = language.Code;
		NativeName = language.NativeName;
		EnglishName = language.EnglishName;
	}

	private bool CanAddLanguage() => !HasErrors;
	private void DoAddLanguage() {
		if (!Validate()) return;
		editing = new(LanguageCode, NativeName) { EnglishName = EnglishName, };
		Parent.AddLanguage(editing);
		PopupDialog.Push(new LanguageManagerView() { DataContext = Parent });
	}

	private bool CanEditLanguage() => !HasErrors;
	private void DoEditLanguage() {
		if (!Validate()) return;
		if (LanguageCode == editing!.Code && NativeName == editing.NativeName && EnglishName == editing.EnglishName) {
			// Nothing to do.
			PopupDialog.Push(new LanguageManagerView() { DataContext = Parent });
			return;
		}

		editing = new(LanguageCode, NativeName) { EnglishName = EnglishName };
		Parent.EditLanguage(editing);
		PopupDialog.Push(new LanguageManagerView() { DataContext = Parent });
	}

	private void DoCancel() {
		PopupDialog.Push(new LanguageManagerView() { DataContext = Parent });
	}

	protected override bool Validate([CallerMemberName] string? propertyName = "") {
		bool all = string.IsNullOrEmpty(propertyName);

		if (all || propertyName is nameof(LanguageCode)) {
			ClearErrors(nameof(LanguageCode));
			if (!rxLangCode.IsMatch(LanguageCode)) {
				SetError("Invalid Language Code", nameof(LanguageCode));
			}
			else if (Parent.KnownLanguages.Any(known => known != editing && known.Code == LanguageCode)) {
				SetError("Already exists", nameof(LanguageCode));
			}
		}

		if (all || propertyName is nameof(NativeName)) {
			if (string.IsNullOrWhiteSpace(NativeName)) {
				SetError("Value Required", nameof(NativeName));
			}
			else if (Parent.KnownLanguages.Any(known => known != editing && known.NativeName == NativeName)) {
				SetError("Already exists", nameof(NativeName));
			}
		}

		if (all || propertyName is nameof(EnglishName)) {
			if (string.IsNullOrWhiteSpace(EnglishName)) {
				SetError("Value Required", nameof(EnglishName));
			}
			else if (Parent.KnownLanguages.Any(known => known != editing && known.EnglishName == EnglishName)) {
				SetError("Already exists", nameof(EnglishName));
			}
		}

		if (all) return !HasErrors;
		return IsValid(propertyName!);
	}
}
