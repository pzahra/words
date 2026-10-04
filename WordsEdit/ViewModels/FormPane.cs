using System.Collections.Concurrent;
using System.Globalization;

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
	public const string Plain = "one";

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

	/// <summary>Writes a form's text; an empty one is removed, as a form with no words is none. True when it changed.</summary>
	public static bool Write(Dictionary<string, string> forms, string category, string text) {
		if (text == "") {
			return forms.Remove(category);
		}
		if (forms.GetValueOrDefault(category) == text) {
			return false;
		}
		forms[category] = text;
		return true;
	}

	/// <summary>
	///     True when a key with forms misses words in <paramref name="language"/>:
	///     a category it counts by that is not optional and has no form (SPEC:
	///     Plural forms → Badges). A language with one category misses none.
	/// </summary>
	public static bool Misses(string language, IReadOnlyDictionary<string, string> forms)
		=> PluralRules.Categories(language).Any(category => Requires(language, category) && forms.GetValueOrDefault(category, "") == "");

	/// <summary>A form a plural key wants in <paramref name="language"/>: one it counts by, beside the plain value, and not optional.</summary>
	public static bool Requires(string language, string category) {
		var categories = PluralRules.Categories(language);
		return categories.Count > 1 && category != Plain && categories.Contains(category) && !PluralRules.Optional(language).ContainsKey(category);
	}
}

/// <summary>The counts a category takes in a language, for the selector's rows and the pick that follows.</summary>
public static class FormNumbers {
	//the counts looked at: every one up to here, then exact millions for the categories that start there
	private const int Scan = 1000;
	private const int Shown = 4;
	//the rules never change, and the menus re-read every row on each refresh
	private static readonly ConcurrentDictionary<(string Culture, string Language, string Category), string> described = [];

	/// <summary>The runs of counts <paramref name="category"/> takes: <c>0, 3–10, 103–110…</c>.</summary>
	public static string Describe(string language, string category)
		=> described.GetOrAdd((CultureInfo.CurrentCulture.Name, language, category), at => Runs(at.Language, at.Category));

	private static string Runs(string language, string category) {
		List<(long From, long To)> runs = [];
		for (int n = 0; n <= Scan; n++) {
			if (PluralRules.Select(language, n) != category) {
				continue;
			}
			if (runs.Count > 0 && runs[^1].To == n - 1) {
				runs[^1] = (runs[^1].From, n);
			}
			else {
				runs.Add((n, n));
			}
		}
		if (runs.Count == 0) {
			runs.AddRange(Millions().Where(n => PluralRules.Select(language, n) == category).Select(n => (n, n)));
		}
		var shown = runs.Take(Shown).Select(run => run.To == Scan ? $"{Number(run.From)}…"
			: run.From == run.To ? Number(run.From) : $"{Number(run.From)}–{Number(run.To)}");
		//an open run already trails off, and only the last run can be open
		bool more = runs.Count > Shown || runs.Count > 0 && runs[^1].From >= 1_000_000;
		return string.Join(", ", shown) + (more ? "…" : "");
	}

	/// <summary>A count <paramref name="category"/> takes: the first from one up, else zero, else a million; null for none.</summary>
	public static long? Sample(string language, string category) {
		for (int n = 1; n <= Scan; n++) {
			if (PluralRules.Select(language, n) == category) {
				return n;
			}
		}
		if (PluralRules.Select(language, 0) == category) {
			return 0;
		}
		return Millions().Cast<long?>().FirstOrDefault(n => PluralRules.Select(language, n!.Value) == category);
	}

	private static IEnumerable<long> Millions() => [1_000_000, 2_000_000, 3_000_000];

	private static string Number(long n) => n.ToString("N0", CultureInfo.CurrentCulture);
}
