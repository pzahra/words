using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using System.Xml.Linq;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The resx codec (SPEC: Import and export, the built-ins): one file per
///     culture gathered by <c>Discover</c>, the comment as context per language,
///     the rest dropped with a gripe, and the fields it keeps unchanged through
///     import, export, import.
/// </summary>
public class ResxCodecTests {
	private const string Neutral = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <resheader name=""resmimetype""><value>text/microsoft-resx</value></resheader>
  <data name=""greeting"" xml:space=""preserve"">
    <value>Hello {0}</value>
    <comment>shown at login</comment>
  </data>
  <data name=""menu.file"" xml:space=""preserve"">
    <value>File</value>
  </data>
  <data name=""$unit"" xml:space=""preserve"">
    <value>kg</value>
  </data>
  <data name=""logo"" type=""System.Drawing.Bitmap, System.Drawing"" mimetype=""application/x-microsoft.net.object.bytearray.base64"">
    <value>AAAA</value>
  </data>
  <data name=""bad]name"" xml:space=""preserve"">
    <value>x</value>
  </data>
</root>
";

	private const string French = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <data name=""greeting"" xml:space=""preserve"">
    <value>Bonjour {0}</value>
    <comment>formal</comment>
  </data>
  <data name=""only.in.fr"" xml:space=""preserve"">
    <value>seulement</value>
  </data>
