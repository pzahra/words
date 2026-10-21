using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using System.Globalization;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The spreadsheet codec (the editor's SPEC: Spreadsheets): a sheet read and
///     written as its section of the <c>schema.ini</c> beside it says, a column's
///     name its role; the sniff that guesses a section for a sheet with none; the
///     column options a new table is laid out by; and the loss that follows the
///     columns.
/// </summary>
public class CsvCodecTests {
	//the machine's code pages and list separator, pinned: ANSI 1252, OEM 437, a comma
	private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

	static CsvCodecTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

	//a file using every channel a sheet can carry, and some it cannot
	private const string Sample = """
		value-fr=Français
		value-de=Deutsch

		[greeting]
		context=shown at login
		value=Hello, "you"
		stale=
		param-0=String:who
		value-fr=Bonjour
		stale-fr=2026-01-01
		comment-fr=vu

		[files]
		value={n} file
		value#other={n} files
		value-fr={n} fichier
		value-fr#other={n} fichiers
		value-de={n} Datei
		stale-de=

		[$unit]
		value=kg

		[lines]
		value=x

		""";

	private static string Folder() {
		string folder = Path.Combine(Path.GetTempPath(), $"CsvCodec-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		return folder;
	}

	private static string Sheet(string folder, string text, Encoding? encoding = null, string name = "Strings.csv") {
		string path = Path.Combine(folder, name);
		File.WriteAllText(path, text, encoding ?? new UTF8Encoding(false));
		return path;
	}

	//the sample, its lines key holding line breaks, which a words.ini writes as continuations
	private static (WordsSession Session, WordsFile File, ExportSource Source) Loaded(string folder) {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(Sample), Path.Combine(folder, "Main.ini"));
		file.Keys["lines"].DefaultValue = "one\ntwo";
		file.Keys["lines"].Entries["fr"].Value = "un\ndeux";
		return (session, file, new ExportSource(session, file, KeyTree.Build(file)));
	}

	private static FormatOptions Options(params (string Key, string Value)[] pairs) {
		var options = new FormatOptions();
		foreach (var (key, value) in pairs) {
			options[key] = value;
		}
		return options;
	}

	[Fact]
	public void Schema_ReadsASectionWithoutCase_AndWritesOneLeavingEveryOtherLine() {
		string folder = Folder();
		try {
			string schema = Path.Combine(folder, SchemaIni.FileName);
			File.WriteAllText(schema, "; kept as it is\r\n[Other.csv]\r\nFormat=TabDelimited\r\n\r\n[STRINGS.CSV]\r\nColNameHeader=False\r\nformat=Delimited(;)\r\nCol2=\"my notes\" Text\r\nCol1=key Text\r\nMaxScanRows=0\r\n; above the next\r\n\r\n[Last.csv]\r\nFormat=CSVDelimited\r\n");
			string sheet = Path.Combine(folder, "Strings.csv");

			Assert.Equal(["Other.csv", "STRINGS.CSV", "Last.csv"], SchemaIni.Sections(sheet));
			FormatOptions options = SchemaIni.Read(sheet)!;
			Assert.Equal("Delimited(;)", options[CsvCodec.FormatOption]); //names without case, as the driver reads them
			Assert.Equal("0", options["maxscanrows"]); //the driver's own, kept
			Assert.Equal("TabDelimited", SchemaIni.Read(sheet, "other.csv")![CsvCodec.FormatOption]); //another sheet's, to borrow
			Assert.Null(SchemaIni.Read(Path.Combine(folder, "None.csv")));
			Assert.Null(SchemaIni.Read(Path.Combine(folder, "elsewhere", "Strings.csv")));

			//the columns: a name in quotes, a type read past
			CsvLayout layout = CsvLayout.From(Options(("Col1", "key Text"), ("Col2", "\"my notes\" Memo"), ("Col3", "\"value-fr\" Memo")));
			Assert.Equal(["fr"], layout.Languages);

			//one section written anew, in the driver's order, every other line as it was:
			//its header and its keys spelled as the file spells them
			options[CsvCodec.FormatOption] = "CSVDelimited";
			SchemaIni.Write(sheet, options);
			Assert.Equal("; kept as it is\r\n[Other.csv]\r\nFormat=TabDelimited\r\n\r\n[STRINGS.CSV]\r\nColNameHeader=False\r\nformat=CSVDelimited\r\nCol1=key Text\r\nCol2=\"my notes\" Text\r\nMaxScanRows=0\r\n; above the next\r\n\r\n[Last.csv]\r\nFormat=CSVDelimited\r\n", File.ReadAllText(schema));

			//a new section goes at the end, after a blank
			SchemaIni.Write(Path.Combine(folder, "New.csv"), Options(("Col1", "key Text"), (CsvCodec.HeaderOption, "True")));
			Assert.EndsWith("[Last.csv]\r\nFormat=CSVDelimited\r\n\r\n[New.csv]\r\nColNameHeader=True\r\nCol1=key Text\r\n", File.ReadAllText(schema));

			//a folder with none gets one: CRLF, no byte-order mark
			string bare = Path.Combine(folder, "bare");
			Directory.CreateDirectory(bare);
			SchemaIni.Write(Path.Combine(bare, "Strings.tsv"), Options((CsvCodec.FormatOption, "TabDelimited")));
			Assert.Equal("[Strings.tsv]\r\nFormat=TabDelimited\r\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(bare, SchemaIni.FileName)));

			//one in LF and the ANSI code page is written back so
			string unix = Path.Combine(folder, "unix");
			Directory.CreateDirectory(unix);
			File.WriteAllBytes(Path.Combine(unix, SchemaIni.FileName), Encoding.GetEncoding(1252).GetBytes("; café\n[A.csv]\nFormat=CSVDelimited\n"));
			SchemaIni.Write(Path.Combine(unix, "B.csv"), Options((CsvCodec.FormatOption, "TabDelimited")));
			Assert.Equal(Encoding.GetEncoding(1252).GetBytes("; café\n[A.csv]\nFormat=CSVDelimited\n\n[B.csv]\nFormat=TabDelimited\n"), File.ReadAllBytes(Path.Combine(unix, SchemaIni.FileName)));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_TakesEachColumnAsItsRole_QuotedAsExcelQuotes_AndGripesOnTheRowsItSkips() {
		string folder = Folder();
		try {
			string sheet = Sheet(folder,
				"key,value,value#one,value#other,context,Status,value-fr,value-fr#one,value-fr#other,stale-fr,comment-fr,stale\r\n" + //1
				"greeting,\"Hello, \"\"you\"\"\",,,shown at login,done,\"Bonjour, \"\"toi\"\"\",,,\"\",,check the tone\r\n" + //2
				"lines,\"one\r\ntwo\",,,,,\"un\ndeux\",,,,,\n" + //3 to 5
				"files,{n} files,{n} file,{n} files,,,{n} fichiers,{n} fichier,{n} fichiers,2026-01-01,au pluriel, Review\n" + //6
				"$unit,kg,,,,,,,,,,\n" + //7
				",orphan,,,,,,,,,,\n" + //8
				"greeting,again,,,,,,,,,,\n" + //9
				"bad key,x,,,,,,,,,,\n" + //10
				"long,x,,,,,,,,,,,extra\n" + //11
				"short,x,,,,,,,,STALE,,,\n" + //12: a trailing empty cell is no word
				"tiny,y\n" + //13
				",,,,\n"); //14
			SchemaIni.Write(sheet, Options((CsvCodec.HeaderOption, "True"), (CsvCodec.FormatOption, "CSVDelimited"), (CsvCodec.CharacterSetOption, "65001")));
			var codec = new CsvCodec(Us);
			var session = new WordsSession();

			WordsFile file = session.Import(codec, codec.Discover(sheet));

			Assert.Equal(Path.Combine(folder, "Strings-words.ini"), file.Path);
			Assert.Equal(["fr"], file.Languages);
			Assert.Equal(["greeting", "lines", "files", "$unit", "bad_key", "short", "tiny"], file.Keys.Keys);

			WordsKey greeting = file.Keys["greeting"];
			Assert.Equal("Hello, \"you\"", greeting.DefaultValue);
			Assert.Equal("shown at login", greeting.Context);
			Assert.Equal("Bonjour, \"toi\"", greeting.Entries["fr"].Value);
			Assert.Equal("", greeting.Entries["fr"].Stale); //"" too a mark with no text
			Assert.Equal("", greeting.Entries["fr"].Comment);
			Assert.Equal("check the tone", greeting.Review);

			WordsKey lines = file.Keys["lines"];
			Assert.Equal("one\ntwo", lines.DefaultValue); //a line break in a cell, CRLF or LF, a line feed
			Assert.Equal("un\ndeux", lines.Entries["fr"].Value);
			Assert.Null(lines.Review); //an empty cell no mark
			Assert.Null(lines.Entries["fr"].Stale);

			WordsKey files = file.Keys["files"];
			Assert.Equal("{n} file", files.Forms["one"]);
			Assert.Equal("{n} files", files.Forms["other"]);
			Assert.Equal("{n} fichier", files.Entries["fr"].Forms["one"]);
			Assert.Equal("{n} fichiers", files.Entries["fr"].Forms["other"]);
			Assert.Equal("2026-01-01", files.Entries["fr"].Stale);
			Assert.Equal("au pluriel", files.Entries["fr"].Comment);
			Assert.Equal("", files.Review); //its column's word a mark with no text, its case and spaces no matter

			Assert.True(file.Keys["$unit"].IsConstant);
			Assert.Equal("x", file.Keys["short"].DefaultValue);
			Assert.Equal("", file.Keys["short"].Entries["fr"].Stale);
			Assert.Equal("", file.Keys["tiny"].Entries["fr"].Value); //a row too short reads empty

			Assert.Equal([
				"Strings.csv: row 8 has no key, so it was skipped",
				"Strings.csv: row 9 gives 'greeting' again, which row 2 gave, and the first stands",
				"Strings.csv: 'bad key' is no words.ini key, loaded as 'bad_key'",
				"Strings.csv: row 11 has words beyond its 12 columns, so it was skipped",
			], file.Errors);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Theory]
	[InlineData("ANSI", 1252, "CSVDelimited", ',')]
	[InlineData("OEM", 437, "Delimited(;)", ';')]
	[InlineData("UNICODE", 1200, "TabDelimited", '\t')]
	[InlineData("65001", 65001, "Delimited(|)", '|')]
	[InlineData("1250", 1250, "CSVDelimited", ',')]
	public void Read_InTheSchemasEncodingAndDelimiter_ColumnsNamedByIt(string characterSet, int page, string format, char delimiter) {
		string folder = Folder();
		try {
			Encoding encoding = page switch {
				1200 => new UnicodeEncoding(false, false),
				65001 => new UTF8Encoding(false),
				_ => Encoding.GetEncoding(page),
			};
			//no header: the schema names the columns, one in quotes, one the driver's own
			string sheet = Sheet(folder, $"cafe{delimiter}Café{delimiter}Été{delimiter}mine\r\n", encoding);
			FormatOptions options = Options((CsvCodec.HeaderOption, "False"), (CsvCodec.FormatOption, format), (CsvCodec.CharacterSetOption, characterSet),
				("Col1", "key Text"), ("Col2", "\"value\" Memo"), ("Col3", "Value-FR Memo"), ("Col4", "F4 Text"));
			var codec = new CsvCodec(Us);

			WordsFile file = new WordsSession().Import(codec, [sheet], options);

			Assert.Empty(file.Errors);
			Assert.Equal("Café", file.Keys["cafe"].DefaultValue);
			Assert.Equal("Été", file.Keys["cafe"].Entries["fr"].Value); //a role's name without case
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_AByteOrderMarkWinsOverTheSchema() {
		string folder = Folder();
		try {
			var codec = new CsvCodec(Us);
			FormatOptions ansi = Options((CsvCodec.CharacterSetOption, "ANSI"));
			string utf8 = Sheet(folder, "key,value\r\ncafe,Café\r\n", new UTF8Encoding(true), "Utf8.csv");
			Assert.Equal("Café", new WordsSession().Import(codec, [utf8], ansi).Keys["cafe"].DefaultValue);
			string utf16 = Sheet(folder, "key,value\r\ncafe,Café\r\n", new UnicodeEncoding(true, true), "Utf16.csv");
			Assert.Equal("Café", new WordsSession().Import(codec, [utf16], ansi).Keys["cafe"].DefaultValue);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_RefusesASchemaWithNoKeyOrNoValue_AndAFixedWidthOne() {
		string folder = Folder();
		try {
			string sheet = Sheet(folder, "name,value,value-fr\r\ngreeting,Hello,Bonjour\r\n");
			var codec = new CsvCodec(Us);

			ILoadedWords noKey = codec.Read([sheet], Options((CsvCodec.HeaderOption, "True")));
			Assert.Empty(noKey.WordKeys);
			Assert.Equal(["Strings.csv: no column is the key (key), so nothing was read"], noKey.Errors);

			ILoadedWords noValue = codec.Read([sheet], Options(("Col1", "key Text"), ("Col2", "F2 Text"), ("Col3", "value-fr Memo")));
			Assert.Empty(noValue.WordKeys);
			Assert.Equal(["Strings.csv: no column is the default's value (value), so nothing was read"], noValue.Errors);

			ILoadedWords fixedWidth = codec.Read([sheet], Options((CsvCodec.FormatOption, "FixedLength"), ("Col1", "key Text"), ("Col2", "value Memo")));
			Assert.Empty(fixedWidth.WordKeys);
			Assert.Equal(["Strings.csv: Format=FixedLength: a fixed-width file is no sheet, so nothing was read"], fixedWidth.Errors);
			Assert.Empty(codec.Preview(sheet, Options((CsvCodec.FormatOption, "FixedLength")), 5));

			//a role two columns name is the first's
			ILoadedWords twice = codec.Read([sheet], Options(("Col1", "key Text"), ("Col2", "value Memo"), ("Col3", "value Memo")));
			Assert.Equal("Hello", twice.WordKeys["greeting"].DefaultValue);
			Assert.Equal(["Strings.csv: column 3 is value, as an earlier column is, so it is read past"], twice.Errors);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Preview_ShowsTheFirstRowsAsTheOptionsSplitThem() {
		string folder = Folder();
		try {
			string sheet = Sheet(folder, "key;value\r\na;\"b;c\"\r\n;\r\nd;e\r\n");
			var codec = new CsvCodec(Us);

			string[][] semicolons = [["key", "value"], ["a", "b;c"], ["", ""]];
			Assert.Equal(semicolons, codec.Preview(sheet, Options((CsvCodec.FormatOption, "Delimited(;)")), 3).Select(row => row.ToArray()));
			string[][] commas = [["key;value"], ["a;\"b;c\""]]; //a comma, and the quote no quote: one cell each
			Assert.Equal(commas, codec.Preview(sheet, Options((CsvCodec.TextDelimiterOption, "none")), 2).Select(row => row.ToArray()));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Sniff_GuessesTheDelimiterTheHeaderAndTheRoles() {
		string folder = Folder();
		try {
			var codec = new CsvCodec(Us);

			//a semicolon splits every row alike, a comma does not; a header, as it names
			//roles; the key and the value first, then roles by name, codes, and the rest read past
			string semi = Sheet(folder, "Key;Value;fr;French (de);Notes;value-it;Comment-FR\r\ngreeting;Hello, you;Salut;Hallo;mine;Ciao;vu\r\nbye;Bye;Salut;Tschüss;;;\r\n", name: "Semi.csv");
			FormatOptions options = codec.Sniff(semi);
			Assert.Equal(Options((CsvCodec.HeaderOption, "True"), (CsvCodec.FormatOption, "Delimited(;)"), (CsvCodec.CharacterSetOption, "65001"),
				("Col1", "key Text"), ("Col2", "value Memo"), ("Col3", "value-fr Memo"), ("Col4", "value-de Memo"), ("Col5", "Notes Text"), ("Col6", "value-it Memo"), ("Col7", "comment-fr Memo")), options);
			WordsFile file = new WordsSession().Import(codec, [semi]); //no section: the sniff's guess
			Assert.Empty(file.Errors);
			Assert.Equal(["fr", "de", "it"], file.Languages);
			Assert.Equal("Hello, you", file.Keys["greeting"].DefaultValue);
			Assert.Equal("Hallo", file.Keys["greeting"].Entries["de"].Value);
			Assert.Equal("vu", file.Keys["greeting"].Entries["fr"].Comment);
			Assert.Equal("Tschüss", file.Keys["bye"].Entries["de"].Value);

			//no header: the key and the value, by place
			string bare = Sheet(folder, "greeting,Hello\r\nbye,Bye\r\n", name: "Bare.csv");
			Assert.Equal(Options((CsvCodec.HeaderOption, "False"), (CsvCodec.FormatOption, "CSVDelimited"), (CsvCodec.CharacterSetOption, "ANSI"),
				("Col1", "key Text"), ("Col2", "value Memo")), codec.Sniff(bare));

			//two delimiters split alike: the one splitting most, then the list separator,
			//which is Excel's; a .tsv tries tab first
			Assert.Equal("CSVDelimited", codec.Sniff(Sheet(folder, "a,b,c;d\r\ne,f,g;h\r\n", name: "Wide.csv"))[CsvCodec.FormatOption]);
			string tie = Sheet(folder, "a,b;c\r\nd,e;f\r\n", name: "Tie.csv");
			Assert.Equal("CSVDelimited", codec.Sniff(tie)[CsvCodec.FormatOption]);
			var german = new CsvCodec(CultureInfo.GetCultureInfo("de-DE"));
			Assert.Equal(';', german.Culture.TextInfo.ListSeparator[0]);
			Assert.Equal("Delimited(;)", german.Sniff(tie)[CsvCodec.FormatOption]);
			Assert.Equal("CSVDelimited", german.Sniff(Path.Combine(folder, "Wide.csv"))[CsvCodec.FormatOption]);
			Assert.Equal("TabDelimited", codec.Sniff(Sheet(folder, "a\tb,c\r\nd\te,f\r\n", name: "Tabs.tsv"))[CsvCodec.FormatOption]);
			Assert.Equal("CSVDelimited", codec.Sniff(Sheet(folder, "a\tb,c\r\nd\te,f\r\n", name: "Tabs.csv"))[CsvCodec.FormatOption]);

			//the encoding: a byte-order mark, else UTF-8 when the bytes are and are not all ASCII, else ANSI
			Assert.Equal("65001", codec.Sniff(Sheet(folder, "a,b\r\n", new UTF8Encoding(true), "Bom.csv"))[CsvCodec.CharacterSetOption]);
			Assert.Equal("UNICODE", codec.Sniff(Sheet(folder, "a,b\r\n", new UnicodeEncoding(false, true), "Wide16.csv"))[CsvCodec.CharacterSetOption]);
			Assert.Equal("ANSI", codec.Sniff(Sheet(folder, "a,Café\r\n", Encoding.GetEncoding(1252), "Ansi.csv"))[CsvCodec.CharacterSetOption]);
			Assert.Equal("65001", codec.Sniff(Sheet(folder, "a,Café\r\n", new UTF8Encoding(false), "Utf8.csv"))[CsvCodec.CharacterSetOption]);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Layout_TicksWhatTheFileUses_HangsALanguagesChannelsOnALanguage_AndMakesTheColumns() {
		string folder = Folder();
		try {
			var (_, _, source) = Loaded(folder);
			var codec = new CsvCodec(Us);

			CsvLayout layout = CsvLayout.For(source);
			Assert.Equal(["fr", "de"], layout.Languages);
			Assert.Equal((true, false, false, true, true, true, true), (layout.Context, layout.Comment, layout.EntryContext, layout.EntryComment, layout.Stale, layout.Review, layout.Forms));
			Assert.True(layout.LanguageChannelsOpen);
			//the forms each language counts by, de's other though no key of it has one yet
			Assert.Equal(["key", "value", "value#other", "context", "value-fr", "value-fr#other", "comment-fr", "stale-fr", "value-de", "value-de#other", "comment-de", "stale-de", "stale"], layout.Columns(source));

			//a language's context, comment and stale mark hang on a language ticked
			layout.Languages.Clear();
			Assert.False(layout.LanguageChannelsOpen);
			Assert.Equal(["key", "value", "value#other", "context", "stale"], layout.Columns(source));
			layout.Languages.Add("de");
			layout.Forms = false;
			Assert.Equal(["key", "value", "context", "value-de", "comment-de", "stale-de", "stale"], layout.Columns(source));

			//made into a section's columns and read back from them
			CsvLayout back = CsvLayout.From(codec.Options(source, CsvLayout.For(source)));
			Assert.Equal(["fr", "de"], back.Languages);
			Assert.Equal((true, false, false, true, true, true, true), (back.Context, back.Comment, back.EntryContext, back.EntryComment, back.Stale, back.Review, back.Forms));

			//a file using none of it ticks none of it
			var session = new WordsSession();
			WordsFile plain = session.Load(new StringReader("value-fr=Français\n\n[k]\nvalue=x\nvalue-fr=y\n"), Path.Combine(folder, "Plain.ini"));
			CsvLayout bare = CsvLayout.For(new ExportSource(session, plain, KeyTree.Build(plain)));
			Assert.Equal((false, false, false, false, false, false, false), (bare.Context, bare.Comment, bare.EntryContext, bare.EntryComment, bare.Stale, bare.Review, bare.Forms));
			Assert.Equal(["key", "value", "value-fr"], bare.Columns(new ExportSource(session, plain, KeyTree.Build(plain))));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_ANewTable_ItsColumnsAsTicked_EveryCellWithWordsQuoted_InUtf8WithAMark() {
		string folder = Folder();
		try {
			var (_, _, source) = Loaded(folder);
			var codec = new CsvCodec(Us);

			ExportUnit unit = Assert.Single(codec.Plan(source, Path.Combine(folder, "Out")));
			Assert.Equal(Path.Combine(folder, "Out.csv"), unit.Path); //a sheet's extension when the target has none
			Assert.Equal(["fr", "de"], unit.Languages);
			Assert.Equal(Path.Combine(folder, "Out.tsv"), Assert.Single(codec.Plan(source, Path.Combine(folder, "Out.tsv"))).Path);

			List<string> gripes = [];
			IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, gripes), unit.Encoding);
			Assert.Empty(gripes);
			Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(unit.Path)[..3]);
			Assert.Equal(
				"\"key\",\"value\",\"value#other\",\"context\",\"value-fr\",\"value-fr#other\",\"comment-fr\",\"stale-fr\",\"value-de\",\"value-de#other\",\"comment-de\",\"stale-de\",\"stale\"\r\n" +
				"\"greeting\",\"Hello, \"\"you\"\"\",,\"shown at login\",\"Bonjour\",,\"vu\",\"2026-01-01\",,,,,\"review\"\r\n" + //a mark with no text its column's word
				"\"files\",\"{n} file\",\"{n} files\",,\"{n} fichier\",\"{n} fichiers\",,,\"{n} Datei\",,,\"stale\",\r\n" +
				"\"$unit\",\"kg\",,,,,,,,,,,\r\n" +
				"\"lines\",\"one\ntwo\",,,\"un\ndeux\",,,,,,,,\r\n",
				File.ReadAllText(unit.Path));

			//a fixed-width section, naming no columns, writes the same, comma-delimited
			var fixedWidth = new StringWriter();
			List<string> fixedGripes = [];
			codec.Write(source, unit, fixedWidth, fixedGripes, Options((CsvCodec.FormatOption, "FixedLength")));
			Assert.Equal(File.ReadAllText(unit.Path), fixedWidth.ToString());
			Assert.Equal(["Out.csv: Format=FixedLength: a fixed-width file is no sheet, so it was written comma-delimited"], fixedGripes);

			//under TextDelimiter=none only a cell that needs quoting is quoted, all the same
			var none = new StringWriter();
			FormatOptions unquoted = codec.Options(source, CsvLayout.For(source));
			unquoted[CsvCodec.TextDelimiterOption] = "none";
			codec.Write(source, unit, none, gripes, unquoted);
			Assert.StartsWith("key,value,", none.ToString());
			Assert.Contains("\r\ngreeting,\"Hello, \"\"you\"\"\",,shown at login,", none.ToString());
			Assert.Contains(",review\r\n", none.ToString());
			Assert.Equal(["Out.csv: TextDelimiter=none, but cells holding the delimiter or a line break were quoted with \""], gripes);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_ATableThatExists_KeepsItsSection_GripesOnALanguageItLacks_AndFailsOnACharacterItsCodePageLacks() {
		string folder = Folder();
		try {
			var (_, file, source) = Loaded(folder);
			var codec = new CsvCodec(Us);
			string sheet = Path.Combine(folder, "Kept.csv");
			FormatOptions section = Options((CsvCodec.HeaderOption, "True"), (CsvCodec.FormatOption, "Delimited(;)"), (CsvCodec.CharacterSetOption, "1252"),
				("Col1", "key Text"), ("Col2", "value Memo"), ("Col3", "value-fr Memo"), ("Col4", "Notes Text"), ("MaxScanRows", "0"));
			CsvCodec.SaveSchema(sheet, section);

			ExportUnit unit = Assert.Single(codec.Plan(source, sheet));
			Assert.Equal(1252, unit.Encoding!.CodePage);
			List<string> gripes = [];
			IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, gripes), unit.Encoding);
			Assert.Equal("\"key\";\"value\";\"value-fr\";\"Notes\"\r\n\"greeting\";\"Hello, \"\"you\"\"\";\"Bonjour\";\r\n\"files\";\"{n} file\";\"{n} fichier\";\r\n\"$unit\";\"kg\";;\r\n\"lines\";\"one\ntwo\";\"un\ndeux\";\r\n",
				File.ReadAllText(sheet, Encoding.GetEncoding(1252)));
			Assert.Equal(["Kept.csv: no column carries de, so its words were not written"], gripes);

			//what it drops follows its columns
			Assert.Equal(WordsFeatures.Context | WordsFeatures.EntryComment | WordsFeatures.Stale | WordsFeatures.NeedsReview | WordsFeatures.PluralForms | WordsFeatures.Parameters,
				codec.Loses(source, section));

			//the tick: the columns set from the section, changed, and the section written anew,
			//its delimiter, encoding and the driver's own keys kept
			CsvLayout layout = CsvLayout.From(section);
			layout.Languages.Add("de");
			CsvCodec.SaveSchema(sheet, codec.Options(source, layout, section));
			FormatOptions changed = codec.OptionsFor(source, sheet);
			Assert.Equal(Options((CsvCodec.HeaderOption, "True"), (CsvCodec.FormatOption, "Delimited(;)"), (CsvCodec.CharacterSetOption, "1252"),
				("Col1", "key Text"), ("Col2", "value Memo"), ("Col3", "value-fr Memo"), ("Col4", "value-de Memo"), ("MaxScanRows", "0")), changed);

			//a character the code page has no byte for fails the write, and leaves the sheet as it was
			file.Keys["lines"].Entries["de"].Value = "日本";
			ExportUnit again = Assert.Single(codec.Plan(source, sheet));
			byte[] before = File.ReadAllBytes(sheet);
			Assert.Throws<EncoderFallbackException>(() => IniWriter.WriteAtomic(again.Path, writer => codec.Write(source, again, writer, []), again.Encoding));
			Assert.Equal(before, File.ReadAllBytes(sheet));
			Assert.Equal(["Kept.csv", "schema.ini"], Directory.GetFiles(folder).Select(Path.GetFileName).Order()); //no temp left behind
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void ExportImportExport_ThroughOneSchema_GivesTheSameBytes_AndTheSniffReadsItBackWhole() {
		string folder = Folder();
		try {
			var (session, original, source) = Loaded(folder);
			var codec = new CsvCodec(Us);
			string sheet = Path.Combine(folder, "Out.csv");
			FormatOptions options = codec.Options(source, CsvLayout.For(source));
			CsvCodec.SaveSchema(sheet, options);
			ExportUnit unit = Assert.Single(codec.Plan(source, sheet, options));
			IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, [], options), unit.Encoding);
			byte[] first = File.ReadAllBytes(sheet);

			void Unchanged(WordsFile again) {
				Assert.Empty(again.Errors);
				Assert.Equal(["fr", "de"], again.Languages);
				Assert.Equal(original.Keys.Keys, again.Keys.Keys);
				foreach (WordsKey before in session.KeysOf(original)) {
					WordsKey after = again.Keys[before.BlockKey];
					Assert.Equal(before.DefaultValue, after.DefaultValue);
					Assert.Equal(before.Context, after.Context);
					Assert.Equal(before.Review, after.Review);
					Assert.Equal(before.IsConstant, after.IsConstant);
					Assert.Equal(before.Forms.Written(), after.Forms.Written());
					foreach (string code in (string[])["fr", "de"]) {
						Assert.Equal(before.Entries[code].Value, after.Entries[code].Value);
						Assert.Equal(before.Entries[code].Stale, after.Entries[code].Stale);
						Assert.Equal(before.Entries[code].Comment, after.Entries[code].Comment);
						Assert.Equal(before.Entries[code].Forms.Written(), after.Entries[code].Forms.Written());
					}
				}
			}

			var through = new WordsSession();
			WordsFile read = through.Import(codec, [sheet]); //by its section
			Unchanged(read);
			var back = new ExportSource(through, read, KeyTree.Build(read));
			ExportUnit second = Assert.Single(codec.Plan(back, sheet));
			IniWriter.WriteAtomic(second.Path, writer => codec.Write(back, second, writer, []), second.Encoding);
			Assert.Equal(first, File.ReadAllBytes(sheet));

			//without its section, the sniff reads it whole
			File.Delete(SchemaIni.For(sheet));
			Unchanged(new WordsSession().Import(codec, [sheet]));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Loss_FollowsTheColumns() {
		string folder = Folder();
		try {
			var (_, _, source) = Loaded(folder);
			var codec = new CsvCodec(Us);
			CsvLayout layout = CsvLayout.For(source);

			//the parameter has no column, whatever is ticked
			Assert.Equal(WordsFeatures.Parameters, codec.Loses(source, codec.Options(source, layout)));
			Assert.Equal(codec.Info.Features, codec.Keeps());

			layout.Context = false;
			layout.Forms = false;
			WordsFeatures lost = codec.Loses(source, codec.Options(source, layout));
			Assert.Equal(WordsFeatures.Parameters | WordsFeatures.Context | WordsFeatures.PluralForms, lost);
			Assert.Equal(WordsFeatures.Constants, codec.Keeps(Options(("Col1", "key Text"), ("Col2", "value Memo")))); //constants by the key's $
			Assert.Equal(codec.Info.Features, codec.Keeps(Options((CsvCodec.HeaderOption, "True")))); //no columns: the layout the file's use ticks
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
