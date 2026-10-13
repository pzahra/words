using PatTech.Localization.Cli;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The <c>words</c> command, run in process (command line SPEC: Calls): the
///     calls, their exit codes, values on the output and gripes on the
///     error stream, a value from stdin, read as the program reads a pipe,
///     <c>--stale</c>, <c>list --missing</c> by the editor's rule and <c>--stale</c>,
///     and a translation's request and answer (editor SPEC: Machine translation).
/// </summary>
public sealed class WordsCommandTests : IDisposable {
	private readonly string path = Path.Combine(Path.GetTempPath(), $"words-{Guid.NewGuid():N}.ini");

	public void Dispose() => File.Delete(path);

	private static string Ini(params string[] lines) => string.Join("\n", lines) + "\n";

	private static readonly string Sample = Ini(
		"value=!en",
		"value-it=Italiano",
		"value-en-GB=English (UK)",
		"",
		"[menu.file]",
		"value=File",
		"value-it=File",
		"",
		"[menu.edit]",
		"value=Edit",
		"",
		"[files]",
		"value=file",
		"value#other=files",
		"value-it=file",
		"",
		"[$brand]",
		"value=Words");

	private (int Code, string Output, string Error) RunWith(string stdin, params string[] args) => RunOn(new StringReader(stdin), args);

	//stdin's bytes, through the reader the program puts on a pipe
	private (int Code, string Output, string Error) RunPiped(byte[] stdin, params string[] args) => RunOn(WordsCommand.Piped(new MemoryStream(stdin)), args);

	private (int Code, string Output, string Error) RunOn(TextReader input, string[] args) {
		var output = new StringWriter { NewLine = "\n" };
		var error = new StringWriter { NewLine = "\n" };
		int code = WordsCommand.Run([.. args.Select(arg => arg == "FILE" ? path : arg)], input, output, error);
		return (code, output.ToString(), error.ToString());
	}

	private (int Code, string Output, string Error) Run(params string[] args) => RunWith("", args);

	[Fact]
	public void Get_PrintsAFieldUnescaped_OrTheWholeBlock() {
		File.WriteAllText(path, Ini("[a]", "value=one\\", "two", "comment=c"));

		Assert.Equal((0, "one\ntwo\n", ""), Run("get", "FILE", "a", "value"));
		Assert.Equal((0, "[a]\ncomment=c\nvalue=one\\\ntwo\n", ""), Run("get", "FILE", "a"));
	}

	[Fact]
	public void WhatIsNotThere_ExitsOne() {
		File.WriteAllText(path, Sample);

		var (code, output, error) = Run("get", "FILE", "nope");
		Assert.Equal((1, ""), (code, output));
		Assert.Contains("no key nope", error);
		Assert.Equal(1, Run("get", "FILE", "menu.edit", "value-it").Code);
		Assert.Equal(1, Run("remove", "FILE", "menu.edit", "value-it").Code);
		Assert.Equal(1, Run("remove", "FILE", "nope").Code);
		Assert.Equal(Sample, File.ReadAllText(path));
	}

	[Theory]
	[InlineData("get", "FILE")]
	[InlineData("set", "FILE", "a", "value")]
	[InlineData("get", "FILE", "menu.edit", "title")]
	[InlineData("fetch", "FILE", "a")]
	[InlineData("get", "missing.ini", "a")]
	[InlineData("list", "FILE", "--stale")]
	[InlineData("list", "FILE", "--missing", "not a code")]
	[InlineData("set", "FILE", "menu.edit", "param-n", "x", "--stale")]
	[InlineData("set", "FILE", "menu edit", "value", "x")]
	[InlineData("set", "FILE", "lang.c#", "value", "x")]
	[InlineData("list", "FILE", "--missing", "it", "--stale", "en-GB")]
	[InlineData("request", "FILE")]
	[InlineData("request", "FILE", "--missing", "it", "--limit", "0")]
	[InlineData("request", "FILE", "--missing", "it", "--brief")]
	[InlineData("apply", "FILE")]
	[InlineData("apply", "FILE", "no-such-answer.json")]
	public void ABadCall_ExitsTwo_AndSaysWhy(params string[] args) {
		File.WriteAllText(path, Sample);

		var (code, output, error) = Run(args);

		Assert.Equal((2, ""), (code, output));
		Assert.StartsWith("words: ", error);
		Assert.Equal(Sample, File.ReadAllText(path));
	}

