using System.Collections.Generic;
using System.Diagnostics;

namespace PatTech.Localization {
	public class WordsParserToWordsProvider(ITakeException? logger = null) : IWordsParserConsumer {
		private readonly ITakeException logger = logger ?? ITakeException.Dummy;

		public IReadOnlyDictionary<string, DictionaryWordsProvider> Languages => _languages;
		public IReadOnlyList<string> LanguageCodes => _languageCodes;

		private readonly Dictionary<string, DictionaryWordsProvider> _languages = [];
		private readonly List<string> _languageCodes = [];

		public void VisitFieldDeclaration(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			switch (fieldType) {
				case "value": {
					if (!_languageCodes.Contains(languageCode)) {
						_languageCodes.Add(languageCode);
					}

					if (!_languages.TryGetValue(languageCode, out var language)) {
						language = [];
						_languages.Add(languageCode, language);
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
						"WP:STALE:`{0}.{1}-{2}",
						blockKey,
						fieldType,
						languageCode));
					break;
				default:
					logger.Warn(string.Format(
						"WP:WHO:`{0}.{1}-{2}",
						blockKey,
						fieldType,
						languageCode));
					break;
			}
		}
		public void VisitFieldContinuation(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			if (fieldType is "value") {
				Debug.Assert(_languageCodes.Contains(languageCode));

				var language = _languages[languageCode];
				language[blockKey] += value;
			}
		}

		void IWordsParserConsumer.VisitBlock(string blockKey) { }
	}
}
