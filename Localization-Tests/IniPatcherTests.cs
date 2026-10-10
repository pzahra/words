using PatTech.Localization.Authoring;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The patcher behind the command line (command line SPEC: Surgical edits):
///     a change replaces the lines of the field it names and nothing else, new
///     fields and keys land where the SPEC puts them, the file keeps its line
///     endings, encoding and BOM, and an edit that would change anything else is
///     refused. Fixtures are joined with explicit breaks, whatever the checkout's.
/// </summary>
public class IniPatcherTests {
	private static string Ini(params string[] lines) => string.Join("\n", lines) + "\n";

	private static IniPatcher Patch(string text) => IniPatcher.FromBytes(Encoding.UTF8.GetBytes(text));

	private static WordsField F(string name) => WordsField.TryParse(name, out var field, out string? problem) ? field : throw new ArgumentException(problem);

	//a hand-written file the writer would not write this way: a long unfolded line,
	//a bare group header, comments, and a chain of [.child] headers
	private static readonly string Menu = Ini(
		"value=!en",
		"value-fr=Français",
		"",
		";the menu",
		"[menu]",
		"[.file]",
		"value=File",
		"",
		"[.edit]",
		"value=Edit",
		"comment=the edit menu",
		"",
		";about",
		"[about]",
		"value=About this app, which is a long line the writer would fold, since it runs on and on well past eighty columns",
		"",
		";the end");

	[Fact]
	public void Set_ReplacesOnlyTheFieldsLines() {
		var patcher = Patch(Menu);

		Assert.Empty(patcher.Set("menu.edit", F("value"), "Modifica"));

		Assert.Equal(Menu.Replace("value=Edit\n", "value=Modifica\n"), patcher.Text);
	}

	[Fact]
	public void Set_ANewField_GoesAfterTheLastOfItsBlock() {
		var patcher = Patch(Menu);

		patcher.Set("menu.edit", F("value-fr"), "Édition");

		Assert.Equal(Menu.Replace("comment=the edit menu\n", "comment=the edit menu\nvalue-fr=Édition\n"), patcher.Text);
	}

	[Fact]
	public void Set_AFieldOfAGroup_GoesRightUnderItsHeader_KeepingTheChain() {
		var patcher = Patch(Menu);

		patcher.Set("menu", F("value"), "Menu");

		Assert.Equal(Menu.Replace("[menu]\n", "[menu]\nvalue=Menu\n"), patcher.Text);
		Assert.Equal("File", patcher.Find("menu.file")!.DefaultValue);
	}

	[Fact]
	public void Set_ANewKey_GoesAfterItsNearestSiblingsChain_AsAFullHeader() {
		var patcher = Patch(Menu);

		patcher.Set("menu.view", F("value"), "View");

		//after the chain [menu] [.file] [.edit], not inside it, and the comment above [about] stays with it
		Assert.Equal(Menu.Replace("comment=the edit menu\n", "comment=the edit menu\n\n[menu.view]\nvalue=View\n"), patcher.Text);
		Assert.Equal(["menu.file", "menu.edit", "menu.view", "about"], patcher.Keys.Select(key => key.BlockKey));
	}

	[Fact]
	public void Set_AKeyWithNoSibling_GoesAfterTheLastBlock_BeforeTheTrailer() {
		var patcher = Patch(Menu);

		patcher.Set("help", F("value"), "Help");

		Assert.EndsWith("on and on well past eighty columns\n\n[help]\nvalue=Help\n\n;the end\n", patcher.Text);
	}

	[Fact]
	public void Set_InAFileWithNoBlocks_AppendsTheKey() {
		var patcher = Patch(Ini("value=!en"));

		patcher.Set("hello", F("value"), "Hello");

		Assert.Equal(Ini("value=!en", "", "[hello]", "value=Hello"), patcher.Text);
	}