	[Fact]
	public void NothingAtAll_ShowsTheCallsOnTheErrorStream_AndHelpOnTheOutput() {
		Assert.Equal(2, Run().Code);
		var (code, output, _) = Run("--help");
		Assert.Equal(0, code);
		Assert.Contains("words set <file> <key> <field> <value>", output);
		Assert.Equal((0, WordsCommand.Version + "\n", ""), Run("--version"));
	}

	[Fact]
	public void Set_ChangesTheOneLine() {
		File.WriteAllText(path, Sample);

		Assert.Equal((0, "", ""), Run("set", "FILE", "menu.edit", "value-it", "Modifica"));

		Assert.Equal(Sample.Replace("value=Edit\n", "value=Edit\nvalue-it=Modifica\n"), File.ReadAllText(path));
	}

	[Fact]
	public void Set_ReadsADashFromStdin_DroppingItsLastBreak() {
		File.WriteAllText(path, Sample);

		Assert.Equal(0, RunWith("first\r\nsecond\r\n", "set", "FILE", "menu.edit", "comment", "-").Code);

		Assert.Contains("[menu.edit]\ncomment=first\\\nsecond\nvalue=Edit\n", File.ReadAllText(path));
	}

	[Fact]
	public void Set_ReadsALoneCarriageReturn_AsTheLastBreak() {
		File.WriteAllText(path, Sample);

		Assert.Equal(0, RunWith("first\r", "set", "FILE", "menu.edit", "comment", "-").Code);

		Assert.Equal((0, "first\n", ""), Run("get", "FILE", "menu.edit", "comment"));
	}

	[Fact]
	public void Set_KeepsALeadingFeffFromStdin_AsTheValuesOwn_AndSaysSo() {
		File.WriteAllText(path, Sample);

		var (code, _, error) = RunPiped(Encoding.UTF8.GetBytes("﻿leading\n"), "set", "FILE", "menu.edit", "value-it", "-");

		Assert.Equal(0, code);
		Assert.Contains("U+FEFF", error);
		Assert.Equal((0, "﻿leading\n", ""), Run("get", "FILE", "menu.edit", "value-it"));
	}

	[Fact]
	public void Set_StdinThatIsNotUtf8_ExitsTwo_AndLeavesTheFile() {
		File.WriteAllText(path, Sample);

		var (code, _, error) = RunPiped([.. "caf"u8, 0xE9, (byte)'\n'], "set", "FILE", "menu.edit", "value-it", "-");

		Assert.Equal(2, code);
		Assert.Contains("stdin is not UTF-8 text", error);
		Assert.Equal(Sample, File.ReadAllText(path));
	}

	[Fact]
	public void Set_IntoUtf16WithoutABom_ExitsTwo_AndLeavesTheFile() {
		byte[] bytes = new UnicodeEncoding(false, false).GetBytes(Sample);
		File.WriteAllBytes(path, bytes);

		var (code, _, error) = Run("set", "FILE", "menu.edit", "value-it", "Modifica");

		Assert.Equal(2, code);
		Assert.Contains("NUL", error);
		Assert.Equal(bytes, File.ReadAllBytes(path));
	}

	[Fact]
	public void AFileThatIsMissing_OrCantBeWritten_IsNamed_WithoutTheCalls() {
		var missing = Run("get", Path.Combine(Path.GetTempPath(), "no-such-words.ini"), "a");
		Assert.Equal(2, missing.Code);
		Assert.Contains("no-such-words.ini: no such file", missing.Error);
		Assert.DoesNotContain("--help", missing.Error);

		File.WriteAllText(path, Sample);
		File.SetAttributes(path, FileAttributes.ReadOnly);
		try {
			var (code, _, error) = Run("set", "FILE", "menu.edit", "value-it", "Modifica");

			Assert.Equal(2, code);
			Assert.Contains($"{path}: can't be written", error);
			Assert.Equal(Sample, File.ReadAllText(path));
		}
		finally {
			File.SetAttributes(path, FileAttributes.Normal);
		}
	}

