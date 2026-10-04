using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     One language of the manager's working copy (SPEC: Languages): the code
///     and the two names, edited live in the pane and checked against the other
///     rows, whether the default is written in it, and the trash that drops the
///     row. <see cref="Origin"/> is the session's entry the row came from; null
///     for one added this sitting.
///     <see cref="DataViewModelBase.HasErrors"/> is the whole truth, for OK; the
///     red text only covers fields that have been typed in, so a fresh row is
///     not scolded for being blank.
/// </summary>
public sealed class LanguageRow : DataViewModelBase {
	private static readonly Regex rxCode = new(@"^[a-z]{2}(-[a-zA-Z]+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
	private readonly LanguageManagerViewModel owner;
	private readonly HashSet<string> touched = [];

	public LanguageEntry? Origin { get; }
	public string Code { get; set => Edit(ref field, value); } = "";
	public string NativeName { get; set => Edit(ref field, value); } = "";
	public string EnglishName { get; set => Edit(ref field, value); } = "";
	/// <summary>Whether the default is written in this row's language; ticking it unticks the row that was.</summary>
	public bool IsDefault {
		get => owner.DefaultRow == this;
		set => owner.DefaultRow = value ? this : IsDefault ? null : owner.DefaultRow;
	}
	/// <summary>Whether applying the row would change the session: new, or no longer as its origin reads.</summary>
	public bool IsChanged => Origin is null || Code != Origin.Code || NativeName != Origin.NativeName || EnglishName != Origin.EnglishName;
	public ICommand RemoveCommand { get; }

	public LanguageRow(LanguageManagerViewModel owner, LanguageEntry? origin) {
		this.owner = owner;
		Origin = origin;
		if (origin is not null) {
			//the session's rows have nothing to hide
			touched.UnionWith([nameof(Code), nameof(NativeName), nameof(EnglishName)]);
		}
		Code = origin?.Code ?? "";
		NativeName = origin?.NativeName ?? "";
		EnglishName = origin?.EnglishName ?? "";
		RemoveCommand = new DelegateCommand(() => owner.Remove(this), () => owner.Rows.Count > 1);
	}

	//a field was typed in: it is fair game for the red text, and every row rechecks
	private void Edit(ref string field, string value, [CallerMemberName] string property = "") {
		if (ChangeProperty(ref field, value, property)) {
			touched.Add(property);
			Validate(property);
		}
	}

	/// <summary>The owner's default row moved to or from this one.</summary>
	internal void DefaultMoved() => AffectProperty(nameof(IsDefault));

	/// <summary>The row as a session entry.</summary>
	public LanguageEntry ToEntry() => new(Code, NativeName) { EnglishName = EnglishName };

	//the red text keeps to the fields that have been typed in
	public override IEnumerable GetErrors(string? propertyName)
		=> string.IsNullOrEmpty(propertyName)
			? touched.SelectMany(property => base.GetErrors(property).Cast<object>())
			: touched.Contains(propertyName) ? base.GetErrors(propertyName) : Array.Empty<object>();

	protected override void RaiseErrorsChanged(string propertyName) {
		if (touched.Contains(propertyName)) {
			base.RaiseErrorsChanged(propertyName);
		}
	}

	/// <summary>Every row rechecks, since one edit can make or unmake a duplicate elsewhere; whether this row passes.</summary>
	protected override bool Validate([CallerMemberName] string propertyName = "") {
		owner.Revalidate();
		return string.IsNullOrEmpty(propertyName) ? !HasErrors : IsValid(propertyName);
	}

	/// <summary>Checks the row against the rules and the other rows; the owner runs it for every row.</summary>
	internal void Check(IReadOnlyList<LanguageRow> others) {
		ClearAllErrors();
		if (!rxCode.IsMatch(Code)) {
			SetError(Words.Known["language.invalid-code"], nameof(Code));
		}
		else if (others.Any(other => other.Code == Code)) {
			SetError(Words.Known["language.exists"], nameof(Code));
		}
		CheckName(NativeName, nameof(NativeName), others.Select(other => other.NativeName));
		CheckName(EnglishName, nameof(EnglishName), others.Select(other => other.EnglishName));
		AffectProperty(nameof(HasErrors));
	}

	private void CheckName(string value, string property, IEnumerable<string> taken) {
		if (string.IsNullOrWhiteSpace(value)) {
			SetError(Words.Known["language.required"], property);
		}
		else if (taken.Contains(value)) {
			SetError(Words.Known["language.exists"], property);
		}
	}
}
