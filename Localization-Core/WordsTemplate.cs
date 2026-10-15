using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PatTech.Localization {
	/// <summary>
	/// A piece of a value, as <see cref="WordsTemplate"/> reads it (SPEC: One pass for
	/// every brace): text, a reference, a constant, a selector, a format item, or a brace
	/// that opens none of them. Each knows its place in the value it was read from, and
	/// the pieces of a value cover it end to end, so <see cref="In(string)"/> gives each
	/// back as written.
	/// </summary>
	/// <param name="Start">Where the piece starts in the value.</param>
	/// <param name="Length">How many of the value's characters it takes, its braces and escapes included.</param>
	public abstract record WordsPiece(int Start, int Length) {
		/// <summary>The piece as written in <paramref name="value"/>, the value it was read from.</summary>
		public string In(string value) => value.Substring(Start, Length);

		/// <summary>Text, shown as it stands.</summary>
		/// <param name="Start">Where the text starts in the value.</param>
		/// <param name="Length">How many of the value's characters it takes, each escape two.</param>
		/// <param name="Value">The text, each escaped brace, <c>{{</c>, one brace.</param>
		public sealed record Text(int Start, int Length, string Value) : WordsPiece(Start, Length);

		/// <summary>A reference, <c>{&gt;key}</c>: the key's words, rendered against it, in its place.</summary>
		/// <param name="Start">Where the reference starts in the value.</param>
		/// <param name="Length">How many of the value's characters it takes, its braces included.</param>
		/// <param name="Key">The key as written; <c>.sub</c> is relative to the key whose words hold it.</param>
		public sealed record Reference(int Start, int Length, string Key) : WordsPiece(Start, Length);

		/// <summary>A constant, <c>{$name}</c>: its value in its place, as text, never looked into.</summary>
		/// <param name="Start">Where the constant starts in the value.</param>
		/// <param name="Length">How many of the value's characters it takes, its braces included.</param>
		/// <param name="Key">The constant's key, <c>$name</c>.</param>
		public sealed record Constant(int Start, int Length, string Key) : WordsPiece(Start, Length);

		/// <summary>A selector, <c>{0#word}</c> or <c>{Count#word}</c>: the form of the key its argument's count picks.</summary>
		/// <param name="Start">Where the selector starts in the value.</param>
		/// <param name="Length">How many of the value's characters it takes, its braces included.</param>
		/// <param name="Argument">The argument counted: a number as string.Format reads it, <c>01</c> being <c>1</c>, or a name.</param>
		/// <param name="Key">The key whose forms it picks among, as written; <c>.sub</c> is relative to the key whose words hold it.</param>
		public sealed record Selector(int Start, int Length, string Argument, string Key) : WordsPiece(Start, Length);

		/// <summary>A format item, <c>{0}</c> or <c>{Name}</c>, with an alignment and a spec: <c>{0,-5:N2}</c>.</summary>
		/// <param name="Start">Where the item starts in the value.</param>
		/// <param name="Length">How many of the value's characters it takes, its braces included.</param>
		/// <param name="Argument">The argument printed: a number as string.Format reads it, <c>01</c> being <c>1</c>, or a name.</param>
		/// <param name="Alignment">The width it is padded to, on the left, or on the right when negative; <see langword="null"/> for none.</param>
		/// <param name="Format">The spec after the <c>:</c>, as the argument's <see cref="IFormattable"/> takes it; <see langword="null"/> for none, or an empty one.</param>
		public sealed record Item(int Start, int Length, string Argument, int? Alignment, string? Format) : WordsPiece(Start, Length);

		/// <summary>
		/// A brace that opens none of the others, through the brace that closes it:
		/// <c>{0:{&gt;fmt}}</c>, <c>{0,x}</c>, <c>{}</c>. A <c>{</c> that nothing closes is
		/// one on its own.
		/// </summary>
		/// <param name="Start">Where the brace is in the value.</param>
		/// <param name="Length">How many of the value's characters it takes, through the brace that closes it.</param>
		public sealed record Malformed(int Start, int Length) : WordsPiece(Start, Length);
	}

	/// <summary>
	/// Reads a value into its <see cref="WordsPiece"/>s, once, in the order it was written
	/// (SPEC: One pass for every brace). <c>{{</c> is one <c>{</c>, and a <c>}</c> that
	/// closes nothing is always itself, <c>}}</c> two of them. A format item's number,
	/// alignment and spec read as string.Format reads them; a name, <c>{Count}</c>, as
	/// <see cref="Words.FormatByName(IFormatProvider?, string, object?, object?[])"/>
	/// reads it, spaces around it and its spec trimmed, and it may take an alignment too.
	/// </summary>
	public static class WordsTemplate {
		private static readonly char[] Braces = ['{', '}'];
		//a number or an alignment from here on is none, about where string.Format stops
		private const int Limit = 1_000_000;

		/// <summary>The pieces of <paramref name="value"/>, a key's words as a file holds them.</summary>
		/// <param name="value">The words to read.</param>
		/// <returns>The pieces in order, covering the value end to end; none for an empty one.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
		public static IReadOnlyList<WordsPiece> Parse(string value) => Read(value, refers: true);

		/// <summary>
		/// The pieces of <paramref name="template"/>, a format string, which refers to
		/// nothing: words rendered already, or a template no dictionary holds. A
		/// <c>{&gt;key}</c> or a <c>{$name}</c> there is <see cref="WordsPiece.Malformed"/>.
		/// </summary>
		/// <param name="template">The format string to read.</param>
		/// <returns>The pieces in order, covering the template end to end; none for an empty one.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
		public static IReadOnlyList<WordsPiece> ParseFormat(string template) => Read(template, refers: false);

		private static WordsPiece[] Read(string text, bool refers) {
			ArgumentNullException.ThrowIfNull(text);
			List<WordsPiece> pieces = [];
			var run = new StringBuilder();
			int start = 0;
			for (int at = 0; at < text.Length;) {
				int brace = text.IndexOf('{', at);
				if (brace < 0) {
					run.Append(text, at, text.Length - at);
					break;
				}
				run.Append(text, at, brace - at);
				at = brace;
				if (at + 1 < text.Length && text[at + 1] == '{') {
					run.Append('{');
					at += 2;
					continue;
				}
				WordsPiece piece = Opened(text, at, refers);
				if (at > start) {
					pieces.Add(new WordsPiece.Text(start, at - start, run.ToString()));
					run.Clear();
				}
				pieces.Add(piece);
				at += piece.Length;
				start = at;
			}
			if (text.Length > start) {
				pieces.Add(new WordsPiece.Text(start, text.Length - start, run.ToString()));
			}
			return [.. pieces];
		}

		//the piece the { at at opens
		private static WordsPiece Opened(string text, int at, bool refers) {
			int i = at + 1;
			if (i < text.Length && text[i] is '>' or '$') {
				int close = text.IndexOfAny(Braces, i + 1);
				if (!refers || close <= i + 1 || text[close] != '}') {
					return Malformed(text, at);
				}
				string key = text[(i + 1)..close];
				return text[i] == '>'
					? new WordsPiece.Reference(at, close + 1 - at, key)
					: new WordsPiece.Constant(at, close + 1 - at, "$" + key);
			}

			//a number as string.Format reads it, spaces only after it; a name with any
			//space but a line break around it, as FormatByName reads one
			bool numbered = i < text.Length && char.IsAsciiDigit(text[i]);
			i = Skip(text, i, numbered);
			int argumentStart = i;
			if (numbered) {
				while (i < text.Length && char.IsAsciiDigit(text[i])) {
					++i;
				}
			}
			else if (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == '_')) {
				while (++i < text.Length && IsWordChar(text[i])) {
				}
			}
			else {
				return Malformed(text, at);
			}
			string argument = text[argumentStart..i];
			if (numbered) {
				if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= Limit) {
					return Malformed(text, at);
				}
				argument = index.ToString(CultureInfo.InvariantCulture);
			}

			//a selector: the argument right inside the brace, a # and a key
			if (i < text.Length && text[i] == '#') {
				int keyStart = ++i;
				while (i < text.Length && text[i] is not ('{' or '}' or '#') && !char.IsWhiteSpace(text[i])) {
					++i;
				}
				if (argumentStart != at + 1 || i == keyStart || i == text.Length || text[i] != '}') {
					return Malformed(text, at);
				}
				return new WordsPiece.Selector(at, i + 1 - at, argument, text[keyStart..i]);
			}

			i = Skip(text, i, numbered);
			int? alignment = null;
			if (i < text.Length && text[i] == ',') {
				i = Skip(text, i + 1, numbered);
				bool left = i < text.Length && text[i] == '-';
				if (left) {
					++i;
				}
				int digits = i;
				while (i < text.Length && char.IsAsciiDigit(text[i])) {
					++i;
				}
				if (i == digits || !int.TryParse(text.AsSpan(digits, i - digits), NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width >= Limit) {
					return Malformed(text, at);
				}
				alignment = left ? -width : width;
				i = Skip(text, i, numbered);
			}

			//a spec runs to the }, and holds no brace; a name's no line break either
			string? format = null;
			if (i < text.Length && text[i] == ':') {
				int specStart = ++i;
				while (i < text.Length && text[i] is not ('{' or '}') && (numbered || text[i] is not ('\r' or '\n'))) {
					++i;
				}
				format = numbered ? text[specStart..i] : text[specStart..i].Trim();
				if (format.Length == 0) {
					format = null;
				}
			}
			if (i == text.Length || text[i] != '}') {
				return Malformed(text, at);
			}
			return new WordsPiece.Item(at, i + 1 - at, argument, alignment, format);
		}

		//a brace that opens nothing, through the brace that closes it, or alone if none does
		private static WordsPiece.Malformed Malformed(string text, int at) {
			int depth = 0;
			for (int i = at; i < text.Length; ++i) {
				if (text[i] == '{') {
					++depth;
				}
				else if (text[i] == '}' && --depth == 0) {
					return new(at, i + 1 - at);
				}
			}
			return new(at, 1);
		}

		//past the spaces at i: string.Format's around a number, any but a line break around a name
		private static int Skip(string text, int i, bool numbered) {
			while (i < text.Length && (numbered ? text[i] == ' ' : char.IsWhiteSpace(text[i]) && text[i] is not ('\r' or '\n'))) {
				++i;
			}
			return i;
		}

		//a regular expression's \w: a letter, a digit, a joining mark or a connector
		private static bool IsWordChar(char c) => CharUnicodeInfo.GetUnicodeCategory(c) switch {
			UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
				or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
				or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation => true,
			_ => false,
		};
	}
}
