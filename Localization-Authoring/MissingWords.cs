using PatTech.Localization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     What a key misses, by the rule Wordsmith's badges show and the command
	///     line's <c>list --missing</c> reads (editor SPEC: Badges, Plural forms →
	///     Badges): the default misses its words when its value is empty, a
	///     language its file registers when its entry's is, unless the default
	///     speaks it; a plural key also wants every form its languages count by,
	///     bar the optional ones. A constant misses nothing.
	/// </summary>
	public static class MissingWords {
		/// <summary>The plain value's category: the <c>one</c> form.</summary>
		public const string Plain = "one";

		/// <summary>A key with a form in any language, the default included.</summary>
		public static bool IsPlural(WordsKey key) => key.Forms.HasWords() || key.Entries.Values.Any(entry => entry.Forms.HasWords());

		/// <summary>A form a plural key wants in <paramref name="language"/>: one it counts by, beside the plain value, and not optional.</summary>
		public static bool Requires(string language, string category) {
			var categories = PluralRules.Categories(language);
			return categories.Count > 1 && category != Plain && categories.Contains(category) && !PluralRules.Optional(language).ContainsKey(category);
		}

		/// <summary>
		///     True when a key with forms misses words in <paramref name="language"/>:
		///     a category it counts by that is not optional and has no form. A
		///     language with one category misses none.
		/// </summary>
		public static bool Misses(string language, IReadOnlyDictionary<string, string> forms)
			=> PluralRules.Categories(language).Any(category => Requires(language, category) && forms.GetValueOrDefault(category, "") == "");

		/// <summary>
		///     Whether a file wants words in <paramref name="code"/>: it registers the
		///     language (listed or <c>!</c>-hidden) and its default does not speak it,
		///     since an empty entry there falls back to the default.
		/// </summary>
		/// <param name="languages">The file's own language table.</param>
		/// <param name="defaultLanguage">The language its default is written in, or <see langword="null"/>.</param>
		/// <param name="code">The language asked about.</param>
		public static bool Wants(IReadOnlyCollection<string> languages, string? defaultLanguage, string code)
			=> languages.Contains(code) && !WordsParser.DefaultSpeaks(defaultLanguage, code);

		/// <summary>The default misses words: its value is empty, or on a plural key a form its language requires.</summary>
		/// <param name="key">The key asked about.</param>
		/// <param name="defaultLanguage">The language the default is written in; English when the file declares none.</param>
		public static bool InDefault(WordsKey key, string? defaultLanguage)
			=> !key.IsConstant && (key.DefaultValue.Trim() == "" || (IsPlural(key) && Misses(defaultLanguage ?? "en", key.Forms)));

		/// <summary>
		///     The entry in <paramref name="code"/> misses words: its value is empty,
		///     or on a plural key a form the language requires. Whether the file wants
		///     words there at all is <see cref="Wants"/>'s question.
		/// </summary>
		public static bool InLanguage(WordsKey key, string code) {
			WordsEntry? entry = key.Entries.GetValueOrDefault(code);
			return !key.IsConstant && (entry is null || entry.Value.Trim() == "" || (IsPlural(key) && Misses(code, entry.Forms)));
		}
	}
}
