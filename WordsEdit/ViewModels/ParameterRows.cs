using System.ComponentModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     A parameter with the translator's input under it (SPEC: Parameters → The
///     inputs): a box for the five plain types, a list of the prefix's members for an
///     <c>enum</c>. What is typed is the session's; one its type cannot read says
///     why, as the box's tooltip.
/// </summary>
public abstract class ParameterRow(ParametersPane pane) : ViewModelBase {
	protected ParametersPane Pane => pane;

	/// <summary>The parameter as the text names it.</summary>
	public abstract string Name { get; }
	/// <summary>How the text writes it: <c>{0}</c>, <c>{Count}</c>.</summary>
	public string Placeholder => FoundParameters.Placeholder(Name);

	/// <summary>What the input is read as.</summary>
	public WordsParameterType Type {
		get;
		protected set {
			if (ChangeProperty(ref field, value)) {
				AffectProperty(nameof(IsEnum));
			}
		}
	} = WordsParameterType.Str;
	public bool IsEnum => Type.Prefix is not null;
	/// <summary>An <c>enum</c>'s members, the keys directly under its prefix.</summary>
	public IReadOnlyList<string> Members { get; private set => ChangeProperty(ref field, value); } = [];

	/// <summary>What the translator typed or picked: no part of the document, and no part of its undo.</summary>
	public string Input {
		get => pane.InputOf(Name);
		set {
			if (value == Input) {
				return;
			}
			pane.Type(Name, value ?? "");
			AffectProperty(nameof(Input));
			Check();
		}
	}
	/// <summary>Why the input will not read, or why an <c>enum</c> offers nothing; null when it is fine.</summary>
	public string? InputError { get; private set => ChangeProperty(ref field, value); }

	/// <summary>Reads the members and the input again.</summary>
	internal void Check() {
		IReadOnlyList<string> members = Type.Prefix is { } prefix ? pane.MembersUnder(prefix) : [];
		if (!members.SequenceEqual(Members)) {
			Members = members;
		}
		InputError = Type.Prefix is { } empty && members.Count == 0 ? ParametersPane.NothingUnder(Name, empty)
			: Input == "" ? null
			: Unread();
	}

	private string? Unread() {
		try {
			Type.Read(Name, Input);
			return null;
		}
		catch (FormatException ex) {
			return ex.Message;
		}
	}

	protected void Renamed() {
		AffectProperty(nameof(Name));
		AffectProperty(nameof(Placeholder));
		AffectProperty(nameof(Input));
	}
}

/// <summary>
///     A definition, the programmer's message: its placeholder, editable; its type;
///     an <c>enum</c>'s prefix; its description, spell-checked in the default's
///     language and undone as any field is; and a trash. A placeholder the default
///     does not use is dimmed, a hint and no mistake.
/// </summary>
public sealed class DefinitionRow : ParameterRow, IDisposable {
	//the description as it last stood, for the field edit's before; the name and the
	//type as the boxes last showed them, so a pick still waiting on a prefix stays
	private string description;
	private string shownKey;
	private WordsParameterType shownType;
	//the boxes are being set from the document: no pick to act on
	private bool following;

	/// <summary>The type names the combo offers, <c>enum</c> last.</summary>
	public static IReadOnlyList<string> TypeNames { get; } = [.. WordsParameterType.Plain.Select(type => type.Name), "enum"];

	public WordsParameter Parameter { get; }
	public override string Name => Parameter.Key;
	/// <summary>The language the description is spell-checked in: the default's.</summary>
	public string? Language { get; }
	public ITextHistory History { get; }
	public ICommand RemoveCommand { get; }

	/// <summary>The placeholder's name as typed: a rename when the box lets go of it.</summary>
	public string NameText {
		get;
		set {
			if (ChangeProperty(ref field, value) && !following) {
				NameError = value == Parameter.Key ? null : Pane.Rename(this, value);
			}
		}
	} = "";
	public string? NameError { get; private set => ChangeProperty(ref field, value); }

