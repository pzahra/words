using System.Globalization;
using System.Text;

namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>
	///     A spreadsheet as CSV (the editor's SPEC: Spreadsheets): one file carrying
	///     every language, a row a key and a column a field, read and written as its
	///     section of the <c>schema.ini</c> beside it says (<see cref="SchemaIni"/>, the
	///     ODBC text driver's), the section being the options. A column's name is its
	///     role as <c>words.ini</c> spells it — <c>key</c>, <c>value</c>,
	///     <c>value-fr</c>, <c>comment-de</c>, <c>value-mt#few</c>, <c>stale-fr</c>,
	///     and <c>stale</c> for the review flag — and a column of any other name is
	///     read past. A sheet with no section reads as <see cref="Sniff"/> guesses it.
	///     Every cell with words is quoted, as Excel quotes them, so a spreadsheet told
	///     to take a quoted cell as text keeps <c>007</c> and <c>1/2</c> as they are; an
	///     empty cell is no text. A stale mark with no text is its column's word,
	///     <c>review</c> or <c>stale</c>, since a spreadsheet drops an empty quoted
	///     cell; the word reads back as a mark with no text, and so does <c>""</c>.
	/// </summary>
	/// <param name="culture">
	///     The culture whose code pages <c>ANSI</c> and <c>OEM</c> name, and whose list
	///     separator a new sheet is delimited by, as Excel's is: the machine's, unless
	///     a test says otherwise.
	/// </param>
	public sealed class CsvCodec(CultureInfo? culture = null) : IWordsImporter, IWordsExporter {
		/// <summary>The manifest name of the format's words.</summary>
		public const string WordsResource = "PatTech.Localization.Authoring.Codecs.csv.words.ini";
		/// <summary>The schema's option saying whether the first row is a header.</summary>
		public const string HeaderOption = "ColNameHeader";
		/// <summary>The schema's option naming the delimiter: <c>CSVDelimited</c>, <c>TabDelimited</c>, <c>Delimited(;)</c>.</summary>
		public const string FormatOption = "Format";
		/// <summary>The schema's option naming the encoding: <c>ANSI</c>, <c>OEM</c>, <c>UNICODE</c>, or a code page.</summary>
		public const string CharacterSetOption = "CharacterSet";
		/// <summary>The schema's option naming the character a cell is quoted in, or <c>none</c>.</summary>
		public const string TextDelimiterOption = "TextDelimiter";
		/// <summary>The key column's name.</summary>
		public const string KeyColumn = "key";
		//how many rows the sniff looks at
		private const int SniffRows = 50;

		/// <summary>The culture whose code pages and list separator stand for the machine's.</summary>
		public CultureInfo Culture { get; } = culture ?? CultureInfo.CurrentCulture;

		/// <inheritdoc/>
		public WordsFormatInfo Info { get; } = new("csv", [".csv", ".tsv"],
			WordsFeatures.Context | WordsFeatures.Comment | WordsFeatures.EntryContext | WordsFeatures.EntryComment
			| WordsFeatures.Stale | WordsFeatures.NeedsReview | WordsFeatures.Constants | WordsFeatures.PluralForms);

		/// <inheritdoc/>
		public TextReader? Init()
			=> new StreamReader(typeof(CsvCodec).Assembly.GetManifestResourceStream(WordsResource)
				?? throw new FileNotFoundException(WordsResource));

		/// <summary>The schema's option for column <paramref name="number"/>, counting from one.</summary>
		public static string ColumnOption(int number) => $"Col{number}";

		/// <summary>A column as the schema writes it: its name, in quotes when it holds a space, and its type.</summary>
		public static string Column(string name, string type) => (name.Any(char.IsWhiteSpace) ? $"\"{name}\"" : name) + " " + type;

		/// <summary>The schema's <c>Format</c> for a delimiter.</summary>
		public static string FormatFor(char delimiter) => delimiter switch {
			',' => "CSVDelimited",
			'\t' => "TabDelimited",
			_ => $"Delimited({delimiter})",
		};

		/// <summary>The delimiter a <c>Format</c> names: a comma, a tab, or a <c>Delimited(x)</c>'s character; <see langword="null"/> for <c>FixedLength</c> and any value naming none.</summary>
		public static char? DelimiterOf(string format) {
			string trimmed = format.Trim();
			if (trimmed.Equals("CSVDelimited", StringComparison.OrdinalIgnoreCase)) {
				return ',';
			}
			if (trimmed.Equals("TabDelimited", StringComparison.OrdinalIgnoreCase)) {
				return '\t';
			}
			return trimmed.StartsWith("Delimited(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')') && trimmed.Length == "Delimited(x)".Length ? trimmed[^2] : null;
		}

		/// <summary>A column as the schema gives it: its name, unquoted, and the driver's type after it, empty when there is none.</summary>
		public static (string Name, string Type) ReadColumn(string column) {
			string trimmed = column.Trim();
			if (trimmed.StartsWith('"')) {
				int close = trimmed.IndexOf('"', 1);
				return close < 0 ? (trimmed[1..], "") : (trimmed[1..close], trimmed[(close + 1)..].Trim());
			}
			int space = trimmed.IndexOfAny([' ', '\t']);
			return space < 0 ? (trimmed, "") : (trimmed[..space], trimmed[(space + 1)..].Trim());
		}

		/// <summary>A sheet carries every language in one file: the pick alone.</summary>
		public IReadOnlyList<string> Discover(string path) => [Path.GetFullPath(path)];

		/// <summary>The sheet's stem with <c>-words.ini</c>, beside it: <c>Strings.csv</c> loads as <c>Strings-words.ini</c>.</summary>
		public string NativePath(IReadOnlyList<string> paths)
			=> Path.Combine(Path.GetDirectoryName(paths[0]) ?? "", Path.GetFileNameWithoutExtension(paths[0]) + "-words.ini");

		/// <summary>The sheet's section of the <c>schema.ini</c> beside it, or the sniff's guess when there is none: what a read with no options reads by.</summary>
		public FormatOptions OptionsFor(string sheet) => SchemaIni.Read(sheet) ?? Sniff(sheet);

		/// <summary>
		///     Reads the sheet (a set is one sheet) as <paramref name="options"/> say, its
		///     section or the sniff's guess when none are given. A schema with no
		///     <c>key</c> column or no <c>value</c> column, or a fixed-width one, reads
		///     nothing, with a gripe. A row with no key is skipped, and so is one whose
		///     key a row above gave, and one with words beyond the columns, each with a
		///     gripe; an empty row is no row.
		/// </summary>
		public ILoadedWords Read(IReadOnlyList<string> paths, FormatOptions? options = null) {
			var loaded = new LoadedWords();
			string path = paths[0];
			string file = Path.GetFileName(path);
			foreach (string other in paths.Skip(1)) {
				loaded.Errors.Add($"{Path.GetFileName(other)}: a sheet is one file, and only {file} was read");
			}
			byte[] bytes = File.ReadAllBytes(path);
			if (CodePages.IsUtf32(bytes)) {
				loaded.Errors.Add($"{file}: the sheet is UTF-32, which is not read");
				return loaded;
			}
			CsvSchema schema = CsvSchema.Parse(options ?? OptionsFor(path), loaded.Errors, file);
			if (schema.FixedLength) {
				loaded.Errors.Add($"{file}: Format=FixedLength: a fixed-width file is no sheet, so nothing was read");
				return loaded;
			}
			List<CsvRow> rows = CsvRows.Split(Text(bytes, schema), schema.Delimiter, schema.Quote);
			//the header is the first row with anything in it, blank lines above it no rows
			int header = schema.Header ? rows.FindIndex(row => !row.IsEmpty) : -1;
			if (schema.Columns.Count == 0) {
				schema.Name(header >= 0 ? rows[header].Cells : [], loaded.Errors, file);
			}
			int keyAt = schema.Columns.FindIndex(column => column.IsKey);
			if (keyAt < 0 || !schema.Columns.Any(column => column.Field is { Type: "value", Language: "", Form: "" })) {
				loaded.Errors.Add($"{file}: no column is {(keyAt < 0 ? "the key (key)" : "the default's value (value)")}, so nothing was read");
				return loaded;
			}
			foreach (string code in schema.Columns.Select(column => column.Field?.Language ?? "").Where(code => code != "").Distinct()) {
				FileNames.Declare(loaded, code);
			}
			Dictionary<string, int> given = [];
			foreach (CsvRow row in schema.Header ? rows.Skip(header < 0 ? rows.Count : header + 1) : rows) {
				if (row.IsEmpty) {
					continue;
				}
				if (row.Cells.Skip(schema.Columns.Count).Any(cell => !string.IsNullOrEmpty(cell))) {
					loaded.Errors.Add($"{file}: row {row.Line} has words beyond its {schema.Columns.Count} columns, so it was skipped");
					continue;
				}
				string name = (row.Cell(keyAt) ?? "").Trim();
				if (name == "") {
					loaded.Errors.Add($"{file}: row {row.Line} has no key, so it was skipped");
					continue;
				}
				if (given.TryGetValue(name, out int first)) {
					loaded.Errors.Add($"{file}: row {row.Line} gives '{name}' again, which row {first} gave, and the first stands");
					continue;
				}
				given[name] = row.Line;
				WordsKey key = loaded.Key(FileNames.BlockKey(loaded, name, file));
				for (int i = 0; i < schema.Columns.Count; i++) {
					//an empty cell is no text; a mark with none comes as its column's word, or quoted empty
					if (schema.Columns[i].Field is { } field && row.Cell(i) is { } text) {
						field.Write(key, field.Type == "stale" && text.Trim().Equals(MarkWord(field), StringComparison.OrdinalIgnoreCase) ? "" : text);
					}
				}
			}
			return loaded;
		}

		/// <summary>
		///     A first guess at a sheet with no section (the editor's SPEC: Spreadsheets,
		///     The sniff): its encoding, by a byte-order mark, else UTF-8 when its bytes
		///     read as UTF-8 and are not all ASCII, else the ANSI code page; its delimiter,
		///     whichever of comma, semicolon, tab and bar splits the first rows into the
		///     same number of cells, the list separator on a tie and tab first in a
		///     <c>.tsv</c>; a header, when a cell of the first row names a role or a
		///     language by its code; and the roles: the key first, the default's value
		///     second, then a header cell naming a role that role, one that is a code,
		///     or ends in one in brackets, a value in that language, and every other
		///     column read past.
		/// </summary>
		public FormatOptions Sniff(string path) {
			byte[] bytes = File.ReadAllBytes(path);
			string characterSet = CodePages.Bom(bytes) switch {
				65001 => "65001",
				1200 => "UNICODE",
				1201 => "1201",
				_ => CodePages.IsUtf8(bytes) && bytes.Any(b => b >= 0x80) ? "65001" : "ANSI",
			};
			string text = CodePages.Decode(bytes, CodePages.Reading(CodePage(characterSet))).Text;
			bool tab = string.Equals(Path.GetExtension(path), ".tsv", StringComparison.OrdinalIgnoreCase);
			char delimiter = SniffDelimiter(text, tab);
			List<CsvRow> rows = [.. CsvRows.Split(text, delimiter, '"', SniffRows).Where(row => !row.IsEmpty)];
			IReadOnlyList<string?> first = rows.Count > 0 ? rows[0].Cells : [];
			bool header = first.Any(cell => cell is not null && (Role(cell) is not null || Code(cell) is not null));
			int width = Math.Max(2, rows.Count == 0 ? 0 : rows.Max(row => row.Cells.Count));

			FormatOptions options = new() {
				[HeaderOption] = header ? "True" : "False",
				[FormatOption] = FormatFor(delimiter),
				[CharacterSetOption] = characterSet,
			};
			HashSet<string> taken = [KeyColumn, "value"];
			options[ColumnOption(1)] = Column(KeyColumn, "Text");
			options[ColumnOption(2)] = Column("value", "Memo");
			for (int i = 2; i < width; i++) {
				string? cell = header && i < first.Count ? first[i]?.Trim() : null;
				string? role = cell is null ? null : Role(cell) ?? (Code(cell) is { } code ? $"value-{code}" : null);
				if (role is not null && taken.Add(role)) {
					options[ColumnOption(i + 1)] = Column(role, "Memo");
				}
				else {
					//read past: the header's own name when it names nothing else, as the driver
					//would name it, else the driver's own F-name
					bool own = !string.IsNullOrWhiteSpace(cell) && !cell.Contains('"') && role is null && taken.Add(cell);
					options[ColumnOption(i + 1)] = Column(own ? cell! : $"F{i + 1}", "Text");
				}
			}
			return options;
		}

		//whichever candidate splits the first rows into the same number of cells, the widest,
		//and of a tie the list separator, Excel's own; tab first in a .tsv
		private char SniffDelimiter(string text, bool tab) {
			char[] candidates = tab ? ['\t', ',', ';', '|'] : [',', ';', '\t', '|'];
			char listSeparator = Culture.TextInfo.ListSeparator is [var separator, ..] ? separator : ',';
			List<(char Delimiter, int Width)> consistent = [];
			foreach (char candidate in candidates) {
				List<CsvRow> rows = [.. CsvRows.Split(text, candidate, '"', SniffRows).Where(row => !row.IsEmpty)];
				if (rows.Count > 0 && rows[0].Cells.Count > 1 && rows.All(row => row.Cells.Count == rows[0].Cells.Count)) {
					consistent.Add((candidate, rows[0].Cells.Count));
				}
			}
			if (tab && consistent.Any(found => found.Delimiter == '\t')) {
				return '\t';
			}
			if (consistent.Count == 0) {
				return tab ? '\t' : candidates.Contains(listSeparator) ? listSeparator : ',';
			}
			int widest = consistent.Max(found => found.Width);
			List<char> best = [.. consistent.Where(found => found.Width == widest).Select(found => found.Delimiter)];
			return best.Contains(listSeparator) ? listSeparator : best[0];
		}

		/// <summary>
		///     The role a column's name gives it, as <c>words.ini</c> spells it: <c>key</c>,
		///     or a value, context, comment or stale field; <see langword="null"/> for any
		///     other name. Case is no matter, as it is none to the driver: <c>Value-FR</c>
		///     is <c>value-fr</c>.
		/// </summary>
		public static string? Role(string name) {
			name = name.Trim();
			if (string.Equals(name, KeyColumn, StringComparison.OrdinalIgnoreCase)) {
				return KeyColumn;
			}
			int type = name.IndexOfAny(['-', '#']);
			name = type < 0 ? name.ToLowerInvariant() : name[..type].ToLowerInvariant() + name[type..];
			return WordsField.TryParse(name, out var field, out _) && field.Type is "value" or "context" or "comment" or "stale" ? field.ToString() : null;
		}

		//a header cell that is a language code alone, or ends in one in brackets: French (fr)
		private static string? Code(string cell) {
			cell = cell.Trim();
			if (FileNames.TryCulture(cell, out string code)) {
				return code;
			}
			int open = cell.LastIndexOf('(');
			return cell.EndsWith(')') && open >= 0 && FileNames.TryCulture(cell[(open + 1)..^1].Trim(), out code) ? code : null;
		}

		/// <summary>
		///     The first <paramref name="rows"/> rows of the sheet, as <paramref name="options"/>
		///     split them, header and all, an empty cell empty: what the dialog shows of
		///     the file. A fixed-width schema shows nothing.
		/// </summary>
		public IReadOnlyList<IReadOnlyList<string>> Preview(string path, FormatOptions options, int rows) {
			CsvSchema schema = CsvSchema.Parse(options, [], Path.GetFileName(path));
			byte[] bytes = File.ReadAllBytes(path);
			if (schema.FixedLength || CodePages.IsUtf32(bytes)) {
				return [];
			}
			//no empty rows: the first shown is the header, however many blank lines come before it
			return [.. CsvRows.Split(Text(bytes, schema), schema.Delimiter, schema.Quote, rows)
				.Where(row => !row.IsEmpty)
				.Select(row => (IReadOnlyList<string>)[.. row.Cells.Select(cell => cell ?? "")])];
		}

		/// <summary>True when the sheet's byte-order mark says UTF-32, which a sheet is never read in.</summary>
		public static bool IsUtf32(string path) {
			using FileStream stream = File.OpenRead(path);
			byte[] head = new byte[4];
			return CodePages.IsUtf32(head[..stream.ReadAtLeast(head, 4, throwOnEndOfStream: false)]);
		}

		//the sheet as text: in the encoding its byte-order mark names, else the schema's
		private string Text(byte[] bytes, CsvSchema schema) => CodePages.Decode(bytes, CodePages.Reading(CodePage(schema.CharacterSet))).Text;

		/// <summary>
		///     Whether a <c>CharacterSet</c> names an encoding a sheet is read and written
		///     in: <c>ANSI</c>, <c>OEM</c>, <c>UNICODE</c>, or a code page the platform
		///     has, but for UTF-32 (12000, 12001), which no spreadsheet reads.
		/// </summary>
		public static bool IsCharacterSet(string characterSet) {
			string trimmed = characterSet.Trim();
			return trimmed.ToUpperInvariant() is "ANSI" or "OEM" or "UNICODE"
				|| int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int page) && !CodePages.IsUtf32Page(page) && CodePages.Known(page);
		}

		/// <summary>The code page a <c>CharacterSet</c> names: <c>ANSI</c> and <c>OEM</c> the culture's, <c>UNICODE</c> UTF-16, or the number; ANSI for any it does not name (<see cref="IsCharacterSet"/>).</summary>
		public int CodePage(string characterSet) => characterSet.Trim().ToUpperInvariant() switch {
			"ANSI" => Culture.TextInfo.ANSICodePage,
			"OEM" => Culture.TextInfo.OEMCodePage,
			"UNICODE" => 1200,
			var number => IsCharacterSet(number) ? int.Parse(number, NumberStyles.None, CultureInfo.InvariantCulture) : Culture.TextInfo.ANSICodePage,
		};

		/// <summary>
		///     A section for <paramref name="source"/> with <paramref name="layout"/>'s
		///     columns. A new table's has a header, the list separator, and UTF-8,
		///     which Excel reads by its byte-order mark; a table that exists, its
		///     <paramref name="section"/> given, keeps all of it but its columns: its
		///     header, delimiter and encoding, and the driver's own keys.
		/// </summary>
		public FormatOptions Options(ExportSource source, CsvLayout layout, FormatOptions? section = null) {
			char listSeparator = Culture.TextInfo.ListSeparator is [var separator, ..] ? separator : ',';
			FormatOptions options = new() {
				[HeaderOption] = "True",
				[FormatOption] = FormatFor(listSeparator is '"' or '\r' or '\n' ? ',' : listSeparator),
				[CharacterSetOption] = "65001",
			};
			foreach (var (key, value) in section ?? []) {
				if (SchemaIni.ColumnNumber(key) is null) {
					options[key] = value;
				}
			}
			int number = 1;
			foreach (string name in layout.Columns(source)) {
				options[ColumnOption(number++)] = Column(name, name == KeyColumn ? "Text" : "Memo");
			}
			return options;
		}

		/// <summary>What an export to <paramref name="sheet"/> writes by when it is given no options: the sheet's section, else a new table's, ticked by what the file uses.</summary>
		public FormatOptions OptionsFor(ExportSource source, string sheet) => SchemaIni.Read(sheet) ?? Options(source, CsvLayout.For(source));

		/// <summary>
		///     The section an export of <paramref name="source"/> goes by: <paramref name="section"/>
		///     as it is when it names columns; one naming none keeps its header, delimiter,
		///     encoding and the driver's own keys, and takes the columns a new table would,
		///     ticked by what the file uses.
		/// </summary>
		public FormatOptions Effective(ExportSource source, FormatOptions section)
			=> CsvSchema.Parse(section, [], "").Columns.Count > 0 ? section : Options(source, CsvLayout.For(source), section);

		/// <summary>One sheet at <paramref name="target"/>, a <c>.csv</c> unless it names a sheet's extension, carrying every language, in the encoding its schema names.</summary>
		public IReadOnlyList<ExportUnit> Plan(ExportSource source, string target, FormatOptions? options = null) {
			string sheet = Info.Claims(target) ? target : Path.ChangeExtension(target, ".csv");
			CsvSchema schema = CsvSchema.Parse(Effective(source, options ?? OptionsFor(source, sheet)), [], Path.GetFileName(sheet));
			return [new ExportUnit(sheet, [.. source.File.Languages], CodePages.Writing(CodePage(schema.CharacterSet)))];
		}

		/// <summary>
		///     <paramref name="options"/> as an export of <paramref name="source"/> must
		///     have them to keep every cell as it is: unchanged, unless under
		///     <c>TextDelimiter=none</c> some cell holds the delimiter or a line break,
		///     when they say <c>TextDelimiter="</c> instead (the editor's SPEC:
		///     Spreadsheets, Export).
		/// </summary>
		public FormatOptions Quoting(ExportSource source, FormatOptions options) {
			CsvSchema schema = CsvSchema.Parse(Effective(source, options), [], "");
			if (schema.Quote is not null || !Cells(source, schema).Any(cell => cell?.IndexOfAny([schema.Delimiter, '\n']) >= 0)) {
				return options;
			}
			FormatOptions quoted = [];
			foreach (var (key, value) in options) {
				quoted[key] = value;
			}
			quoted[TextDelimiterOption] = "\"";
			return quoted;
		}

		/// <summary>
		///     Writes the sheet: the header row when the schema has one, then a row a key
		///     in the tree's order, a cell a column, a column read past left empty, rows
		///     ending CRLF and a line break in a cell LF. Every cell with words is quoted;
		///     under <c>TextDelimiter=none</c> none is, and a cell holding the delimiter
		///     or a line break has each written as a space, with a gripe naming its key.
		///     A stale mark with no text is its column's word. A field the file fills, in
		///     the default or a language with a value column, that no column carries is a
		///     gripe; a language with no value column is left out, and no gripe.
		/// </summary>
		public void Write(ExportSource source, ExportUnit unit, TextWriter writer, ICollection<string> gripes, FormatOptions? options = null) {
			string file = Path.GetFileName(unit.Path);
			FormatOptions section = options ?? OptionsFor(source, unit.Path);
			CsvSchema schema = CsvSchema.Parse(Effective(source, section), gripes, file);
			if (CsvSchema.Parse(section, [], file).Columns.Count == 0 && !schema.Header) {
				gripes.Add($"{file}: its section names no columns and {HeaderOption}=False, so nothing names them and the sheet will not read back");
			}
			if (schema.FixedLength) {
				schema.Delimiter = ',';
				gripes.Add($"{file}: Format=FixedLength: a fixed-width file is no sheet, so it was written comma-delimited");
			}
			if (!schema.Columns.Any(column => column.IsKey) || !schema.Columns.Any(column => column.Field is { Type: "value", Language: "", Form: "" })) {
				gripes.Add($"{file}: no column is the key or the default's value, so the sheet will not read back");
			}
			foreach (WordsField missing in Missing(source, schema.Columns).Distinct()) {
				gripes.Add($"{file}: no column carries {missing}, so its words were not written");
			}
			List<string> spaced = [];
			if (schema.Header) {
				Row(schema.Columns.Select(column => (string?)column.Name));
			}
			foreach (WordsKey key in source.Keys()) {
				if (Row(Cells(key, schema))) {
					spaced.Add(key.BlockKey);
				}
			}
			if (spaced.Count > 0) {
				gripes.Add($"{file}: {TextDelimiterOption}=none, so the delimiter and line breaks in the cells of {spaced.Count} keys were written as spaces ({string.Join(", ", spaced)})");
			}

			//true when a cell had to lose the delimiter or a line break
			bool Row(IEnumerable<string?> cells) {
				bool changed = false;
				List<string> written = [];
				foreach (string? text in cells) {
					written.Add(Cell(text, ref changed));
				}
				writer.Write(string.Join(schema.Delimiter, written));
				writer.Write("\r\n");
				return changed;
			}

			string Cell(string? text, ref bool changed) {
				if (text is null) {
					return "";
				}
				if (schema.Quote is not { } quote) {
					//bare, as the schema wants: what would need a quote is a space
					if (text.IndexOfAny([schema.Delimiter, '\n']) < 0) {
						return text;
					}
					changed = true;
					return text.Replace(schema.Delimiter, ' ').Replace('\n', ' ');
				}
				string mark = quote.ToString();
				return mark + text.Replace(mark, mark + mark) + mark;
			}
		}

		//every key's cells as written, before quoting
		private static IEnumerable<string?> Cells(ExportSource source, CsvSchema schema)
			=> source.Keys().SelectMany(key => Cells(key, schema));

		//a key's cells, a line break in one LF, as a reader gives it back
		private static IEnumerable<string?> Cells(WordsKey key, CsvSchema schema)
			=> schema.Columns.Select(column => (column.IsKey ? key.BlockKey : column.Field is { } field ? Text(field, key) : null)?.Replace("\r\n", "\n").Replace('\r', '\n'));

		//a field's cell: its text, and a stale mark with none its column's word
		private static string? Text(WordsField field, WordsKey key) {
			string? text = field.Read(key);
			return text == "" && field.Type == "stale" ? MarkWord(field) : text;
		}

		//the word a stale mark with no text is written as: review for the review flag, stale for a language's
		private static string MarkWord(WordsField field) => field.Language == "" ? "review" : "stale";

		//the fields the file fills that no column carries: the default's, and those of each
		//language with a value column, a language with none being left out (the editor's
		//SPEC: Spreadsheets, Loss follows the columns). A mark with no text is filled
		private static IEnumerable<WordsField> Missing(ExportSource source, IReadOnlyList<CsvColumn> columns) {
			HashSet<string> carried = [.. columns.Select(column => column.Field?.ToString()).OfType<string>()];
			List<string> languages = [.. columns.Select(column => column.Field).OfType<WordsField>()
				.Where(field => field is { Type: "value", Form: "" } && field.Language != "").Select(field => field.Language).Distinct()];
			foreach (WordsKey key in source.Keys()) {
				List<WordsField> fields = [new("context", "", ""), new("comment", "", ""), new("stale", "", ""),
					.. key.Forms.Keys.Select(form => new WordsField("value", "", form))];
				foreach (string code in languages) {
					if (key.Entries.TryGetValue(code, out WordsEntry? entry)) {
						fields.AddRange([new("context", code, ""), new("comment", code, ""), new("stale", code, ""),
							.. entry.Forms.Keys.Select(form => new WordsField("value", code, form))]);
					}
				}
				foreach (WordsField field in fields) {
					if (!carried.Contains(field.ToString()) && field.Read(key) is not null) {
						yield return field;
					}
				}
			}
		}

		//the feature a field that no column carries is lost from
		private static WordsFeatures FeatureOf(WordsField field) => field switch {
			{ Form: not "" } => WordsFeatures.PluralForms,
			{ Type: "context", Language: "" } => WordsFeatures.Context,
			{ Type: "comment", Language: "" } => WordsFeatures.Comment,
			{ Type: "context" } => WordsFeatures.EntryContext,
			{ Type: "comment" } => WordsFeatures.EntryComment,
			{ Type: "stale", Language: "" } => WordsFeatures.NeedsReview,
			{ Type: "stale" } => WordsFeatures.Stale,
			_ => WordsFeatures.None,
		};

		/// <summary>
		///     What a sheet keeps written with <paramref name="options"/>: what its
		///     columns carry, and constants by the key's <c>$</c>. With none, or a
		///     section naming no columns, whose sheet is written in the layout the
		///     file's use ticks, every feature a sheet can carry.
		/// </summary>
		public WordsFeatures Keeps(FormatOptions? options = null) {
			List<CsvColumn> columns = options is null ? [] : CsvSchema.Parse(options, [], "").Columns;
			if (columns.Count == 0) {
				return Info.Features;
			}
			List<WordsField> fields = [.. columns.Select(column => column.Field).OfType<WordsField>()];
			WordsFeatures kept = WordsFeatures.Constants;
			void Keep(WordsFeatures feature, Func<WordsField, bool> carries) {
				if (fields.Any(carries)) {
					kept |= feature;
				}
			}
			Keep(WordsFeatures.Context, field => field is { Type: "context", Language: "" });
			Keep(WordsFeatures.Comment, field => field is { Type: "comment", Language: "" });
			Keep(WordsFeatures.EntryContext, field => field is { Type: "context", Language: not "" });
			Keep(WordsFeatures.EntryComment, field => field is { Type: "comment", Language: not "" });
			Keep(WordsFeatures.Stale, field => field is { Type: "stale", Language: not "" });
			Keep(WordsFeatures.NeedsReview, field => field is { Type: "stale", Language: "" });
			Keep(WordsFeatures.PluralForms, field => field.Form != "");
			return kept;
		}

		/// <summary>
		///     What a sheet of <paramref name="source"/> keeps written with
		///     <paramref name="options"/>, through the section it goes by
		///     (<see cref="Effective"/>): a feature is kept when every field of it the
		///     file fills, in the default and in each language with a value column, has
		///     a column. A language with no value column is left out by choice, and loses
		///     nothing (the editor's SPEC: Spreadsheets, Loss follows the columns).
		/// </summary>
		public WordsFeatures Keeps(ExportSource source, FormatOptions? options) {
			FormatOptions effective = Effective(source, options ?? Options(source, CsvLayout.For(source)));
			WordsFeatures kept = Info.Features;
			foreach (WordsField missing in Missing(source, CsvSchema.Parse(effective, [], "").Columns)) {
				kept &= ~FeatureOf(missing);
			}
			return kept;
		}

		/// <summary>Writes <paramref name="options"/> as <paramref name="sheet"/>'s section of the <c>schema.ini</c> beside it.</summary>
		public static void SaveSchema(string sheet, FormatOptions options) => SchemaIni.Write(sheet, options);
	}

	/// <summary>
	///     A new table's column options (the editor's SPEC: Spreadsheets, Export): which
	///     languages get a column, and which of what a sheet can carry rides along. A
	///     language's context, comment and stale mark hang on a language ticked, and plural
	///     forms go to the default and to each language ticked.
	/// </summary>
	public sealed class CsvLayout {
		/// <summary>The languages with a column, in the file's order.</summary>
		public List<string> Languages { get; } = [];
		/// <summary>The default's context, <c>context</c>.</summary>
		public bool Context { get; set; }
		/// <summary>The default's comment, <c>comment</c>.</summary>
		public bool Comment { get; set; }
		/// <summary>Each language's context, <c>context-xx</c>.</summary>
		public bool EntryContext { get; set; }
		/// <summary>Each language's comment, <c>comment-xx</c>.</summary>
		public bool EntryComment { get; set; }
		/// <summary>Each language's stale mark, <c>stale-xx</c>.</summary>
		public bool Stale { get; set; }
		/// <summary>The review flag, <c>stale</c>.</summary>
		public bool Review { get; set; }
		/// <summary>A column for each plural form: those the file's keys write, the default's and each language's, and those a language counts by.</summary>
		public bool Forms { get; set; }
		/// <summary>A language's context, comment and stale mark can be ticked: some language is.</summary>
		public bool LanguageChannelsOpen => Languages.Count > 0;

		/// <summary>The layout a new table for <paramref name="source"/> starts with: every language, and each thing the file uses ticked.</summary>
		public static CsvLayout For(ExportSource source) {
			WordsFeatures used = source.Used();
			var layout = new CsvLayout {
				Context = (used & WordsFeatures.Context) != 0,
				Comment = (used & WordsFeatures.Comment) != 0,
				EntryContext = (used & WordsFeatures.EntryContext) != 0,
				EntryComment = (used & WordsFeatures.EntryComment) != 0,
				Stale = (used & WordsFeatures.Stale) != 0,
				Review = (used & WordsFeatures.NeedsReview) != 0,
				Forms = (used & WordsFeatures.PluralForms) != 0,
			};
			layout.Languages.AddRange(source.File.Languages);
			return layout;
		}

		/// <summary>The layout a section's columns make: its languages by their value columns, and each thing its columns carry.</summary>
		public static CsvLayout From(FormatOptions options) {
			List<WordsField> fields = [.. CsvSchema.Parse(options, [], "").Columns.Select(column => column.Field).OfType<WordsField>()];
			var layout = new CsvLayout {
				Context = fields.Any(field => field is { Type: "context", Language: "" }),
				Comment = fields.Any(field => field is { Type: "comment", Language: "" }),
				EntryContext = fields.Any(field => field is { Type: "context", Language: not "" }),
				EntryComment = fields.Any(field => field is { Type: "comment", Language: not "" }),
				Stale = fields.Any(field => field is { Type: "stale", Language: not "" }),
				Review = fields.Any(field => field is { Type: "stale", Language: "" }),
				Forms = fields.Any(field => field.Form != ""),
			};
			layout.Languages.AddRange(fields.Where(field => field is { Type: "value", Form: "" } && field.Language != "").Select(field => field.Language).Distinct());
			return layout;
		}

		/// <summary>
		///     The columns' names, in their order: the key, the default's value, its
		///     forms, context and comment, then each language's the same way and its
		///     stale mark, and the review flag last.
		/// </summary>
		public IReadOnlyList<string> Columns(ExportSource source) {
			List<string> columns = [CsvCodec.KeyColumn, "value"];
			if (Forms) {
				columns.AddRange(FormsUsed(source, null).Select(form => $"value#{form}"));
			}
			if (Context) {
				columns.Add("context");
			}
			if (Comment) {
				columns.Add("comment");
			}
			foreach (string code in Languages) {
				columns.Add($"value-{code}");
				if (Forms) {
					columns.AddRange(FormsUsed(source, code).Select(form => $"value-{code}#{form}"));
				}
				if (EntryContext) {
					columns.Add($"context-{code}");
				}
				if (EntryComment) {
					columns.Add($"comment-{code}");
				}
				if (Stale) {
					columns.Add($"stale-{code}");
				}
			}
			if (Review) {
				columns.Add("stale");
			}
			return columns;
		}

		//the forms the default or a language gets a column for, in CLDR's order: each one the
		//file's keys write in it, and each one the language counts by that a plural key wants
		//(MissingWords.Requires), so a translator has a column for a form not yet written
		private static IEnumerable<string> FormsUsed(ExportSource source, string? code) {
			HashSet<string> used = [.. source.Keys()
				.Select(key => code is null ? key.Forms : key.Entries.GetValueOrDefault(code)?.Forms)
				.OfType<Dictionary<string, string>>()
				.SelectMany(forms => forms.Written().Select(form => form.Key))];
			string language = code ?? source.File.DefaultLanguage ?? "";
			return PatTech.Localization.PluralRules.Names.Where(form => used.Contains(form) || MissingWords.Requires(language, form));
		}
	}

	//a schema section read: whether a header comes first, how cells are split and quoted, the
	//encoding, and each column's name and role, a column read past having none
	internal sealed class CsvSchema {
		public bool Header = true;
		public char Delimiter = ',';
		public char? Quote = '"';
		public string CharacterSet = "ANSI";
		public bool FixedLength;
		public List<CsvColumn> Columns = [];

		public static CsvSchema Parse(FormatOptions options, ICollection<string> gripes, string file) {
			var schema = new CsvSchema();
			if (options.TryGetValue(CsvCodec.HeaderOption, out string? header)) {
				schema.Header = !string.Equals(header.Trim(), "False", StringComparison.OrdinalIgnoreCase);
			}
			if (options.TryGetValue(CsvCodec.FormatOption, out string? format)) {
				string trimmed = format.Trim();
				if (CsvCodec.DelimiterOf(trimmed) is { } delimiter) {
					schema.Delimiter = delimiter;
				}
				else if (trimmed.Equals("FixedLength", StringComparison.OrdinalIgnoreCase)) {
					schema.FixedLength = true; //the caller says what came of it
				}
				else {
					gripes.Add($"{file}: Format={trimmed} names no delimiter, so it is taken as comma-delimited");
				}
			}
			if (options.TryGetValue(CsvCodec.CharacterSetOption, out string? characterSet)) {
				string trimmed = characterSet.Trim();
				if (CsvCodec.IsCharacterSet(trimmed)) {
					schema.CharacterSet = trimmed;
				}
				else if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int page) && CodePages.IsUtf32Page(page)) {
					gripes.Add($"{file}: CharacterSet={trimmed}: UTF-32 is no sheet encoding, so it is taken as ANSI");
				}
				else {
					gripes.Add($"{file}: CharacterSet={trimmed} names no code page here, so it is taken as ANSI");
				}
			}
			if (options.TryGetValue(CsvCodec.TextDelimiterOption, out string? quote)) {
				string trimmed = quote.Trim();
				schema.Quote = trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : trimmed.Length > 0 ? trimmed[0] : '"';
			}
			Dictionary<int, string> columns = [];
			foreach (var (key, value) in options) {
				if (SchemaIni.ColumnNumber(key) is { } number) {
					columns[number] = value;
				}
			}
			if (columns.Count > 0) {
				List<string?> names = [];
				for (int number = 1; number <= columns.Keys.Max(); number++) {
					names.Add(columns.TryGetValue(number, out string? column) ? CsvCodec.ReadColumn(column).Name : $"F{number}");
				}
				schema.Name(names, gripes, file, [.. Enumerable.Range(1, names.Count).Select(number => columns.TryGetValue(number, out string? column) ? CsvCodec.ReadColumn(column).Type : "Text")]);
			}
			return schema;
		}

		//the columns by their names, each name's role its own, and a role a column before
		//it took read past; with no names, a header's cells, as the driver names them
		public void Name(IReadOnlyList<string?> names, ICollection<string> gripes, string file, IReadOnlyList<string>? types = null) {
			Columns.Clear();
			HashSet<string> taken = [];
			for (int i = 0; i < names.Count; i++) {
				string name = names[i]?.Trim() is { Length: > 0 } given ? given : $"F{i + 1}";
				string type = types?[i] ?? "Text";
				string? role = CsvCodec.Role(name);
				if (role is not null && !taken.Add(role)) {
					gripes.Add($"{file}: column {i + 1} is {role}, as an earlier column is, so it is read past");
					role = null;
				}
				Columns.Add(new CsvColumn(name, type, role == CsvCodec.KeyColumn, role is null or CsvCodec.KeyColumn ? null : Field(role)));
			}
		}

		private static WordsField Field(string role) => WordsField.TryParse(role, out var field, out _) ? field : throw new ArgumentException(role);
	}

	//a column: its name and type as the schema gives them, and its role: the key, a field, or neither
	internal sealed record CsvColumn(string Name, string Type, bool IsKey, WordsField? Field);

	//a row: the line it starts on, and its cells, an empty one null and a quoted empty one empty
	internal sealed record CsvRow(int Line, List<string?> Cells) {
		public bool IsEmpty => Cells.All(string.IsNullOrEmpty);
		public string? Cell(int index) => index < Cells.Count ? Cells[index] : null;
	}

	internal static class CsvRows {
		/// <summary>
		///     Splits a sheet's text into rows as Excel quotes them: a cell opening with
		///     the quote runs to the quote that closes it, a doubled quote inside it is
		///     one, and a line break inside it is a line feed; rows end with CRLF, LF or
		///     CR. An empty cell is null and a quoted empty one empty. The
		///     <paramref name="limit"/> counts rows with anything in them, so blank lines
		///     never use it up.
		/// </summary>
		public static List<CsvRow> Split(string text, char delimiter, char? quote, int limit = int.MaxValue) {
			List<CsvRow> rows = [];
			List<string?> cells = [];
			var cell = new StringBuilder();
			bool quoted = false, inQuotes = false;
			int line = 1, rowLine = 1, full = 0;
			for (int i = 0; i < text.Length && full < limit; i++) {
				char c = text[i];
				if (inQuotes) {
					if (c == quote) {
						if (i + 1 < text.Length && text[i + 1] == quote) {
							cell.Append(c);
							i++;
						}
						else {
							inQuotes = false;
						}
					}
					else if (c is '\r' or '\n') {
						if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') {
							i++;
						}
						cell.Append('\n');
						line++;
					}
					else {
						cell.Append(c);
					}
				}
				else if (c == quote && cell.Length == 0 && !quoted) {
					inQuotes = quoted = true;
				}
				else if (c == delimiter) {
					EndCell();
				}
				else if (c is '\r' or '\n') {
					if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') {
						i++;
					}
					EndCell();
					AddRow();
					cells = [];
					rowLine = ++line;
				}
				else {
					cell.Append(c);
				}
			}
			if ((cells.Count > 0 || cell.Length > 0 || quoted) && full < limit) {
				EndCell();
				AddRow();
			}
			return rows;

			void AddRow() {
				var row = new CsvRow(rowLine, cells);
				rows.Add(row);
				if (!row.IsEmpty) {
					full++;
				}
			}

			void EndCell() {
				cells.Add(quoted || cell.Length > 0 ? cell.ToString() : null);
				cell.Clear();
				quoted = inQuotes = false;
			}
		}
	}

	/// <summary>The encodings a sheet and a <c>schema.ini</c> are read and written in, .NET's code pages registered once.</summary>
	internal static class CodePages {
		static CodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

		/// <summary>True when the platform has the code page.</summary>
		public static bool Known(int page) {
			try {
				_ = Writing(page);
				return true;
			}
			catch (Exception ex) when (ex is ArgumentException or NotSupportedException) {
				return false;
			}
		}

		/// <summary>The ANSI code page of <paramref name="culture"/>, the machine's when none.</summary>
		public static Encoding Ansi(CultureInfo? culture) => Reading((culture ?? CultureInfo.CurrentCulture).TextInfo.ANSICodePage);

		/// <summary>A code page to read in: what it cannot read becomes U+FFFD.</summary>
		public static Encoding Reading(int page) => page switch {
			65001 => new UTF8Encoding(false),
			1200 => new UnicodeEncoding(false, false),
			1201 => new UnicodeEncoding(true, false),
			_ => Encoding.GetEncoding(page, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback),
		};

		/// <summary>A code page to write in: UTF-8 and UTF-16 with a byte-order mark, which Excel reads them by, and a character the page has no byte for an error, never a <c>?</c>.</summary>
		public static Encoding Writing(int page) => page switch {
			65001 => new UTF8Encoding(true, true),
			1200 => new UnicodeEncoding(false, true, true),
			1201 => new UnicodeEncoding(true, true, true),
			_ => Encoding.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback),
		};

		/// <summary>The code page a byte-order mark names: 65001, UTF-32's 12000 or 12001, which comes first as it starts as UTF-16 LE's does, 1200 or 1201; none without one.</summary>
		public static int? Bom(byte[] bytes) => bytes switch {
			[0xEF, 0xBB, 0xBF, ..] => 65001,
			[0xFF, 0xFE, 0x00, 0x00, ..] => 12000,
			[0x00, 0x00, 0xFE, 0xFF, ..] => 12001,
			[0xFF, 0xFE, ..] => 1200,
			[0xFE, 0xFF, ..] => 1201,
			_ => null,
		};

		/// <summary>UTF-32, little-endian or big: no sheet's encoding, nor a <c>schema.ini</c>'s.</summary>
		public static bool IsUtf32Page(int page) => page is 12000 or 12001;

		/// <summary>True when the bytes' byte-order mark says UTF-32.</summary>
		public static bool IsUtf32(byte[] bytes) => Bom(bytes) is { } page && IsUtf32Page(page);

		/// <summary>True when the bytes read as UTF-8 without a fault.</summary>
		public static bool IsUtf8(byte[] bytes) {
			try {
				_ = new UTF8Encoding(false, true).GetString(bytes);
				return true;
			}
			catch (DecoderFallbackException) {
				return false;
			}
		}

		/// <summary>The text, in the encoding a byte-order mark names, else <paramref name="otherwise"/>; and the encoding to write it back in, the mark with it.</summary>
		public static (string Text, Encoding Encoding) Decode(byte[] bytes, Encoding otherwise) => Bom(bytes) switch {
			65001 => (new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true)),
			1200 => (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(false, true)),
			1201 => (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(true, true)),
			_ => (otherwise.GetString(bytes), otherwise),
		};
	}
}
