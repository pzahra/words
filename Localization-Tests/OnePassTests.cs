using System.Globalization;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     Each brace of a value read once, formatted or not (runtime SPEC: One pass for every
///     brace): what a reference or a constant brings in is never re-read with the text
///     around it, and an escape means the same wherever the value goes.
/// </summary>
[Collection("Words globals")]
public class OnePassTests {
	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	private static IWords Load(string ini) => WordsBuilder.Create().LoadString(ini).ToWords("en");

	[Fact]
	public void AnEscapedBrace_IsOneBrace_FormattedOrNot() {
		var en = Load("[k]\nvalue={0} {{0} {{>k}\n");

		Assert.Equal("{0} {0} {>k}", en["k"]);
		Assert.Equal("7 {0} {>k}", en.Format("k", 7));         //{{0} was filled, and {{>k} threw
		Assert.Equal("7 {0} {>k}", en.RenderKey("k", [7]));
		Assert.Equal("7 {0} {>k}", Words.RenderKey(en.Provider, "k", [7]));
	}

	[Fact]
	public void AReferencedKeysEscape_NeedsNoSecondEscape_InAFormattedValue() {
		var en = Load("[literal]\nvalue=a {{0} b\n\n[outer]\nvalue={0}: {>literal}\n");

		Assert.Equal("{0}: a {0} b", en["outer"]);
		Assert.Equal("7: a {0} b", en.Format("outer", 7));     //was 7: a 7 b
	}

	[Fact]
	public void AReferencesItems_AreFilledByTheOuterCall() {
		var en = Load("[inner]\nvalue=({0:N1})\n\n[outer]\nvalue={>inner}!\n");

		Assert.Equal("(2.5)!", en.Format(CultureInfo.InvariantCulture, "outer", 2.5));
	}

	[Fact]
	public void AConstantsValue_IsText_AndAddsNoItem() {
		var en = Load("[$item]\nvalue={1} or {{\n\n[k]\nvalue={0} and {$item}\n");

		Assert.Equal("a and {1} or {{", en.Format("k", "a", "b")); //was a and b or {
	}

	[Fact]
	public void AReferenceInAFormatItem_IsMalformed_ShownAsWritten_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[fmt]\nvalue=N2}{1\n\n[k]\nvalue={0:{>fmt}} then {1}\n");

		Assert.Equal("{0:{>fmt}} then b", en.Format(CultureInfo.InvariantCulture, "k", 7, "b")); //was 7.00b then b
		Assert.Contains("WORDS:FORMAT:`{0:{>fmt}}`", logger.Messages);
		logger.Messages.Clear();
		Assert.Equal("{0:{>fmt}} then {1}", en["k"]);
		Assert.Contains("WORDS:FORMAT:`{0:{>fmt}}`", logger.Messages);
	}

	[Fact]
	public void ALoneBrace_IsMalformed_AndAClosingOneIsAlwaysItself() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[k]\nvalue={0}} c}} a { b\n\n[nested]\nvalue=a { b {0}} c\n");

		Assert.Equal("7} c}} a { b", en.Format("k", 7));        //string.Format threw on both
		Assert.Equal(["WORDS:FORMAT:`{`"], logger.Messages);
		//a brace that opens nothing runs through the brace closing it, what it holds included
		Assert.Equal("a { b {0}} c", en.Format("nested", 7));
		Assert.Equal("WORDS:FORMAT:`{ b {0}}`", logger.Messages[^1]);
	}

	[Fact]
	public void TheFilesOwnEscapes_CollapseOnce() {
		var en = Load("[k]\nvalue=\\\\\\\\server and \"\" and ''''\n");

		Assert.Equal("\\\\server and \"\" and ''", en["k"]);   //was \server and " and '
	}

	[Fact]
	public void AName_InAPositionalCall_IsMarked_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[k]\nvalue={Name} has {0}\n");

		Assert.Equal("#{Name}# has 3", en.Format("k", 3));       //string.Format threw
		Assert.Equal(["WORDS:FIELD:`Name`"], logger.Messages);
	}

	[Fact]
	public void AnItemWithNothingToRead_CollapsesToItsArgumentMarked_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[k]\nvalue={0} {2,6:N2}|\n\n[named]\nvalue=[{Nope,6:N2}] {Name}\n");

		Assert.Equal("1 #{2}#|", en.Format("k", 1, 2));          //string.Format threw
		Assert.Equal("[#{Nope}#] Pat", en.FormatByName("named", new { Name = "Pat" })); //was [  #Nope#]
		Assert.Equal(["WORDS:FIELD:`2`", "WORDS:FIELD:`Nope`"], logger.Messages);
		Assert.Equal("a v", Words.FormatByName("{0} {1}", "v", "a")); //one past them is the value itself
		Assert.Equal("a #{2}#", Words.FormatByName("{0} {2}", "v", "a"));
	}

	//a member with an index, two of them, no getter or a span: reflection would throw reading one
	private sealed class Awkward {
		public string this[int index] => "by number";
		public string this[string name] => "by name";
		public string Secret { set { } }
		public ReadOnlySpan<char> Span => "span";
		public string Name => "Pat";
	}

	//a name reads a public field or a property with a getter, and nothing else: an
	//indexer is none, so string's Chars no longer throws, and shows as a name none answers
	[Fact]
	public void AName_ReachingAnIndexerOrNoGetter_IsNoMember_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[k]\nvalue={Chars} {Length}\n");

		Assert.Equal("#{Chars}# 3", Words.FormatByName("{Chars} {Length}", "abc")); //1.5.0 threw
		Assert.Equal("#{Chars}# 3", en.FormatByName("k", "abc"));
		Assert.Equal("#{Chars}# 3", en.FormatParams("k", "abc"));
		Assert.Equal("#{Item}# #{Secret}# #{Span}# Pat", Words.FormatByName("{Item} {Secret} {Span} {Name}", new Awkward()));
		Assert.Equal(["Chars", "Chars", "Chars", "Item", "Secret", "Span"], logger.Messages.Select(message => message["WORDS:FIELD:`".Length..^1]));
	}

	[Fact]
	public void ASelectorPastTheArguments_PicksOther_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[word]\nvalue=word\nvalue#other=words\n\n[k]\nvalue={0} {1#word}\n");

		Assert.Equal("1 words", en.Format("k", 1));               //string.Format threw
		Assert.Equal(["WORDS:FIELD:`1`"], logger.Messages);
	}

	[Fact]
	public void AName_TakesAnAlignment() {
		var en = Load("[k]\nvalue=[{Name,4}|{Name,-4}]\n");

		Assert.Equal("[  ab|ab  ]", en.FormatByName("k", new { Name = "ab" }));
	}

	[Fact]
	public void AFormatItsArgumentRejects_IsMarked_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		Words.Logger = logger;
		var en = Load("[k]\nvalue=[{0,5:Q}] {0} {1:N1}\n");

		Assert.Equal("[#{0}#] 3 2.5", en.Format(CultureInfo.InvariantCulture, "k", 3, 2.5)); //string.Format threw
		Assert.Equal(["WORDS:FORMAT:`{0,5:Q}`"], logger.Messages);
	}

	//an IFormatProvider whose formatter shouts strings and leaves the rest to the argument
	private sealed class Shouting(CultureInfo culture) : IFormatProvider, ICustomFormatter {
		public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : culture.GetFormat(formatType);
		public string Format(string? format, object? arg, IFormatProvider? formatProvider) => (arg as string)?.ToUpperInvariant()!;
	}

	[Theory]
	[InlineData("{0}")]
	[InlineData("{0,9}|")]
	[InlineData("{0,-9}|")]
	[InlineData("{1:N2}")]
	[InlineData("{1,12:N2}|")]
	[InlineData("{2:D5}")]
	[InlineData("{2,-6:X}|")]
	[InlineData("{3:yyyy-MM-dd HH:mm}")]
	[InlineData("{3:d}")]
	[InlineData("{3}")]
	[InlineData("[{4}]")]
	[InlineData("[{4,5}]")]
	[InlineData("{5}")]
	[InlineData("{5,-8}|")]
	[InlineData("{0:E3} {1:P1}")]
	[InlineData("{0:0.##}")]
	[InlineData("{{0 {6}")]
	[InlineData("{7:C}")]
	[InlineData("x{0}y{1}z{2}")]
	[InlineData("{2:#,##0}")]
	public void ATemplateWithNoWordsSyntax_FormatsAsStringFormatFormatsIt(string template) {
		object?[] args = [3.14159, 1234.5678, 42, new DateTime(2026, 10, 15, 13, 45, 30), null, "text", true, 1234567.891m];
		IFormatProvider?[] providers = [null, CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("fr-FR"),
			CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.GetCultureInfo("ar-SA"), new Shouting(CultureInfo.GetCultureInfo("en-GB"))];
		foreach (IFormatProvider? provider in providers) {
			Assert.Equal(string.Format(provider, template, args), Words.FormatByName(provider, template, null, args));
		}
	}

	[Fact]
	public void PreFormatByName_WritesATemplateStringFormatReads() {
		var (template, arguments) = Words.PreFormatByName("{{x} {Name:N1} }", new { Name = 2.5 });

		Assert.Equal("{{x}} {1:N1} }}", template);
		Assert.Equal("{x} 2.5 }", string.Format(CultureInfo.InvariantCulture, template, arguments));
	}
}
