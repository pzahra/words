using System.Collections.Generic;
using System.Diagnostics;

namespace PatTech.Localization {
	/// <summary>
	/// The standard <see cref="IWordsParserConsumer"/>: collects <c>value</c> fields
	/// into one dictionary per language, ready for <see cref="WordsBuilder"/> to
	/// flatten. <c>comment</c>, <c>context</c> and <c>param</c> fields are ignored;
	/// <c>stale</c> fields and unknown field types are reported to the logger.
	/// </summary>
	/// <param name="logger">Receives warnings about overwritten keys, stale values and unknown fields; <see langword="null"/> discards them.</param>
	public class WordsParserToWordsProvider(ITakeException? logger = null) : IWordsParserConsumer {
		private readonly ITakeException logger = logger ?? ITakeException.Dummy;

		/// <summary>
		/// The words collected so far, one dictionary per language code. The empty
		/// string keys the language-less default.
		/// </summary>
		public IReadOnlyDictionary<string, DictionaryWordsProvider> Languages => languages;
		/// <summary>
		/// The language codes in the order they were first encountered, which drives
		/// the ordering of <see cref="WordsBuilder.GetLanguages"/>.
		/// </summary>
		public IReadOnlyList<string> LanguageCodes => languageCodes;

		private readonly Dictionary<string, DictionaryWordsProvider> languages = [];
		private readonly List<string> languageCodes = [];

		/// <summary>
		/// Stores a <c>value</c> field in its language's dictionary, overwriting (and
		/// warning about) any earlier value for the same key — that is what makes later
		/// <see cref="WordsBuilder.Load(string)"/> calls win. Other field types are
		/// metadata: ignored, or logged in the case of <c>stale</c> and unknowns.
		/// </summary>
		public void VisitFieldDeclaration(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			switch (fieldType) {
				case "value": {
					if (!languageCodes.Contains(languageCode)) {
						languageCodes.Add(languageCode);
					}

					if (!languages.TryGetValue(languageCode, out var language)) {
						language = [];
						languages.Add(languageCode, language);
					}

					if (!language.TryAdd(blockKey, value)) {
						logger.Warn(string.Format(
							"WB:KOVR:`{0}`-`{1}` = {2}",
							blockKey,
							languageCode,
							value));
						language[blockKey] = value;
					}
					break;
				}
				case "comment":
				case "context":
				case "param":
					// safely ignored
					break;
				case "stale":
					logger.Warn(string.Format(
						"WP:STALE:`{0}.{1}-{2}`",
						blockKey,
						fieldType,
						languageCode));
					break;
				default:
					logger.Warn(string.Format(
						"WP:WHO:`{0}.{1}-{2}`",
						blockKey,
						fieldType,
						languageCode));
					break;
			}
		}
		/// <summary>
		/// Appends <paramref name="value"/> to the <c>value</c> field declared just
		/// before it; continuations of any other field type are discarded.
		/// </summary>
		public void VisitFieldContinuation(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			if (fieldType is "value") {
				Debug.Assert(languageCodes.Contains(languageCode));

				var language = languages[languageCode];
				language[blockKey] += value;
			}
		}

		void IWordsParserConsumer.VisitBlock(string baseKey, string name) { }
	}
}
