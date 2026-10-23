using System.Text;
using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>
///     Encodings (SPEC: A transaction): a byte-order mark names the encoding and is kept; without
///     one a file reads as UTF-8, or as its rules say; a mark the rules refuse, a byte its
///     encoding does not read, or a NUL is no text to edit, though it can be replaced whole.
/// </summary>
public class EncodingTests {
	private static IniDocument Read(byte[] bytes, IniRules? rules = null) => IniDocument.Read(bytes, IniDialect.Sections(rules ?? new IniRules()));

	[Fact]
	public void AFileWithNoMark_ReadsAsUtf8_AndWritesBackSo() {
		IniDocument document = Read("[s]\nx=é\n"u8.ToArray());
		Assert.Equal("é", document.Get("s", "x"));
		Assert.False(document.ByteOrderMark);
		document.Set("s", "x", "ü");
		Assert.Equal("[s]\nx=ü\n"u8.ToArray(), document.ToBytes());
	}

	[Fact]
	public void AFileWithNoMark_ReadsInTheFirstOfItsRulesEncodingsThatReadsIt() {
		byte[] latin = [.. "[s]\nx="u8, 0xE9, (byte)'\n'];
		Assert.False(Read(latin).IsText);
		var rules = new IniRules { Encodings = [new UTF8Encoding(false), Encoding.Latin1] };
		IniDocument document = Read(latin, rules);
		Assert.Equal("é", document.Get("s", "x"));
		Assert.Equal(Encoding.Latin1.WebName, document.Encoding!.WebName);
		//UTF-8 first: what reads as UTF-8 is
		Assert.Equal(new UTF8Encoding(false).WebName, Read("[s]\nx=é\n"u8.ToArray(), rules).Encoding!.WebName);
	}

	[Fact]
	public void AMark_NamesTheEncoding_AndIsKept() {
		byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("[s]\r\nx=1\r\n")];
		IniDocument document = Read(utf16);
		Assert.True(document.ByteOrderMark);
		Assert.Equal("1", document.Get("s", "x"));
		document.Set("s", "x", "2");
		Assert.Equal((byte[])[.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("[s]\r\nx=2\r\n")], document.ToBytes());

		byte[] utf8 = [0xEF, 0xBB, 0xBF, .. "[s]\nx=1\n"u8];
		IniDocument marked = Read(utf8);
		marked.Set("s", "x", "2");
		Assert.Equal((byte[])[0xEF, 0xBB, 0xBF, .. "[s]\nx=2\n"u8], marked.ToBytes());
	}

	[Fact]
	public void AMarkTheRulesRefuse_IsNoText_RefusedForEdits_AndReplaceableWhole() {
		byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("[s]\r\nx=1\r\n")];
		IniDocument document = Read(utf16, new IniRules { ByteOrderMarks = IniByteOrderMarks.Refused });
		Assert.False(document.IsText);
		Assert.Equal(IniEncodingFailure.MarkRefused, Assert.Throws<IniEncodingException>(() => document.Get("s", "x")).Failure);
		Assert.Equal(IniEncodingFailure.MarkRefused, Assert.Throws<IniEncodingException>(() => document.Set("s", "x", "2")).Failure);
		Assert.Equal(utf16, document.ToBytes());
		document.Replace("[s]\r\nx=2\r\n", new UTF8Encoding(false));
		Assert.True(document.IsText);
		Assert.Equal("[s]\r\nx=2\r\n"u8.ToArray(), document.ToBytes());
	}

	[Fact]
	public void AMarkDropped_IsReadPastAndNeverWritten_WhereItNamesAnEncodingTheRulesList() {
		var rules = new IniRules { ByteOrderMarks = IniByteOrderMarks.Dropped, Encodings = [new UTF8Encoding(false), Encoding.Latin1] };
		IniDocument document = Read([0xEF, 0xBB, 0xBF, .. "[s]\nx=1\n"u8], rules);
		Assert.False(document.ByteOrderMark);
		document.Set("s", "x", "2");
		Assert.Equal("[s]\nx=2\n"u8.ToArray(), document.ToBytes());

		byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("[s]\r\nx=1\r\n")];
		Assert.Equal(IniEncodingFailure.MarkRefused, Assert.Throws<IniEncodingException>(() => Read(utf16, rules).Get("s", "x")).Failure);
	}

	[Fact]
	public void AByteItsEncodingDoesNotRead_OrANul_IsNoText() {
		IniDocument bad = Read([.. "[s]\nx="u8, 0xFF, (byte)'\n']);
		Assert.Equal(IniEncodingFailure.NotText, Assert.Throws<IniEncodingException>(() => bad.Sections).Failure);
		//UTF-16 with no mark reads as UTF-8 full of NULs
		Assert.False(Read(Encoding.Unicode.GetBytes("[s]\r\nx=1\r\n")).IsText);
	}

	[Fact]
	public void TextItsEncodingCannotWrite_IsRefusedWhenItsBytesAreMade() {
		IniDocument document = Read("[s]\nx=1\n"u8.ToArray(), new IniRules { Encodings = [Encoding.Latin1] });
		document.Set("s", "x", "€");
		IniEncodingException refused = Assert.Throws<IniEncodingException>(document.ToBytes);
		Assert.Equal(IniEncodingFailure.Unencodable, refused.Failure);
	}

	[Fact]
	public void ADocumentNothingChanged_IsItsBytesAsRead() {
		byte[] bytes = [.. "[s]\r\nx=1\n"u8];
		Assert.Equal(bytes, Read(bytes).ToBytes());
		Assert.False(Read(bytes).IsEdited);
	}
}
