using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>
///     The documents (SPEC: A transaction): every line outside an edit kept byte for byte with its
///     break; a line added takes the file's first break, and one after an unbroken last line gives
///     it the break and goes without; the section <c>""</c> at the head and under <c>[]</c>; a line
///     no element stays where it stands; a refused edit leaves the document as it was; names with
///     and without case; a section named twice; trimming.
/// </summary>
public class DocumentTests {
	[Fact]
	public void EveryLineOutsideAnEdit_IsKeptByteForByte_EachWithItsBreak() {
		const string Text = "top = 1\r\n[s]\nx = 1 ; part of the value\r  y=2\n\n; a note\r\n[t]\r\nz=3";
		IniDocument document = Ini.Read(Text);
		document.Set("s", "y", "5");
		Assert.Equal(Text.Replace("  y=2", "  y=5"), document.Text);
		Assert.Equal("1 ; part of the value", document.Get("s", "x"));
	}

	[Fact]
	public void ALineAddedAfterAnUnbrokenLastLine_GivesItTheBreak_AndGoesWithout() {
		IniDocument document = Ini.Read("[s]\r\nx=1");
		document.Set("s", "y", "2");
		Assert.Equal("[s]\r\nx=1\r\ny=2", document.Text);
	}

	[Fact]
	public void ALineAdded_TakesTheFilesFirstBreak_ElseTheDialects_ElseTheSystems() {
		IniDocument first = Ini.Read("a=1\n[s]\r\nx=1\r\n");
		first.Set("s", "y", "2");
		Assert.Equal("a=1\n[s]\r\nx=1\r\ny=2\n", first.Text);

		IniDocument dialects = Ini.Read("[s]", new IniRules { LineBreak = "\r\n" });
		dialects.Set("s", "x", "1");
		Assert.Equal("[s]\r\nx=1", dialects.Text);

		IniDocument systems = Ini.Read("[s]");
		systems.Set("s", "x", "1");
		Assert.Equal($"[s]{Environment.NewLine}x=1", systems.Text);
	}

	[Fact]
	public void ANewSection_GoesAtTheEnd_AfterABlankLine() {
		IniDocument document = Ini.Read("[s]\nx=1\n");
		document.Set("t", "y", "2");
		Assert.Equal("[s]\nx=1\n\n[t]\ny=2\n", document.Text);

		IniDocument empty = IniDocument.New(IniDialect.Sections(new IniRules { LineBreak = "\n" }));
		empty.Set("t", "y", "2");
		Assert.Equal("[t]\ny=2\n", empty.Text);
	}

	[Fact]
	public void AKeySetManyTimes_IsWrittenOnce() {
		IniDocument document = Ini.Read("[s]\nx=1\n");
		foreach (string value in (string[])["2", "3", "4"]) {
			document.Set("s", "x", value);
		}
		Assert.Equal("[s]\nx=4\n", document.Text);
	}

	[Fact]
	public void ARefusedEdit_LeavesTheDocumentAsItWas() {
		//x runs on to nothing, so a line added after it would be its continuation
		IniDocument document = Ini.Read("[s]\nx=a\\", new IniRules { Continuations = IniContinuations.Backslash });
		Assert.Equal("a\n", document.Get("s", "x"));
		IniCheckException refused = Assert.Throws<IniCheckException>(() => document.Set("s", "y", "1"));
		Assert.Contains(refused.Differences, difference => difference is { Section: "s", Key: "x", Expected: "a\n", Actual: "a\ny=1" });
		Assert.Contains(refused.Differences, difference => difference is { Section: "s", Key: "y", Expected: "1", Actual: null });
		Assert.Equal("[s]\nx=a\\", document.Text);
		Assert.False(document.IsEdited);
	}