	/// <summary>The type picked: a retype, except <c>enum</c>, which waits for a prefix.</summary>
	public string TypeName {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				AffectProperty(nameof(IsEnumPicked));
				Retype();
			}
		}
	} = "";
	public bool IsEnumPicked => TypeName == "enum";
	/// <summary>An <c>enum</c>'s prefix as typed: a retype when the box lets go of a key's name.</summary>
	public string PrefixText {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				Retype();
			}
		}
	} = "";
	public string? PrefixError { get; private set => ChangeProperty(ref field, value); }

	/// <summary>The default does not use it: dimmed, with a hint.</summary>
	public bool IsUnused {
		get;
		private set {
			if (ChangeProperty(ref field, value)) {
				AffectProperty(nameof(UnusedTip));
			}
		}
	}
	public string? UnusedTip => IsUnused ? Words.Known["parameters.unused"] : null;

	public DefinitionRow(ParametersPane pane, WordsParameter parameter, string? language, ITextHistory history) : base(pane) {
		Parameter = parameter;
		Language = language;
		History = history;
		description = parameter.Description;
		shownKey = parameter.Key;
		shownType = parameter.DataType;
		following = true;
		NameText = parameter.Key;
		TypeName = parameter.DataType.Name;
		PrefixText = parameter.DataType.Prefix ?? "";
		following = false;
		Type = parameter.DataType;
		RemoveCommand = new DelegateCommand(() => pane.Remove(this));
		Parameter.PropertyChanged += OnParameterChanged;
	}

	/// <summary>The definition as the document holds it now.</summary>
	internal void Follow(ParameterSlot slot) {
		IsUnused = !slot.Used;
		following = true;
		if (Parameter.Key != shownKey) {
			shownKey = Parameter.Key;
			NameText = Parameter.Key;
			NameError = null;
		}
		if (Parameter.DataType != shownType) {
			shownType = Parameter.DataType;
			TypeName = shownType.Name;
			PrefixText = shownType.Prefix ?? "";
			PrefixError = null;
		}
		following = false;
		Type = Parameter.DataType;
		Check();
	}

	//the type picked and the prefix typed, as one type once they make one
	private void Retype() {
		if (following) {
			return;
		}
		if (TypeName == shownType.Name && (PrefixText ?? "") == (shownType.Prefix ?? "")) {
			PrefixError = null;
			return;
		}
		WordsParameterType? type = null;
		if (!IsEnumPicked) {
			type = WordsParameterType.Plain.First(plain => plain.Name == TypeName);
			PrefixError = null;
		}
		else if (WordsParser.IsKeyName(PrefixText ?? "")) {
			type = WordsParameterType.Enum(PrefixText!);
			PrefixError = null;
		}
		else {
			PrefixError = PrefixText is null or "" ? null : Words.Known["parameters.prefix"];
		}
		if (type is not null && type != Parameter.DataType) {
			shownType = type;
			Pane.Retype(this, type);
		}
	}

	private void OnParameterChanged(object? sender, PropertyChangedEventArgs e) {
		switch (e.PropertyName) {
			case nameof(WordsParameter.Description):
				string before = description;
				description = Parameter.Description;
				Pane.Described(this, before, description);
				break;
			case nameof(WordsParameter.Key):
				Renamed();
				break;
		}
	}

	public void Dispose() => Parameter.PropertyChanged -= OnParameterChanged;
}

/// <summary>A parameter the default uses and nobody defined: faint, <c>+ {0}</c>, a click making it a definition of the type guessed.</summary>
public sealed class FoundRow : ParameterRow {
	public FoundRow(ParametersPane pane, ParameterSlot slot) : base(pane) {
		Name = slot.Name;
		Type = slot.Type;
		AdoptCommand = new DelegateCommand(() => pane.Adopt(this));
		Check();
	}

	public override string Name { get; }
	public string AdoptTip => Words.Known.Format("parameters.adopt", Placeholder);
	public ICommand AdoptCommand { get; }
}
