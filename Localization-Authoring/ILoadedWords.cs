namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The neutral document surface one file loads into — what
	///     <see cref="WordsSession.Load(ILoadedWords, string, string?)"/> absorbs: the keys,
	///     the languages they know and the ones the file declares, the comment
	///     runs, the settings references and the gripes. The ini parser fills it
	///     (<see cref="WordsParserToLocalizationProvider"/>); an
	///     <see cref="IWordsImporter"/> fills it from any other format and gets the
	///     whole of loading — label disambiguation, empty-key dropping, language
	///     backfill, reload in place — for free.
	/// </summary>
	public interface ILoadedWords {
		/// <summary>Every key by its (unprefixed) block key, in document order.</summary>
		IReadOnlyDictionary<string, WordsKey> WordKeys { get; }
		/// <summary>Every language the keys carry entries for, by code, in order of first appearance.</summary>
		IReadOnlyDictionary<string, LanguageEntry> KnownLanguages { get; }
		/// <summary>The codes the file declares — its own language table — in its order.</summary>
		IReadOnlyList<string> DeclaredLanguages { get; }
		/// <summary>The language the default is written in (<c>value=!xx</c> atop an ini), or <see langword="null"/> when the file does not say.</summary>
		string? DefaultLanguage => null;
		/// <summary>The comment run above the language table.</summary>
		string Preamble { get; }
		/// <summary>The comment run after the last block.</summary>
		string Trailer { get; }
		/// <summary>Comment runs by the (unprefixed) block key they sat above.</summary>
		IReadOnlyDictionary<string, string> BlockComments { get; }
		/// <summary>The project settings file, relative to the file, or empty.</summary>
		string Settings { get; }
		/// <summary>The per-language settings files, code → relative path.</summary>
		IReadOnlyDictionary<string, string> LanguageSettings { get; }
		/// <summary>What the reader dropped or guessed; the file loads regardless.</summary>
		IReadOnlyList<string> Errors { get; }
	}

	/// <summary>
	///     An <see cref="ILoadedWords"/> filled by hand — what an importer builds.
	///     <see cref="Declare"/> and <see cref="Key"/> keep the shape the session
	///     expects: every key an entry for every known language.
	/// </summary>
	public sealed class LoadedWords : ILoadedWords {
		/// <inheritdoc cref="ILoadedWords.WordKeys"/>
		public OrderedDictionary<string, WordsKey> WordKeys { get; } = new();
		/// <inheritdoc cref="ILoadedWords.KnownLanguages"/>
		public OrderedDictionary<string, LanguageEntry> KnownLanguages { get; } = new();
		/// <inheritdoc cref="ILoadedWords.DeclaredLanguages"/>
		public List<string> DeclaredLanguages { get; } = [];
		/// <inheritdoc cref="ILoadedWords.DefaultLanguage"/>
		public string? DefaultLanguage { get; set; }
		/// <inheritdoc cref="ILoadedWords.Preamble"/>
		public string Preamble { get; set; } = "";
		/// <inheritdoc cref="ILoadedWords.Trailer"/>
		public string Trailer { get; set; } = "";
		/// <inheritdoc cref="ILoadedWords.BlockComments"/>
		public Dictionary<string, string> BlockComments { get; } = [];
		/// <inheritdoc cref="ILoadedWords.Settings"/>
		public string Settings { get; set; } = "";
		/// <inheritdoc cref="ILoadedWords.LanguageSettings"/>
		public Dictionary<string, string> LanguageSettings { get; } = [];
		/// <inheritdoc cref="ILoadedWords.Errors"/>
		public List<string> Errors { get; } = [];

		IReadOnlyDictionary<string, WordsKey> ILoadedWords.WordKeys => WordKeys;
		IReadOnlyDictionary<string, LanguageEntry> ILoadedWords.KnownLanguages => KnownLanguages;
		IReadOnlyList<string> ILoadedWords.DeclaredLanguages => DeclaredLanguages;
		IReadOnlyDictionary<string, string> ILoadedWords.BlockComments => BlockComments;
		IReadOnlyDictionary<string, string> ILoadedWords.LanguageSettings => LanguageSettings;
		IReadOnlyList<string> ILoadedWords.Errors => Errors;

		/// <summary>
		///     Declares a language: it joins the table and the known languages, and
		///     every key so far gets an entry for it. Returns its entry, new or found.
		/// </summary>
		/// <param name="code">The language code, <c>fr</c> or <c>en-GB</c>.</param>
		/// <param name="nativeName">Its label in its own words; a <c>!</c> prefix declares without listing.</param>
		/// <param name="englishName">Its label in English, when it has one of its own.</param>
		public LanguageEntry Declare(string code, string nativeName, string? englishName = null) {
			if (!KnownLanguages.TryGetValue(code, out var language)) {
				language = new LanguageEntry(code, nativeName);
				KnownLanguages[code] = language;
				foreach (WordsKey key in WordKeys.Values) {
					key.Entries.TryAdd(code, new WordsEntry());
				}
			}
			if (englishName is not null) {
				language.EnglishName = englishName;
			}
			if (!DeclaredLanguages.Contains(code)) {
				DeclaredLanguages.Add(code);
			}
			return language;
		}

		/// <summary>
		///     The key at <paramref name="blockKey"/>, added with an entry for every
		///     known language when it is new; a <c>$</c>-marked key is a constant.
		/// </summary>
		public WordsKey Key(string blockKey) {
			if (!WordKeys.TryGetValue(blockKey, out var key)) {
				key = new WordsKey(blockKey) { IsConstant = blockKey.StartsWith('$') };
				foreach (LanguageEntry language in KnownLanguages.Values) {
					key.Entries[language.Code] = new WordsEntry();
				}
				WordKeys[blockKey] = key;
			}
			return key;
		}
	}
}
