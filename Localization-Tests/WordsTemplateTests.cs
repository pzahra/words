using System.Globalization;
using Xunit;
using static PatTech.Localization.WordsPiece;

namespace PatTech.Localization.Tests;

//the piece, not the PatTech.Text namespace the ini driver brings in, which a using static loses to
using Text = WordsPiece.Text;

/// <summary>
///     A value read into its pieces, once, in the order it was written (runtime SPEC:
///     One pass for every brace).
/// </summary>
public class WordsTemplateTests {
	[Fact]
	public void PlainWords_AreOneText_AndNoWordsAreNone() {
		Assert.Equal([new Text(0, 5, "Hello")], WordsTemplate.Parse("Hello"));
		Assert.Empty(WordsTemplate.Parse(""));
	}

	[Theory]
	[InlineData("a {{b} c", "a {b} c")]
	[InlineData("{{0}", "{0}")]
	[InlineData("{{0}}", "{0}}")]
	[InlineData("{{>key}", "{>key}")]
	[InlineData("{{0#word}", "{0#word}")]
	[InlineData("a } b", "a } b")]
	[InlineData("}}}", "}}}")]
	[InlineData("{{{{", "{{")]
	[InlineData("{{\"a\": {{\"b\": 1}}", "{\"a\": {\"b\": 1}}")]
	public void AnEscapedOpeningBrace_IsOneBrace_AndAClosingOneIsAlwaysItself(string value, string shown)
		=> Assert.Equal([new Text(0, value.Length, shown)], WordsTemplate.Parse(value));

	[Fact]
	public void ReferencesAndConstants_AreTheirKeys() {
		Assert.Equal([new Text(0, 3, "Hi "), new Reference(3, 7, "name"), new Text(10, 1, "!")], WordsTemplate.Parse("Hi {>name}!"));
		Assert.Equal([new Reference(0, 7, ".sub")], WordsTemplate.Parse("{>.sub}"));
		Assert.Equal([new Constant(0, 7, "$unit")], WordsTemplate.Parse("{$unit}"));
	}

	[Fact]
	public void ASelector_NamesItsArgumentAndKey() {
		Assert.Equal([new Item(0, 3, "0", null, null), new Text(3, 1, " "), new Selector(4, 8, "0", "word")], WordsTemplate.Parse("{0} {0#word}"));
		Assert.Equal([new Selector(0, 10, "Count", ".n")], WordsTemplate.Parse("{Count#.n}"));
		Assert.Equal([new Selector(0, 6, "1", "w")], WordsTemplate.Parse("{01#w}"));
	}

	[Theory]
	[InlineData("{0}", "0", null, null)]
	[InlineData("{01}", "1", null, null)]
	[InlineData("{0,5:N2}", "0", 5, "N2")]
	[InlineData("{1 , -5 :X}", "1", -5, "X")]
	[InlineData("{0:}", "0", null, null)]
	[InlineData("{0: N2}", "0", null, " N2")]
	[InlineData("{0:a\nb}", "0", null, "a\nb")]
	[InlineData("{Name}", "Name", null, null)]
	[InlineData("{ Name : d }", "Name", null, "d")]
	[InlineData("{\tName,-3}", "Name", -3, null)]
	[InlineData("{_x1:g2}", "_x1", null, "g2")]
	[InlineData("{Größe}", "Größe", null, null)]
	[InlineData("{0,9999999}", "0", 9999999, null)] //as wide as string.Format pads
	[InlineData("{0,-0009999999}", "0", -9999999, null)]
	[InlineData("{0000000000001}", "1", null, null)]
	public void AFormatItem_ReadsItsArgumentAlignmentAndSpec(string value, string argument, int? alignment, string? format)
		=> Assert.Equal([new Item(0, value.Length, argument, alignment, format)], WordsTemplate.Parse(value));

	[Theory]
	[InlineData("{0:{>fmt}}")]
	[InlineData("{0:N{>digits}}")]
	[InlineData("{0,x}")]
	[InlineData("{0,}")]
	[InlineData("{0,- 5}")]
	[InlineData("{}")]
	[InlineData("{ 0}")]
	[InlineData("{0x}")]
	[InlineData("{0\t}")]
	[InlineData("{>}")]
	[InlineData("{$}")]
	[InlineData("{0#}")]
	[InlineData("{0#a b}")]
	[InlineData("{ Count#word}")]
	[InlineData("{Count #word}")]
	[InlineData("{Ärger}")]
	[InlineData("{x-y}")]
	[InlineData("{Name:a\nb}")]
	[InlineData("{99999999999}")]
	[InlineData("{10000000}")]
	[InlineData("{0,10000000}")]
	[InlineData("{ hello {0} }")]
	public void ABraceThatOpensNothing_IsMalformed_ThroughTheBraceClosingIt(string value)
		=> Assert.Equal([new Malformed(0, value.Length)], WordsTemplate.Parse(value));