	[Fact]
	public void TheElements_CutEveryLineTheDialectRecognises() {
		IniDocument document = Ini.Read("\n\n; one\n; two\n[s] \nx=1\nnot an entry\n");
		Assert.Collection(document.Elements,
			blank => Assert.Equal((0, 2), (Assert.IsType<IniBlank>(blank).Line, blank.Count)),
			comment => Assert.Equal(["; one", "; two"], Assert.IsType<IniComment>(comment).Lines),
			header => Assert.Equal(("s", 4), (Assert.IsType<IniHeader>(header).Name, header.Line)),
			field => Assert.Equal(("x", "1", "s"), (Assert.IsType<IniField>(field).Key, ((IniField)field).Value, field.Section)));
	}

	[Fact]
	public void FieldsAboveTheFirstHeader_AreInTheSectionNamedEmpty_AndABracketPairOpensItAgain() {
		IniDocument document = Ini.Read("a=1\n[s]\nb=2\n[]\nc=3\n");
		Assert.Equal(["", "s"], document.Sections);
		Assert.Equal(["a", "c"], document.Keys(""));
		Assert.Equal("3", document.Get("", "c"));
	}

	[Fact]
	public void ADialectTakingNoBrackets_LeavesThemAndTheLinesUnderThem_AsLinesItDoesNotRecognise() {
		const string Text = "a=1\n[s]\nb=2\n[]\nc=3\\\n[t]\n[u]\nd=4\n";
		IniDocument document = Ini.Read(Text, new IniRules { EmptyHeader = false, Continuations = IniContinuations.Backslash });
		Assert.Equal(["", "s", "u"], document.Sections);
		Assert.Equal(["a"], document.Keys(""));
		Assert.Equal(["b"], document.Keys("s"));
		//c ran on over [t], read past with it
		Assert.DoesNotContain(document.Elements, element => element is IniHeader { Name: "t" or "" });
		document.Set("s", "b", "5");
		document.RemoveSection("u");
		Assert.Equal("a=1\n[s]\nb=5\n[]\nc=3\\\n[t]\n", document.Text);
		Assert.Equal(IniEditFailure.EmptyHeader, Assert.Throws<IniEditException>(() => document.RenameSection("s", "")).Failure);
	}

	[Fact]
	public void ANewSectionNamedEmpty_StartsAtTheHead() {
		IniDocument document = Ini.Read("; a note\n[s]\nb=2\n");
		document.Set("", "a", "1");
		Assert.Equal("a=1\n\n; a note\n[s]\nb=2\n", document.Text);
		document.Set("", "z", "2");
		Assert.Equal("a=1\nz=2\n\n; a note\n[s]\nb=2\n", document.Text);
	}

	[Fact]
	public void TheHead_Renamed_GainsAHeader_AndMoved_IsGatheredUnderBrackets() {
		IniDocument renamed = Ini.Read("; a note\na=1\n[s]\nb=2");
		renamed.RenameSection("", "top");
		Assert.Equal("; a note\n[top]\na=1\n[s]\nb=2", renamed.Text);

		IniDocument moved = Ini.Read("a=1\n\n[s]\nb=2\n\n[]\nc=3\n\n[t]\nd=4");
		moved.MoveSection("", "s");
		Assert.Equal("[s]\nb=2\n\n[]\na=1\nc=3\n\n[t]\nd=4", moved.Text);
		Assert.Equal(["a", "c"], moved.Keys(""));

		IniDocument refused = Ini.Read("a=1\n[s]\nb=2", new IniRules { EmptyHeader = false });
		Assert.Equal(IniEditFailure.EmptyHeader, Assert.Throws<IniEditException>(() => refused.MoveSection("", "s")).Failure);
	}

	[Fact]
	public void ALineNoElement_StaysWhereItStands_WhileTheElementsAroundItChange() {
		IniDocument document = Ini.Read("[s]\nx=1\nnot an entry\ny=2\n\n[t]\nz=3\n\n[u]\nw=4\n");
		document.Set("s", "x", "5");
		document.Remove("s", "y");
		Assert.Equal("[s]\nx=5\nnot an entry\n\n[t]\nz=3\n\n[u]\nw=4\n", document.Text);
		document.MoveSection("s", "u");
		Assert.Equal("not an entry\n\n[t]\nz=3\n\n[u]\nw=4\n\n[s]\nx=5\n", document.Text);
		document.RemoveSection("t");
		Assert.Equal("not an entry\n\n[u]\nw=4\n\n[s]\nx=5\n", document.Text);
	}