	[Fact]
	public void Set_Stale_MarksTheLanguagesEntry_WithWordsOrWithout() {
		File.WriteAllText(path, Sample);

		Run("set", "FILE", "menu.edit", "value-it", "Modifica", "--stale", "machine translated");
		Run("set", "FILE", "menu.file", "value-it", "Archivio", "--stale");
		Run("set", "FILE", "files", "value", "file", "--stale=reworded");

		string text = File.ReadAllText(path);
		Assert.Contains("value-it=Modifica\nstale-it=machine translated\n", text);
		Assert.Contains("value-it=Archivio\nstale-it=\n", text);
		Assert.Contains("value#other=files\nstale=\nvalue-it=file\n", text); //the default's mark keeps no words
	}

	[Fact]
	public void Set_ADefaultThatChanges_StalesTheTranslationsWithWords_KeepingAMarkThere() {
		File.WriteAllText(path, Ini(
			"value=!en",
			"value-it=Italiano",
			"value-de=Deutsch",
			"value-fr=Français",
			"",
			"[k]",
			"value=Hello",
			"value-it=Ciao",
			"value-de=Hallo",
			"stale-de=machine translated",
			"value-fr=",
			"",
			"[same]",
			"value=Same",
			"value-it=Uguale",
			"",
			"[files]",
			"value=file",
			"value#other=files",
			"value-it=file"));

		Assert.Equal(0, Run("set", "FILE", "k", "value", "Hello!").Code);
		Assert.Equal(0, Run("set", "FILE", "same", "value", "Same").Code); //no change, nothing stale
		Assert.Equal(0, Run("set", "FILE", "files", "value#other", "filez").Code); //a form is the default too

		var (code, stamp, _) = Run("get", "FILE", "k", "stale-it");
		Assert.Equal(0, code);
		Assert.True(DateTimeOffset.TryParse(stamp.TrimEnd('\n'), CultureInfo.InvariantCulture, out _));
		Assert.Equal((0, "machine translated\n", ""), Run("get", "FILE", "k", "stale-de")); //a mark there stays
		Assert.Equal(1, Run("get", "FILE", "k", "stale-fr").Code); //no words: missing, not stale
		Assert.Equal(1, Run("get", "FILE", "same", "stale-it").Code);
		Assert.Equal(0, Run("get", "FILE", "files", "stale-it").Code);

		//a translation's own value stales nothing else
		Assert.Equal(0, Run("set", "FILE", "same", "value-it", "Uguali").Code);
		Assert.Equal(1, Run("get", "FILE", "same", "stale-it").Code);
	}

	//the file keeps a value's line breaks as \n, so a shell's \r\n is no change
	[Fact]
	public void Set_AMultiLineDefault_SetToItsOwnText_StalesNothing() {
		File.WriteAllText(path, Ini("value=!en", "value-it=Italiano", "", "[lines]", "value=one\\", "two", "value-it=uno\\", "due"));

		Assert.Equal(0, Run("set", "FILE", "lines", "value", "one\r\ntwo").Code);
		Assert.Equal(0, Run("set", "FILE", "lines", "value", "one\rtwo").Code);
		Assert.Equal(0, RunWith("one\r\ntwo\r\n", "set", "FILE", "lines", "value", "-").Code);
		Assert.Equal(1, Run("get", "FILE", "lines", "stale-it").Code);

		Assert.Equal(0, Run("set", "FILE", "lines", "value", "one\r\nthree").Code);
		Assert.Equal(0, Run("get", "FILE", "lines", "stale-it").Code);
	}

	[Fact]
	public void Set_Stale_BeforeTheValue_TakesNoWords() {
		File.WriteAllText(path, Sample);

		Assert.Equal(0, Run("set", "FILE", "--stale", "menu.edit", "value-it", "Modifica").Code);

		Assert.Contains("value-it=Modifica\nstale-it=\n", File.ReadAllText(path));
	}

	[Fact]
	public void Set_PassesOnTheGripesItAdds() {
		File.WriteAllText(path, Sample);

		var (code, _, error) = Run("set", "FILE", "menu.edit", "value-fr", "Édition");

		Assert.Equal(0, code);
		Assert.Contains("language 'fr' has entries but no top-of-file label", error);
	}

