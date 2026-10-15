using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     A language manager's list brought to one file's table (editor SPEC:
///     Languages): <see cref="LanguageTable.Apply"/> orders the operations so that
///     a swap, a cycle or a code handed from a removed language to a new one goes
///     through, and makes them through whatever stands in front of the table.
/// </summary>
public class LanguagePlanTests {
	private const string Ini = "value-en=English\nvalue-de=Deutsch\nvalue-fr=Français\n\n[k]\nvalue=x\nvalue-en=ex\nvalue-de=ix\nvalue-fr=ixe\n";

	private static (WordsSession Session, WordsFile File) Load(string ini = Ini) {
		var session = new WordsSession();
		return (session, session.Load(new StringReader(ini), "Main"));
	}

	//the file's languages as a manager opens on them
	private static List<PlannedLanguage> Rows(WordsSession session, WordsFile file)
		=> [.. session.Languages.For(file).Select(label => new PlannedLanguage(label, new LanguageEntry(label), label.Code == file.DefaultLanguage))];

	private static PlannedLanguage Recode(PlannedLanguage row, string code) => row with { Label = new LanguageEntry(row.Label) { Code = code } };

	private static string Said(WordsSession session, string code) => session.FileNamed("Main").Keys["k"].Entries[code].Value;

	private static void NothingParked(WordsSession session, WordsFile file) {
		Assert.DoesNotContain(session.Languages.Known, language => language.Code.StartsWith("zz-"));
		Assert.DoesNotContain(file.Languages, code => code.StartsWith("zz-"));
	}

	[Fact]
	public void ASwap_AndACycle_GoThrough_TheWordsFollowingTheirLanguage() {
		var (session, file) = Load();
		var rows = Rows(session, file);

		Assert.Empty(session.Languages.Apply(file, [Recode(rows[0], "de"), Recode(rows[1], "en"), rows[2]]));
		Assert.Equal(["de", "en", "fr"], file.Languages);
		Assert.Equal(("ex", "ix"), (Said(session, "de"), Said(session, "en")));
		Assert.Equal("English", file.Labels["de"].NativeName); //a re-code keeps the row's label
		NothingParked(session, file);

		rows = Rows(session, file);
		Assert.Empty(session.Languages.Apply(file, [Recode(rows[0], "en"), Recode(rows[1], "fr"), Recode(rows[2], "de")]));
		Assert.Equal(("ex", "ix", "ixe"), (Said(session, "en"), Said(session, "fr"), Said(session, "de")));
		NothingParked(session, file);
	}

	[Fact]
	public void ANewRow_TakesARemovedLanguagesCode_WithoutItsWords() {
		var (session, file) = Load("value-en=English\n\n[k]\nvalue=x\nvalue-en=ex\n");

		Assert.Empty(session.Languages.Apply(file, [new PlannedLanguage(null, new LanguageEntry("en", "English (new)"))]));

		Assert.Equal(["en"], file.Languages);
		Assert.Equal("English (new)", file.Labels["en"].NativeName);
		Assert.Equal("", Said(session, "en"));
		NothingParked(session, file);
	}

	[Fact]
	public void TheDefaultsLanguage_IsDeclaredOnlyWhenTheTickMoved() {
		var (session, file) = Load("value=!it\n" + Ini); //written in a language the table does not list
		var rows = Rows(session, file);

		session.Languages.Apply(file, rows);
		Assert.Equal("it", file.DefaultLanguage); //no row had the tick, and none has it now

		session.Languages.Apply(file, [rows[0] with { IsDefault = true }, rows[1], rows[2]]);
		Assert.Equal("en", file.DefaultLanguage);

		rows = Rows(session, file);
		session.Languages.Apply(file, [Recode(rows[0], "en-AU"), rows[1], rows[2]]);
		Assert.Equal("en-AU", file.DefaultLanguage); //the ticked row re-coded, the default with it
	}

	[Fact]
	public void TheTable_TakesTheRowsOrder_AndWhatItRefusesIsSaid() {
		var (session, file) = Load();
		var rows = Rows(session, file);

		session.Languages.Apply(file, [rows[2], rows[0], rows[1]]);
		Assert.Equal(["fr", "en", "de"], file.Languages);

		var (alone, only) = Load("value-en=English\n\n[k]\nvalue=x\n");
		Assert.Equal(["en"], alone.Languages.Apply(only, [])); //a file's last language stays
		Assert.Equal(["en"], only.Languages);
	}

	//what stands in front of the table makes every change, as the editor's undo records them
	[Fact]
	public void EveryOperation_GoesThroughTheEditsGiven() {
		var (session, file) = Load();
		var rows = Rows(session, file);
		var edits = new Recorder(session.Languages);

		session.Languages.Apply(file, [Recode(rows[0], "de"), Recode(rows[1], "en"), rows[2] with { IsDefault = true }], edits);

		Assert.Equal(["Rename de", "Rename en", "Rename zz-", "Declare fr"], edits.Made);
		Assert.Equal(("ex", "ix"), (Said(session, "de"), Said(session, "en")));
	}

	private sealed class Recorder(LanguageTable table) : ILanguageEdits {
		public List<string> Made { get; } = [];

		public bool Add(WordsFile file, LanguageEntry language) => Note($"Add {language.Code}", table.Add(file, language));
		public bool Remove(WordsFile file, string code) => Note($"Remove {code}", table.Remove(file, code));
		public LanguageEntry Rename(WordsFile file, string code, LanguageEntry replacement) => Note($"Rename {(code.StartsWith("zz-") ? "zz-" : code)}", table.Rename(file, code, replacement));
		public void Reorder(WordsFile file, int from, int to) {
			if (from != to) { //a move that moves nothing is no change
				Made.Add($"Reorder {from} {to}");
			}
			table.Reorder(file, from, to);
		}

		public void Declare(WordsFile file, string? code) {
			Made.Add($"Declare {code}");
			table.Declare(file, code);
		}

		private T Note<T>(string made, T result) {
			Made.Add(made);
			return result;
		}
	}
}