	[Fact]
	public void NamesDifferingOnlyInCase_AreOneSectionWithoutCase_AndTwoExactly() {
		const string Text = "[S]\nX=1\n[s]\nx=2\ny=3\n";
		IniDocument without = Ini.Read(Text);
		Assert.Equal(["S"], without.Sections);
		Assert.Equal(["X", "y"], without.Keys("s"));
		Assert.Equal("2", without.Get("S", "x"));

		IniDocument exact = Ini.Read(Text, new IniRules { Names = StringComparer.Ordinal });
		Assert.Equal(["S", "s"], exact.Sections);
		Assert.Equal("1", exact.Get("S", "X"));
		Assert.Null(exact.Get("S", "x"));
	}

	[Fact]
	public void ARename_ByCaseAlone_RenamesEveryOccurrence() {
		IniDocument document = Ini.Read("[S]\nx=1\n[ s ] ; kept\ny=2\n", new IniRules { CommentsAfterEntries = true });
		document.RenameSection("s", "Settings");
		Assert.Equal("[Settings]\nx=1\n[ Settings ] ; kept\ny=2\n", document.Text);
		document.RenameSection("settings", "SETTINGS");
		Assert.Equal(["SETTINGS"], document.Sections);
	}

	[Fact]
	public void ARename_IsRefused_OntoATakenName_ForNoSuchSection_AndForANameThatWillNotWrite() {
		IniDocument document = Ini.Read("[s]\nx=1\n[t]\ny=2\n");
		Assert.Equal(IniEditFailure.NameTaken, Assert.Throws<IniEditException>(() => document.RenameSection("s", "T")).Failure);
		Assert.Equal(IniEditFailure.NoSuchSection, Assert.Throws<IniEditException>(() => document.RenameSection("u", "v")).Failure);
		Assert.Equal(IniEditFailure.InvalidName, Assert.Throws<IniEditException>(() => document.RenameSection("s", "a]b")).Failure);
		Assert.Equal(IniEditFailure.InvalidName, Assert.Throws<IniEditException>(() => document.Set("s", "a=b", "1")).Failure);
		Assert.Equal(IniEditFailure.InvalidName, Assert.Throws<IniEditException>(() => document.Set("s", "; a", "1")).Failure);
		Assert.Equal("[s]\nx=1\n[t]\ny=2\n", document.Text);
	}

	[Fact]
	public void ASectionNamedTwice_ReadsAsOne_TheLastOfAKeyWinning() {
		IniDocument document = Ini.Read("[s]\nx=1\n[t]\n[s]\nx=2\ny=3\n");
		Assert.Equal(["s", "t"], document.Sections);
		Assert.Equal(["x", "y"], document.Keys("s"));
		Assert.Equal("2", document.Get("s", "x"));
	}

	[Fact]
	public void AKeySet_LandsInTheFirstOccurrence_AndAnOccurrenceLeftEmptyGoes() {
		IniDocument document = Ini.Read("[s]\nx=1\n\n[t]\nz=1\n\n[s]\nx=2");
		document.Set("s", "x", "9");
		Assert.Equal("[s]\nx=9\n\n[t]\nz=1", document.Text);

		IniDocument added = Ini.Read("[s]\nx=1\n\n[t]\nz=1\n\n[s]\nw=2\n");
		added.Set("s", "y", "9");
		Assert.Equal("[s]\nx=1\ny=9\n\n[t]\nz=1\n\n[s]\nw=2\n", added.Text);
	}