	[Fact]
	public void Set_ATranslationThatDropsOrAddsAParameter_IsWritten_WithANote() {
		File.WriteAllText(path, Ini(
			"value=!en",
			"value-fr=Français",
			"",
			"[files]",
			"value={0} files"));

		var (code, _, error) = Run("set", "FILE", "files", "value-fr", "des fichiers");
		Assert.Equal((0, "words: files: the fr words drop {0}, which the default uses, so the app's value never shows\n"), (code, error));
		Assert.Contains("value-fr=des fichiers\n", File.ReadAllText(path));

		Assert.Equal("words: files: the fr words use {1}, which the default does not and no param- defines\n",
			Run("set", "FILE", "files", "value-fr", "{0} fichiers de {1}").Error);
		Assert.Equal("", Run("set", "FILE", "files", "value-fr", "{0} fichiers").Error);
		Assert.Equal("", Run("set", "FILE", "files", "value-fr#other", "{0} fichiers").Error);
		Assert.Equal("", Run("set", "FILE", "files", "value-fr", "{>elsewhere.files}").Error); //its one file: a reference out of it may carry {0}
	}

	[Fact]
	public void ARefusedEdit_ExitsTwo_AndLeavesTheFile() {
		string text = "[a]\nvalue=A\\";
		File.WriteAllText(path, text);

		var (code, _, error) = Run("set", "FILE", "a", "value-fr", "A");

		Assert.Equal(2, code);
		Assert.Contains("refused", error);
		Assert.Equal(text, File.ReadAllText(path));
	}

	[Fact]
	public void Remove_DropsAFieldOrAKey() {
		File.WriteAllText(path, Sample);

		Assert.Equal(0, Run("remove", "FILE", "files", "value#other").Code);
		Assert.Equal(0, Run("remove", "FILE", "menu.edit").Code);

		Assert.Equal(Sample.Replace("value#other=files\n", "").Replace("[menu.edit]\nvalue=Edit\n\n", ""), File.ReadAllText(path));
	}

	[Fact]
	public void Remove_DropsAnEmptyField_ThatGetCallsNone() {
		File.WriteAllText(path, Sample);
		Run("set", "FILE", "menu.edit", "context-it", "");
		Assert.Contains("value=Edit\ncontext-it=\n", File.ReadAllText(path));
		Assert.Equal(1, Run("get", "FILE", "menu.edit", "context-it").Code);

		Assert.Equal((0, "", ""), Run("remove", "FILE", "menu.edit", "context-it"));

		Assert.Equal(Sample, File.ReadAllText(path));
	}

	[Fact]
	public void AChildHeaderBeforeAnyBase_IsAKeyNoCallWrites_ButEachReaches() {
		//[.x] opens the key .x, which a runtime skips, as every call reads it
		File.WriteAllText(path, Ini("value=!en", "", "[.x]", "value=X", "", "[a]", "value=A"));

		Assert.Equal((0, ".x\na\n", ""), Run("list", "FILE"));
		Assert.Equal((0, "X\n", ""), Run("get", "FILE", ".x", "value"));
		Assert.Equal(1, Run("get", "FILE", "x").Code);
		Assert.Equal(2, Run("set", "FILE", ".x", "value", "Y").Code);
		Assert.Equal(0, Run("remove", "FILE", ".x").Code);

		Assert.Equal(Ini("value=!en", "", "[a]", "value=A"), File.ReadAllText(path));
	}

	[Fact]
	public void List_PrintsTheKeys_UnderAPrefix() {
		File.WriteAllText(path, Sample);

		Assert.Equal((0, "menu.file\nmenu.edit\nfiles\n$brand\n", ""), Run("list", "FILE"));
		Assert.Equal((0, "menu.file\nmenu.edit\n", ""), Run("list", "FILE", "menu."));
	}

	[Fact]
	public void List_Missing_ReadsTheEditorsRule() {
		File.WriteAllText(path, Sample);

		//menu.edit has no Italian, files no Italian plural; a constant misses nothing
		Assert.Equal((0, "menu.edit\nfiles\n", ""), Run("list", "FILE", "--missing", "IT"));
		var under = Run("list", "FILE", "menu", "--missing", "it");
		Assert.Equal((0, "menu.edit\n"), (under.Code, under.Output));
	}

