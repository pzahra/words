using PatTech.Localization;

namespace PatTech.Localization.Authoring {
	public class WordsParserToLocalizationProvider : IWordsParserConsumer {
		public IReadOnlyList<string> Errors => errors;
		public IReadOnlyDictionary<string, WordsKey> WordKeys => wordKeys;
		public IReadOnlyDictionary<string, LanguageEntry> KnownLanguages => knownLanguages;

		/// <summary>The comment run above the language labels, at the very top of the file.</summary>
		public string Preamble { get; private set; } = "";
		/// <summary>The comment run after the last block, at the very end of the file.</summary>
		public string Trailer => string.Join('\n', pendingComments);
		/// <summary>
		///     The codes declared by top-of-file labels, in declaration order — the
		///     file's own language table. Languages that only appear on fields are
		///     in <see cref="KnownLanguages"/> (with a <c>!code</c> placeholder
		///     label and a gripe in <see cref="Errors"/>) but not here.
		/// </summary>
		public IReadOnlyList<string> DeclaredLanguages => declaredLanguages;

		private readonly List<string> errors = [];
		private readonly Dictionary<string, LanguageEntry> knownLanguages = [];
		private readonly Dictionary<string, WordsKey> wordKeys = [];
		private readonly List<string> pendingComments = [];
		private readonly List<string> declaredLanguages = [];

		public WordsParserToLocalizationProvider() { }

		public void VisitComment(string text) => pendingComments.Add(text);

		private string TakePendingComments() {
			var text = string.Join('\n', pendingComments);
			pendingComments.Clear();
			return text;
		}

		private static string AppendRun(string existing, string run)
			=> existing == "" ? run : $"{existing}\n{run}";

		public void VisitFieldDeclaration(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;
			if (pendingComments.Count != 0) {
				// comments in the language section belong to the file preamble;
				// a run between fields hoists to its block's banner
				if (wordKeys.Count == 0) {
					Preamble = AppendRun(Preamble, TakePendingComments());
				}
				else if (wordKeys.TryGetValue(blockKey, out var owner)) {
					owner.Banner = AppendRun(owner.Banner, TakePendingComments());
				}
			}
			if (wordKeys.Count == 0) {
				switch (fieldType) {
					case "value":
						knownLanguages[languageCode] = new LanguageEntry(languageCode, value);
						if (!declaredLanguages.Contains(languageCode)) {
							declaredLanguages.Add(languageCode);
						}
						break;
					case "comment":
						if (knownLanguages.TryGetValue(languageCode, out var language)) {
							language.EnglishName = value;
						}
						else {
							throw new Exception("Name for language never declared");
						}
						break;
				}
			}
			else {
				if (languageCode != "" && !knownLanguages.ContainsKey(languageCode) && fieldType != "param") {
					knownLanguages[languageCode] = new LanguageEntry(languageCode);
					errors.Add($"language '{languageCode}' has entries but no top-of-file label; declare it with value-{languageCode}= (a !Label declares without listing)");
					foreach (WordsKey localizationKeyToUpdate in wordKeys.Values) {
						localizationKeyToUpdate.Entries[languageCode] = new WordsEntry();
					}
				}
				var localizationKey = wordKeys[blockKey];
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
						localizationKey.Entries[languageCode].Value = value;
						break;
					case (not "", "context"):
						localizationKey.Entries[languageCode].Context = value;
						break;
					case (not "", "comment"):
						localizationKey.Entries[languageCode].Comment = value;
						break;
					case (not "", "stale"):
						localizationKey.Entries[languageCode].Stale = value;
						break;
					case (not "", "param"):
						if (!localizationKey.Parameters.Any(parameter => parameter.Key == languageCode)) {
							var values = value.Split(':', count: 2);
							string dataTypeName = values.Length > 1 ? values[0] : "String";
							string providedValue = values.Length > 1 ? values[1] : values[0];
							WordsParameter parameterToAdd = new(
								key: languageCode,
								dataType: WordsParameterType.Select(dataTypeName),
								value: providedValue
							);
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

			var localizationKey = wordKeys[blockKey];
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
					localizationKey.Entries[languageCode].Value += value;
					break;
				case (not "", "context"):
					localizationKey.Entries[languageCode].Context += value;
					break;
				case (not "", "comment"):
					localizationKey.Entries[languageCode].Comment += value;
					break;
				case (not "", "param"):
					// continue the value of the parameter this line belongs to
					foreach (var parameter in localizationKey.Parameters) {
						if (parameter.Key == languageCode) {
							parameter.Value += value;
							break;
						}
					}
					break;
				default:
					errors.Add($"{nameof(WordsParserToLocalizationProvider)}.{nameof(VisitFieldContinuation)} unrecognized `{blockKey}.{fieldType}-{languageCode}`");
					break;
			}
		}

		void IWordsParserConsumer.VisitBlock(string baseKey, string name) {
			WordsKey keyToAdd;
			if (name[0] == '.') {
				baseKey += name;
			}
			if (baseKey[0] == '$') {
				keyToAdd = new WordsKey(baseKey) {
					IsConstant = true
				};
			}
			else {
				keyToAdd = new WordsKey(baseKey);
			}
			if (wordKeys.TryAdd(keyToAdd.BlockKey, keyToAdd)) {
				foreach (LanguageEntry language in knownLanguages.Values) {
					keyToAdd.Entries[language.Code] = new WordsEntry();
				}
			}
			if (pendingComments.Count != 0) {
				// the run above a header banners that block, even when the
				// header re-opens a block declared earlier
				var owner = wordKeys[keyToAdd.BlockKey];
				owner.Banner = AppendRun(owner.Banner, TakePendingComments());
			}
		}
	}
}
