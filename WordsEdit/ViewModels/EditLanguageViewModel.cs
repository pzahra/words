using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

public class EditLanguageViewModel : DataViewModelBase {
	public override string Title => IsEdit ? Words.Known["language.edit"] : Words.Known["language.add"];
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
		Close();
	}

	private bool CanEditLanguage() => !HasErrors;
	private void DoEditLanguage() {
		if (!Validate()) return;
		if (LanguageCode == editing!.Code && NativeName == editing.NativeName && EnglishName == editing.EnglishName) {
			// Nothing to do.
			Close();
			return;
		}

		editing = new(LanguageCode, NativeName) { EnglishName = EnglishName };
		Parent.EditLanguage(editing);
		Close();
	}

	private void DoCancel() => Close();

	protected override bool Validate([CallerMemberName] string? propertyName = "") {
		bool all = string.IsNullOrEmpty(propertyName);
		if (all) {
			ClearAllErrors();
		}

		if (all || propertyName is nameof(LanguageCode)) {
			ClearErrors(nameof(LanguageCode));
			if (!rxLangCode.IsMatch(LanguageCode)) {
				SetError(Words.Known["language.invalid-code"], nameof(LanguageCode));
			}
			else if (Parent.KnownLanguages.Any(known => known != editing && known.Code == LanguageCode)) {
				SetError(Words.Known["language.exists"], nameof(LanguageCode));
			}
		}

		if (all || propertyName is nameof(NativeName)) {
			ClearErrors(nameof(NativeName));
			if (string.IsNullOrWhiteSpace(NativeName)) {
				SetError(Words.Known["language.required"], nameof(NativeName));
			}
			else if (Parent.KnownLanguages.Any(known => known != editing && known.NativeName == NativeName)) {
				SetError(Words.Known["language.exists"], nameof(NativeName));
			}
		}

		if (all || propertyName is nameof(EnglishName)) {
			ClearErrors(nameof(EnglishName));
			if (string.IsNullOrWhiteSpace(EnglishName)) {
				SetError(Words.Known["language.required"], nameof(EnglishName));
			}
			else if (Parent.KnownLanguages.Any(known => known != editing && known.EnglishName == EnglishName)) {
				SetError(Words.Known["language.exists"], nameof(EnglishName));
			}
		}

		if (all) return !HasErrors;
		return IsValid(propertyName!);
	}
}
