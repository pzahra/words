using PatTech.Localization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The ini reader: the parser's events, gathered into the document surface
	///     (<see cref="ILoadedWords"/>) the session loads.
	/// </summary>
	public class WordsParserToLocalizationProvider : IWordsParserConsumer, ILoadedWords {
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
		///     The language the default is written in, from a keyless <c>value=!xx</c>
		///     in the top-of-file language section, or <see langword="null"/>. The
		///     <c>!</c> keeps a runtime from listing the default as a language of its own.
		/// </summary>
		public string? DefaultLanguage { get; private set; }
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
		private readonly HashSet<(string Key, string Type, string Language)> valuesRead = [];
		//each parameter's text as read so far, split again as each line extends it, so
		//a type is read from the whole text however the writer folded it
		private readonly Dictionary<WordsParameter, string> parameterTexts = [];

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
					case "value" when languageCode == "":
						//the keyless value slot names the default's language, as !xx
						if (!value.StartsWith('!')) {
							errors.Add($"value={value} at the top of the file names the default's language: write it value=!{value}, or a runtime lists the default as a language");
						}
						string code = value.TrimStart('!').Trim();
						if (code == "") {
							DefaultLanguage = null;
						}
						else if (LanguageCode.TryParse(code, out var spoken)) {
							DefaultLanguage = spoken.ToString();
						}
						else {
							errors.Add($"value=!{code} at the top of the file: {LanguageCodeRule}");
							DefaultLanguage = code;
						}
						break;
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
					case var form when form.Contains('#'):
						errors.Add($"{Named(form, languageCode)} at the top of the file: a language label has no plural forms, ignored");
						break;
				}
			}
			else {
				//a value declared again in its key overwrites the first, which Save then drops
				if (fieldType.StartsWith("value", StringComparison.Ordinal) && !valuesRead.Add((blockKey, fieldType, languageCode))) {
					errors.Add($"{blockKey}: {Named(fieldType, languageCode)} is declared again, and the last one wins; Save writes only it");
				}
				if (languageCode != "" && !knownLanguages.ContainsKey(languageCode) && fieldType != "param") {
					knownLanguages[languageCode] = new LanguageEntry(languageCode);
					errors.Add($"language '{languageCode}' has entries but no top-of-file label; declare it with value-{languageCode}= (a !Label declares without listing)");
					foreach (WordsKey localizationKeyToUpdate in wordKeys.Values) {
						localizationKeyToUpdate.Entries[languageCode] = new WordsEntry();
					}
				}
				var localizationKey = wordKeys[blockKey];
				switch ((languageCode, fieldType)) {
					case (_, var form) when form.StartsWith("value#", StringComparison.Ordinal):
						StoreForm(localizationKey, languageCode, form[6..], value);
						break;
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
							var (type, description) = WordsParameterType.Split(value);
							WordsParameter parameterToAdd = new(languageCode, type, description);
							parameterTexts[parameterToAdd] = value;
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
				case (_, var form) when form.StartsWith("value#", StringComparison.Ordinal):
					// a dropped form's tail goes with it, griped once already
					var forms = FormsOf(localizationKey, languageCode);
					if (forms.ContainsKey(form[6..])) {
						forms[form[6..]] += value;
					}
					break;
				case ("", "value"):
					localizationKey.DefaultValue += value;
					break;
				case ("", "context"):
					localizationKey.Context += value;
					break;
				case ("", "comment"):
					localizationKey.Comment += value;
					break;
				case ("", "stale"):
					// default stale is a flag (NeedsReview) with no stored text, so
					// a continuation has nothing to extend — swallow, don't gripe
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
				case (not "", "stale"):
					// a long stale message wraps like any other field; without this
					// case the tail was dropped and logged as unrecognized
					localizationKey.Entries[languageCode].Stale += value;
					break;
				case (not "", "param"):
					// continue the text of the parameter this line belongs to
					if (localizationKey.Parameters.FirstOrDefault(parameter => parameter.Key == languageCode) is { } continued) {
						string text = parameterTexts[continued] += value;
						(continued.DataType, continued.Description) = WordsParameterType.Split(text);
					}
					break;
				default:
					errors.Add($"{nameof(WordsParserToLocalizationProvider)}.{nameof(VisitFieldContinuation)} unrecognized `{blockKey}.{fieldType}-{languageCode}`");
					break;
			}
		}

		/// <summary>The key-name grammar in a sentence, for a gripe (runtime SPEC: Key names).</summary>
		public const string KeyNameRule = "a key is segments of any script's letters and digits, _ and -, joined by dots and in NFC, and a $constant is one segment";
		/// <summary>The language-code grammar in a sentence, for a gripe (runtime SPEC: Language codes).</summary>
		public const string LanguageCodeRule = "a language code is language(-Script)?(-REGION)?, as en, ceb, es-419, zh-Hans-CN";

		//the parser read past it, so it is not here to save
		void IWordsParserConsumer.VisitBadLanguage(string blockKey, string name) {
			string where = blockKey == "" ? "at the top of the file" : $"in [{blockKey}]";
			errors.Add($"{name} {where}: no language code, so a runtime skips it and so does Save: {LanguageCodeRule}");
		}

		private static string Field(string languageCode, string form)
			=> languageCode == "" ? $"value#{form}" : $"value-{languageCode}#{form}";

		//a field's name as the file writes it: value-fr#few for the type value#few in fr
		private static string Named(string fieldType, string languageCode) {
			int mark = fieldType.IndexOf('#');
			return languageCode == "" ? fieldType : mark < 0 ? $"{fieldType}-{languageCode}" : $"{fieldType[..mark]}-{languageCode}{fieldType[mark..]}";
		}

		private static Dictionary<string, string> FormsOf(WordsKey key, string languageCode)
			=> languageCode == "" ? key.Forms : key.Entries[languageCode].Forms;

		//a plural form (runtime SPEC: Plural forms): one of CLDR's categories is kept,
		//with a gripe when a runtime would never read it; any other word is no form
		private void StoreForm(WordsKey key, string languageCode, string form, string value) {
			string field = Field(languageCode, form);
			if (!PluralRules.Names.Contains(form)) {
				errors.Add($"{key.BlockKey}: {field} names no plural category ({string.Join(", ", PluralRules.Names)}), dropped");
				return;
			}
			string language = languageCode != "" ? languageCode : DefaultLanguage ?? "en";
			var categories = PluralRules.Categories(language);
			if (form == "one") {
				errors.Add($"{key.BlockKey}: {field} is kept, but a runtime ignores it: the plain value is the one form");
			}
			else if (categories.Count == 1) {
				errors.Add($"{key.BlockKey}: {field} is kept, but '{language}' has one form, the plain value, and a runtime reads nothing else");
			}
			else if (!categories.Contains(form)) {
				errors.Add($"{key.BlockKey}: {field} is kept, but '{language}' counts no whole number as {form} ({string.Join(", ", categories)})");
			}
			FormsOf(key, languageCode)[form] = value;
		}

		void IWordsParserConsumer.VisitBlock(string baseKey, string name) {
			WordsKey keyToAdd;
			//no key to keep: the parser reads past its fields
			if (name == "") {
				errors.Add($"[]: names no key, so a runtime skips it and every field under it, and a save drops them: {KeyNameRule}");
				return;
			}
			if (name[0] == '.') {
				baseKey += name;
			}
			//kept, so an editor can rename it, but a runtime reads past it (runtime SPEC: Key names)
			if (!WordsParser.IsKeyName(baseKey)) {
				errors.Add($"[{name}]: '{baseKey}' is no key name, so a runtime skips it: {KeyNameRule}");
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
