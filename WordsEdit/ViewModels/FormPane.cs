namespace WordsEdit.ViewModels;

/// <summary>How a row of the form selector stands in the pane's language (SPEC: Plural forms).</summary>
public enum FormRow {
	/// <summary>The plain value, the <c>one</c> form.</summary>
	Plain,
	/// <summary>A category the language counts by.</summary>
	Live,
	/// <summary>A category the language counts by that usually reads like another (<see cref="PluralRules.Optional"/>).</summary>
	Optional,
	/// <summary>A category no whole number reaches in the language: greyed.</summary>
	Unused,
}

/// <summary>
///     One value pane's plural forms (SPEC: Plural forms): the form picked, the
///     plain value being <see cref="Plain"/>, and how each of CLDR's six rows
///     reads in the pane's language — live with the numbers it takes, optional
///     with the form it reads instead, or greyed — and whether it has words.
///     The pane reads the language and the forms through its owner, so it
///     follows the selection without being told.
/// </summary>
public sealed class FormPane(Func<string> language, Func<IReadOnlyDictionary<string, string>?> forms, Func<string> plain) : ViewModelBase {
	/// <summary>The plain value's row: the <c>one</c> form.</summary>
	public const string Plain = MissingWords.Plain;

	/// <summary>The form the pane shows; <see cref="Plain"/> for the plain value.</summary>
	public string Form {
		get;
		set {
			if (ChangeProperty(ref field, value ?? Plain)) {
				AffectProperty(nameof(IsPlain));
				AffectProperty(nameof(Badge));
			}
		}
	} = Plain;
	public bool IsPlain => Form == Plain;
	/// <summary>The language whose rules the rows read.</summary>
	public string Language => language();
	/// <summary>The button's badge: the form picked, or nothing on the plain value.</summary>
	public string? Badge => IsPlain ? null : Form;

	public bool HasWords(string category)
		=> category == Plain ? plain() != "" : forms()?.GetValueOrDefault(category, "") is { Length: > 0 };

	public FormRow Row(string category) {
		var categories = PluralRules.Categories(Language);
		return category == Plain ? FormRow.Plain
			: categories.Count < 2 || !categories.Contains(category) ? FormRow.Unused
			: PluralRules.Optional(Language).ContainsKey(category) ? FormRow.Optional
			: FormRow.Live;
	}

	/// <summary>A greyed row with no words cannot be picked: nothing new is written where no count reads it.</summary>
	public bool CanPick(string category) => Row(category) != FormRow.Unused || HasWords(category);

	/// <summary>Anything beside the plain value to pick: a language with forms, or a form written anyway.</summary>
	public bool AnyToPick => PluralRules.Categories(Language).Count > 1 || PluralRules.Names.Any(category => category != Plain && HasWords(category));

	[return: Localized]
	public string Label(string category) => Row(category) switch {
		FormRow.Plain when PluralRules.Categories(Language).Count < 2 => Words.Known["forms.plain-only"],
		FormRow.Plain => Words.Known.Format("forms.plain", FormNumbers.Describe(Language, category)),
		FormRow.Live => Words.Known.Format("forms.live", category, FormNumbers.Describe(Language, category)),
		FormRow.Optional when PluralRules.Optional(Language)[category] == Plain => Words.Known.Format("forms.optional-plain", category, FormNumbers.Describe(Language, category)),
		FormRow.Optional => Words.Known.Format("forms.optional", category, FormNumbers.Describe(Language, category), PluralRules.Optional(Language)[category]),
		_ => Words.Known.Format("forms.unused", category),
	};

	/// <summary>The rows have words or rules other than they did: the button and its pick follow.</summary>
	public void Refresh() {
		AffectProperty(nameof(Badge));
		AffectProperty(nameof(Language));
	}
}
