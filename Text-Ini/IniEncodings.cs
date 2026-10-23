using System.Text;

namespace PatTech.Text.Ini;

//a file's bytes into text, strictly, and back (SPEC.md, A transaction: the file is decoded strictly)
internal static class IniEncodings {
	//each byte-order mark and the encoding it names, UTF-32 LE ahead of UTF-16 LE, whose mark it starts with
	private static readonly (byte[] Mark, Func<Encoding> Encoding)[] Marks = [
		([0xFF, 0xFE, 0x00, 0x00], () => new UTF32Encoding(bigEndian: false, byteOrderMark: false)),
		([0x00, 0x00, 0xFE, 0xFF], () => new UTF32Encoding(bigEndian: true, byteOrderMark: false)),
		([0xEF, 0xBB, 0xBF], () => new UTF8Encoding(false)),
		([0xFF, 0xFE], () => new UnicodeEncoding(bigEndian: false, byteOrderMark: false)),
		([0xFE, 0xFF], () => new UnicodeEncoding(bigEndian: true, byteOrderMark: false)),
	];

	//the text, the encoding it read in and the mark it carried; or why it is no text
	internal static (string? Text, Encoding? Encoding, byte[] Mark, IniEncodingFailure? Failure) Decode(byte[] bytes, IniRules rules) {
		foreach ((byte[] mark, Func<Encoding> named) in Marks) {
			if (bytes.AsSpan().StartsWith(mark)) {
				Encoding encoding = named();
				if (rules.ByteOrderMarks == IniByteOrderMarks.Refused
					|| (rules.ByteOrderMarks == IniByteOrderMarks.Dropped && !rules.Encodings.Any(listed => listed.CodePage == encoding.CodePage))) {
					return (null, encoding, mark, IniEncodingFailure.MarkRefused);
				}
				string? text = TryDecode(bytes, mark.Length, encoding);
				byte[] kept = rules.ByteOrderMarks == IniByteOrderMarks.Kept ? mark : [];
				return text is null ? (null, encoding, mark, IniEncodingFailure.NotText) : (text, encoding, kept, null);
			}
		}
		foreach (Encoding encoding in rules.Encodings) {
			string? text = TryDecode(bytes, 0, encoding);
			if (text is not null) {
				return (text, encoding, [], null);
			}
		}
		return (null, null, [], IniEncodingFailure.NotText);
	}

	//null for bytes the encoding does not read, or a NUL, which no text file holds
	private static string? TryDecode(byte[] bytes, int skip, Encoding encoding) {
		try {
			string text = Strict(encoding).GetString(bytes, skip, bytes.Length - skip);
			return text.Contains('\0') ? null : text;
		}
		catch (DecoderFallbackException) {
			return null;
		}
	}

	internal static byte[] Encode(string text, Encoding encoding, byte[] mark, string? path) {
		try {
			byte[] body = Strict(encoding).GetBytes(text);
			return [.. mark, .. body];
		}
		catch (EncoderFallbackException e) {
			throw new IniEncodingException(IniEncodingFailure.Unencodable, encoding.WebName, path, e);
		}
	}

	//the mark that names encoding, for a document replaced whole with one
	internal static byte[] MarkOf(Encoding encoding) {
		byte[] preamble = encoding.GetPreamble();
		if (preamble.Length > 0) {
			return preamble;
		}
		return encoding switch {
			UTF8Encoding => [0xEF, 0xBB, 0xBF],
			UTF32Encoding => Strict(encoding).GetBytes("﻿"),
			UnicodeEncoding => Strict(encoding).GetBytes("﻿"),
			_ => [],
		};
	}

	private static Encoding Strict(Encoding encoding) {
		var strict = (Encoding)encoding.Clone();
		strict.DecoderFallback = DecoderFallback.ExceptionFallback;
		strict.EncoderFallback = EncoderFallback.ExceptionFallback;
		return strict;
	}
}