	[Fact]
	public void Set_WritesThePairAsTheWriterDoes_ContinuedAndFolded() {
		string text = "A first line\nand a second one, " + string.Concat(Enumerable.Repeat("long enough to fold ", 8)) + "\n  indented";
		var writer = new StringWriter { NewLine = "\n" };
		new IniWriter(writer).WritePair("value-fr", text);
		var patcher = Patch(Menu);

		patcher.Set("about", F("value-fr"), text);

		Assert.Contains("columns\n" + writer + "\n;the end", patcher.Text);
		Assert.Equal(text, patcher.Find("about")!.Entries["fr"].Value);
		Assert.Equal(text, IniPatcher.FromBytes(patcher.ToBytes()).Find("about")!.Entries["fr"].Value);
	}

	[Fact]
	public void Set_KeepsTheBom_TheCrlf_AndAMissingFinalBreak() {
		byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("value=!en\r\n\r\n[a]\r\nvalue=A")];
		var patcher = IniPatcher.FromBytes(bytes);

		patcher.Set("a", F("comment"), "first\nsecond");

		Assert.Equal([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("value=!en\r\n\r\n[a]\r\nvalue=A\r\ncomment=first\\\r\nsecond")], patcher.ToBytes());
	}

	[Fact]
	public void Set_LeavesEachLinesOwnBreak() {
		var patcher = Patch("value=!en\n\r\n[a]\rvalue=A\r\n\n[b]\nvalue=B\n");

		patcher.Set("b", F("value"), "Bee");

		Assert.Equal("value=!en\n\r\n[a]\rvalue=A\r\n\n[b]\nvalue=Bee\n", patcher.Text);
	}

	[Fact]
	public void Set_KeepsUtf16() {
		var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
		var patcher = IniPatcher.FromBytes([.. utf16.GetPreamble(), .. utf16.GetBytes(Ini("[a]", "value=A"))]);

		patcher.Set("a", F("value"), "Ä");

		Assert.Equal([.. utf16.GetPreamble(), .. utf16.GetBytes(Ini("[a]", "value=Ä"))], patcher.ToBytes());
	}

	[Fact]
	public void TextThatIsNotUtf8_DoesNotOpen() {
		Assert.Throws<InvalidDataException>(() => IniPatcher.FromBytes([.. "[a]\nvalue=caf"u8, 0xE9, (byte)'\n']));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Set_KeepsUtf32_WhoseBomStartsLikeUtf16s(bool bigEndian) {
		var utf32 = new UTF32Encoding(bigEndian, byteOrderMark: true);
		var patcher = IniPatcher.FromBytes([.. utf32.GetPreamble(), .. utf32.GetBytes(Ini("[a]", "value=A"))]);

		patcher.Set("a", F("value"), "Ä");

		Assert.Equal([.. utf32.GetPreamble(), .. utf32.GetBytes(Ini("[a]", "value=Ä"))], patcher.ToBytes());
	}

	[Fact]
	public void Utf16WithoutABom_DoesNotOpen() {
		//every other byte a NUL, which is UTF-8 too
		var refused = Assert.Throws<InvalidDataException>(() => IniPatcher.FromBytes(new UnicodeEncoding(false, false).GetBytes(Ini("[a]", "value=A"))));

		Assert.Contains("NUL", refused.Message);
	}

	[Fact]
	public void Set_RefusesANul_WhichWouldLeaveTheFileNoText() {
		var patcher = Patch(Menu);

		Assert.Throws<ArgumentException>(() => patcher.Set("about", F("value"), "a\0b"));
		Assert.Equal(Menu, patcher.Text);
	}

	[Fact]
	public void Set_AParameterOfNoType_IsWrittenAsGiven() {
		// the reader keeps a text whose first words name no type whole, so it
		// needs nothing in front
		var patcher = Patch(Ini("[a]", "value=A"));

		var gripes = patcher.Set("a", F("param-u"), "http://x");

		Assert.Equal(Ini("[a]", "value=A", "param-u=http://x"), patcher.Text);
		Assert.Equal("http://x", F("param-u").Read(patcher.Find("a")!));
		Assert.Equal(WordsParameterType.Str, Assert.Single(patcher.Find("a")!.Parameters).DataType);
		Assert.Empty(gripes);
	}

	[Fact]
	public void Remove_AnEmptyField_DropsItsLine() {
		var patcher = Patch(Ini("[a]", "value=A", "context=", "", "[b]", "comment="));

		Assert.True(patcher.Declares("a", F("context")));
		patcher.Remove("a", F("context"));
		patcher.Remove("b", F("comment")); //a block of nothing but the empty field, read as a group

		Assert.Equal(Ini("[a]", "value=A", "", "[b]"), patcher.Text);
		Assert.False(patcher.Declares("a", F("context")));
	}

	[Fact]
	public void Set_TheTextAFieldHolds_LeavesItsLinesAsWritten() {
		var patcher = Patch(Menu);
		string about = patcher.Find("about")!.DefaultValue;

		Assert.Empty(patcher.Set("about", F("value"), about));

		Assert.Equal(Menu, patcher.Text); //still unfolded, as the hand wrote it
	}

	[Fact]
	public void Set_AFieldDeclaredTwice_KeepsItsFirstPlace() {
		var patcher = Patch(Ini("[a]", "value=one", "comment=c", "value=two"));

		var gripes = patcher.Set("a", F("value"), "three");

		Assert.Equal(Ini("[a]", "value=three", "comment=c"), patcher.Text);
		Assert.Contains(gripes, gripe => gripe.Contains("declared 2 times"));
	}

	[Fact]
	public void Set_ReportsTheGripesItAdds_AndOnlyThose() {
		var patcher = Patch(Ini("value=!en", "value-mt=Malti", "", "[a]", "value=A", "value-xx=stray"));
		Assert.NotEmpty(patcher.Errors); //xx has entries but no label

		var gripes = patcher.Set("a", F("value-mt#one"), "Wieħed");

		Assert.Equal("a: value-mt#one is kept, but a runtime ignores it: the plain value is the one form", Assert.Single(gripes));
	}

	[Fact]
	public void AnEditThatWouldSpill_IsRefused_AndChangesNothing() {
		//the last line continues, so a field added after it would become its text
		string text = "[a]\nvalue=A\\";
		var patcher = Patch(text);

		var refused = Assert.Throws<IniPatchException>(() => patcher.Set("a", F("context"), "c"));

		Assert.Contains("a value", refused.Message);
		Assert.Equal(text, patcher.Text);
		Assert.Equal("A\n", patcher.Find("a")!.DefaultValue);
	}

	[Fact]
	public void Set_RefusesAKeyNoHeaderCanName() {
		var patcher = Patch(Menu);

		Assert.Throws<ArgumentException>(() => patcher.Set("a]b", F("value"), "x"));
		Assert.Throws<ArgumentException>(() => patcher.Set(".a", F("value"), "x"));
	}

	[Fact]
	public void Remove_AField_DropsItsLines_ContinuationsIncluded() {
		var patcher = Patch(Ini("[a]", "value=A", "comment=one\\", "two", "context=c"));

		patcher.Remove("a", F("comment"));

		Assert.Equal(Ini("[a]", "value=A", "context=c"), patcher.Text);
	}

	[Fact]
	public void Remove_AKey_DropsItsBlock_AndOneBlankLine() {
		var patcher = Patch(Menu);

		patcher.Remove("about");

		//its comment stands where it stood, now above nothing but the trailer
		Assert.Equal(Menu.Replace("[about]\nvalue=About this app, which is a long line the writer would fold, since it runs on and on well past eighty columns\n", ""), patcher.Text);
	}

	[Fact]
	public void Remove_TheLastKey_LeavesNoBlankLineAtTheEnd() {
		var patcher = Patch(Ini("[a]", "value=A", "", "[b]", "value=B"));

		patcher.Remove("b");

		Assert.Equal(Ini("[a]", "value=A"), patcher.Text);
	}

	[Fact]
	public void Remove_AKeyThatBasesChildren_KeepsItsHeaderBare() {
		var patcher = Patch(Ini("[menu]", "value=Menu", "", "[.file]", "value=File"));

		patcher.Remove("menu");

		Assert.Equal(Ini("[menu]", "", "[.file]", "value=File"), patcher.Text);
		Assert.Null(patcher.Find("menu"));
		Assert.Equal("File", patcher.Find("menu.file")!.DefaultValue);
	}

	[Fact]
	public void Remove_AReopenedKey_DropsEveryBlockOfIt() {
		var patcher = Patch(Ini("[a]", "value=A", "", "[b]", "value=B", "", "[a]", "comment=again"));

		patcher.Remove("a");

		Assert.Equal(Ini("[b]", "value=B"), patcher.Text);
	}

	[Fact]
	public void Remove_WhatIsNotThere_Throws() {
		var patcher = Patch(Menu);

		Assert.Throws<KeyNotFoundException>(() => patcher.Remove("menu")); //a group, not a key
		Assert.Throws<KeyNotFoundException>(() => patcher.Remove("about", F("value-fr")));
		Assert.Throws<KeyNotFoundException>(() => patcher.Remove("nope", F("value")));
	}

	[Fact]
	public void Keys_ComeInFileOrder_WithoutGroups_AndABlockPrintsUnderAFullHeader() {
		var patcher = Patch(Menu);

		Assert.Equal(["menu.file", "menu.edit", "about"], patcher.Keys.Select(key => key.BlockKey));
		Assert.Equal("[menu.edit]\ncomment=the edit menu\nvalue=Edit", patcher.Block("menu.edit"));
		Assert.Null(patcher.Block("menu"));
	}

	[Fact]
	public void Save_ReplacesTheFile() {
		string path = Path.Combine(Path.GetTempPath(), $"patch-{Guid.NewGuid():N}.ini");
		try {
			File.WriteAllText(path, Menu);
			var patcher = IniPatcher.Open(path);
			patcher.Set("about", F("value-fr"), "À propos");

			patcher.Save(path);

			Assert.Equal(patcher.ToBytes(), File.ReadAllBytes(path));
			Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*")); //no temporary left
		}
		finally {
			File.Delete(path);
		}
	}

	[Theory]
	[InlineData("value", "value", "", "")]
	[InlineData("value-FR", "value", "fr", "")]
	[InlineData("value-pt-br#FEW", "value", "pt-BR", "few")]
	[InlineData("stale-fr", "stale", "fr", "")]
	[InlineData("value-zh-hans-cn", "value", "zh-Hans-CN", "")]
	[InlineData("value-es-419#many", "value", "es-419", "many")]
	[InlineData("context-ceb", "context", "ceb", "")]
	[InlineData("param-Count", "param", "Count", "")] //a parameter's name, as written
	public void AFieldIsNamedAsTheFileNamesIt(string name, string type, string language, string form) {
		Assert.True(WordsField.TryParse(name, out var field, out _));
		Assert.Equal(new WordsField(type, language, form), field);
	}

	[Theory]
	[InlineData("title")]
	[InlineData("context#few")]
	[InlineData("value-fr#lots")]
	[InlineData("param")]
	[InlineData("value fr")]
	[InlineData("value-english")]
	[InlineData("value-en-US-POSIX")]
	public void WhatIsNoField_SaysWhy(string name) {
		Assert.False(WordsField.TryParse(name, out _, out string? problem));
		Assert.Contains(name, problem);
	}

	[Fact]
	public void TheParser_SaysWhichLineEachVisitComesFrom() {
		var lines = new LineRecorder();

		new WordsParser(lines).Load(new StringReader(Ini("value=!en", "", "[a]", "value=one\\", "two", ";note")));

		Assert.Equal(["1 field value", "3 block a", "4 field value", "5 more value", "6 comment"], lines.Visits);
	}

	private sealed class LineRecorder : IWordsParserConsumer {
		public List<string> Visits { get; } = [];
		private int line;
		public void VisitLine(int number) => line = number;
		public void VisitBlock(string baseKey, string key) => Visits.Add($"{line} block {key}");
		public void VisitFieldDeclaration(FieldKey key, string text) => Visits.Add($"{line} field {key.FieldType}");
		public void VisitFieldContinuation(FieldKey key, string value) => Visits.Add($"{line} more {key.FieldType}");
		public void VisitComment(string text) => Visits.Add($"{line} comment");
	}
}
