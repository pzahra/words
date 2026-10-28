using PatTech.Localization.Authoring;
using PatTech.Text.Ini;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The shared corpus (editor SPEC: Ini files through the driver): Core's reader, the ini
///     driver's words dialect and the writer, pinned to read a <c>words.ini</c> alike. Each text
///     reads, key by key and field by field, the same through Core and through the dialect, and
///     each key Core reads, written as the writer writes it, reads back the same through the
///     dialect. Core is the authority on the grammar: a case the corpus finds them reading apart
///     is pinned as it reads, noted, and settled with Core's next change.
/// </summary>
public class WordsCorpusTests {
	public static TheoryData<string, string> Corpus => new() {
		{ "a marker pair ending a line", "[a]\nvalue=one\\_\n\n[b]\nvalue=one_\\\n" },
		{ "doubled markers and quotes", "[a]\nvalue=a\\\\b__c''d\n" },
		{ "a continued value", "[a]\nvalue=one\\\ntwo_\nthree\ncomment=c\n" },
		{ "a continuation taking a header", "[s]\nvalue=a\\\n[t]\ncomment=c\n" },
		{ "a continuation taking a blank line", "[a]\nvalue=a\\\n\ncomment=c\n" },
		{ "the spaces ending a value", "[a]\nvalue=a  \ncomment=  c\n" },
		{ "indented lines", "[a]\n  value=A\n  [b]\ncomment=c\n" },
		{ "a chain and its cuts", "[menu]\n[.file]\nvalue=File\n\n[.edit]\nvalue=Edit\n[.edit.undo]\nvalue=Undo\n\n[about]\nvalue=About\n" },
		{ "a chain re-based by a full header", "[menu]\n[.file]\nvalue=File\n[tools.a]\nvalue=A\n[.b]\nvalue=B\n" },
		{ "the language table", "value=!en\nvalue-fr=Français\nvalue-de=\n\n[k]\nvalue=x\nvalue-fr=y\n" },
		{ "the fields under [] read past", "[a]\nvalue=A\n[]\nvalue=junk\n[b]\nvalue=B\n" },
		{ "either separator", "[a]\nvalue:A\ncomment = c\n" },
		{ "a ; after a value is its own", "[a]\nvalue=A ; not a comment\n\n; a comment\n[b]\nvalue=B\n" },
		{ "a header's tail read past", "[a] the tail\nvalue=A\n" },
		{ "plural forms, parameters and stale marks", "[n]\nvalue={count} file\nvalue#other={count} files\nparam-count=int:how many\nvalue-fr={count} fichier\nstale-fr=2026-01-01\n" },
		{ "field names in any case", "[a]\nVALUE=A\nValue-FR=B\n" },
		{ "a key declared twice", "[a]\nvalue=A\n\n[b]\nvalue=B\n\n[a]\ncomment=again\n" },
		{ "a constant", "[$brand]\nvalue=Words\n\n[a]\nvalue={$brand}\n" },
		{ "names compared exactly", "[A]\nvalue=1\n\n[a]\nvalue=2\n" },
		{ "a stale mark and a parameter without words", "[a]\nvalue=A\nparam-n=\nstale=\nvalue-fr=B\nstale-fr=\n" },
		{ "the spaces inside a header's brackets", "[ a ]\nvalue=A\n\n[b]\nvalue=B\n[ .c]\nvalue=C\n" },
	};

	//the language table, the settings references and the comments, as Core and as the dialect read them
	[Theory]
	[InlineData("value=!en\nvalue-fr=Français\nvalue-de=\n\n; above k\n[k]\nvalue=x\n\n;\tlast\n")]
	[InlineData("value=Words\nvalue-mt=Malti\nvalue-it=Italiano\nparam=settings.ini\nparam-it=it.ini\n\n[k]\nvalue=x\n")]
	public void CoreAndTheWordsDialect_ReadTheFileAlike(string text) {
		var document = IniDocument.Read(Encoding.UTF8.GetBytes(text), WordsDialect.Instance);
		var core = WordsReading.Of(text);
		//the dialect's section "" as Core reads labels
		var table = Read(string.Concat(document.Keys("").Select(key => $"{key}={document.Get("", key)}\n")));

		Assert.Equal(core.Words.DeclaredLanguages, table.DeclaredLanguages);
		Assert.True(WordsDialect.SameTable(core.Words, table));
		Assert.Equal(core.Words.DefaultLanguage, table.DefaultLanguage);
		Assert.Equal(core.Words.Settings, table.Settings);
		Assert.Equal(core.Words.LanguageSettings.OrderBy(pair => pair.Key), table.LanguageSettings.OrderBy(pair => pair.Key));
		Assert.Equal(core.Comments, document.Elements.OfType<IniComment>().SelectMany(block => block.Lines).Select(line => line.TrimStart()[1..]));
	}

