using PatTech.Localization;
using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using PatTech.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The spreadsheet's dialogs (SPEC: Spreadsheets, The dialog and Export),
///     driven headless with <see cref="FakeDialogs"/>: Import opens the sheet's
///     section, the guess, or a section borrowed, and writes the section it reads
///     by; Export shows a new table's ticks, or a table's columns and the tick
///     that changes them, and writes the section with the sheet. What the codec
///     reads and writes is its own tests' business.
/// </summary>
public class SpreadsheetTests {
	private const string Sheet = "key,value,fr,Notes\r\ngreeting,Hello,Bonjour,mine\r\nbye,Bye,Salut,\r\n";

	private const string Ini = @"
value-en=English
value-fr=Français

[greeting]
context=Said on arrival
comment=Keep it short
value=Hello
value-fr=Bonjour
";

	private static string Folder() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSpreadsheet-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		return folder;
	}

	//picks the sheet in Import, hands its dialog to answer, and says whether the dialog came
	private static (MainWindowViewModel vm, FakeDialogs dialogs) Import(string sheet, Action<SheetImportViewModel> answer) {
		var dialogs = new FakeDialogs { FilesToOpen = [sheet] };
		var vm = new MainWindowViewModel(dialogs);
		dialogs.OnShow = dialog => answer(Assert.IsType<SheetImportViewModel>(dialog));
		vm.ImportCommand.Execute(null);
		return (vm, dialogs);
	}

	[Fact]
	public void Import_OpensWithTheGuess_ShowsTheFirstRows_AndWritesTheSectionItReadsBy() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, Sheet);

			var (vm, dialogs) = Import(sheet, dialog => {
				FakeDialogs.Rendered(dialog.Blurb);
				SheetChoice layout = Assert.Single(dialog.Layouts); //no schema.ini: the guess alone
				Assert.Same(layout, dialog.Layout);
				Assert.Equal("", layout.Value);
				Assert.True(dialog.Header);
				Assert.Equal("CSVDelimited", dialog.Delimiter!.Value);
				Assert.Equal("ANSI", dialog.CharacterSet!.Value);
				Assert.All(dialog.Delimiters.Concat(dialog.CharacterSets), choice => FakeDialogs.Rendered(choice.Label));
				Assert.Equal(["key", "value", "value-fr", ""], dialog.Columns.Select(column => column.Role));
				Assert.Equal(Words.Known["sheet.read-past"], dialog.Columns[3].Meaning);
				Assert.Equal(Words.Known["sheet.role-key"], dialog.Columns[0].Meaning);
				Assert.Equal(Words.Known.Format("sheet.role-value-in", "French"), dialog.Columns[2].Meaning); //named as the import will declare it
				Assert.Contains("value-fr", dialog.Roles);
				Assert.Equal([true, false, false], dialog.Rows.Select(row => row.IsHeader));
				Assert.Equal(["greeting", "Hello", "Bonjour", "mine"], dialog.Rows[1].Cells.Select(cell => cell.Text));
				Assert.Equal(["bye", "Bye", "Salut", ""], dialog.Rows[2].Cells.Select(cell => cell.Text));
				Assert.True(dialog.CanImport);

				//a delimiter that splits each row in one: one column, and the roles come back with the comma
				dialog.Delimiter = dialog.Delimiters.Single(choice => choice.Value == "Delimited(;)");
				Assert.Single(dialog.Columns);
				Assert.Equal(["key,value,fr,Notes"], dialog.Rows[0].Cells.Select(cell => cell.Text));
				Assert.Equal(Words.Known["sheet.no-value"], dialog.Problem);
				dialog.Delimiter = dialog.Delimiters[0];
				Assert.Equal(["key", "value", "value-fr", ""], dialog.Columns.Select(column => column.Role));

				dialog.ImportCommand.Execute(null);
			});

			WordsFile file = Assert.Single(vm.Session.Files);
			Assert.Equal(Path.Combine(folder, "Strings-words.ini"), file.Path);
			Assert.Equal("Bonjour", file.Keys["greeting"].Entries["fr"].Value);
			Assert.Equal("Salut", file.Keys["bye"].Entries["fr"].Value);
			Assert.True(vm.IsDirty); //the words.ini is not on disk yet
			Assert.Empty(dialogs.Notices);
			Assert.Equal(new FormatOptions {
				[CsvCodec.HeaderOption] = "True", [CsvCodec.FormatOption] = "CSVDelimited", [CsvCodec.CharacterSetOption] = "ANSI",
				["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "value-fr Memo", ["Col4"] = "Notes Text",
			}, SchemaIni.Read(sheet));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_Cancelled_ImportsNothing_AndWritesNothing() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, Sheet);

			var (vm, dialogs) = Import(sheet, dialog => dialog.CancelCommand.Execute(null));

			Assert.Single(dialogs.Shown);
			Assert.Empty(vm.Session.Files);
			Assert.False(File.Exists(SchemaIni.For(sheet)));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_ARoleCorrected_ReadsAsCorrected_AndARoleAmissHoldsImportBack() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, Sheet);

			var (vm, _) = Import(sheet, dialog => {
				//no key, a role twice, a role that is none: each said, and Import does nothing
				dialog.Columns[0].Role = "";
				Assert.Equal(Words.Known["sheet.no-key"], dialog.Problem);
				Assert.False(dialog.CanImport);
				dialog.ImportCommand.Execute(null);
				Assert.Null(dialog.Options);
				dialog.Columns[0].Role = "Key"; //no matter the case
				dialog.Columns[2].Role = "value";
				Assert.Equal(Words.Known.Format("sheet.twice", "value"), dialog.Problem);
				dialog.Columns[2].Role = "notes";
				Assert.Equal(Words.Known.Format("sheet.bad-role", "notes"), FakeDialogs.Rendered(dialog.Problem));
				Assert.Equal(Words.Known["sheet.no-role"], dialog.Columns[2].Meaning);

				//the language's value, and the column read past made its comment
				dialog.Columns[2].Role = "value-fr";
				dialog.Columns[3].Role = "comment-fr";
				Assert.Equal("", dialog.Problem);
				Assert.Equal(Words.Known.Format("sheet.role-comment-in", "French"), dialog.Columns[3].Meaning);
				dialog.ImportCommand.Execute(null);
			});

			WordsKey greeting = Assert.Single(vm.Session.Files).Keys["greeting"];
			Assert.Equal("mine", greeting.Entries["fr"].Comment);
			Assert.Equal("comment-fr Memo", SchemaIni.Read(sheet)!["Col4"]);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_OpensWithTheSheetsSection_OrOneBorrowed() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, "greeting;Hello;Hallo\r\n");
			CsvCodec.SaveSchema(sheet, new FormatOptions {
				[CsvCodec.HeaderOption] = "False", [CsvCodec.FormatOption] = "Delimited(;)", [CsvCodec.CharacterSetOption] = "65001",
				["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "value-de Memo",
			});
			byte[] schema = File.ReadAllBytes(SchemaIni.For(sheet));

			//the sheet's own section: what it went out with
			var (own, _) = Import(sheet, dialog => {
				Assert.Equal(["Strings.csv", ""], dialog.Layouts.Select(layout => layout.Value));
				Assert.Equal(Words.Known["sheet.layout-own"], dialog.Layout!.Label);
				Assert.False(dialog.Header);
				Assert.Equal("Delimited(;)", dialog.Delimiter!.Value);
				Assert.Equal("65001", dialog.CharacterSet!.Value);
				Assert.Equal(["key", "value", "value-de"], dialog.Columns.Select(column => column.Role));
				Assert.Equal([false], dialog.Rows.Select(row => row.IsHeader));
				dialog.ImportCommand.Execute(null);
			});
			Assert.Equal("Hallo", Assert.Single(own.Session.Files).Keys["greeting"].Entries["de"].Value);
			Assert.Equal(schema, File.ReadAllBytes(SchemaIni.For(sheet))); //the same section, written the same

			//a translator's copy, with no section of its own, borrows the layout it went out with
			string copy = Path.Combine(folder, "Strings_fr.csv");
			File.WriteAllText(copy, "greeting;Hello;Bonjour\r\n");
			var (borrowed, _) = Import(copy, dialog => {
				Assert.Equal(["", "Strings.csv"], dialog.Layouts.Select(layout => layout.Value));
				Assert.Equal("", dialog.Layout!.Value); //the guess first
				dialog.Layout = dialog.Layouts[1];
				FakeDialogs.Rendered(dialog.Layout.Label);
				Assert.Equal(["key", "value", "value-de"], dialog.Columns.Select(column => column.Role));
				dialog.Columns[2].Role = "value-fr";
				dialog.ImportCommand.Execute(null);
			});
			Assert.Equal("Bonjour", Assert.Single(borrowed.Session.Files).Keys["greeting"].Entries["fr"].Value);
			Assert.Equal(["Strings.csv", "Strings_fr.csv"], SchemaIni.Sections(sheet));
			Assert.Equal("value-de Memo", SchemaIni.Read(sheet)!["Col3"]); //the one borrowed from stands
			Assert.Equal("value-fr Memo", SchemaIni.Read(copy)!["Col3"]);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	//the file loaded, Export open on it, the sheet the format and the target
	private static (FakeDialogs dialogs, ExportViewModel export) Export(string folder, string target, string ini = Ini) {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader(ini), Path.Combine(folder, "Strings.ini"));
		vm.ExportCommand.Execute(null);
		var export = Assert.IsType<ExportViewModel>(Assert.Single(dialogs.Shown));
		dialogs.Shown.Clear();
		Assert.Null(export.Sheet);
		export.Format = export.Formats.Single(row => row.Format.Info.Id == "csv");
		export.Target = target;
		return (dialogs, export);
	}

	[Fact]
	public void Export_ToANewTable_TicksWhatTheFileUses_AndWritesTheSectionWithTheSheet() {
		string folder = Folder();
		try {
			string target = Path.Combine(folder, "Out.csv");
			var (_, export) = Export(folder, target);

			SheetColumnsViewModel sheet = export.Sheet!;
			Assert.True(sheet.IsNew);
			Assert.True(sheet.ShowTicks);
			Assert.False(sheet.CanChange);
			Assert.Equal(["en", "fr"], sheet.Languages.Where(language => language.IsTicked).Select(language => language.Code));
			Assert.Equal([WordsFeatures.Context, WordsFeatures.Comment], sheet.Features.Where(feature => feature.IsTicked).Select(feature => feature.Feature));
			Assert.All(sheet.Features, feature => FakeDialogs.Rendered(feature.Name));
			Assert.Equal(Words.Known.Format("export.sheet-columns", "key, value, context, comment, value-en, value-fr"), sheet.Columns);
			Assert.Equal(Words.Known["export.loses-nothing"], export.Loss); //left alone, it loses nothing a column could keep

			//a tick off: the column goes, and the loss line says so
			sheet.Features.Single(feature => feature.Feature == WordsFeatures.Context).IsTicked = false;
			Assert.Same(sheet, export.Sheet); //the ticks stay as the plan follows them
			Assert.Equal(Words.Known.Format("export.sheet-columns", "key, value, comment, value-en, value-fr"), sheet.Columns);
			Assert.Contains(WordsFeatures.Context.Describe("S"), export.Loss);

			//a language's channels grey with no language ticked
			foreach (SheetLanguageTick language in sheet.Languages) {
				language.IsTicked = false;
			}
			Assert.False(sheet.LanguageChannelsOpen);
			Assert.All(sheet.Features.Where(feature => (feature.Feature & (WordsFeatures.EntryContext | WordsFeatures.EntryComment | WordsFeatures.Stale)) != 0),
				feature => Assert.False(feature.IsEnabled));
			sheet.Languages.Single(language => language.Code == "fr").IsTicked = true;
			Assert.True(sheet.Features.Single(feature => feature.Feature == WordsFeatures.Stale).IsEnabled);

			bool closed = false;
			export.CloseRequested += () => closed = true;
			export.ExportCommand.Execute(null);

			Assert.True(closed);
			//a new table is delimited by the list separator, as Excel is
			char d = new CsvCodec().Culture.TextInfo.ListSeparator[0];
			Assert.Equal($"\"key\"{d}\"value\"{d}\"comment\"{d}\"value-fr\"\r\n\"greeting\"{d}\"Hello\"{d}\"Keep it short\"{d}\"Bonjour\"\r\n", File.ReadAllText(target));
			Assert.Equal(new FormatOptions {
				[CsvCodec.HeaderOption] = "True", [CsvCodec.FormatOption] = CsvCodec.FormatFor(d), [CsvCodec.CharacterSetOption] = "65001",
				["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "comment Memo", ["Col4"] = "value-fr Memo",
			}, SchemaIni.Read(target));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_ToATableThatExists_KeepsItsColumns_OrChangesThemByTheTick() {
		string folder = Folder();
		try {
			string target = Path.Combine(folder, "Out.csv");
			CsvCodec.SaveSchema(target, new FormatOptions {
				[CsvCodec.HeaderOption] = "True", [CsvCodec.FormatOption] = "Delimited(;)", [CsvCodec.CharacterSetOption] = "65001",
				["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "value-fr Memo", ["MaxScanRows"] = "0",
			});
			byte[] schema = File.ReadAllBytes(SchemaIni.For(target));
			var (_, export) = Export(folder, target);

			SheetColumnsViewModel sheet = export.Sheet!;
			Assert.False(sheet.IsNew);
			Assert.False(sheet.ShowTicks);
			Assert.True(sheet.CanChange);
			Assert.Equal(Words.Known.Format("export.sheet-columns", "key, value, value-fr"), sheet.Columns);
			Assert.Contains(WordsFeatures.Context.Describe("S"), export.Loss); //what its columns drop

			export.ExportCommand.Execute(null);
			Assert.Equal("\"key\";\"value\";\"value-fr\"\r\n\"greeting\";\"Hello\";\"Bonjour\"\r\n", File.ReadAllText(target));
			Assert.Equal(schema, File.ReadAllBytes(SchemaIni.For(target))); //kept as it was

			//the tick: the columns open, set from the section, and the section is written anew
			(_, export) = Export(folder, target);
			sheet = export.Sheet!;
			sheet.Change = true;
			Assert.True(sheet.ShowTicks);
			Assert.Equal(["fr"], sheet.Languages.Where(language => language.IsTicked).Select(language => language.Code));
			Assert.Empty(sheet.Features.Where(feature => feature.IsTicked));
			sheet.Features.Single(feature => feature.Feature == WordsFeatures.Context).IsTicked = true;
			Assert.Equal(Words.Known.Format("export.sheet-columns", "key, value, context, value-fr"), sheet.Columns);
			export.ExportCommand.Execute(null);

			Assert.Equal("\"key\";\"value\";\"context\";\"value-fr\"\r\n\"greeting\";\"Hello\";\"Said on arrival\";\"Bonjour\"\r\n", File.ReadAllText(target));
			Assert.Equal(new FormatOptions {
				[CsvCodec.HeaderOption] = "True", [CsvCodec.FormatOption] = "Delimited(;)", [CsvCodec.CharacterSetOption] = "65001",
				["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "context Memo", ["Col4"] = "value-fr Memo", ["MaxScanRows"] = "0",
			}, SchemaIni.Read(target));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_ToASectionNamingNoColumns_GoesByTheColumnsTheFileUses_AndLeavesTheSection() {
		string folder = Folder();
		try {
			string target = Path.Combine(folder, "Out.csv");
			CsvCodec.SaveSchema(target, new FormatOptions {
				[CsvCodec.HeaderOption] = "True", [CsvCodec.FormatOption] = "Delimited(;)", [CsvCodec.CharacterSetOption] = "65001",
			});
			byte[] schema = File.ReadAllBytes(SchemaIni.For(target));
			var (dialogs, export) = Export(folder, target);

			SheetColumnsViewModel sheet = export.Sheet!;
			Assert.False(sheet.IsNew);
			Assert.Equal(Words.Known.Format("export.sheet-columns", "key, value, context, comment, value-en, value-fr"), sheet.Columns);
			sheet.Change = true; //the ticks open as the file uses them
			Assert.Equal(["en", "fr"], sheet.Languages.Where(language => language.IsTicked).Select(language => language.Code));
			Assert.Equal([WordsFeatures.Context, WordsFeatures.Comment], sheet.Features.Where(feature => feature.IsTicked).Select(feature => feature.Feature));
			sheet.Change = false;

			export.ExportCommand.Execute(null);
			Assert.Equal("\"key\";\"value\";\"context\";\"comment\";\"value-en\";\"value-fr\"\r\n\"greeting\";\"Hello\";\"Said on arrival\";\"Keep it short\";;\"Bonjour\"\r\n", File.ReadAllText(target));
			Assert.Equal(schema, File.ReadAllBytes(SchemaIni.For(target))); //still naming none

			//with no header either, nothing names the columns, and Export says the sheet won't read back
			CsvCodec.SaveSchema(target, new FormatOptions {
				[CsvCodec.HeaderOption] = "False", [CsvCodec.FormatOption] = "Delimited(;)", [CsvCodec.CharacterSetOption] = "65001",
			});
			(dialogs, export) = Export(folder, target);
			export.ExportCommand.Execute(null);
			GripesViewModel gripes = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
			Assert.Contains(gripes.Gripes, gripe => gripe.Contains("names no columns"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	//a value holding the delimiter, and a translation a line break
	private const string Awkward = @"
value-en=English
value-fr=Français

[greeting]
value=Hello; you
value-fr=Bonjour\
à vous
";

	//a table saying TextDelimiter=none, which can't keep the awkward cells as they are
	private static void SaveBare(string target)
		=> CsvCodec.SaveSchema(target, new FormatOptions {
			[CsvCodec.HeaderOption] = "True", [CsvCodec.FormatOption] = "Delimited(;)", [CsvCodec.CharacterSetOption] = "65001",
			[CsvCodec.TextDelimiterOption] = "none", ["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "value-fr Memo",
		});

	private static WordsKey ReadBack(string target)
		=> new WordsSession().Import(new CsvCodec(), [target], SchemaIni.Read(target)).Keys["greeting"];

	[Fact]
	public void Export_UnderTextDelimiterNone_ACellThatNeedsQuoting_ChangesTheSectionToQuote() {
		string folder = Folder();
		try {
			string target = Path.Combine(folder, "Out.csv");
			SaveBare(target);
			var (dialogs, export) = Export(folder, target, Awkward);
			Assert.False(export.Sheet!.WritesSection);
			bool closed = false;
			export.CloseRequested += () => closed = true;
			export.ExportCommand.Execute(null);

			Assert.True(closed);
			Assert.Empty(dialogs.Notices);
			Assert.Equal("\"", SchemaIni.Read(target)![CsvCodec.TextDelimiterOption]);
			Assert.Equal("value-fr Memo", SchemaIni.Read(target)!["Col3"]); //the rest as it was
			Assert.Equal("\"key\";\"value\";\"value-fr\"\r\n\"greeting\";\"Hello; you\";\"Bonjour\nà vous\"\r\n", File.ReadAllText(target));
			GripesViewModel gripes = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
			Assert.Equal(Words.Known.Format("export.quoted", SchemaIni.For(target)), Assert.Single(gripes.Gripes));
			WordsKey read = ReadBack(target);
			Assert.Equal("Hello; you", read.DefaultValue);
			Assert.Equal("Bonjour\nà vous", read.Entries["fr"].Value);

			//the ticks changed too: the switch to a quote is said all the same
			SaveBare(target);
			(dialogs, export) = Export(folder, target, Awkward);
			export.Sheet!.Change = true;
			export.ExportCommand.Execute(null);
			Assert.Equal("\"", SchemaIni.Read(target)![CsvCodec.TextDelimiterOption]);
			gripes = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
			Assert.Equal(Words.Known.Format("export.quoted", SchemaIni.For(target)), Assert.Single(gripes.Gripes));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_UnderTextDelimiterNone_ASectionLocked_WritesTheCellsBare_WithSpacesForWhatNeededAQuote() {
		string folder = Folder();
		string schema = "";
		try {
			string target = Path.Combine(folder, "Out.csv");
			SaveBare(target);
			schema = SchemaIni.For(target);
			byte[] before = File.ReadAllBytes(schema);
			File.SetAttributes(schema, FileAttributes.ReadOnly);
			var (dialogs, export) = Export(folder, target, Awkward);
			bool closed = false;
			export.CloseRequested += () => closed = true;
			export.ExportCommand.Execute(null);

			Assert.True(closed);
			Assert.Empty(dialogs.Notices); //no failure: the sheet went by its section
			Assert.Equal(before, File.ReadAllBytes(schema));
			Assert.Equal("key;value;value-fr\r\ngreeting;Hello  you;Bonjour à vous\r\n", File.ReadAllText(target));
			GripesViewModel gripes = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
			Assert.Equal(2, gripes.Gripes.Count);
			Assert.StartsWith(Words.Known.Format("export.schema-locked", schema, "").Split('')[0], gripes.Gripes[0]);
			Assert.Contains("(greeting)", gripes.Gripes[1]);
			WordsKey read = ReadBack(target);
			Assert.Equal("Hello  you", read.DefaultValue);
			Assert.Equal("Bonjour à vous", read.Entries["fr"].Value);
		}
		finally {
			if (File.Exists(schema)) {
				File.SetAttributes(schema, FileAttributes.Normal);
			}
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_ChangedTicks_ASectionThatCantBeWritten_WritesNothing_AndStaysOpen() {
		string folder = Folder();
		string schema = "";
		try {
			string target = Path.Combine(folder, "Out.csv");
			SaveBare(target);
			schema = SchemaIni.For(target);
			File.SetAttributes(schema, FileAttributes.ReadOnly);
			var (dialogs, export) = Export(folder, target, Awkward);
			export.Sheet!.Change = true;
			bool closed = false;
			export.CloseRequested += () => closed = true;
			export.ExportCommand.Execute(null);

			Assert.False(closed);
			Assert.Contains(schema, Assert.Single(dialogs.Notices));
			Assert.False(File.Exists(target));
			Assert.Empty(dialogs.Shown);

			//a schema.ini in UTF-16, which the driver doesn't read, is refused the same way
			File.SetAttributes(schema, FileAttributes.Normal);
			File.WriteAllText(schema, "[Out.csv]\r\nFormat=CSVDelimited\r\n", System.Text.Encoding.Unicode);
			byte[] utf16 = File.ReadAllBytes(schema);
			(dialogs, export) = Export(folder, target);
			Assert.True(export.Sheet!.WritesSection); //read as no file: a new table
			export.ExportCommand.Execute(null);
			Assert.Contains(schema, Assert.Single(dialogs.Notices));
			Assert.False(File.Exists(target));
			Assert.Equal(utf16, File.ReadAllBytes(schema));
		}
		finally {
			if (File.Exists(schema)) {
				File.SetAttributes(schema, FileAttributes.Normal);
			}
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_TheSheetsWriteFailing_PutsTheSchemaBack_OrSaysItCouldNot() {
		string folder = Folder();
		try {
			string target = Path.Combine(folder, "Out.csv");
			SaveBare(target);
			string schema = SchemaIni.For(target);
			byte[] before = File.ReadAllBytes(schema);
			var (dialogs, export) = Export(folder, target, Awkward);
			string? seen = null;
			export.WriteFile = (_, _, _) => {
				seen = SchemaIni.Read(target)![CsvCodec.TextDelimiterOption];
				throw new IOException("the sheet is open elsewhere");
			};
			bool closed = false;
			export.CloseRequested += () => closed = true;
			export.ExportCommand.Execute(null);

			Assert.Equal("\"", seen); //the section went first
			Assert.False(closed);
			Assert.Contains(target, Assert.Single(dialogs.Notices));
			Assert.Equal(before, File.ReadAllBytes(schema)); //and back, byte for byte
			Assert.False(File.Exists(target));

			//a new table's schema.ini, made by Export, goes again
			File.Delete(schema);
			(dialogs, export) = Export(folder, target);
			export.WriteFile = (_, _, _) => throw new IOException("the sheet is open elsewhere");
			export.ExportCommand.Execute(null);
			Assert.Single(dialogs.Notices);
			Assert.False(File.Exists(schema));

			//putting it back failing too: both are said, and the plan goes by what Export wrote
			SaveBare(target);
			(dialogs, export) = Export(folder, target, Awkward);
			export.WriteFile = (_, _, _) => throw new IOException("the sheet is open elsewhere");
			export.PutBackSchema = (_, _) => throw new IOException("the schema is open elsewhere");
			export.ExportCommand.Execute(null);
			Assert.Equal(2, dialogs.Notices.Count);
			Assert.Contains(target, dialogs.Notices[0]);
			Assert.Equal(Words.Known.Format("export.schema-changed", schema, target, "the schema is open elsewhere"), dialogs.Notices[1]);
			Assert.Equal("\"", SchemaIni.Read(target)![CsvCodec.TextDelimiterOption]); //what Export wrote, as it says
			Assert.Equal("\"", export.Sheet!.Options[CsvCodec.TextDelimiterOption]);

			//so trying again, the schema.ini now locked, quotes the sheet as its section says
			File.SetAttributes(schema, FileAttributes.ReadOnly);
			export.WriteFile = IniWriter.WriteAtomic;
			export.ExportCommand.Execute(null);
			Assert.Equal("\"key\";\"value\";\"value-fr\"\r\n\"greeting\";\"Hello; you\";\"Bonjour\nà vous\"\r\n", File.ReadAllText(target));
		}
		finally {
			foreach (string file in Directory.GetFiles(folder)) {
				File.SetAttributes(file, FileAttributes.Normal);
			}
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_ASchemaThatCantBeRead_LeavesNoPlan_AndSaysWhy() {
		string folder = Folder();
		string other = Folder();
		try {
			string old = Path.Combine(folder, "Old.csv");
			var (_, export) = Export(folder, old);
			Assert.True(export.CanExport);

			string schema = Path.Combine(other, SchemaIni.FileName);
			File.WriteAllText(schema, "[New.csv]\r\nFormat=CSVDelimited\r\n");
			string target = Path.Combine(other, "New.csv");
			using (new FileStream(schema, FileMode.Open, FileAccess.Read, FileShare.None)) {
				export.Target = target;
				Assert.False(export.CanExport);
				Assert.Empty(export.Plan);
				Assert.Null(export.Sheet);
				Assert.True(export.HasPlanProblem);
				Assert.Contains(schema, FakeDialogs.Rendered(export.PlanProblem));
				export.ExportCommand.Execute(null);
			}
			Assert.False(File.Exists(old));
			Assert.False(File.Exists(target));

			//the lock gone, the target typed again plans again
			export.Target = "";
			export.Target = target;
			Assert.True(export.CanExport);
			Assert.False(export.HasPlanProblem);
			Assert.Equal("", export.PlanProblem);
		}
		finally {
			Directory.Delete(folder, recursive: true);
			Directory.Delete(other, recursive: true);
		}
	}

	[Fact]
	public void Import_ASheetInUtf32_IsRefusedWithTheReason_AndASectionNamingItReadsAsAnsi() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, Sheet, new System.Text.UTF32Encoding(bigEndian: false, byteOrderMark: true));
			var (vm, _) = Import(sheet, dialog => {
				Assert.Equal(Words.Known["sheet.utf32"], FakeDialogs.Rendered(dialog.Problem));
				Assert.False(dialog.CanImport);
				Assert.Empty(dialog.Rows);
				dialog.ImportCommand.Execute(null);
				Assert.Null(dialog.Options);
				dialog.CancelCommand.Execute(null);
			});
			Assert.Empty(vm.Session.Files);

			//a section naming UTF-32 for a sheet that is not: ANSI, and UTF-32 never offered
			File.WriteAllText(sheet, "greeting,Hello\r\n");
			foreach (string page in new[] { "12000", "12001" }) {
				CsvCodec.SaveSchema(sheet, new FormatOptions {
					[CsvCodec.HeaderOption] = "False", [CsvCodec.FormatOption] = "CSVDelimited", [CsvCodec.CharacterSetOption] = page,
					["Col1"] = "key Text", ["Col2"] = "value Memo",
				});
				Import(sheet, dialog => {
					Assert.Equal("ANSI", dialog.CharacterSet!.Value);
					Assert.DoesNotContain(dialog.CharacterSets, choice => choice.Value is "12000" or "12001");
					Assert.Equal("", dialog.Problem);
					dialog.CancelCommand.Execute(null);
				});
			}
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_ASectionThatCantBeSaved_IsSaid_AndImportsNothing() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, Sheet);
			string schema = SchemaIni.For(sheet);
			File.WriteAllText(schema, "[Other.csv]\r\nFormat=CSVDelimited\r\n", System.Text.Encoding.Unicode);
			byte[] utf16 = File.ReadAllBytes(schema);

			var (vm, dialogs) = Import(sheet, dialog => dialog.ImportCommand.Execute(null));

			Assert.Empty(vm.Session.Files);
			Assert.Contains(schema, Assert.Single(dialogs.Notices));
			Assert.Equal(utf16, File.ReadAllBytes(schema));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_KeepsTheColumnsASectionDeclares_ThoughTheFirstRowsLeaveThemEmpty() {
		string folder = Folder();
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, string.Concat(Enumerable.Range(1, 8).Select(n => $"k{n},Value {n}\r\n")) + "k9,Value 9,Valeur 9\r\n");
			CsvCodec.SaveSchema(sheet, new FormatOptions {
				[CsvCodec.HeaderOption] = "False", [CsvCodec.FormatOption] = "CSVDelimited", [CsvCodec.CharacterSetOption] = "65001",
				["Col1"] = "key Text", ["Col2"] = "value Memo", ["Col3"] = "value-fr Memo",
			});

			var (vm, _) = Import(sheet, dialog => {
				Assert.Equal(["key", "value", "value-fr"], dialog.Columns.Select(column => column.Role));
				//a delimiter that splits nothing narrows to one column; back, the three return, roles kept
				dialog.Delimiter = dialog.Delimiters.Single(choice => choice.Value == "Delimited(;)");
				Assert.Single(dialog.Columns);
				dialog.Delimiter = dialog.Delimiters.Single(choice => choice.Value == "CSVDelimited");
				Assert.Equal(["key", "value", "value-fr"], dialog.Columns.Select(column => column.Role));
				dialog.Header = true;
				dialog.Header = false; //a choice that refreshes keeps them too
				Assert.Equal(["key", "value", "value-fr"], dialog.Columns.Select(column => column.Role));
				dialog.ImportCommand.Execute(null);
			});

			Assert.Equal("value-fr Memo", SchemaIni.Read(sheet)!["Col3"]);
			Assert.Equal("Valeur 9", Assert.Single(vm.Session.Files).Keys["k9"].Entries["fr"].Value);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