	[Fact]
	public void List_Missing_WhereTheDefaultSpeaks_OrTheFileIsSilent_IsNothing() {
		File.WriteAllText(path, Sample);

		var speaks = Run("list", "FILE", "--missing", "en-GB");
		var silent = Run("list", "FILE", "--missing", "fr");

		Assert.Equal((0, ""), (speaks.Code, speaks.Output));
		Assert.Contains("the default speaks en-GB", speaks.Error);
		Assert.Equal((0, ""), (silent.Code, silent.Output));
		Assert.Contains("does not declare fr", silent.Error);
	}

	[Fact]
	public void List_Stale_ListsTheKeysMarkedStale_AndWithMissing_EitherTakes() {
		File.WriteAllText(path, Sample);
		Run("set", "FILE", "menu.file", "value-it", "Archivio", "--stale");

		Assert.Equal((0, "menu.file\n", ""), Run("list", "FILE", "--stale", "it"));
		Assert.Equal((0, "menu.file\nmenu.edit\nfiles\n", ""), Run("list", "FILE", "--missing", "it", "--stale", "it"));
		Assert.Equal((0, "", ""), Run("list", "FILE", "--stale", "en-GB"));
	}

	//the request a call prints, its items' keys, and the document to fill
	private JsonNode Requested(params string[] args) {
		var (code, output, error) = Run(["request", "FILE", .. args]);
		Assert.Equal((0, ""), (code, error));
		return JsonNode.Parse(output)!;
	}

	private static IEnumerable<string> Keys(JsonNode request) => request["items"]!.AsArray().Select(item => (string)item!["key"]!);

	[Fact]
	public void Request_AsksForWhatListWouldList_UnderAPrefix_AndUpToALimit() {
		File.WriteAllText(path, Sample);
		Run("set", "FILE", "menu.file", "value-it", "Archivio", "--stale");

		Assert.Equal(["menu.edit", "files"], Keys(Requested("--missing", "it")));
		Assert.Equal(["menu.file", "menu.edit", "files"], Keys(Requested("--stale", "it", "--missing", "it")));
		Assert.Equal(["files"], Keys(Requested("files", "--missing", "it")));
		Assert.Equal(["menu.edit"], Keys(Requested("--missing", "it", "--limit", "1")));
		var request = Requested("--missing", "it", "--brief", "Informal: tu.");
		Assert.Equal("Informal: tu.", (string)request["brief"]!);
		Assert.Equal(Path.GetFileName(path), (string)request["file"]!);
		Assert.Equal("Italiano", (string)request["target"]!["name"]!);
		//files has its Italian plain value, and lacks its plural
		Assert.Equal("""{"value#other":null}""", request["items"]![1]!["translation"]!.ToJsonString());
	}

	[Fact]
	public void Request_WithNothingToAsk_ExitsOne_PrintingNothing() {
		File.WriteAllText(path, Sample);

		var undeclared = Run("request", "FILE", "--missing", "fr");
		var none = Run("request", "FILE", "--stale", "it");

		Assert.Equal((1, ""), (undeclared.Code, undeclared.Output));
		Assert.Contains("does not declare fr", undeclared.Error);
		Assert.Equal((1, ""), (none.Code, none.Output));
		Assert.Contains("nothing in", none.Error);
	}

	[Fact]
	public void Apply_WritesTheFilledSlots_EachKeyMarkedStale_InTheWritersOrder() {
		File.WriteAllText(path, Sample);
		var answer = Requested("--missing", "it");
		answer["items"]![0]!["translation"]!["value"] = "Modifica";
		answer["items"]![1]!["translation"]!["value#other"] = "file";

		var (code, output, error) = RunWith("Here it is:\n```json\n" + answer.ToJsonString() + "\n```\n", "apply", "FILE", "-");

		Assert.Equal((0, "", "words: 2 written, 0 left, 0 skipped\n"), (code, output, error));
		string text = File.ReadAllText(path);
		Assert.Contains("[menu.edit]\nvalue=Edit\nvalue-it=Modifica\nstale-it=machine translated\n", text);
		Assert.Contains("[files]\nvalue=file\nvalue#other=files\nvalue-it=file\nvalue-it#other=file\nstale-it=machine translated\n", text);
	}