</root>
";

	private static string Folder() {
		string folder = Path.Combine(Path.GetTempPath(), $"ResxCodec-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		return folder;
	}

	[Fact]
	public void Split_TakesACultureTailAndNothingElse() {
		Assert.Equal(("Strings", ""), ResxCodec.Split("Strings.resx"));
		Assert.Equal(("Strings", "fr"), ResxCodec.Split("Strings.fr.resx"));
		Assert.Equal(("Strings", "fr-CA"), ResxCodec.Split("Strings.fr-ca.resx")); //canonical casing
		Assert.Equal(("My.App.Strings", "de"), ResxCodec.Split("My.App.Strings.de.resx"));
		Assert.Equal(("My.App.Strings", ""), ResxCodec.Split("My.App.Strings.resx")); //a dotted stem is not a culture
		Assert.Equal(("Strings.Designer", ""), ResxCodec.Split("Strings.Designer.resx"));
	}

	[Fact]
	public void Discover_GathersTheNeutralFileFirstThenEveryCultureSibling() {
		string folder = Folder();
		try {
			foreach (string name in (string[])["Strings.resx", "Strings.fr.resx", "Strings.de-DE.resx", "Strings.Designer.resx", "Other.fr.resx"]) {
				File.WriteAllText(Path.Combine(folder, name), "<root/>");
			}
			var codec = new ResxCodec();

			string[] expected = [Path.Combine(folder, "Strings.resx"), Path.Combine(folder, "Strings.de-DE.resx"), Path.Combine(folder, "Strings.fr.resx")];
			Assert.Equal(expected, codec.Discover(Path.Combine(folder, "Strings.fr.resx")));
			Assert.Equal(expected, codec.Discover(Path.Combine(folder, "Strings.resx"))); //whichever of the set is picked

			//no neutral file: the cultures alone, and Read says so; the native ini is still the stem's
			File.Delete(Path.Combine(folder, "Strings.resx"));
			IReadOnlyList<string> cultures = codec.Discover(Path.Combine(folder, "Strings.de-DE.resx"));
			Assert.Equal(expected[1..], cultures);
			Assert.Contains(codec.Read(cultures).Errors, error => error.Contains("no neutral"));
			Assert.Equal(Path.Combine(folder, "Strings.ini"), codec.NativePath(cultures));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_MapsCommentsToContextPerLanguage_AndGripesOnWhatItSkips() {
		string folder = Folder();
		try {
			File.WriteAllText(Path.Combine(folder, "Strings.resx"), Neutral);
			File.WriteAllText(Path.Combine(folder, "Strings.fr.resx"), French);
			var codec = new ResxCodec();
			var session = new WordsSession();

			WordsFile file = session.Import(codec, codec.Discover(Path.Combine(folder, "Strings.resx")));

			//a native file beside the set: Ctrl+S writes ini, never resx
			Assert.Equal(Path.Combine(folder, "Strings.ini"), file.Path);
			Assert.Equal(["fr"], file.Languages);
			Assert.Equal("français", session.Languages.Find("fr")!.NativeName);
			Assert.Equal("French", session.Languages.Find("fr")!.EnglishName);
			//the neutral file's keys lead, in its order; a key only a culture has follows
			Assert.Equal(["Strings.greeting", "Strings.menu.file", "Strings.$unit", "Strings.bad_name", "Strings.only.in.fr"], session.Keys.Keys);
			WordsKey greeting = session.Keys["Strings.greeting"];
			Assert.Equal("Hello {0}", greeting.DefaultValue);
			Assert.Equal("shown at login", greeting.Context);
			Assert.Equal("Bonjour {0}", greeting.Entries["fr"].Value);
			Assert.Equal("formal", greeting.Entries["fr"].Context);
			Assert.True(session.Keys["Strings.$unit"].IsConstant);
			Assert.Equal("", session.Keys["Strings.only.in.fr"].DefaultValue);
			Assert.Equal("seulement", session.Keys["Strings.only.in.fr"].Entries["fr"].Value);
			//the bitmap and the renamed key are gripes, not crashes
			Assert.Equal(2, file.Errors.Count);
			Assert.Contains(file.Errors, error => error.Contains("'logo' is not a string resource"));
			Assert.Contains(file.Errors, error => error.Contains("'bad]name'") && error.Contains("'bad_name'"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Read_BadXmlIsAGripeNotACrash() {
		string folder = Folder();
		try {
			File.WriteAllText(Path.Combine(folder, "Strings.resx"), "<root><data name=\"k\"><value>unclosed</root>");
			File.WriteAllText(Path.Combine(folder, "Strings.fr.resx"), "<?xml version=\"1.0\"?><resources/>");
			var codec = new ResxCodec();

			ILoadedWords loaded = codec.Read(codec.Discover(Path.Combine(folder, "Strings.resx")));

			Assert.Empty(loaded.WordKeys);
			Assert.Equal(["fr"], loaded.DeclaredLanguages);
			Assert.Equal(2, loaded.Errors.Count);
			Assert.Contains(loaded.Errors, error => error.StartsWith("Strings.resx: not well-formed XML"));
			Assert.Contains(loaded.Errors, error => error.StartsWith("Strings.fr.resx: no string resources"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Write_PlansOneFilePerCulture_KeepsContextsAndConstants_DropsTheRestWithAGripe() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(@"; about Main
value=!en
value-en=English
value-fr=Français
param=wordsmith.ini

[greeting]
context=shown at login
comment=keep it short
value=Hello {0}
param-0=the visitor
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
value#other=Files
value-fr#other=Fichiers
context-fr=untranslated, with a note

[.file.open]
value=Open
"), "Main.ini");
		var codec = new ResxCodec();
		var source = new ExportSource(session, file, KeyTree.Build(session, file));

		Assert.Equal(WordsFeatures.All & ~(WordsFeatures.Context | WordsFeatures.EntryContext | WordsFeatures.Constants), codec.Loses(source));

		//the target names the neutral file; a culture tail on it is dropped
		IReadOnlyList<ExportUnit> units = codec.Plan(source, Path.Combine("out", "Strings.fr.resx"));
		Assert.Equal([Path.Combine("out", "Strings.resx"), Path.Combine("out", "Strings.en.resx"), Path.Combine("out", "Strings.fr.resx")], units.Select(unit => unit.Path));
		Assert.Equal([[], ["en"], ["fr"]], units.Select(unit => unit.Languages));

		var neutral = new StringWriter();
		List<string> gripes = [];
		codec.Write(source, units[0], neutral, gripes);
		XDocument document = XDocument.Parse(neutral.ToString());
		Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", neutral.ToString());
		Assert.Equal("text/microsoft-resx", document.Root!.Elements("resheader").First().Element("value")!.Value);
		Assert.Equal(["greeting", "$unit", "menu.file", "menu.file.open"], document.Root.Elements("data").Select(data => (string)data.Attribute("name")!)); //tree order, label stripped
		XElement greeting = document.Root.Elements("data").First();
		Assert.Equal("Hello {0}", greeting.Element("value")!.Value);
		Assert.Equal("shown at login", greeting.Element("comment")!.Value);
		Assert.Equal("preserve", (string)greeting.Attribute(XNamespace.Xml + "space")!);
		Assert.Null(document.Root.Elements("data").ElementAt(2).Element("comment")); //no context, no comment element
		//what the neutral file dropped, by field, counted
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped comment= (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped param-x= (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped stale= (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped the preamble and the comments between blocks (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped the settings references (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped the default's language (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.resx: dropped value#form= (1)"));
		Assert.Equal(7, gripes.Count);

		var french = new StringWriter();
		gripes.Clear();
		codec.Write(source, units[2], french, gripes);
		document = XDocument.Parse(french.ToString());
		//only what is translated: an empty satellite entry would shadow the default
		XElement only = Assert.Single(document.Root!.Elements("data"));
		Assert.Equal("greeting", (string)only.Attribute("name")!);
		Assert.Equal("Bonjour {0}", only.Element("value")!.Value);
		Assert.Equal("formal", only.Element("comment")!.Value);
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.fr.resx: dropped comment-fr= (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.fr.resx: dropped stale-fr= (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.fr.resx: dropped context-fr= on an untranslated key (1)"));
		Assert.Contains(gripes, gripe => gripe.StartsWith("Strings.fr.resx: dropped value-fr#form= (1)")); //an untranslated entry's forms too
		Assert.Equal(4, gripes.Count);

		//nothing in English beyond the defaults: a header-only satellite, no gripes
		var english = new StringWriter();
		gripes.Clear();
		codec.Write(source, units[1], english, gripes);
		Assert.Empty(XDocument.Parse(english.ToString()).Root!.Elements("data"));
		Assert.Empty(gripes);

		//two cultures in one unit is a caller's mistake
		Assert.Throws<ArgumentException>(() => codec.Write(source, new ExportUnit("x.resx", ["en", "fr"]), new StringWriter(), gripes));
	}

	[Fact]
	public void ImportExportImport_KeepsTheFieldsResxHolds() {
		string folder = Folder();
		try {
			File.WriteAllText(Path.Combine(folder, "Strings.resx"), Neutral);
			File.WriteAllText(Path.Combine(folder, "Strings.fr.resx"), French);
			var codec = new ResxCodec();
			var session = new WordsSession();
			WordsFile imported = session.Import(codec, codec.Discover(Path.Combine(folder, "Strings.resx")));
			var source = new ExportSource(session, imported, KeyTree.Build(session, imported));
			string outFolder = Path.Combine(folder, "out");
			Directory.CreateDirectory(outFolder);

			List<string> gripes = [];
			foreach (ExportUnit unit in codec.Plan(source, Path.Combine(outFolder, "Strings.resx"))) {
				IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, gripes));
			}
			Assert.Empty(gripes); //an imported set uses only what resx keeps
			Assert.Equal(["Strings.fr.resx", "Strings.resx"], Directory.GetFiles(outFolder).Select(Path.GetFileName).Order());

			WordsFile again = session.Import(codec, codec.Discover(Path.Combine(outFolder, "Strings.resx")));
			Assert.Empty(again.Errors); //the writer emits only what the reader takes
			foreach (WordsKey before in session.KeysOf(imported)) {
				WordsKey after = session.Keys[again.Label + before.BlockKey[imported.Label.Length..]];
				Assert.Equal(before.DefaultValue, after.DefaultValue);
				Assert.Equal(before.Context, after.Context);
				Assert.Equal(before.IsConstant, after.IsConstant);
				Assert.Equal(before.Entries["fr"].Value, after.Entries["fr"].Value);
				Assert.Equal(before.Entries["fr"].Context, after.Entries["fr"].Context);
			}
			Assert.Equal(session.KeysOf(imported).Count(), session.KeysOf(again).Count());
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
			WordsFile file = session.Load(new StringReader("value-fr=Français\n\n[padded]\nvalue=x\nvalue-fr=y\n\n[blank]\nvalue=x\n\n[lines]\nvalue=x\n\n[markup]\nvalue=x\n"), Path.Combine(folder, "Main.ini"));
			session.Keys["Main.padded"].DefaultValue = "  padded  ";
			session.Keys["Main.padded"].Entries["fr"].Value = "\tonglet ";
			session.Keys["Main.blank"].DefaultValue = " ";
			session.Keys["Main.lines"].DefaultValue = "one\ntwo\n";
			session.Keys["Main.markup"].DefaultValue = "<a> & \"b\" <https://x.y>";
			var codec = new ResxCodec();
			var source = new ExportSource(session, file, KeyTree.Build(session, file));
			List<string> gripes = [];
			foreach (ExportUnit unit in codec.Plan(source, Path.Combine(folder, "Strings.resx"))) {
				IniWriter.WriteAtomic(unit.Path, writer => codec.Write(source, unit, writer, gripes));
			}

			string neutral = File.ReadAllText(Path.Combine(folder, "Strings.resx"));
			Assert.Contains("<value>  padded  </value>", neutral); //no trimming, no CDATA: entities
			Assert.Contains("<value>&lt;a&gt; &amp; \"b\" &lt;https://x.y&gt;</value>", neutral);

			ILoadedWords back = codec.Read(codec.Discover(Path.Combine(folder, "Strings.resx")));
			Assert.Equal("  padded  ", back.WordKeys["padded"].DefaultValue);
			Assert.Equal("\tonglet ", back.WordKeys["padded"].Entries["fr"].Value);
			Assert.Equal(" ", back.WordKeys["blank"].DefaultValue); //whitespace alone survives: xml:space="preserve" makes it significant
			Assert.Equal("one\ntwo\n", back.WordKeys["lines"].DefaultValue); //newlines come back as \n, the ini parser's own
			Assert.Equal("<a> & \"b\" <https://x.y>", back.WordKeys["markup"].DefaultValue);

			//another tool's CDATA reads as the text it wraps; whitespace alone outside
			//an xml:space="preserve" scope is insignificant to XML, and gone
			File.WriteAllText(Path.Combine(folder, "Cdata.resx"), "<root><data name=\"k\" xml:space=\"preserve\"><value><![CDATA[<b>bold</b> & more]]></value></data><data name=\"w\"><value> </value></data></root>");
			ILoadedWords cdata = codec.Read([Path.Combine(folder, "Cdata.resx")]);
			Assert.Equal("<b>bold</b> & more", cdata.WordKeys["k"].DefaultValue);
			Assert.Equal("", cdata.WordKeys["w"].DefaultValue);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
