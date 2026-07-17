using System.Collections.Generic;
using System.Diagnostics;

namespace PatTech.Localization {
	public class WordsParserToWordsProvider(ITakeException? logger = null) : IWordsParserConsumer {
		private readonly ITakeException logger = logger ?? ITakeException.Dummy;

		public IReadOnlyDictionary<string, DictionaryWordsProvider> Languages => languages;
		public IReadOnlyList<string> LanguageCodes => languageCodes;

		private readonly Dictionary<string, DictionaryWordsProvider> languages = [];
		private readonly List<string> languageCodes = [];

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
				Debug.Assert(languageCodes.Contains(languageCode));

				var language = languages[languageCode];
				language[blockKey] += value;
			}
		}

		void IWordsParserConsumer.VisitBlock(string baseKey, string name) { }
	}
}
