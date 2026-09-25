using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using PatTech.Utils;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The format seam (SPEC: Import and export): the document surface every
///     importer loads through, the export source and its loss, the registry, and
///     the native ini written to the interface as its own reference — reading what
///     Load reads, writing what Save writes, losing nothing.
/// </summary>
public class WordsFormatsTests {
	//a file using every feature the model has, in exactly the shape the writer
	//emits, so the round trips are byte for byte — in the platform's line ending,
	//which the writer uses whatever this source file was checked out with
	private static readonly string Full = @"; about Main
value-en=English
comment-en=English
value-fr=Français
comment-fr=French
param=wordsmith.ini

[greeting]
context=shown at login
comment=keep it short
value=Hello {0}
param-0=String:Pat
stale=
value-fr=Bonjour {0}
stale-fr=2026-01-01
context-fr=formal
comment-fr=vu

[$unit]
value=kg

[menu]
; the file menu
[.file]
value=File
value-fr=Fichier

[.file.open]
value=Open
value-fr=Ouvrir

; the end
".ReplaceLineEndings();

	private static string Save(WordsSession session, WordsFile file) {
		var output = new StringWriter();
		session.Save(file, KeyTree.Build(session, file), output);
		return output.ToString();
	}

	private static ExportSource Source(WordsSession session, WordsFile file)
		=> new(session, file, KeyTree.Build(session, file));

	/// <summary>Reads nothing from disk: the surface is built by hand, the way a foreign importer builds it.</summary>
	private sealed class FooImporter : IWordsImporter {
		public WordsFormatInfo Info { get; } = new("foo", [".foo"], WordsFeatures.None);
		public TextReader? Init() => null;
		public IReadOnlyList<string> Discover(string path) => [path];
		public string NativePath(IReadOnlyList<string> paths) => Path.ChangeExtension(paths[0], ".ini");
		public ILoadedWords Read(IReadOnlyList<string> paths, FormatOptions? options = null) {
			var loaded = new LoadedWords();
			loaded.Declare("en", "English");
			loaded.Key("a").DefaultValue = Path.GetFileName(paths[0]);
			loaded.Key("a").Entries["en"].Value = "A";
			loaded.Key("empty"); //no fields: the session drops it, as it drops a bare header
			loaded.Declare("fr", "Français", "French"); //declared after a key: the key gets its entry
			loaded.Errors.Add("guessed something");
			return loaded;
		}
	}

	/// <summary>Keeps the two context channels and nothing else; writes one line per key.</summary>
	private sealed class ContextOnly : IWordsExporter {
		public WordsFormatInfo Info { get; } = new("ctx", [".ctx"], WordsFeatures.Context | WordsFeatures.Comment);
		public TextReader? Init() => null;
		public IReadOnlyList<ExportUnit> Plan(ExportSource source, string target, FormatOptions? options = null) => [new ExportUnit(target, [])];
		public void Write(ExportSource source, ExportUnit unit, TextWriter writer, ICollection<string> gripes, FormatOptions? options = null) {
			foreach (WordsKey key in source.Keys()) {
				writer.WriteLine($"{key.BlockKey}={key.DefaultValue}");
			}
			gripes.Add("dropped the rest");
		}
	}

	[Fact]
	public void IniCodec_ReadsWhatLoadReads_AndImportIsLoad() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsFormats-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Main.ini");
			File.WriteAllText(path, Full);
			var codec = new IniCodec();

			IReadOnlyList<string> discovered = codec.Discover(path);
			Assert.Equal([path], discovered);

			var session = new WordsSession();
			WordsFile file = session.Import(codec, discovered);

			//the native path is the ini itself: importing ini is loading it
			Assert.Equal(path, file.Path);
			Assert.Same(file, session.FileAt(path));
			Assert.Empty(file.Errors);
			Assert.Equal(Full, Save(session, file));