	[Fact]
	public void ABraceNothingCloses_IsMalformedAlone_AndWhatFollowsReadsOn() {
		Assert.Equal([new Text(0, 2, "a "), new Malformed(2, 1), new Text(3, 2, " b")], WordsTemplate.Parse("a { b"));
		Assert.Equal([new Malformed(0, 1), new Text(1, 1, "0")], WordsTemplate.Parse("{0"));
		Assert.Equal([new Malformed(0, 1), new Text(1, 2, ">a"), new Item(3, 3, "b", null, null)], WordsTemplate.Parse("{>a{b}"));
		Assert.Equal([new Malformed(0, 1), new Text(1, 3, " b "), new Reference(4, 6, "key")], WordsTemplate.Parse("{ b {>key}"));
	}

	[Fact]
	public void AFormatString_RefersToNothing() {
		Assert.Equal([new Malformed(0, 6), new Text(6, 1, " "), new Malformed(7, 4)], WordsTemplate.ParseFormat("{>key} {$c}"));
		Assert.Equal([new Text(0, 2, "{"), new Item(2, 3, "0", null, null), new Text(5, 1, " "), new Selector(6, 8, "0", "word")], WordsTemplate.ParseFormat("{{{0} {0#word}"));
	}

	[Theory]
	[InlineData("Hi {>name}, {0,5:N2} {{x}} {Count#.n} {$c} {0:{>f}} } {")]
	[InlineData("{{{0}}}")]
	[InlineData("a{b}c{>d}e")]
	public void ThePieces_CoverTheValue_EachAsWritten(string value) {
		var pieces = WordsTemplate.Parse(value);
		Assert.Equal(value, string.Concat(pieces.Select(piece => piece.In(value))));
		Assert.Equal(pieces.Skip(1).Select(piece => piece.Start), pieces.SkipLast(1).Select(piece => piece.Start + piece.Length));
	}

	//string.Format's own reading of a numbered template: it throws exactly where a piece
	//is malformed. A } closing nothing is the one difference, one alone throwing there
	//and }} being one, and none here is alone
	[Theory]
	[InlineData("{0}")]
	[InlineData("{0,5}")]
	[InlineData("{0,-5}")]
	[InlineData("{0 ,5}")]
	[InlineData("{0, 5}")]
	[InlineData("{0,5 }")]
	[InlineData("{0 , -5 :X}")]
	[InlineData("{0:N2}")]
	[InlineData("{0:}")]
	[InlineData("{01}")]
	[InlineData("{0:a\nb}")]
	[InlineData("x{1}y{2}z")]
	[InlineData("{{0}}")]
	[InlineData("{0:x}}}")]
	[InlineData("{0,- 5}")]
	[InlineData("{ 0}")]
	[InlineData("{0x}")]
	[InlineData("{0:{x}")]
	[InlineData("{0,}")]
	[InlineData("{0,x}")]
	[InlineData("{}")]
	[InlineData("{0")]
	[InlineData("{0:N2")]
	[InlineData("{0\t}")]
	[InlineData("{0 :d}")]
	//its bounds: seven digits, after any zeros, for an alignment and an index alike
	[InlineData("{0,1000000}")]
	[InlineData("{0,9999999}")]
	[InlineData("{0,-9999999}")]
	[InlineData("{0,10000000}")]
	[InlineData("{0,-10000000}")]
	[InlineData("{0,00000000009}")]
	[InlineData("{0,000000010000000}")]
	[InlineData("{000000000000}")]
	[InlineData("{10000000}")]
	public void ANumberedTemplate_IsMalformed_WhereStringFormatThrows(string template) {
		bool throws;
		try {
			_ = string.Format(CultureInfo.InvariantCulture, template, 10, 20, 30);
			throws = false;
		}
		catch (FormatException) {
			throws = true;
		}
		Assert.Equal(throws, WordsTemplate.ParseFormat(template).Any(piece => piece is Malformed));
	}
}
