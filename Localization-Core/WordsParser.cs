using PatTech.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PatTech.Localization {
	/// <summary>
	/// Identifies a single field in a <c>words.ini</c> file: which block it belongs to,
	/// which field it is (<c>value</c>, <c>comment</c>, <c>context</c>, <c>stale</c>, ...)
	/// and which language variant, e.g. the <c>value-en-GB</c> line of <c>[group.key]</c>.
	/// </summary>
	public readonly struct FieldKey : IEquatable<FieldKey> {
		/// <summary>
		/// Converts a (BlockKey, FieldType, LanguageCode) tuple to a <see cref="FieldKey"/>.
		/// </summary>
		public static implicit operator FieldKey(in (string BlockKey, string FieldType, string LanguageCode) tuple) {
			return new FieldKey(tuple.BlockKey, tuple.FieldType, tuple.LanguageCode);
		}

		/// <summary>
		/// Compares all three components for equality.
		/// </summary>
		public static bool operator ==(in FieldKey lhs, in FieldKey rhs) => lhs.Equals(in rhs);
		/// <summary>
		/// Compares all three components for inequality.
		/// </summary>
		public static bool operator !=(in FieldKey lhs, in FieldKey rhs) => !(lhs == rhs);

		/// <summary>
		/// The fully resolved block key, e.g. <c>"group.key"</c> — dot-relative block
		/// headers such as <c>[.sub]</c> have already been expanded by the parser.
		/// </summary>
		public readonly string BlockKey;
		/// <summary>
		/// The field name without its language suffix: <c>"value"</c>, <c>"comment"</c>,
		/// <c>"context"</c>, <c>"stale"</c>, and so on. A plural form travels with it,
		/// lowercased: <c>value-ru#few</c> is the field type <c>"value#few"</c> in <c>"ru"</c>.
		/// </summary>
		public readonly string FieldType;
		/// <summary>
		/// The normalized language suffix, e.g. <c>"en"</c> or <c>"zh-Hant-TW"</c>;
		/// the empty string means the language-less default. A key's <c>param</c>
		/// field names a parameter here instead, as written.
		/// </summary>
		public readonly string LanguageCode;

		/// <summary>
		/// Creates a key from its three components; none may be <see langword="null"/>.
		/// </summary>
		/// <exception cref="ArgumentNullException">Any component is <see langword="null"/>.</exception>
		public FieldKey(
				string blockKey,
				string fieldType,
				string languageCode) {
			ArgumentNullException.ThrowIfNull(blockKey);
			ArgumentNullException.ThrowIfNull(fieldType);
			ArgumentNullException.ThrowIfNull(languageCode);

			BlockKey = blockKey;
			FieldType = fieldType;
			LanguageCode = languageCode;
		}

		/// <summary>
		/// Splits the key back into its three components.
		/// </summary>
		public void Deconstruct(
				out string blockKey,
				out string fieldType,
				out string languageCode) {
			Debug.Assert(BlockKey != null);
			Debug.Assert(FieldType != null);
			Debug.Assert(LanguageCode != null);

			blockKey = BlockKey;
			fieldType = FieldType;
			languageCode = LanguageCode;
		}

		/// <summary>
		/// Equal when <paramref name="obj"/> is a <see cref="FieldKey"/> with the
		/// same three components.
		/// </summary>
		public override bool Equals(object? obj) => obj is FieldKey other && Equals(in other);
		/// <summary>
		/// Combines the hash codes of all three components.
		/// </summary>
		public override int GetHashCode() => HashCode.Combine(BlockKey, FieldType, LanguageCode);

		/// <summary>
		/// Equal when all three components match exactly (ordinal, case-sensitive).
		/// </summary>
		public bool Equals(in FieldKey other) {
			return BlockKey == other.BlockKey
				&& FieldType == other.FieldType
				&& LanguageCode == other.LanguageCode;
		}

		bool IEquatable<FieldKey>.Equals(FieldKey other) => Equals(in other);
	}
	/// <summary>
	/// Receives parse events from <see cref="WordsParser"/> as it walks a
	/// <c>words.ini</c> file, in document order.
	/// </summary>
	public interface IWordsParserConsumer {
		/// <summary>
		/// A <c>[block]</c> header was read.
		/// </summary>
		/// <param name="baseKey">The block key that dot-relative headers (<c>[.sub]</c>) resolve against.</param>
		/// <param name="key">The header text as written, which may start with a dot, or be
		/// empty: <c>[]</c> names no key, and the parser reads past every field under it, as
		/// they would otherwise read as the file's top-of-file fields.</param>
		void VisitBlock(string baseKey, string key);
		/// <summary>
		/// A <c>field=text</c> line was read; escapes are already collapsed and any
		/// trailing continuation marker (<c>\</c> or <c>_</c>) stripped.
		/// </summary>
		/// <param name="key">Which block, field and language the text belongs to.</param>
		/// <param name="text">The first segment of the field's text.</param>
		void VisitFieldDeclaration(FieldKey key, string text);
		/// <summary>
		/// A continuation line for the most recent declaration was read; append
		/// <paramref name="value"/> to the text accumulated so far. A preceding line
		/// that ended with <c>\</c> has already contributed its newline.
		/// </summary>
		/// <param name="key">The same key passed to the originating <see cref="VisitFieldDeclaration(FieldKey, string)"/>.</param>
		/// <param name="value">The next segment of the field's text.</param>
		void VisitFieldContinuation(FieldKey key, string value);
		/// <summary>
		/// A comment line was read: <paramref name="text"/> is everything after the
		/// first <c>;</c>, with any whitespace before it dropped. Runs of consecutive
		/// comment lines arrive as consecutive calls. Ignored unless overridden.
		/// </summary>
		/// <param name="text">The comment text, without the leading <c>;</c>.</param>
		void VisitComment(string text) { }
		/// <summary>
		/// The parser is about to read line <paramref name="number"/>: every visit until
		/// the next call comes from that line. For tools that edit a file in place.
		/// Ignored unless overridden.
		/// </summary>
		/// <param name="number">The line's number, from 1 at the start of the reader <see cref="WordsParser.Load(TextReader)"/> was given.</param>
		void VisitLine(int number) { }
		/// <summary>
		/// A field whose language is no language code (<see cref="Localization.LanguageCode"/>),
		/// <c>value-english=…</c>, was read past, its continuation lines with it.
		/// Ignored unless overridden.
		/// </summary>
		/// <param name="blockKey">The block it is in, resolved as for <see cref="FieldKey.BlockKey"/>.</param>
		/// <param name="name">The field's name as written, e.g. <c>value-english</c>.</param>
		void VisitBadLanguage(string blockKey, string name) { }
	}

	/// <summary>
	/// A line-based parser for the <c>words.ini</c> format: <c>[block]</c> headers
	/// (including dot-relative <c>[.sub]</c> inheritance), <c>field-lang#form=text</c> pairs
	/// with <c>=</c> or <c>:</c> (the language and the plural form both optional),
	/// line continuations via trailing <c>\</c> (keep newline)
	/// or <c>_</c> (same line), and comment lines starting with <c>;</c> — reported via
	/// <see cref="IWordsParserConsumer.VisitComment(string)"/> so authoring tools can
	/// round-trip them. Blank lines are skipped. It holds no state of its own beyond the
	/// current position; results go to the <see cref="IWordsParserConsumer"/> it was built with.
	/// </summary>
	public class WordsParser {
		private static readonly Regex rxKeySegment = new(@"^\w[\w-]*\z", RegexOptions.Compiled);
		private static readonly Regex rxKeyName = new(@"^(\$\w[\w-]*|\w[\w-]*(\.\w[\w-]*)*)\z", RegexOptions.Compiled);

		/// <summary>
		/// Whether <paramref name="key"/> is a key's name (runtime SPEC: Key names): segments
		/// of letters, digits, <c>_</c> and <c>-</c>, each starting with one of the first
		/// three, joined by dots (<c>menu.file-open</c>); or a constant, <c>$</c> and one
		/// segment, which has no children. A block named otherwise — a space, <c>#</c>,
		/// <c>=</c>, an empty segment — is skipped with a warning.
		/// </summary>
		/// <param name="key">A full key, as a <c>[.child]</c> header resolves to.</param>
		public static bool IsKeyName(string key) => key is not null && rxKeyName.IsMatch(key);

		/// <summary>Whether <paramref name="segment"/> is one dotted segment of a key's name (<see cref="IsKeyName"/>).</summary>
		public static bool IsKeySegment(string segment) => segment is not null && rxKeySegment.IsMatch(segment);

		/// <summary>
		/// Whether the default, written in <paramref name="defaultLanguage"/>, speaks for
		/// <paramref name="languageCode"/>: the default's code is this one, or one it falls
		/// back to (<see cref="LanguageCode.Chain"/>). An <c>en</c> default speaks for
		/// <c>en-AU</c>, a <c>zh-Hant</c> one for <c>zh-Hant-TW</c>; an <c>en-AU</c> default
		/// speaks for neither <c>en-US</c> nor <c>en</c>. Where the default speaks, falling
		/// back to it is no missing word.
		/// </summary>
		/// <param name="defaultLanguage">The language a top-of-file <c>value=!xx</c> declares, or <see langword="null"/> when none is declared.</param>
		/// <param name="languageCode">The language asked about.</param>
		public static bool DefaultSpeaks(string? defaultLanguage, string languageCode) {
			if (string.IsNullOrEmpty(defaultLanguage) || string.IsNullOrEmpty(languageCode)) {
				return false;
			}
			if (LanguageCode.TryParse(defaultLanguage, out var spoken) && LanguageCode.TryParse(languageCode, out var asked)) {
				return asked.StartsWith(spoken);
			}
			return string.Equals(languageCode, defaultLanguage, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Normalizes a language code to canonical casing, each subtag by its kind
		/// (<see cref="LanguageCode"/>): <c>"EN-gb"</c> becomes <c>"en-GB"</c>,
		/// <c>"sr-latn-rs"</c> becomes <c>"sr-Latn-RS"</c>. The empty string (the
		/// language-less default) passes through unchanged.
		/// </summary>
		/// <param name="languageIdentifier">A language code, e.g. <c>"en"</c>, <c>"en-GB"</c> or <c>"zh-Hans-CN"</c>.</param>
		/// <exception cref="ArgumentException"><paramref name="languageIdentifier"/> is not of the form <c>language(-Script)?(-REGION)?</c>.</exception>
		public static string NormalizeLanguageCasing(string languageIdentifier) {
			ArgumentNullException.ThrowIfNull(languageIdentifier);
			if (languageIdentifier == "") {
				return languageIdentifier;
			}
			if (!LanguageCode.TryParse(languageIdentifier, out var code)) {
				throw new ArgumentException($"'{languageIdentifier}' is no language code: language(-Script)?(-REGION)?, as en, ceb, es-419, zh-Hans-CN", nameof(languageIdentifier));
			}
			return code.ToString();
		}

		private readonly IWordsParserConsumer consumer;

		/// <summary>
		/// Whether a field's suffix is a language, as in a <c>words.ini</c>, read as a
		/// <see cref="LanguageCode"/> and normalized; the default. An ini that borrows the
		/// format for suffixes of its own (a settings file's <c>scheme-decode=</c>) turns it
		/// off, and gets each as written.
		/// </summary>
		public bool LanguageSuffixes { get; init; } = true;

		/// <summary>
		/// Creates a parser that reports everything it reads to <paramref name="consumer"/>.
		/// </summary>
		/// <param name="consumer">The visitor that collects the parsed fields.</param>
		/// <exception cref="ArgumentNullException"><paramref name="consumer"/> is <see langword="null"/>.</exception>
		public WordsParser(IWordsParserConsumer consumer) {
			ArgumentNullException.ThrowIfNull(consumer);

			this.consumer = consumer;
		}

		static readonly Regex rxBlock = new(
			@"^\[(?<1>[^]]*)\]",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxPair = new(
			@"^(?<key>\w+)(-(?<lang>\w+(?:-\w+)*))?(?<form>#\w+)?\s*[:=]\s*(?<text>.*)",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxIsContinuedLine = new(
			@"^([\\_].|[^\\_])*[\\_]$",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxComment = new(
			@"^\s*;(?<text>.*)",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxBlankLine = new(
			@"^\s*$",
			RegexOptions.Compiled);
		static readonly Regex rxUnescape = new(
			@"([\\_'])\1",
			RegexOptions.Compiled);

		/// <summary>
		/// Reads <paramref name="reader"/> to the end, emitting a visit to the consumer
		/// for each block header, field declaration and continuation line found.
		/// Unrecognized lines are silently skipped; a field whose language is no
		/// <see cref="LanguageCode"/> is read past with a
		/// <see cref="IWordsParserConsumer.VisitBadLanguage(string, string)"/>. May be
		/// called repeatedly to concatenate sources.
		/// </summary>
		/// <param name="reader">The <c>words.ini</c> text to parse.</param>
		/// <returns>This parser, for chaining.</returns>
		public WordsParser Load(TextReader reader) {
			ArgumentNullException.ThrowIfNull(reader);

			string baseBlockKey = "";
			string currentBlockKey = "";
			FieldKey? target = null;
			//a field read past runs on through its continuation lines
			bool readingPast = false;
			//under [], which names no key, every field is read past
			bool emptyBlock = false;
			int number = 0;
			while (reader.ReadLine() is string line) {
				consumer.VisitLine(++number);
				if (readingPast) {
					readingPast = rxIsContinuedLine.IsMatch(line);
					continue;
				}
				if (TryReadLine(ref target, line, first: false)) {
					continue;
				}
				else if (rxComment.TryMatch(line, out var comment)) {
					consumer.VisitComment(comment.Groups["text"].Value);
				}
				else if (rxBlankLine.IsMatch(line)) {
					continue;
				}
				else if (rxBlock.TryMatch(line, out var block)) {
					// open a new block.
					string name = block.Groups[1].Value;
					emptyBlock = name == "";
					if (emptyBlock) {
						//no base for a [.child] either, which resolves to no key
						currentBlockKey = baseBlockKey = "";
					}
					else if (name[0] == '.') {
						currentBlockKey = baseBlockKey + name;
					}
					else {
						currentBlockKey = baseBlockKey = name;
					}
					consumer.VisitBlock(baseBlockKey, name);
				}
				else if (rxPair.TryMatch(line, out var pair)) {
					string field = pair.Groups["key"].Value;
					string lang = pair.Groups["lang"].Value;
					var text = pair.Groups["text"].Value;
					if (emptyBlock) {
						readingPast = rxIsContinuedLine.IsMatch(text);
						continue;
					}
					//a key's param- names a parameter; everywhere else the suffix is a language
					if (lang != "" && LanguageSuffixes && !(field == "param" && currentBlockKey != "")) {
						if (!LanguageCode.TryParse(lang, out var code)) {
							consumer.VisitBadLanguage(currentBlockKey, $"{field}-{lang}{pair.Groups["form"].Value}");
							readingPast = rxIsContinuedLine.IsMatch(text);
							continue;
						}
						lang = code.ToString();
					}
					var fieldKey = field + pair.Groups["form"].Value.ToLowerInvariant();
					target = new FieldKey(currentBlockKey, fieldKey, lang);
					var lineRead = TryReadLine(ref target, text, first: true);
					if (!lineRead) {
						throw new UnreachableException();
					}
				}
			}
			return this;
		}

		private bool TryReadLine(ref FieldKey? target, string text, bool first) {
			if (target is null) {
				if (first) {
					throw new InvalidOperationException("field declaration must have a target");
				}
				return false;
			}

			var isContinued = rxIsContinuedLine.IsMatch(text);
			if (isContinued) {
				var newlineKept = text.EndsWith('\\');
				text = text[..^1];
				if (newlineKept) {
					text += '\n';
				}
			}

			text = rxUnescape.Replace(text, "$1");

			if (first) {
				consumer.VisitFieldDeclaration(target.Value, text);
			}
			else {
				consumer.VisitFieldContinuation(target.Value, text);
			}
			if (!isContinued) {
				target = null;
			}
			return true;
		}
	}
}
