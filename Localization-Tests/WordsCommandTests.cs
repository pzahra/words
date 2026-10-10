using PatTech.Localization.Cli;
using System.Globalization;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The <c>words</c> command, run in process (editor SPEC: A command line for
///     tools): the calls, their exit codes, values on the output and gripes on the
///     error stream, a value from stdin, read as the program reads a pipe,
///     <c>--stale</c>, and <c>list --missing</c> by the editor's rule.
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

		Assert.Contains("value=Edit\ncomment=first\\\nsecond\n", File.ReadAllText(path));
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
		Assert.Contains("value-it=file\nstale=\n", text); //the default's mark keeps no words
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
	public void ARefusedEdit_ExitsTwo_AndLeavesTheFile() {
		string text = "[a]\nvalue=A\\";
		File.WriteAllText(path, text);

		var (code, _, error) = Run("set", "FILE", "a", "context", "c");

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
}