	//a link's target may change, some pages having one per language, but a person
	//checks the URL: the key goes in with its hand raised, and the change is named
	[Fact]
	public void Apply_AChangedLinkTarget_IsWritten_RaisingTheKeysHand() {
		File.WriteAllText(path, Ini("value=!en", "value-it=Italiano", "", "[help]", "value=See [the docs](https://example.com/docs)"));
		var answer = Requested("--missing", "it");
		answer["items"]![0]!["translation"]!["value"] = "Vedi [la guida](https://example.com/it/docs)";

		var (code, _, error) = RunWith(answer.ToJsonString(), "apply", "FILE", "-");

		Assert.Equal(0, code);
		Assert.Equal("words: help: written, its hand raised: the it words link to https://example.com/it/docs in place of https://example.com/docs\n"
			+ "words: 1 written, 0 left, 0 skipped, 1 hand raised\n", error);
		Assert.EndsWith("[help]\nvalue=See [the docs](https://example.com/docs)\nstale=\nvalue-it=Vedi [la guida](https://example.com/it/docs)\nstale-it=machine translated\n", File.ReadAllText(path));
	}

	[Fact]
	public void Apply_MarksWithTheWordsStaleGives_FromAFile() {
		File.WriteAllText(path, Sample);
		var answer = Requested("--missing", "it", "--limit", "1");
		answer["items"]![0]!["translation"]!["value"] = "Modifica";
		string file = path + ".json";
		File.WriteAllText(file, answer.ToJsonString());
		try {
			Assert.Equal(0, Run("apply", "FILE", file, "--stale", "DeepL").Code);
		}
		finally {
			File.Delete(file);
		}

		Assert.Contains("value-it=Modifica\nstale-it=DeepL\n", File.ReadAllText(path));
	}

	[Fact]
	public void Apply_TheRequestSentBackUntouched_WritesNothing_AndExitsZero() {
		File.WriteAllText(path, Sample);
		var (_, request, _) = Run("request", "FILE", "--missing", "it");

		Assert.Equal((0, "", "words: 0 written, 2 left, 0 skipped\n"), RunWith(request, "apply", "FILE", "-"));
		Assert.Equal(Sample, File.ReadAllText(path));
	}

	[Fact]
	public void Apply_SkipsAKeyWhoseAnswerBreaksATokenOrChangedSince_NamingIt_AndExitsOne() {
		File.WriteAllText(path, Sample);
		var answer = Requested("--missing", "it");
		answer["items"]![0]!["translation"]!["value"] = "Modifica {0}";
		answer["items"]![1]!["translation"]!["value#other"] = "file";
		Run("set", "FILE", "files", "value#other", "filez"); //since the request

		var (code, _, error) = RunWith(answer.ToJsonString(), "apply", "FILE", "-");

		Assert.Equal(1, code);
		Assert.Contains("words: menu.edit: skipped: the it words use {0}, which the default does not and no param- defines\n", error);
		Assert.Contains("words: files: skipped: its default changed since the request\n", error);
		Assert.EndsWith("words: 0 written, 0 left, 2 skipped\n", error);
		Assert.DoesNotContain("Modifica", File.ReadAllText(path));
	}

	[Fact]
	public void Apply_WhatIsNoTranslationDocument_OrARefusedEdit_ExitsTwo_AndLeavesTheFile() {
		//the last line continues, so a field added after it would become its text
		string text = Ini("value=!en", "value-it=Italiano", "", "[b]", "value=B", "", "[a]") + "value=A\\";
		File.WriteAllText(path, text);
		var answer = Requested("--missing", "it");
		answer["items"]![0]!["translation"]!["value"] = "Bi";
		answer["items"]![1]!["translation"]!["value"] = "Ah";
		var undeclared = JsonNode.Parse(answer.ToJsonString())!;
		undeclared["target"]!["code"] = "fr";

		Assert.Equal(2, RunWith("{}", "apply", "FILE", "-").Code);
		Assert.Contains("does not declare fr", RunWith(undeclared.ToJsonString(), "apply", "FILE", "-").Error);
		var refused = RunWith(answer.ToJsonString(), "apply", "FILE", "-");
		Assert.Equal(2, refused.Code);
		Assert.Contains("refused", refused.Error);
		Assert.Equal(text, File.ReadAllText(path)); //b's translation too
	}
}
