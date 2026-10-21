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
	private static (FakeDialogs dialogs, ExportViewModel export) Export(string folder, string target) {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader(Ini), Path.Combine(folder, "Strings.ini"));
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
}
