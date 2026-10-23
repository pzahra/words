using System.Globalization;
using System.IO;
using System.Windows.Input;
using PatTech.Localization.Authoring.Codecs;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>One choice of a dropdown: what the schema writes, and how the dialog names it.</summary>
public sealed record SheetChoice(string Value, string Label);

/// <summary>A cell of the preview: its text on one line, and as the sheet has it.</summary>
public sealed record SheetCell(string Text, string Full);

/// <summary>A row of the preview, the header row marked.</summary>
public sealed record SheetRow(bool IsHeader, IReadOnlyList<SheetCell> Cells);

/// <summary>
///     One column of the sheet as the dialog shows it: its number, its role as
///     <c>words.ini</c> spells it (none, read past), and what that role is, in
///     words. A column read past keeps the name its schema or header gave it.
/// </summary>
public sealed class SheetColumn(SheetImportViewModel parent, int number, string role, string name, string type) : ViewModelBase {
	public int Number => number;
	/// <summary>The name the column keeps read past: its own, or the driver's F-name when it had a role or none.</summary>
	public string Name => name;
	/// <summary>The driver's type, kept for a column read past.</summary>
	public string Type => type;
	/// <summary>The role, typed or picked: <c>key</c>, <c>value-fr</c>, <c>stale</c>; empty to read it past.</summary>
	public string Role { get; set => _ = ChangeProperty(ref field, value) && parent.ColumnsChanged(); } = role;
	/// <summary>The role in words: a language by name, a form by its category.</summary>
	public string Meaning => parent.Meaning(Role);
	/// <summary>The roles the dropdown offers; any other is typed.</summary>
	public IReadOnlyList<string> Roles => parent.Roles;

	internal void Reword() => AffectProperty(nameof(Meaning));
}

/// <summary>
///     The spreadsheet dialog (SPEC: Spreadsheets, The dialog): what a sheet is,
///     said before it is read. It opens with the sheet's section of the
///     <c>schema.ini</c> beside it, or the codec's guess without one, and offers
///     the folder's other sections to borrow; the first rows show as the choices
///     split and decode them, a role above each column. Import keeps the choices as
///     <see cref="Options"/>, which the caller writes as the sheet's section and
///     reads by; Cancel leaves them null.
/// </summary>
public sealed class SheetImportViewModel : DialogViewModel {
	private const int PreviewRows = 8;
	private readonly MainWindowViewModel parent;
	//every column met, kept past a delimiter that splits fewer, so a role outlives a change of mind
	private readonly List<SheetColumn> columns = [];
	//what the layout says that the dialog does not ask: TextDelimiter, the driver's own keys
	private FormatOptions kept = [];
	private bool loading;
	//the columns the layout declares, and the delimiter it declares them under: a column
	//the first rows leave empty is kept while the sheet splits as the layout says
	private int declared;
	private string? declaredUnder;
	//a sheet in UTF-32, which is not read, whatever the choices
	private readonly bool utf32;
	//the folder's schema.ini, read once when the dialog opens, each layout then read from it
	private readonly SchemaFile schema;

	public override string Title => Words.Known["sheet.title"];
	public CsvCodec Codec { get; }
	/// <summary>The sheet being imported.</summary>
	public string Sheet { get; }
	public string Blurb => Words.Known.Format("sheet.blurb", Path.GetFileName(Sheet));

	/// <summary>Where the choices come from: the sheet's own section, the guess, or another sheet's section.</summary>
	public IReadOnlyList<SheetChoice> Layouts { get; }
	public SheetChoice? Layout {
		get;
		set {
			if (ChangeProperty(ref field, value) && value is not null) {
				Load(OptionsOf(value));
			}
		}
	}
	public bool Header { get; set => _ = ChangeProperty(ref field, value) && Refresh(); }
	public IReadOnlyList<SheetChoice> Delimiters { get; private set => ChangeProperty(ref field, value); } = [];
	public SheetChoice? Delimiter { get; set => _ = ChangeProperty(ref field, value) && Refresh(); }
	public IReadOnlyList<SheetChoice> CharacterSets { get; private set => ChangeProperty(ref field, value); } = [];
	public SheetChoice? CharacterSet { get; set => _ = ChangeProperty(ref field, value) && Refresh(); }

