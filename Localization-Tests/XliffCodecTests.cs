using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using System.Xml.Linq;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The XLIFF 1.2 codec (SPEC: Import and export, the built-ins): one file per
///     target language, the four note channels kept apart, the target state and
///     Words attributes carrying the stale and review flags, and everything but
///     the freeform comments and settings references unchanged through import,
///     export, import.
/// </summary>
public class XliffCodecTests {
	private static readonly XNamespace X = "urn:oasis:names:tc:xliff:document:1.2";
	private static readonly XNamespace W = "https://github.com/pzahra/words";

	private const string French = @"<?xml version=""1.0"" encoding=""utf-8""?>
<xliff version=""1.2"" xmlns=""urn:oasis:names:tc:xliff:document:1.2"" xmlns:w=""https://github.com/pzahra/words"">
  <file original=""Strings.ini"" source-language=""en"" target-language=""fr"" datatype=""plaintext"">
    <body>
      <trans-unit id=""greeting"" approved=""no"">
        <source>Hello {0}</source>
        <target state=""needs-review-translation"" w:stale=""2026-01-01"">Bonjour {0}</target>
        <note from=""developer"" annotates=""source"">shown at login</note>
        <note from=""translator"" annotates=""source"">keep it short</note>
        <note from=""developer"" annotates=""target"">formal</note>
        <note from=""translator"" annotates=""target"">vu</note>
        <w:param name=""0"" type=""String"">Pat</w:param>
      </trans-unit>
      <trans-unit id=""$unit"" translate=""no"">
        <source>kg</source>
      </trans-unit>
      <group id=""menu"">
        <trans-unit id=""menu.file"">
          <source>File</source>
          <target state=""needs-translation""></target>
          <note>a bare note is the developer's, on the source</note>
        </trans-unit>
      </group>
      <trans-unit id=""frozen"" translate=""no"">
        <source>Frozen</source>
        <target state=""translated"">Gelé</target>
      </trans-unit>
      <trans-unit>
        <source>no id</source>
      </trans-unit>
    </body>
  </file>
</xliff>
";

	private const string German = @"<?xml version=""1.0"" encoding=""utf-8""?>
<xliff version=""1.2"" xmlns=""urn:oasis:names:tc:xliff:document:1.2"">
  <file original=""Strings.ini"" source-language=""en"" target-language=""de"" datatype=""plaintext"">
    <body>
      <trans-unit id=""greeting"">
        <source>Hello {0}</source>
        <target state=""translated"">Hallo {0}</target>
        <note from=""developer"" annotates=""source"">shown at login</note>
      </trans-unit>
    </body>
  </file>
</xliff>
";

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
context-fr=untranslated, with a note

; the end
".ReplaceLineEndings();