	[Theory]
	[MemberData(nameof(Corpus))]
	public void CoreAndTheWordsDialect_ReadEachKeyAlike(string name, string text) {
		Assert.NotNull(name);
		Assert.Equal(Core(text), Dialect(text));
	}

	[Theory]
	[MemberData(nameof(Corpus))]
	public void EachKeyTheWriterWrites_ReadsBackAlikeThroughTheWordsDialect(string name, string text) {
		Assert.NotNull(name);
		var read = Read(text);
		var written = new StringWriter { NewLine = "\n" };
		using (var ini = new IniWriter(written)) {
			foreach (WordsKey key in read.WordKeys.Values.Where(key => !key.IsEmpty())) {
				ini.WriteBlock(key);
			}
		}
		Assert.Equal(Core(text), Dialect(written.ToString()));
	}

	//the driver's GroupCuts over section names and the writer's over its tree, alike
	[Theory]
	[InlineData("value=!en\n\n[app.title]\nvalue=Words\n\n[about]\nvalue=About\n\n", "app.version")]
	[InlineData("value=!en\n\n[menu]\n[.file]\nvalue=File\n\n[.edit]\nvalue=Edit\n\n", "menu.view")]
	[InlineData("value=!en\n\n[menu]\n[.file]\nvalue=File\n\n[.edit]\nvalue=Edit\n\n", "menu.file.recent")]
	[InlineData("value=!en\n\n[tools]\n[.a.x]\nvalue=X\n\n[.b]\nvalue=B\n\n", "tools.a.y")]
	[InlineData("value=!en\n\n[a]\nvalue=A\n\n", "b.c")]
	[InlineData("", "a")]
	public void AKeyTheDialectAdds_IsCutAsTheWriterCutsIt(string text, string key) {
		Assert.Equal(text, Saved(text));
		using var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes(text));

		patcher.Set(key, new WordsField("value", "", ""), "New");

		//the key added to the model and saved, apart from the patch
		Assert.Equal(Saved(text, (session, file) => session.AddKey(file, key).DefaultValue = "New"), patcher.Text);
	}

	//a text as Wordsmith's Save writes it, edited on the model first
	private static string Saved(string text, Action<WordsSession, WordsFile>? edit = null) {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(text), "Corpus");
		edit?.Invoke(session, file);
		var written = new StringWriter { NewLine = "\n" };
		session.Save(file, KeyTree.Build(file), written);
		return written.ToString();
	}

	[Fact]
	public void TheLanguageTable_IsTheSectionAboveTheFirstHeader() {
		var document = IniDocument.Read(Encoding.UTF8.GetBytes("value=!en\nvalue-fr=Français\n\n[k]\nvalue=x\n"), WordsDialect.Instance);

		Assert.Equal(["value", "value-fr"], document.Keys(""));
		Assert.Equal("Français", document.Get("", "value-fr"));
		Assert.Equal(["fr"], Read(document.Text).DeclaredLanguages);
		Assert.Equal("en", Read(document.Text).DefaultLanguage);
	}

	private static WordsParserToLocalizationProvider Read(string text) {
		var words = new WordsParserToLocalizationProvider();
		new WordsParser(words).Load(new StringReader(text));
		return words;
	}

	//key → field → text as Core reads them, a bare group left out and an empty field none
	private static SortedDictionary<string, SortedDictionary<string, string>> Core(string text) {
		SortedDictionary<string, SortedDictionary<string, string>> keys = new(StringComparer.Ordinal);
		foreach (var (name, key) in Read(text).WordKeys) {
			if (!key.IsEmpty()) {
				keys[name] = new(WordsField.All(key).ToDictionary(pair => pair.Field.ToString(), pair => pair.Text), StringComparer.Ordinal);
			}
		}
		return keys;
	}

	//the same as the words dialect reads them
	private static SortedDictionary<string, SortedDictionary<string, string>> Dialect(string text) {
		var document = IniDocument.Read(Encoding.UTF8.GetBytes(text), WordsDialect.Instance);
		SortedDictionary<string, SortedDictionary<string, string>> keys = new(StringComparer.Ordinal);
		foreach (string section in document.Sections.Where(section => section.Length > 0).Distinct()) {
			SortedDictionary<string, string> fields = new(StringComparer.Ordinal);
			foreach (string key in document.Keys(section)) {
				//an empty field is none, but for a stale mark and a parameter, as Core reads them
				if (document.Get(section, key) is { } value && WordsField.TryParse(key, out var field, out _)
					&& (value.Length > 0 || field.Type is "stale" or "param")) {
					fields[field.ToString()] = value;
				}
			}
			if (fields.Count > 0) {
				keys[section] = fields;
			}
		}
		return keys;
	}
}