			//ini is one file; a set is a caller's mistake, not a partial read
			Assert.Throws<ArgumentException>(() => codec.Read([path, path]));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void IniCodec_WritesWhatSaveWrites_AndLosesNothing() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(Full), "Main.ini");
		var codec = new IniCodec();
		ExportSource source = Source(session, file);

		Assert.Equal(WordsFeatures.All, source.Used()); //the fixture uses everything
		Assert.Equal(WordsFeatures.None, codec.Loses(source));

		ExportUnit unit = Assert.Single(codec.Plan(source, "Out.ini"));
		Assert.Equal("Out.ini", unit.Path);
		Assert.Equal(["en", "fr"], unit.Languages);

		var output = new StringWriter();
		List<string> gripes = [];
		codec.Write(source, unit, output, gripes);
		Assert.Empty(gripes);
		Assert.Equal(Full, output.ToString());
		Assert.Equal(Save(session, file), output.ToString());
	}

	[Fact]
	public void Load_ASurfaceBuiltByHand_GoesThroughTheSamePipeline() {
		//label disambiguation, empty-key dropping, language backfill, the gripes
		//convention: an importer inherits all of it from the one load path
		var session = new WordsSession();
		var importer = new FooImporter();

		WordsFile one = session.Import(importer, [Path.Combine("one", "strings.foo")]);
		WordsFile two = session.Import(importer, [Path.Combine("two", "strings.foo")]);

		//the foreign file becomes a native one: same name, ini extension
		Assert.Equal(Path.Combine("one", "strings.ini"), one.Path);
		Assert.Equal(["strings", "strings-2"], session.Files.Select(file => file.Label));
		Assert.Equal(["strings.a", "strings-2.a"], session.Keys.Keys);
		Assert.Equal("strings.foo", session.Keys["strings.a"].DefaultValue);
		Assert.Equal("A", session.Keys["strings.a"].Entries["en"].Value);
		Assert.True(session.Keys["strings.a"].Entries.ContainsKey("fr"));
		Assert.Equal(["en", "fr"], two.Languages);
		Assert.Equal("French", session.Languages.Find("fr")!.EnglishName);
		Assert.Equal(["guessed something"], one.Errors);
		Assert.Contains("value-fr=Français", Save(session, one));

		//and nothing at all is a caller's mistake
		Assert.Throws<ArgumentException>(() => session.Import(importer, []));
	}

	[Fact]
	public void ExportSource_WalksTheTreeAndIsRefusedOnSavesTerms() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(Full), "Main.ini");

		//the tree decides the order, as it does for Save: siblings as built, a
		//parent before its children
		var reversed = KeyTree.Build(file.Label, session.KeysOf(file).Reverse(), file.BlockComments);
		var source = new ExportSource(session, file, reversed);
		Assert.Equal(["Main.menu.file", "Main.menu.file.open", "Main.$unit", "Main.greeting"], source.Keys().Select(key => key.BlockKey));
		Assert.Equal(["en", "fr"], source.Languages.Select(language => language.Code));

		//a tree that is not the file's, or misses some of its keys, is refused before a byte lands
		var alien = KeyTree.Build("Other", session.KeysOf(file), file.BlockComments);
		Assert.Throws<InvalidOperationException>(() => new ExportSource(session, file, alien));
		var partial = KeyTree.Build(file.Label, session.KeysOf(file).Take(1), file.BlockComments);
		Assert.Throws<InvalidOperationException>(() => new ExportSource(session, file, partial));
	}

	[Fact]
	public void Loses_IsWhatTheDocumentUsesMinusWhatTheFormatKeeps() {
		var session = new WordsSession();
		WordsFile full = session.Load(new StringReader(Full), "Main.ini");
		WordsFile plain = session.Load(new StringReader("value-en=English\n\n[k]\nvalue=x\nvalue-en=ex\n"), "Plain.ini");
		var format = new ContextOnly();

		Assert.Equal(WordsFeatures.All & ~(WordsFeatures.Context | WordsFeatures.Comment), format.Loses(Source(session, full)));
		//values are not a feature: a plain file loses nothing, even to a format keeping nothing
		Assert.Equal(WordsFeatures.None, Source(session, plain).Used());
		Assert.Equal(WordsFeatures.None, format.Loses(Source(session, plain)));

		//the comment half of FreeComments follows the tree, where the editor keeps comments
		WordsFile commented = session.Load(new StringReader("value-en=English\n\n[k]\n; above k\nvalue=x\n"), "Commented.ini");
		Assert.Equal(WordsFeatures.FreeComments, Source(session, commented).Used());
		var bare = KeyTree.Build(commented.Label, session.KeysOf(commented), new Dictionary<string, string>());
		Assert.Equal(WordsFeatures.None, new ExportSource(session, commented, bare).Used());

		var output = new StringWriter();
		List<string> gripes = [];
		ExportSource source = Source(session, full);
		format.Write(source, Assert.Single(format.Plan(source, "Out.ctx")), output, gripes);
		Assert.Equal("Main.greeting=Hello {0}\nMain.$unit=kg\nMain.menu.file=File\nMain.menu.file.open=Open\n", output.ToString().ReplaceLineEndings("\n"));
		Assert.Equal(["dropped the rest"], gripes);
	}

	[Fact]
	public void Registry_FindsByIdAndExtension_RefusesADuplicate() {
		WordsFormats formats = WordsFormats.BuiltIn();

		Assert.Equal(["ini", "resx", "xliff"], formats.All.Select(format => format.Info.Id));
		IWordsFormat ini = formats.All[0];
		Assert.IsType<IniCodec>(ini);
		Assert.Same(ini, formats.Find("ini"));
		Assert.IsType<ResxCodec>(formats.Find("resx"));
		Assert.Null(formats.Find("csv"));
		Assert.Same(ini, formats.ImporterFor(Path.Combine("x", "Strings.INI"))); //extensions compare loosely
		Assert.Same(ini, formats.ExporterFor("Strings.ini"));
		Assert.IsType<ResxCodec>(formats.ImporterFor("Strings.fr.resx"));
		Assert.IsType<XliffCodec>(formats.ImporterFor("Strings.fr.xlf"));
		Assert.IsType<XliffCodec>(formats.ExporterFor("Strings.xliff"));
		Assert.Null(formats.ImporterFor("Strings.csv"));
		Assert.Equal("format.ini.name", ini.Info.NameKey);

		formats.Add(new FooImporter());
		Assert.Equal(["ini", "resx", "xliff", "foo"], formats.Importers.Select(format => format.Info.Id));
		Assert.Equal(["ini", "resx", "xliff"], formats.Exporters.Select(format => format.Info.Id)); //an importer alone does not export
		Assert.Throws<ArgumentException>(() => formats.Add(new FooImporter()));
	}

	[Fact]
	public void Registry_LoadWords_BringsEachFormatsChrome_UnlistedAndUnderTheHosts() {
		List<string> gripes = [];
		var collector = new GripeCollector();
		WordsBuilder builder;
		using (collector.Listen(gripes)) {
			builder = WordsFormats.BuiltIn().LoadWords(WordsBuilder.Create(collector));
		}
		Assert.Empty(gripes); //the fragment parses clean

		//the host's own words load last and win; the format's languages stay off the menu
		builder.Load(new StringReader("value-en=English\n\n[format.ini.name]\nvalue=Overridden\n"));
		IWords words = builder.ToWords("en");
		Assert.Equal("Overridden", words["format.ini.name"]);
		Assert.Equal(["en"], builder.GetLanguages().Select(language => language.Key));

		IWords stacked = WordsFormats.BuiltIn().LoadWords(WordsBuilder.Create()).ToWords("en");
		Assert.Equal("Words", stacked["format.ini.name"]);
		Assert.Equal(".NET resources", stacked["format.resx.name"]);
		Assert.Equal("XLIFF 1.2", stacked["format.xliff.name"]);
		Assert.Empty(WordsFormats.BuiltIn().LoadWords(WordsBuilder.Create()).GetLanguages());

		//the seam's own words come first: every feature names itself by key, and the name is there
		Assert.Equal("Context", stacked["feature.context"]);
		Assert.Equal("Translator comment", WordsFeatures.Comment.Describe("G", stacked));
		foreach (WordsFeatures flag in Enum.GetValues<WordsFeatures>()) {
			if (flag is WordsFeatures.None or WordsFeatures.All) {
				continue;
			}
			WordsAttribute? attribute = flag.GetEnumMemberAttribute<WordsAttribute>();
			Assert.NotNull(attribute);
			Assert.True(stacked.TryGetValue(attribute.Key, out _), $"{flag}: no words at {attribute.Key}");
		}
	}
}