	[Fact]
	public void ASectionNamedTwice_IsRemovedRenamedAndMoved_InEveryOccurrence() {
		const string Text = "[s]\nx=1\n\n[t]\nz=1\n\n[s]\ny=2\n\n[u]\nw=1";
		IniDocument removed = Ini.Read(Text);
		removed.RemoveSection("s");
		Assert.Equal("[t]\nz=1\n\n[u]\nw=1", removed.Text);

		IniDocument renamed = Ini.Read(Text);
		renamed.RenameSection("s", "v");
		Assert.Equal(Text.Replace("[s]", "[v]"), renamed.Text);

		IniDocument moved = Ini.Read(Text);
		moved.MoveSection("s", "u");
		Assert.Equal("[t]\nz=1\n\n[u]\nw=1\n\n[s]\nx=1\ny=2", moved.Text);
		Assert.Equal(["t", "u", "s"], moved.Sections);
	}

	[Fact]
	public void ASectionReplaced_TakesThePairsInTheirOrder_WhereItsFirstFieldStood() {
		IniDocument document = Ini.Read("[s]\n; kept\nb=1\na=2\n\n; about t\n[t]\nz=1\n");
		document.ReplaceSection("s", [new("a", "1"), new("c", "3")]);
		Assert.Equal("[s]\n; kept\na=1\nc=3\n\n; about t\n[t]\nz=1\n", document.Text);
		document.ReplaceSection("u", [new("k", "v")]);
		Assert.Equal("[s]\n; kept\na=1\nc=3\n\n; about t\n[t]\nz=1\n\n[u]\nk=v\n", document.Text);
		Assert.Throws<ArgumentException>(() => document.ReplaceSection("s", [new("a", "1"), new("A", "2")]));
	}

	[Fact]
	public void ASectionTheEditsEmptied_Goes_AndOneEmptyByHandOrHoldingOnlyComments_Stays() {
		IniDocument emptied = Ini.Read("[s]\nx=1\n\n[t]\ny=1");
		emptied.Remove("s", "x");
		Assert.Equal("[t]\ny=1", emptied.Text);
		Assert.Equal(["t"], emptied.Sections);

		IniDocument byHand = Ini.Read("[s]\n\n[c]\n; only this\n\n[t]\ny=1");
		byHand.Remove("t", "y");
		Assert.Equal("[s]\n\n[c]\n; only this", byHand.Text);
		Assert.Equal(["s", "c"], byHand.Sections);

		//an occurrence empty by hand keeps the section standing when its other one goes
		IniDocument twice = Ini.Read("[s]\n\n[t]\nz=1\n\n[s]\nx=1\n");
		twice.Remove("s", "x");
		Assert.Equal("[s]\n\n[t]\nz=1\n", twice.Text);
		Assert.Equal(["s", "t"], twice.Sections);
	}

	[Fact]
	public void AnElementReadBeforeAnEdit_IsRefusedOnceItNoLongerStandsWhereItStood() {
		IniDocument document = Ini.Read("[s]\n; about x\nx=1\n");
		IniComment comment = document.Elements.OfType<IniComment>().Single();
		document.Set("", "a", "1");
		Assert.Equal(IniEditFailure.Moved, Assert.Throws<IniEditException>(() => document.RemoveComment(comment)).Failure);
		document.RemoveComment(document.Elements.OfType<IniComment>().Single());
		Assert.Equal("a=1\n\n[s]\nx=1\n", document.Text);
	}

	[Fact]
	public void ADocumentReplacedWhole_OverAFileAndOverNone_IsTheCallersText() {
		IniDocument document = Ini.Read("[s]\nx=1\n");
		document.Replace("[t]\r\ny=2\r\n", new System.Text.UTF8Encoding(false), byteOrderMark: true);
		Assert.Equal(["t"], document.Sections);
		Assert.Equal((byte[])[0xEF, 0xBB, 0xBF, .. "[t]\r\ny=2\r\n"u8.ToArray()], document.ToBytes());

		IniDocument none = IniDocument.New(IniDialect.Sections(new IniRules()));
		Assert.True(none.IsNew);
		none.Replace("[t]\n", System.Text.Encoding.Latin1);
		Assert.Equal("[t]\n"u8.ToArray(), none.ToBytes());
	}
}