	/// <summary>A column for each cell the widest previewed row has.</summary>
	public IReadOnlyList<SheetColumn> Columns { get; private set => ChangeProperty(ref field, value); } = [];
	/// <summary>The first rows, as the choices split and decode them.</summary>
	public IReadOnlyList<SheetRow> Rows { get; private set => ChangeProperty(ref field, value); } = [];
	/// <summary>The roles a column's dropdown offers: the default's, then each language's the session or the sheet knows.</summary>
	public IReadOnlyList<string> Roles { get; }

	/// <summary>Why the sheet cannot be read as the choices stand; empty when it can.</summary>
	public string Problem { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(CanImport)) && AffectProperty(nameof(HasProblem)); } = "";
	public bool HasProblem => Problem != "";
	public bool CanImport => Problem == "";
	/// <summary>The sheet's section as Import left it; null until then, and after Cancel.</summary>
	public FormatOptions? Options { get; private set; }

	public ICommand ImportCommand { get; }
	public ICommand CancelCommand { get; }

	public SheetImportViewModel(MainWindowViewModel parent, CsvCodec codec, string sheet) {
		ArgumentNullException.ThrowIfNull(parent);
		this.parent = parent;
		Codec = codec;
		Sheet = sheet;
		utf32 = CsvCodec.IsUtf32(sheet);
		parent.Help.Arise(HelpTopics.Sheet); //its bubble opens on the role row, once the dialog shows it
		ImportCommand = new DelegateCommand(DoImport, () => CanImport);
		CancelCommand = new DelegateCommand(Close);

		string own = Path.GetFileName(sheet);
		schema = SchemaIni.Load(sheet);
		IReadOnlyList<string> sections = schema.Sections;
		List<SheetChoice> layouts = [];
		if (sections.FirstOrDefault(section => string.Equals(section, own, StringComparison.OrdinalIgnoreCase)) is { } mine) {
			layouts.Add(new(mine, Words.Known["sheet.layout-own"]));
		}
		layouts.Add(new("", Words.Known["sheet.layout-guess"]));
		layouts.AddRange(sections.Where(section => !string.Equals(section, own, StringComparison.OrdinalIgnoreCase))
			.Select(section => new SheetChoice(section, Words.Known.Format("sheet.layout-borrow", section))));
		Layouts = layouts;

		//the languages the session knows, then any a layout on offer names
		List<string> codes = [.. parent.Session.Languages.Known.Select(language => language.Code)];
		foreach (SheetChoice layout in layouts) {
			foreach (var (key, value) in OptionsOf(layout)) {
				if (SchemaIni.ColumnNumber(key) is not null && CsvCodec.Role(CsvCodec.ReadColumn(value).Name) is { } role
					&& WordsField.TryParse(role, out var field, out _) && field.Language != "" && !codes.Contains(field.Language)) {
					codes.Add(field.Language);
				}
			}
		}
		Roles = [CsvCodec.KeyColumn, "value", "context", "comment", "stale",
			.. codes.SelectMany(code => (string[])[$"value-{code}", $"context-{code}", $"comment-{code}", $"stale-{code}"])];

		Layout = layouts[0];
	}

	private FormatOptions OptionsOf(SheetChoice layout)
		=> layout.Value == "" ? Codec.Sniff(Sheet) : schema.Read(layout.Value) ?? Codec.Sniff(Sheet);

	//the choices a layout makes, and its columns by number: named by the layout, else by the
	//header when the layout names none, as the reader names them
	private void Load(FormatOptions options) {
		loading = true;
		kept = [];
		foreach (var (key, value) in options) {
			if (SchemaIni.ColumnNumber(key) is null && !IsAsked(key)) {
				kept[key] = value;
			}
		}
		Header = !(options.TryGetValue(CsvCodec.HeaderOption, out string? header) && header.Trim().Equals("False", StringComparison.OrdinalIgnoreCase));
		LoadDelimiter(options.GetValueOrDefault(CsvCodec.FormatOption) ?? CsvCodec.FormatFor(','));
		LoadCharacterSet(options.GetValueOrDefault(CsvCodec.CharacterSetOption)?.Trim() ?? "ANSI");

		Dictionary<int, (string Name, string Type)> named = [];
		foreach (var (key, value) in options) {
			if (SchemaIni.ColumnNumber(key) is { } number) {
				named[number] = CsvCodec.ReadColumn(value);
			}
		}
		IReadOnlyList<IReadOnlyList<string>> preview = Preview();
		IReadOnlyList<string> headerCells = named.Count == 0 && Header && preview.Count > 0 ? preview[0] : [];
		declared = named.Count == 0 ? headerCells.Count : named.Keys.Max();
		declaredUnder = Delimiter?.Value;
		int width = Math.Max(Width(preview), declared);
		columns.Clear();
		for (int number = 1; number <= width; number++) {
			var (name, type) = named.TryGetValue(number, out var column) ? column
				: number <= headerCells.Count && headerCells[number - 1].Trim() != "" ? (headerCells[number - 1].Trim(), "Text")
				: ($"F{number}", "Text");
			string role = CsvCodec.Role(name) ?? "";
			columns.Add(new SheetColumn(this, number, role, role == "" ? name : $"F{number}", type));
		}
		loading = false;
		Refresh();
	}

	private static bool IsAsked(string key)
		=> key.Equals(CsvCodec.HeaderOption, StringComparison.OrdinalIgnoreCase)
		|| key.Equals(CsvCodec.FormatOption, StringComparison.OrdinalIgnoreCase)
		|| key.Equals(CsvCodec.CharacterSetOption, StringComparison.OrdinalIgnoreCase);

	private void LoadDelimiter(string format) {
		List<SheetChoice> choices = [
			new(CsvCodec.FormatFor(','), Words.Known["sheet.comma"]),
			new(CsvCodec.FormatFor(';'), Words.Known["sheet.semicolon"]),
			new(CsvCodec.FormatFor('\t'), Words.Known["sheet.tab"]),
			new(CsvCodec.FormatFor('|'), Words.Known["sheet.bar"]),
		];
		char? delimiter = CsvCodec.DelimiterOf(format);
		SheetChoice? chosen = choices.FirstOrDefault(choice => CsvCodec.DelimiterOf(choice.Value) == delimiter);
		if (delimiter is { } other && chosen is null) {
			chosen = new(CsvCodec.FormatFor(other), Words.Known.Format("sheet.delimiter-other", other));
			choices.Add(chosen);
		}
		Delimiters = choices;
		Delimiter = chosen; //none for FixedLength: the user picks one
	}

	private void LoadCharacterSet(string characterSet) {
		//one a sheet is never read in, UTF-32 among them, is taken as ANSI, as the codec takes it
		if (!CsvCodec.IsCharacterSet(characterSet)) {
			characterSet = "ANSI";
		}
		List<SheetChoice> choices = [
			new("ANSI", Words.Known.Format("sheet.ansi", Codec.CodePage("ANSI"))),
			new("OEM", Words.Known.Format("sheet.oem", Codec.CodePage("OEM"))),
			new("65001", Words.Known["sheet.utf8"]),
			new("UNICODE", Words.Known["sheet.utf16"]),
		];
		SheetChoice? chosen = choices.FirstOrDefault(choice => string.Equals(choice.Value, characterSet, StringComparison.OrdinalIgnoreCase));
		if (chosen is null) {
			chosen = new(characterSet, Words.Known.Format("sheet.code-page", characterSet));
			choices.Add(chosen);
		}
		CharacterSets = choices;
		CharacterSet = chosen;
	}

	private IReadOnlyList<IReadOnlyList<string>> Preview()
		=> Delimiter is null ? [] : Codec.Preview(Sheet, Current(), PreviewRows);

	private static int Width(IReadOnlyList<IReadOnlyList<string>> rows) => rows.Count == 0 ? 0 : rows.Max(row => row.Count);

	//the preview and the columns over again, after a choice that changes how the sheet splits
	private bool Refresh() {
		if (loading) {
			return true;
		}
		IReadOnlyList<IReadOnlyList<string>> preview = Preview();
		int width = preview.Count == 0 ? columns.Count : Width(preview);
		if (Delimiter?.Value == declaredUnder) {
			width = Math.Max(width, declared);
		}
		while (columns.Count < width) {
			columns.Add(new SheetColumn(this, columns.Count + 1, "", $"F{columns.Count + 1}", "Text"));
		}
		Columns = [.. columns.Take(width)];
		Rows = [.. preview.Select((row, index) => new SheetRow(index == 0 && Header,
			[.. Enumerable.Range(0, width).Select(i => i < row.Count ? new SheetCell(OneLine(row[i]), row[i]) : new SheetCell("", ""))]))];
		Validate();
		return true;
	}

	private static string OneLine(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", " ⏎ ");

	internal bool ColumnsChanged() {
		if (!loading) {
			Validate();
		}
		return true;
	}

	//what keeps the sheet from reading: UTF-32, no delimiter, a role that is none, a role
	//twice, no key, no value
	private void Validate() {
		Problem = utf32 ? Words.Known["sheet.utf32"] : Delimiter is null ? Words.Known["sheet.no-delimiter"] : RoleProblem();
		foreach (SheetColumn column in Columns) {
			column.Reword();
		}
	}

	private string RoleProblem() {
		if (Columns.FirstOrDefault(column => column.Role.Trim() != "" && CsvCodec.Role(column.Role) is null) is { } bad) {
			return Words.Known.Format("sheet.bad-role", bad.Role.Trim());
		}
		List<string> roles = [.. Columns.Select(column => CsvCodec.Role(column.Role)).OfType<string>()];
		if (roles.GroupBy(role => role).FirstOrDefault(group => group.Count() > 1) is { } twice) {
			return Words.Known.Format("sheet.twice", twice.Key);
		}
		return !roles.Contains(CsvCodec.KeyColumn) ? Words.Known["sheet.no-key"]
			: !roles.Contains("value") ? Words.Known["sheet.no-value"]
			: "";
	}

	/// <summary>A role in words: the key, a field of the default or of a language named as the editor names it, a form by its category, or read past.</summary>
	public string Meaning(string role) {
		if (role.Trim() == "") {
			return Words.Known["sheet.read-past"];
		}
		string? canonical = CsvCodec.Role(role);
		if (canonical is null) {
			return Words.Known["sheet.no-role"];
		}
		if (canonical == CsvCodec.KeyColumn) {
			return Words.Known["sheet.role-key"];
		}
		_ = WordsField.TryParse(canonical, out var field, out _);
		string language = field.Language == "" ? "" : LanguageName(field.Language);
		string meaning = (field.Type, field.Language == "") switch {
			("value", true) => Words.Known["sheet.role-value"],
			("value", false) => Words.Known.Format("sheet.role-value-in", language),
			("context", true) => Words.Known["sheet.role-context"],
			("context", false) => Words.Known.Format("sheet.role-context-in", language),
			("comment", true) => Words.Known["sheet.role-comment"],
			("comment", false) => Words.Known.Format("sheet.role-comment-in", language),
			(_, true) => Words.Known["sheet.role-review"],
			_ => Words.Known.Format("sheet.role-stale-in", language),
		};
		return field.Form == "" ? meaning : Words.Known.Format("sheet.role-form", meaning, field.Form);
	}

	//a language as the editor names it, or as the import will declare it when the session has none of it
	private string LanguageName(string code) {
		if (parent.Session.Languages.Find(code) is { } known) {
			return known.DisplayName;
		}
		try {
			return CultureInfo.GetCultureInfo(code, predefinedOnly: true).EnglishName;
		}
		catch (CultureNotFoundException) {
			return code;
		}
	}

	//the section as the choices stand: each column by its role, a column read past by the name it keeps
	private FormatOptions Current() {
		FormatOptions options = new() {
			[CsvCodec.HeaderOption] = Header ? "True" : "False",
		};
		if (Delimiter is not null) {
			options[CsvCodec.FormatOption] = Delimiter.Value;
		}
		if (CharacterSet is not null) {
			options[CsvCodec.CharacterSetOption] = CharacterSet.Value;
		}
		foreach (var (key, value) in kept) {
			options[key] = value;
		}
		foreach (SheetColumn column in Columns) {
			string? role = CsvCodec.Role(column.Role);
			string type = role == CsvCodec.KeyColumn ? "Text" : role is not null ? "Memo" : column.Type == "" ? "Text" : column.Type;
			options[CsvCodec.ColumnOption(column.Number)] = CsvCodec.Column(role ?? column.Name, type);
		}
		return options;
	}

	private void DoImport() {
		if (!CanImport) {
			return;
		}
		Options = Current();
		Close();
	}

	//the dialog gone, its bubble's moment with it
	public override void Closed() => parent.Help.Pass(HelpTopics.Sheet);
}
