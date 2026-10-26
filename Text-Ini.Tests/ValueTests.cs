using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>
///     Values and comments (SPEC: Values and comments), each rule on and off: continued, quoted and
///     escaped values read as their text and write back in their own form; a value its form cannot
///     hold takes the dialect's first; comment blocks stand while their neighbours move or go; a
///     comment after an entry outlives an edit to its value.
/// </summary>
public class ValueTests {
	private static readonly IniRules Backslash = new() { Continuations = IniContinuations.Backslash };
	private static readonly IniRules Words = new() { Continuations = IniContinuations.Both, Escapes = IniEscapes.TakesNext };

	[Fact]
	public void ABackslashContinuedValue_ReadsWithItsBreaks_AndWritesBackContinued() {
		IniDocument document = Ini.Read("[s]\nx=one\\\ntwo\ny=1\n", Backslash);
		Assert.Equal("one\ntwo", document.Get("s", "x"));
		Assert.Equal(IniValueForm.Backslash, document.Elements.OfType<IniField>().First().Form);
		document.Set("s", "x", "three\nfour\nfive");
		Assert.Equal("[s]\nx=three\\\nfour\\\nfive\ny=1\n", document.Text);
		document.Set("s", "x", "six");
		Assert.Equal("[s]\nx=six\ny=1\n", document.Text);
	}

	[Fact]
	public void AnUnderscoreContinuedValue_ReadsJoined_AndWritesBackOnOneLine() {
		IniDocument document = Ini.Read("[s]\nx=one_\ntwo\n", new IniRules { Continuations = IniContinuations.Underscore });
		Assert.Equal("onetwo", document.Get("s", "x"));
		document.Set("s", "x", "three");
		Assert.Equal("[s]\nx=three\n", document.Text);
	}

	[Fact]
	public void WithoutContinuations_AMarkerIsTheValues() {
		IniDocument document = Ini.Read("[s]\nx=one\\\ny=1\n");
		Assert.Equal("one\\", document.Get("s", "x"));
		Assert.Equal("1", document.Get("s", "y"));
	}

	[Fact]
	public void ADoubledMarker_ReadsAsOne_AndAMarkerMeantAsItself_IsWrittenDoubled() {
		IniDocument document = Ini.Read("[s]\nx=a\\\\b\ny=c\\\\\n", Backslash);
		Assert.Equal("a\\b", document.Get("s", "x"));
		Assert.Equal("c\\", document.Get("s", "y"));
		document.Set("s", "x", "d\\");
		Assert.Equal("[s]\nx=d\\\\\ny=c\\\\\n", document.Text);
		Assert.Equal("d\\", document.Get("s", "x"));
	}

	[Fact]
	public void WithMarkersTakingWhatFollows_APairEndingALine_RunsOnToNothing() {
		IniDocument document = Ini.Read("[s]\nx=one\\_\ny=one_\\\nz=a\\\nb\n", Words);
		Assert.Equal("one\\_", document.Get("s", "x"));
		Assert.Equal("one_\\", document.Get("s", "y"));
		Assert.Equal("a\nb", document.Get("s", "z"));
		//doubled, they read as one
		Assert.Equal("a\\b", Ini.Read("[s]\nx=a\\\\b\n", Words).Get("s", "x"));
	}

	[Fact]
	public void AContinuedLine_TakesAHeaderACommentOrABlank_AsItsContinuation() {
		IniDocument header = Ini.Read("[s]\nx=a\\\n[t]\ny=1\n", Backslash);
		Assert.Equal("a\n[t]", header.Get("s", "x"));
		Assert.Equal(["s"], header.Sections);
		Assert.Equal("1", header.Get("s", "y"));

		Assert.Equal("a\n; c", Ini.Read("[s]\nx=a\\\n; c\n", Backslash).Get("s", "x"));
		IniDocument blank = Ini.Read("[s]\nx=a\\\n\nz=1\n", Backslash);
		Assert.Equal("a\n", blank.Get("s", "x"));
		Assert.Equal("1", blank.Get("s", "z"));
	}