	private static string Folder() {
		string folder = Path.Combine(Path.GetTempPath(), $"XliffCodec-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		return folder;
	}

	[Fact]
	public void Discover_GathersEveryCultureSibling_APickWithoutACultureLeads() {
		string folder = Folder();
		try {
			foreach (string name in (string[])["Strings.fr.xlf", "Strings.de-DE.xlf", "Strings.Designer.xlf", "Other.fr.xlf", "Strings.it.xliff"]) {
				File.WriteAllText(Path.Combine(folder, name), "<xliff/>");
			}
			var codec = new XliffCodec();

			string[] expected = [Path.Combine(folder, "Strings.de-DE.xlf"), Path.Combine(folder, "Strings.fr.xlf")];
			Assert.Equal(expected, codec.Discover(Path.Combine(folder, "Strings.fr.xlf"))); //the pick's extension, not the other one
			Assert.Equal(Path.Combine(folder, "Strings.ini"), codec.NativePath(expected));

			//a file named without a culture leads its siblings; its content names its language
			File.WriteAllText(Path.Combine(folder, "Strings.xlf"), "<xliff/>");
			Assert.Equal([Path.Combine(folder, "Strings.xlf"), .. expected], codec.Discover(Path.Combine(folder, "Strings.xlf")));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_TakesNotesStatesFlagsAndParameters_AndGripesOnWhatItCannot() {
		string folder = Folder();
		try {
			File.WriteAllText(Path.Combine(folder, "Strings.fr.xlf"), French);
			File.WriteAllText(Path.Combine(folder, "Strings.de.xlf"), German);
			File.WriteAllText(Path.Combine(folder, "Strings.it.xlf"), "<xliff version=\"2.0\" xmlns=\"urn:oasis:names:tc:xliff:document:2.0\"><file id=\"f\"/></xliff>");
			File.WriteAllText(Path.Combine(folder, "Strings.es.xlf"), "<root/>");
			var codec = new XliffCodec();
			var session = new WordsSession();

			WordsFile file = session.Import(codec, codec.Discover(Path.Combine(folder, "Strings.fr.xlf")));

			Assert.Equal(Path.Combine(folder, "Strings.ini"), file.Path);
			Assert.Equal(["de", "fr"], file.Languages); //the set's order: by code
			Assert.Equal("en", file.DefaultLanguage); //the source text is the default
			Assert.Equal("français", session.Languages.Find("fr")!.NativeName);
			Assert.Equal("French", session.Languages.Find("fr")!.EnglishName); //an English source: names in English
			Assert.Equal(["Strings.greeting", "Strings.$unit", "Strings.menu.file", "Strings.frozen"], session.Keys.Keys);

			WordsKey greeting = session.Keys["Strings.greeting"];
			Assert.Equal("Hello {0}", greeting.DefaultValue);
			Assert.Equal("shown at login", greeting.Context); //once, though both files carry the note
			Assert.Equal("keep it short", greeting.Comment);
			Assert.True(greeting.NeedsReview);
			WordsParameter parameter = Assert.Single(greeting.Parameters);
			Assert.Equal(("0", "String", "Pat"), (parameter.Key, parameter.DataType.Name, parameter.Value));
			Assert.Equal("Bonjour {0}", greeting.Entries["fr"].Value);
			Assert.Equal("2026-01-01", greeting.Entries["fr"].Stale);
			Assert.Equal("formal", greeting.Entries["fr"].Context);
			Assert.Equal("vu", greeting.Entries["fr"].Comment);
			Assert.Equal("Hallo {0}", greeting.Entries["de"].Value);
			Assert.Null(greeting.Entries["de"].Stale);

			Assert.True(session.Keys["Strings.$unit"].IsConstant);
			WordsKey menuFile = session.Keys["Strings.menu.file"]; //found inside a group
			Assert.Equal("a bare note is the developer's, on the source", menuFile.Context);
			Assert.Equal("", menuFile.Entries["fr"].Value);
			Assert.Null(menuFile.Entries["fr"].Stale);
			Assert.Equal("Gelé", session.Keys["Strings.frozen"].Entries["fr"].Value);
			Assert.False(session.Keys["Strings.frozen"].IsConstant);

			Assert.Equal(4, file.Errors.Count);
			Assert.Contains(file.Errors, error => error.StartsWith("Strings.es.xlf: not an XLIFF document"));
			Assert.Contains(file.Errors, error => error.StartsWith("Strings.it.xlf: XLIFF 2.0 is not supported"));
			Assert.Contains(file.Errors, error => error.Contains("translate=\"no\" on 'frozen' ignored"));
			Assert.Contains(file.Errors, error => error.Contains("without an id"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_FromASourceInAnotherLanguage_GivesNoEnglishNames() {
		string folder = Folder();
		try {
			File.WriteAllText(Path.Combine(folder, "Strings.de.xlf"), German.Replace("source-language=\"en\"", "source-language=\"it\""));
			var codec = new XliffCodec();
			var session = new WordsSession();

			WordsFile file = session.Import(codec, codec.Discover(Path.Combine(folder, "Strings.de.xlf")));

			Assert.Equal("it", file.DefaultLanguage);
			LanguageEntry de = session.Languages.Find("de")!;
			Assert.Equal("Deutsch", de.NativeName);
			Assert.Equal("", de.EnglishName); //"German" is no name in Italian
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Write_PlansOneFilePerLanguage_AndSaysEverythingItKnows() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(Full), "Main.ini");
		var codec = new XliffCodec();
		var source = new ExportSource(session, file, KeyTree.Build(session, file));

		Assert.Equal(WordsFeatures.FreeComments | WordsFeatures.Settings, codec.Loses(source));

		IReadOnlyList<ExportUnit> units = codec.Plan(source, Path.Combine("out", "Strings.fr.xlf"));
		Assert.Equal([Path.Combine("out", "Strings.en.xlf"), Path.Combine("out", "Strings.fr.xlf")], units.Select(unit => unit.Path));
		Assert.Equal([Path.Combine("out", "Strings.en.xliff"), Path.Combine("out", "Strings.fr.xliff")], codec.Plan(source, Path.Combine("out", "Strings.xliff")).Select(unit => unit.Path));
		Assert.Equal([["en"], ["fr"]], units.Select(unit => unit.Languages));

		var output = new StringWriter();
		List<string> gripes = [];
		codec.Write(source, units[1], output, gripes, new FormatOptions { [XliffCodec.SourceLanguageOption] = "en-US" });
		Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", output.ToString());
		XDocument document = XDocument.Parse(output.ToString());
		XElement xliffFile = document.Root!.Element(X + "file")!;
		Assert.Equal("1.2", (string)document.Root.Attribute("version")!);
		Assert.Equal("Main.ini", (string)xliffFile.Attribute("original")!);
		Assert.Equal("en-US", (string)xliffFile.Attribute("source-language")!);
		Assert.Equal("fr", (string)xliffFile.Attribute("target-language")!);
		List<XElement> units2 = [.. xliffFile.Element(X + "body")!.Elements(X + "trans-unit")];
		Assert.Equal(["greeting", "$unit", "menu.file", "menu.file.open"], units2.Select(unit => (string)unit.Attribute("id")!)); //tree order, label stripped

		XElement greeting = units2[0];
		Assert.Equal("no", (string)greeting.Attribute("approved")!);
		Assert.Null(greeting.Attribute("translate"));
		Assert.Equal("Hello {0}", greeting.Element(X + "source")!.Value);
		XElement target = greeting.Element(X + "target")!;
		Assert.Equal("Bonjour {0}", target.Value);
		Assert.Equal("needs-review-translation", (string)target.Attribute("state")!);
		Assert.Equal("2026-01-01", (string)target.Attribute(W + "stale")!);
		Assert.Equal([("developer", "source", "shown at login"), ("translator", "source", "keep it short"), ("developer", "target", "formal"), ("translator", "target", "vu")],
			greeting.Elements(X + "note").Select(note => ((string)note.Attribute("from")!, (string)note.Attribute("annotates")!, note.Value)));
		XElement parameter = Assert.Single(greeting.Elements(W + "param"));
		Assert.Equal(("0", "String", "Pat"), ((string)parameter.Attribute("name")!, (string)parameter.Attribute("type")!, parameter.Value));

		Assert.Equal("no", (string)units2[1].Attribute("translate")!); //the constant
		Assert.Equal("needs-translation", (string)units2[1].Element(X + "target")!.Attribute("state")!);
		Assert.Equal("translated", (string)units2[2].Element(X + "target")!.Attribute("state")!);
		Assert.Empty(units2[2].Elements(X + "note"));
		//an untranslated entry still carries its note
		XElement open = units2[3];
		Assert.Equal("needs-translation", (string)open.Element(X + "target")!.Attribute("state")!);
		Assert.Equal("untranslated, with a note", Assert.Single(open.Elements(X + "note")).Value);

		Assert.Equal(2, gripes.Count);
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.fr.xlf: dropped the preamble and the comments between blocks"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.fr.xlf: dropped the settings references"));

		//the source language defaults to the file's default language, else en; a unit must carry exactly one language
		var english = new StringWriter();
		codec.Write(source, units[0], english, gripes);
		Assert.Equal("en", (string)XDocument.Parse(english.ToString()).Root!.Element(X + "file")!.Attribute("source-language")!);
		file.DefaultLanguage = "en-AU";
		var australian = new StringWriter();
		codec.Write(source, units[0], australian, gripes);
		Assert.Equal("en-AU", (string)XDocument.Parse(australian.ToString()).Root!.Element(X + "file")!.Attribute("source-language")!);
		Assert.Throws<ArgumentException>(() => codec.Write(source, new ExportUnit("x.xlf", []), new StringWriter(), gripes));
	}

	[Fact]
	public void ImportExportImport_KeepsEverythingButTheCommentsAndSettings() {
		string folder = Folder();
		try {
			var session = new WordsSession();
			WordsFile original = session.Load(new StringReader(Full), Path.Combine(folder, "Main.ini"));
			original.DefaultLanguage = "en-AU";
			var codec = new XliffCodec();
			var source = new ExportSource(session, original, KeyTree.Build(session, original));
			string outFolder = Path.Combine(folder, "out");
			Directory.CreateDirectory(outFolder);

			List<string> gripes = [];
			foreach (ExportUnit unit in codec.Plan(source, Path.Combine(outFolder, "Main.xlf"))) {
				IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, gripes));
			}
			Assert.Equal(["Main.en.xlf", "Main.fr.xlf"], Directory.GetFiles(outFolder).Select(Path.GetFileName).Order());

			WordsFile again = session.Import(codec, codec.Discover(Path.Combine(outFolder, "Main.fr.xlf")));
			Assert.Empty(again.Errors);
			Assert.Equal(Path.Combine(outFolder, "Main.ini"), again.Path);
			Assert.Equal(["en", "fr"], again.Languages);
			Assert.Equal("en-AU", again.DefaultLanguage); //source-language, there and back
			foreach (WordsKey before in session.KeysOf(original)) {
				WordsKey after = session.Keys[again.Label + before.BlockKey[original.Label.Length..]];
				Assert.Equal(before.DefaultValue, after.DefaultValue);
				Assert.Equal(before.Context, after.Context);
				Assert.Equal(before.Comment, after.Comment);
				Assert.Equal(before.NeedsReview, after.NeedsReview);
				Assert.Equal(before.IsConstant, after.IsConstant);
				Assert.Equal(before.Parameters.Select(p => (p.Key, p.DataType.Name, p.Value)), after.Parameters.Select(p => (p.Key, p.DataType.Name, p.Value)));
				foreach (string code in (string[])["en", "fr"]) {
					Assert.Equal(before.Entries[code].Value, after.Entries[code].Value);
					Assert.Equal(before.Entries[code].Stale, after.Entries[code].Stale);
					Assert.Equal(before.Entries[code].Context, after.Entries[code].Context);
					Assert.Equal(before.Entries[code].Comment, after.Entries[code].Comment);
				}
			}
			Assert.Equal(session.KeysOf(original).Count(), session.KeysOf(again).Count());
			//and what was lost is exactly the two features the format does not keep
			Assert.Equal("", again.Preamble);
			Assert.Equal("", again.Settings);
			Assert.Empty(again.BlockComments);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Values_KeepTheirWhitespaceAndMarkupCharacters_AndCdataReadsAsText() {
		string folder = Folder();
		try {
			var session = new WordsSession();
			WordsFile file = session.Load(new StringReader("value-fr=Français\n\n[padded]\nvalue=x\nvalue-fr=y\n\n[blank]\nvalue=x\nvalue-fr=y\n\n[lines]\nvalue=x\n\n[markup]\nvalue=x\n"), Path.Combine(folder, "Main.ini"));
			session.Keys["Main.padded"].DefaultValue = "  padded  ";
			session.Keys["Main.padded"].Entries["fr"].Value = "\tonglet ";
			session.Keys["Main.blank"].DefaultValue = " ";
			session.Keys["Main.blank"].Entries["fr"].Value = " ";
			session.Keys["Main.blank"].Entries["fr"].Stale = "a mark\nof two lines";
			session.Keys["Main.lines"].DefaultValue = "one\ntwo\n";
			session.Keys["Main.markup"].DefaultValue = "<a> & \"b\" <https://x.y>";
			var codec = new XliffCodec();
			var source = new ExportSource(session, file, KeyTree.Build(session, file));
			List<string> gripes = [];
			ExportUnit unit = Assert.Single(codec.Plan(source, Path.Combine(folder, "Strings.xlf")));
			IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, gripes));

			string text = File.ReadAllText(unit.Path);
			Assert.Contains("<source>  padded  </source>", text); //no trimming, no CDATA: entities
			Assert.Contains("<source>&lt;a&gt; &amp; \"b\" &lt;https://x.y&gt;</source>", text);
			Assert.Contains("w:stale=\"a mark&#xA;of two lines\"", text); //a newline in an attribute is entitized, or the parser would fold it

			ILoadedWords back = codec.Read([unit.Path]);
			Assert.Equal("  padded  ", back.WordKeys["padded"].DefaultValue);
			Assert.Equal("\tonglet ", back.WordKeys["padded"].Entries["fr"].Value);
			Assert.Equal(" ", back.WordKeys["blank"].DefaultValue); //whitespace alone survives: the trans-unit is xml:space="preserve"
			Assert.Equal(" ", back.WordKeys["blank"].Entries["fr"].Value);
			Assert.Equal("a mark\nof two lines", back.WordKeys["blank"].Entries["fr"].Stale);
			Assert.Equal("one\ntwo\n", back.WordKeys["lines"].DefaultValue); //newlines come back as \n, the ini parser's own
			Assert.Equal("<a> & \"b\" <https://x.y>", back.WordKeys["markup"].DefaultValue);

			//another tool's CDATA reads as the text it wraps
			File.WriteAllText(Path.Combine(folder, "Cdata.fr.xlf"), "<xliff version=\"1.2\"><file target-language=\"fr\"><body><trans-unit id=\"k\"><source><![CDATA[<b>bold</b> & more]]></source><target><![CDATA[ padded ]]></target></trans-unit></body></file></xliff>");
			ILoadedWords cdata = codec.Read([Path.Combine(folder, "Cdata.fr.xlf")]);
			Assert.Equal("<b>bold</b> & more", cdata.WordKeys["k"].DefaultValue);
			Assert.Equal(" padded ", cdata.WordKeys["k"].Entries["fr"].Value);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
