using PatTech.Localization;
using WordsEdit.ViewModels;

namespace WordsEdit.Utils {
	public class WordsParserToLocalizationProvider : IWordsParserConsumer {
		public IReadOnlyList<string> Errors => errors;
		public IReadOnlyList<LocalizationKey> LocalizationKeys => localizationKeys;
		public IReadOnlyDictionary<string, LocalizationKey> LocalizationKeysDictionary => localizationKeysDictionary;
		public IReadOnlyDictionary<string, LocalizationLanguage> LocalizationLanguagesDictionary => localizationLanguagesDictionary;

		private readonly List<string> errors = [];
		private readonly Dictionary<string, LocalizationLanguage> localizationLanguagesDictionary = [];
		private readonly List<LocalizationKey> localizationKeys = [];
		private readonly Dictionary<string, LocalizationKey> localizationKeysDictionary = [];

		public WordsParserToLocalizationProvider() { }

		public void VisitFieldDeclaration(FieldKey key, string value) {
			value = FormatFromINI(value);
			var (blockKey, fieldType, languageCode) = key;
			if (localizationKeysDictionary.Count == 0) {
				switch (fieldType) {
					case "value":
						localizationLanguagesDictionary[languageCode] = new LocalizationLanguage(languageCode, value);
						break;
					case "comment":
						if (localizationLanguagesDictionary.TryGetValue(languageCode, out var language)) {
							language.EnglishName = value;
						}
						else {
							throw new Exception("Name for language never declared");
						}
						break;
				}
			}
			else {
				var localizationKey = localizationKeysDictionary[blockKey];
				if (languageCode != "" && !localizationLanguagesDictionary.ContainsKey(languageCode) && fieldType != "param") {
					localizationLanguagesDictionary[languageCode] = new LocalizationLanguage(languageCode);
					foreach (LocalizationKey localizationKeyToUpdate in localizationKeys) {
						localizationKeyToUpdate.LanguageData[languageCode] = new LocalizationKeyLanguageData();
					}
				}
				switch ((languageCode, fieldType)) {
					case ("", "value"):
						localizationKey.DefaultValue = value;
						break;
					case ("", "context"):
						localizationKey.Context = value;
						break;
					case ("", "comment"):
						localizationKey.Comment = value;
						break;
					case ("", "stale"):
						localizationKey.NeedsReview = true;
						break;
					case (not "", "value"):
						localizationKey.LanguageData[languageCode].Value = value;
						break;
					case (not "", "context"):
						localizationKey.LanguageData[languageCode].LanguageContext = value;
						break;
					case (not "", "comment"):
						localizationKey.LanguageData[languageCode].LanguageComment = value;
						break;
					case (not "", "stale"):
						localizationKey.LanguageData[languageCode].StaleComment = value;
						break;
					case (not "", "param"):
						if (!localizationKey.Parameters.Any(parameter => parameter.Key == languageCode)) {
							var values = value.Split(':', count: 2);
							string dataTypeName = values.Length > 1 ? values[0] : "String";
							string providedValue = values.Length > 1 ? values[1] : values[0];
							LocalizationParameter parameterToAdd = new() {
								Key = languageCode,
								DataType = LocalizationParameterType.Select(dataTypeName),
								Value = providedValue,
							};
							localizationKey.Parameters.Add(parameterToAdd);
						}
						break;
					default:
						errors.Add($"{nameof(WordsParserToLocalizationProvider)}.{nameof(VisitFieldDeclaration)} unrecognized `{blockKey}.{fieldType}-{languageCode}`");
						break;
				};
			}
		}
		public void VisitFieldContinuation(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			var localizationKey = localizationKeysDictionary[blockKey];
			switch ((languageCode, fieldType)) {
				case ("", "value"):
					localizationKey.DefaultValue += value;
					break;
				case ("", "context"):
					localizationKey.Context += value;
					break;
				case ("", "comment"):
					localizationKey.Comment += value;
					break;
				case (not "", "value"):
					localizationKey.LanguageData[languageCode].Value += value;
					break;
				case (not "", "context"):
					localizationKey.LanguageData[languageCode].LanguageContext += value;
					break;
				case (not "", "comment"):
					localizationKey.LanguageData[languageCode].LanguageComment += value;
					break;
				case (not "", "param"):
					if (!localizationKey.Parameters.Any(parameter => parameter.Key == languageCode)) {
						int index = localizationKey.Parameters.FindIndex(parameter => parameter.Key == languageCode);
						LocalizationParameter parameterToEdit = localizationKey.Parameters[index];
						parameterToEdit.Value += value;
					}
					break;
				default:
					errors.Add($"{nameof(WordsParserToLocalizationProvider)}.{nameof(VisitFieldContinuation)} unrecognized `{blockKey}.{fieldType}-{languageCode}`");
					break;
			}
		}

		void IWordsParserConsumer.VisitBlock(string blockKey) {
			LocalizationKey keyToAdd;
			if (blockKey[0] == '$') {
				keyToAdd = new LocalizationKey(blockKey) {
					IsConstant = true
				};
			}
			else {
				keyToAdd = new LocalizationKey(blockKey);
			}
			if (localizationKeysDictionary.TryAdd(keyToAdd.BlockKey, keyToAdd)) {
				foreach (LocalizationLanguage language in localizationLanguagesDictionary.Values) {
					keyToAdd.LanguageData[language.Code] = new LocalizationKeyLanguageData();
				}
				localizationKeys.Add(keyToAdd);
			}
		}

		static string FormatFromINI(string input) {
			input = input.Replace("''", "'");
			input = input.Replace("__", "_");
			return input;
		}
	}
}