	[Fact]
	public void AQuotedValue_RunsOnUntilItsClosingQuote_ItsBreaksRaw_AndADoubledQuoteIsOne() {
		var quotes = new IniRules { Quotes = IniQuotes.Both };
		IniDocument document = Ini.Read("[s]\r\nx=\" a\r\n b \"\r\ny='say ''hi'''\r\n", quotes);
		Assert.Equal(" a\r\n b ", document.Get("s", "x"));
		Assert.Equal("say 'hi'", document.Get("s", "y"));
		Assert.Equal(IniValueForm.SingleQuoted, document.Elements.OfType<IniField>().Last().Form);
		document.Set("s", "x", "c\nd");
		Assert.Equal("[s]\r\nx=\"c\nd\"\r\ny='say ''hi'''\r\n", document.Text);
		document.Set("s", "y", "it's");
		Assert.Equal("[s]\r\nx=\"c\nd\"\r\ny='it''s'\r\n", document.Text);
		Assert.Equal("it's", document.Get("s", "y"));

		//without quotes, a quote is the value's
		Assert.Equal("\"a\"", Ini.Read("[s]\nx=\"a\"\n").Get("s", "x"));
	}

	[Fact]
	public void AValueItsFormCannotHold_TakesTheDialectsFirst() {
		IniDocument continued = Ini.Read("[s]\nx=a\n", new IniRules { Continuations = IniContinuations.Backslash, Quotes = IniQuotes.Double });
		continued.Set("s", "x", "b\nc");
		Assert.Equal("[s]\nx=b\\\nc\n", continued.Text);
		continued.Set("s", "x", " padded ");
		Assert.Equal("[s]\nx=\" padded \"\n", continued.Text);

		IniDocument quoted = Ini.Read("[s]\nx=a\n", new IniRules { Quotes = IniQuotes.Double });
		quoted.Set("s", "x", "b\nc");
		Assert.Equal("[s]\nx=\"b\nc\"\n", quoted.Text);

		IniDocument plain = Ini.Read("[s]\nx=a\n");
		Assert.Equal(IniEditFailure.Unwritable, Assert.Throws<IniEditException>(() => plain.Set("s", "x", "b\nc")).Failure);
		Assert.Equal(IniEditFailure.Unwritable, Assert.Throws<IniEditException>(() => plain.Set("s", "x", " padded")).Failure);
		Assert.Equal("[s]\nx=a\n", plain.Text);
	}

	[Fact]
	public void ACommentBlock_StandsWhileTheElementBelowItMovesOrGoes_AndMovesOrGoesItselfWhenAsked() {
		IniDocument document = Ini.Read("[s]\n; about x\nx=1\ny=2\n\n; about t\n[t]\nz=3\n\n[u]\nw=4\n");
		document.Remove("s", "x");
		Assert.Equal("[s]\n; about x\ny=2\n\n; about t\n[t]\nz=3\n\n[u]\nw=4\n", document.Text);
		//which section a comment is about is its client's to say; the blank line under t still parts it from u
		document.MoveSection("t", "u");
		Assert.Equal("[s]\n; about x\ny=2\n\n; about t\n\n[u]\nw=4\n\n[t]\nz=3\n", document.Text);
	}

	[Fact]
	public void ACommentBlock_IsEditedMovedAndRemoved_AsOne() {
		IniDocument document = Ini.Read("[s]\n; one\n; two\nx=1\n\n[t]\ny=2\n");
		IniComment block = document.Elements.OfType<IniComment>().Single();
		document.SetComment(block, ["; three"]);
		Assert.Equal("[s]\n; three\nx=1\n\n[t]\ny=2\n", document.Text);

		block = document.Elements.OfType<IniComment>().Single();
		document.MoveComment(block, document.Elements.OfType<IniField>().Last());
		Assert.Equal("[s]\nx=1\n\n[t]\n; three\ny=2\n", document.Text);

		document.AddComment(null, ["; the end"]);
		Assert.Equal("[s]\nx=1\n\n[t]\n; three\ny=2\n; the end\n", document.Text);
		document.RemoveComment(document.Elements.OfType<IniComment>().First());
		Assert.Equal("[s]\nx=1\n\n[t]\ny=2\n; the end\n", document.Text);

		Assert.Equal(IniEditFailure.NotAComment, Assert.Throws<IniEditException>(() => document.AddComment(null, ["x=2"])).Failure);
	}

