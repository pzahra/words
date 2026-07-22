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
		/// The field name before any language suffix: <c>"value"</c>, <c>"comment"</c>,
		/// <c>"context"</c>, <c>"stale"</c>, and so on.
		/// </summary>
		public readonly string FieldType;
		/// <summary>
		/// The normalized language suffix, e.g. <c>"en"</c> or <c>"en-GB"</c>;
		/// the empty string means the language-less default.
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
		/// <param name="key">The header text as written, which may start with a dot.</param>
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
	}

	/// <summary>
	/// A line-based parser for the <c>words.ini</c> format: <c>[block]</c> headers
	/// (including dot-relative <c>[.sub]</c> inheritance), <c>field-lang=text</c> pairs
	/// with <c>=</c> or <c>:</c>, line continuations via trailing <c>\</c> (keep newline)
	/// or <c>_</c> (same line), and comment lines starting with <c>;</c>. Blank lines
	/// are skipped. It holds no state of its own beyond the current position; results go
	/// to the <see cref="IWordsParserConsumer"/> it was built with.
	/// </summary>
	public class WordsParser {
		private static readonly Regex rxLanguageName = new(
			@"^(?<lang>\w+)(-(?<region>\w+))?$",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture
		);

		/// <summary>
		/// Normalizes a language identifier to canonical casing: language lowercase,
		/// region uppercase, e.g. <c>"EN-gb"</c> becomes <c>"en-GB"</c>. The empty
		/// string (the language-less default) passes through unchanged.
		/// </summary>
		/// <param name="languageIdentifier">A language code, optionally with a region, e.g. <c>"en"</c> or <c>"en-GB"</c>.</param>
		/// <exception cref="ArgumentException"><paramref name="languageIdentifier"/> is not of the form <c>lang</c> or <c>lang-REGION</c>.</exception>
		public static string NormalizeLanguageCasing(string languageIdentifier) {
			// NOTE: I thought about enforcing casing for this, but it would have been
			// likely a performance hit to keep track of mismatches in exchange for a
			// harder time for the user.

			ArgumentNullException.ThrowIfNull(languageIdentifier);
			if (languageIdentifier == "") {
				return languageIdentifier;
			}
			if (!rxLanguageName.TryMatch(languageIdentifier, out var match)) {
				throw new ArgumentException("not a valid language");
			}

			var language = match.Groups["lang"].Value;
			var region = match.Groups["region"].Value;

			if (region.Length > 0) {
				return $"{language.ToLowerInvariant()}-{region.ToUpperInvariant()}";
			}
			else {
				return $"{language.ToLowerInvariant()}";
			}
		}

		private readonly IWordsParserConsumer consumer;

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
			@"^\[(?<1>[^]]+)\]",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxPair = new(
			@"^(?<key>\w+)(-(?<lang>\w+(?:-\w+)?))?\s*[:=]\s*(?<text>.*)",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxIsContinuedLine = new(
			@"^([\\_].|[^\\_])*[\\_]$",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxSkippableLine = new(
			@"^\s*(;|$)",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxUnescape = new(
			@"([\\_'])\1",
			RegexOptions.Compiled);

		/// <summary>
		/// Reads <paramref name="reader"/> to the end, emitting a visit to the consumer
		/// for each block header, field declaration and continuation line found.
		/// Unrecognized lines are silently skipped. May be called repeatedly to
		/// concatenate sources.
		/// </summary>
		/// <param name="reader">The <c>words.ini</c> text to parse.</param>
		/// <returns>This parser, for chaining.</returns>
		public WordsParser Load(TextReader reader) {
			ArgumentNullException.ThrowIfNull(reader);

			string baseBlockKey = "";
			string currentBlockKey = "";
			FieldKey? target = null;
			while (reader.ReadLine() is string line) {
				if (TryReadLine(ref target, line, first: false)) {
					continue;
				}
				else if (rxSkippableLine.IsMatch(line)) {
					
					continue;
				}
				else if (rxBlock.TryMatch(line, out var block)) {
					// open a new block.
					string name = block.Groups[1].Value;
					if (name[0] == '.') {
						currentBlockKey = baseBlockKey + name;
					}
					else {
						currentBlockKey = baseBlockKey = name;
					}
					consumer.VisitBlock(baseBlockKey, name);
				}
				else if (rxPair.TryMatch(line, out var pair)) {
					string lang = NormalizeLanguageCasing(pair.Groups["lang"].Value);
					var text = pair.Groups["text"].Value;
					var fieldKey = pair.Groups["key"].Value;
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
