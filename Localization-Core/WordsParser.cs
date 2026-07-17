using PatTech.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PatTech.Localization {
	public readonly struct FieldKey : IEquatable<FieldKey> {
		public static implicit operator FieldKey(in (string BlockKey, string FieldType, string LanguageCode) tuple) {
			return new FieldKey(tuple.BlockKey, tuple.FieldType, tuple.LanguageCode);
		}

		public static bool operator ==(in FieldKey lhs, in FieldKey rhs) => lhs.Equals(in rhs);
		public static bool operator !=(in FieldKey lhs, in FieldKey rhs) => !(lhs == rhs);

		public readonly string BlockKey;
		public readonly string FieldType;
		public readonly string LanguageCode;

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

		public override bool Equals(object? obj) => obj is FieldKey other && Equals(in other);
		public override int GetHashCode() => HashCode.Combine(BlockKey, FieldType, LanguageCode);

		public bool Equals(in FieldKey other) {
			return BlockKey == other.BlockKey
				&& FieldType == other.FieldType
				&& LanguageCode == other.LanguageCode;
		}

		bool IEquatable<FieldKey>.Equals(FieldKey other) => Equals(in other);
	}
	public interface IWordsParserConsumer {
		void VisitBlock(string baseKey, string key);
		void VisitFieldDeclaration(FieldKey key, string text);
		void VisitFieldContinuation(FieldKey key, string value);
	}

	public class WordsParser {
		private static readonly Regex rxLanguageName = new(
			@"^(?<lang>\w+)(-(?<region>\w+))?$",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture
		);

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
			@"^\s*[;|$]",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		static readonly Regex rxUnescape = new(
			@"([\\_'])\1",
			RegexOptions.Compiled);

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