	[Fact]
	public void CommentMarkers_AreTheDialects() {
		const string Text = "# hash\n; semicolon\n[s]\nx=1\n";
		Assert.Equal(["; semicolon"], Ini.Read(Text).Elements.OfType<IniComment>().Single().Lines);
		Assert.Equal(["# hash"], Ini.Read(Text, new IniRules { Comments = IniCommentMarkers.Hash }).Elements.OfType<IniComment>().Single().Lines);
		Assert.Equal(["# hash", "; semicolon"], Ini.Read(Text, new IniRules { Comments = IniCommentMarkers.Either }).Elements.OfType<IniComment>().Single().Lines);
	}

	[Fact]
	public void Separators_AreTheDialects() {
		const string Text = "[s]\na:b=c\n";
		Assert.Equal("c", Ini.Read(Text).Get("s", "a:b"));
		Assert.Equal("b=c", Ini.Read(Text, new IniRules { Separators = IniSeparators.Either }).Get("s", "a"));
		IniDocument colon = Ini.Read("[s]\na: 1\n", new IniRules { Separators = IniSeparators.Colon });
		Assert.Equal("1", colon.Get("s", "a"));
		colon.Set("s", "b", "2");
		Assert.Equal("[s]\na: 1\nb:2\n", colon.Text);
	}

	[Fact]
	public void ACommentAfterAnEntry_OutlivesAnEditToItsValue() {
		var after = new IniRules { CommentsAfterEntries = true, Quotes = IniQuotes.Double };
		IniDocument document = Ini.Read("[s] ; the settings\nwidth=900   ; at 100%\nname=\"a ; b\" ; quoted\nurl=a;b\n", after);
		Assert.Equal("900", document.Get("s", "width"));
		Assert.Equal("a ; b", document.Get("s", "name"));
		Assert.Equal("a;b", document.Get("s", "url"));
		Assert.Equal("; the settings", document.Elements.OfType<IniHeader>().Single().Comment);
		Assert.Equal("; at 100%", document.Elements.OfType<IniField>().First().Comment);
		document.Set("s", "width", "1200");
		document.Set("s", "name", "c");
		Assert.Equal("[s] ; the settings\nwidth=1200   ; at 100%\nname=\"c\" ; quoted\nurl=a;b\n", document.Text);
		//a value that would start a comment of its own is quoted
		document.Set("s", "url", "x ; y");
		Assert.Equal("x ; y", document.Get("s", "url"));
		Assert.EndsWith("url=\"x ; y\"\n", document.Text);

		//without, the comment is the value's
		Assert.Equal("900   ; at 100%", Ini.Read("[s]\nwidth=900   ; at 100%\n").Get("s", "width"));
	}

	[Fact]
	public void AnEmptyValue_WithACommentAfterIt_ReadsEmpty_AndIsWritten() {
		var after = new IniRules { CommentsAfterEntries = true, Quotes = IniQuotes.Double };
		IniDocument document = Ini.Read("[s]\nx= ; note\ny=old ; kept\nz=;not\n", after);
		Assert.Equal("", document.Get("s", "x"));
		Assert.Equal("; note", document.Elements.OfType<IniField>().First().Comment);
		Assert.Equal(";not", document.Get("s", "z"));
		document.Set("s", "y", "");
		Assert.Equal("", document.Get("s", "y"));
		Assert.Equal("[s]\nx= ; note\ny= ; kept\nz=;not\n", document.Text);
		//a value starting with a marker is quoted, as spaces before it would start a comment
		document.Set("s", "x", "; a");
		Assert.Equal("; a", document.Get("s", "x"));
	}
}
