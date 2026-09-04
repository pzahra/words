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
		///     Freeform comment runs by the block key they sat above — the block
		///     is where the file put them, not what they belong to; an authoring
		///     tool should let them stand alone and re-anchor by position.
		/// </summary>
		public IReadOnlyDictionary<string, string> BlockComments => blockComments;
		/// <summary>
		///     The codes declared by top-of-file labels, in declaration order — the
		///     file's own language table. Languages that only appear on fields are
		///     in <see cref="KnownLanguages"/> (with a <c>!code</c> placeholder
		///     label and a gripe in <see cref="Errors"/>) but not here.
		/// </summary>
		public IReadOnlyList<string> DeclaredLanguages => declaredLanguages;
		/// <summary>
		///     The project settings file named by a keyless <c>param=</c> in the
		///     top-of-file language section — an authoring tool's use of that
		///     otherwise idle slot (SPEC: Markdown previews); the path as written,
		///     relative to the file, or empty. Captured and preserved only: reading
		///     it is <see cref="ProjectSettings"/>' job.
		/// </summary>
		public string Settings { get; private set; } = "";
		/// <summary>
		///     The per-language settings files named by keyless <c>param-xx=</c>
		///     lines, code → path as written, in order of appearance. A code here
		///     declares no language.
		/// </summary>
		public IReadOnlyDictionary<string, string> LanguageSettings => languageSettings;

		private readonly List<string> errors = [];
		private readonly Dictionary<string, LanguageEntry> knownLanguages = [];
		private readonly Dictionary<string, WordsKey> wordKeys = [];
		private readonly List<string> pendingComments = [];
		private readonly List<string> declaredLanguages = [];
		private readonly Dictionary<string, string> blockComments = [];
		private readonly Dictionary<string, string> languageSettings = [];

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
				// a run between fields hoists above its block
				if (wordKeys.Count == 0) {
					Preamble = AppendRun(Preamble, TakePendingComments());
				}
				else if (blockKey != "") {
					blockComments[blockKey] = AppendRun(blockComments.GetValueOrDefault(blockKey, ""), TakePendingComments());
				}
			}
			if (wordKeys.Count == 0) {
				switch (fieldType) {
					case "value":
						if (knownLanguages.TryGetValue(languageCode, out var named)) {
							//its comment- label came first and made the entry; this is the name
							named.NativeName = value;
						}
						else {
							knownLanguages[languageCode] = new LanguageEntry(languageCode, value);
						}
						if (!declaredLanguages.Contains(languageCode)) {
							declaredLanguages.Add(languageCode);
						}
						break;
					case "comment":
						if (knownLanguages.TryGetValue(languageCode, out var language)) {
							language.EnglishName = value;
						}
						else {
							//a comment- label ahead of its value- label: keep loading, keep
							//the English name, and gripe. A later value- names it; without
							//one it stays a !code placeholder and is never written back
							knownLanguages[languageCode] = new LanguageEntry(languageCode) { EnglishName = value };
							errors.Add($"language '{languageCode}' has a comment-{languageCode} label before (or without) its value-{languageCode} label");
						}
						break;
					case "param":
						// the keyless param slot names the project settings file:
						// param= for the dictionary, param-xx= for language xx
						if (languageCode == "") {
							Settings = value;
						}
						else {
							languageSettings[languageCode] = value;
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

			if (wordKeys.Count == 0) {
				// still in the top-of-file language section — no block to attach to.
				// These fields wrap too (a long folder path, a long label), so
				// continue them here rather than fault on the missing block key.
				switch (fieldType) {
					case "param" when languageCode == "":
						Settings += value;
						break;
					case "param" when languageSettings.ContainsKey(languageCode):
						languageSettings[languageCode] += value;
						break;
					case "value" when knownLanguages.TryGetValue(languageCode, out var labelValue):
						labelValue.NativeName += value;
						break;
					case "comment" when knownLanguages.TryGetValue(languageCode, out var labelComment):
						labelComment.EnglishName += value;
						break;
				}
				return;
			}

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
				// the run above a header anchors to that block, even when the
				// header re-opens a block declared earlier
				string blockKey = keyToAdd.BlockKey;
				blockComments[blockKey] = AppendRun(blockComments.GetValueOrDefault(blockKey, ""), TakePendingComments());
			}
		}
	}
}
