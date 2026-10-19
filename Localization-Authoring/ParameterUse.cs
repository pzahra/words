using PatTech.Localization;
using System.Globalization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The parameters a key's words use, found as the runtime renders and fills
	///     them (editor SPEC: Parameters → Defined by hand, found to help).
	/// </summary>
	/// <param name="Names">
	///     Each parameter once: numbered ones first, by number, then named ones as
	///     they first appear. A number is named as string.Format reads it, <c>{01}</c>
	///     being <c>1</c>.
	/// </param>
	/// <param name="Counted">The names a selector counts by, <c>{0#word}</c>: those an <c>int</c> is guessed for.</param>
	/// <param name="Unfollowed">
	///     A reference, or a selector's key, the words do not have, as one into a file
	///     not loaded: what it would bring in may use anything.
	/// </param>
	public sealed record FoundParameters(IReadOnlyList<string> Names, IReadOnlySet<string> Counted, bool Unfollowed) {
		/// <summary>
		///     The words come back round to an entry they are rendering, a reference or a
		///     selector's form: an app cuts it there and shows <c># ∞ #</c>.
		/// </summary>
		public bool Circular { get; init; }

		/// <summary>The placeholder a parameter is written as: <c>{0}</c>, <c>{Name}</c>.</summary>
		public static string Placeholder(string name) => $"{{{name}}}";
	}

	/// <summary>A parameter an editor offers an input for (<see cref="ParameterUse.Slots"/>).</summary>
	/// <param name="Name">The parameter as the text names it.</param>
	/// <param name="Type">What its input is read as: its definition's type, or the one guessed.</param>
	/// <param name="Definition">Its definition; <see langword="null"/> for one found and not defined.</param>
	/// <param name="Used">Whether the default uses it: a definition the default does not use is no mistake, only a hint.</param>
	public sealed record ParameterSlot(string Name, WordsParameterType Type, WordsParameter? Definition, bool Used);

	/// <summary>What a translation's parameters miss beside its default's (editor SPEC: Parameters → Translation check).</summary>
	/// <param name="Dropped">Those the default uses and the translation never does: the app's value never shows.</param>
	/// <param name="Extra">
	///     Those the translation uses that the default does not and no definition
	///     names: an app passing what the default needs shows it marked, <c>#{1}#</c> or
	///     <c>#{Name}#</c>.
	/// </param>
	public sealed record ParameterMismatch(IReadOnlyList<string> Dropped, IReadOnlyList<string> Extra) {
		/// <summary>Nothing dropped and nothing extra.</summary>
		public static ParameterMismatch None { get; } = new([], []);

		/// <summary>True when anything is dropped or extra.</summary>
		public bool Any => Dropped.Count != 0 || Extra.Count != 0;
	}

	/// <summary>
	///     The parameters a key uses and the translation check, the rule Wordsmith's
	///     badges and the command line's note read, as <see cref="MissingWords"/> is
	///     (editor SPEC: Parameters). A use is a parameter printed, <c>{0}</c>,
	///     <c>{0:N2}</c>, <c>{Name}</c>, or counted, <c>{0#word}</c>, over the plain value
	///     and every form. The words are read in the runtime's own pieces
	///     (<see cref="WordsTemplate"/>), each brace once, so the finder agrees with it:
	///     <c>{{0}</c> is a brace and text, no use; a malformed brace holds none; and a
	///     constant is text, which brings in no parameter. A reference's words are its own
	///     and their uses are the key's; a selector may pick any form of its key the
	///     language counts by, so the uses of every one of them are. A malformed brace is
	///     caught where words are written (<see cref="Malformed(WordsKey)"/>).
	/// </summary>
	public static class ParameterUse {
		/// <summary>
		///     What the default uses, its references and selectors followed through
		///     <paramref name="provider"/>, the words a default preview reads.
		/// </summary>
		/// <param name="key">The key asked about.</param>
		/// <param name="provider">The default's words, every loaded file's.</param>
		/// <param name="language">The language the default is written in, whose rules pick a selector's forms; English when the file declares none.</param>
		public static FoundParameters InDefault(WordsKey key, IWordsProvider provider, string? language)
			=> Find(key, provider, language ?? "en", key.DefaultValue, key.Forms);

		/// <summary>
		///     The parameters an editor shows a key's inputs for (editor SPEC: Parameters
		///     → The inputs): each definition, in the key's order, and whether the
		///     default uses it; then each parameter <paramref name="found"/> that no
		///     definition names, its type guessed, <c>int</c> for a selector's count and
		///     <c>str</c> otherwise.
		/// </summary>
		/// <param name="key">The key whose definitions come first.</param>
		/// <param name="found">What its default uses (<see cref="InDefault"/>).</param>
		public static IReadOnlyList<ParameterSlot> Slots(WordsKey key, FoundParameters found) {
			var used = found.Names.ToHashSet();
			List<ParameterSlot> slots = [.. key.Parameters.Select(parameter => new ParameterSlot(parameter.Key, parameter.DataType, parameter, used.Contains(Normal(parameter.Key))))];
			var defined = key.Parameters.Select(parameter => Normal(parameter.Key)).ToHashSet();
			foreach (string name in found.Names.Where(name => !defined.Contains(name))) {
				slots.Add(new ParameterSlot(name, found.Counted.Contains(name) ? WordsParameterType.Int : WordsParameterType.Str, null, true));
			}
			return slots;
		}

		/// <summary>
		///     What the translation in <paramref name="code"/> uses, followed through its
		///     own language's words. A translation with forms and no plain value reads
		///     the plain value it falls back to, as the runtime does.
		/// </summary>
		/// <param name="key">The key asked about.</param>
		/// <param name="code">The translation's language.</param>
		/// <param name="provider">The language's words, every loaded file's.</param>
		public static FoundParameters InLanguage(WordsKey key, string code, IWordsProvider provider) {
			WordsEntry? entry = key.Entries.GetValueOrDefault(code);
			return Find(key, provider, code, Plain(key, entry, provider), entry?.Forms ?? []);
		}

		/// <summary>
		///     What the translation drops and adds: the default's use is what it must
		///     keep, and <paramref name="defined"/> widens what it may use. Only use is
		///     compared, so printing where the default counts, a form leaving out what
		///     another keeps, or a format of its own is no mismatch. A side with a
		///     reference it cannot follow may carry anything: nothing is dropped from a
		///     translation with one, and nothing is extra beside a default with one.
		/// </summary>
		/// <param name="inDefault">What the default uses.</param>
		/// <param name="inTranslation">What the translation uses.</param>
		/// <param name="defined">The parameters the key defines, <c>param-x</c>, by name.</param>
		public static ParameterMismatch Compare(FoundParameters inDefault, FoundParameters inTranslation, IEnumerable<string> defined) {
			var definitions = defined.Select(Normal).ToHashSet();
			string[] dropped = inTranslation.Unfollowed ? [] : [.. inDefault.Names.Except(inTranslation.Names)];
			string[] extra = inDefault.Unfollowed ? [] : [.. inTranslation.Names.Except(inDefault.Names).Where(name => !definitions.Contains(name))];
			return dropped.Length == 0 && extra.Length == 0 ? ParameterMismatch.None : new(dropped, extra);
		}

		/// <summary>
		///     The check for the translation in <paramref name="code"/>: none for a
		///     constant, or a translation without words, which is missing rather than
		///     mismatched (<see cref="MissingWords"/>).
		/// </summary>
		/// <param name="key">The key asked about.</param>
		/// <param name="code">The translation's language.</param>
		/// <param name="defaults">The default's words, every loaded file's.</param>
		/// <param name="defaultLanguage">The language the default is written in; English when the file declares none.</param>
		/// <param name="translations">The language's words, every loaded file's.</param>
		public static ParameterMismatch Check(WordsKey key, string code, IWordsProvider defaults, string? defaultLanguage, IWordsProvider translations) {
			if (Checked(key, code) is null) {
				return ParameterMismatch.None;
			}
			return Compare(InDefault(key, defaults, defaultLanguage), InLanguage(key, code, translations), key.Parameters.Select(parameter => parameter.Key));
		}

		/// <summary>
		///     Whether the check for the translation in <paramref name="code"/>
		///     (<see cref="Check"/>) reads another key's words, so that an edit to that
		///     key may change it: the words it reads, the default's and the
		///     translation's, the plain value it falls back to among them, bring in a
		///     reference or a selector. A constant brings in text alone, and a check
		///     that reads nothing, for a constant key or a translation without words,
		///     follows nothing.
		/// </summary>
		/// <param name="key">The key asked about.</param>
		/// <param name="code">The translation's language.</param>
		/// <param name="translations">The language's words, every loaded file's, as <see cref="Check"/> reads them.</param>
		public static bool Follows(WordsKey key, string code, IWordsProvider translations) {
			if (Checked(key, code) is not { } entry) {
				return false;
			}
			return Brings(key.DefaultValue) || key.Forms.Values.Any(Brings)
				|| Brings(Plain(key, entry, translations)) || entry.Forms.Values.Any(Brings);
		}

		//the translation a check reads: none for a constant, or one without words,
		//which is missing rather than mismatched
		private static WordsEntry? Checked(WordsKey key, string code)
			=> key.IsConstant || key.Entries.GetValueOrDefault(code) is not { } entry || (entry.Value == "" && !entry.Forms.HasWords()) ? null : entry;

		//a translation's plain value, or the one it falls back to, as the runtime reads it
		private static string Plain(WordsKey key, WordsEntry? entry, IWordsProvider provider)
			=> entry?.Value is { Length: > 0 } value ? value : provider.TryGetValue(key.BlockKey, out string? fallback) ? fallback : "";

		//a reference or a selector, read as the runtime reads them; a constant brings in text alone
		private static bool Brings(string text) => WordsTemplate.Parse(text).Any(piece => piece is WordsPiece.Reference or WordsPiece.Selector);

		//the plain value, then each form in CLDR's order, each rendered as the entry it is
		private static FoundParameters Find(WordsKey key, IWordsProvider provider, string language, string plain, IReadOnlyDictionary<string, string> forms) {
			var walk = new Walk(provider, language);
			walk.Words(plain, key.BlockKey, key.BlockKey);
			foreach (var (form, text) in forms.Written()) {
				walk.Words(text, $"{key.BlockKey}#{form}", key.BlockKey);
			}
			return walk.Found();
		}

		/// <summary>
		///     The references, constants and selectors <paramref name="text"/> holds,
		///     as written, <c>{&gt;key}</c>, <c>{$name}</c> and <c>{0#word}</c>, which
		///     <c>Selects</c>; read in the runtime's pieces, so an escaped brace holds none.
		/// </summary>
		public static IEnumerable<(string Token, bool Selects)> Tokens(string text)
			=> WordsTemplate.Parse(text)
				.Where(piece => piece is WordsPiece.Reference or WordsPiece.Constant or WordsPiece.Selector)
				.Select(piece => (piece.In(text), piece is WordsPiece.Selector));

		/// <summary>
		///     The format items <paramref name="text"/> holds, each by its parameter and
		///     written one way: <c>{0,5:N2}</c>, the number as string.Format reads it
		///     (<c>{00}</c> is <c>{0}</c>), no spaces around the alignment, and an empty
		///     format none. Read in the runtime's pieces, so an escaped brace holds none,
		///     and what a reference or a selector brings in is not the text's own.
		/// </summary>
		public static IEnumerable<(string Name, string Item)> Items(string text)
			=> WordsTemplate.Parse(text).OfType<WordsPiece.Item>().Select(item => (item.Argument, Written(item)));

		private static string Written(WordsPiece.Item item) {
			string alignment = item.Alignment is int width ? $",{width.ToString(CultureInfo.InvariantCulture)}" : "";
			string format = item.Format is { } spec ? $":{spec}" : "";
			return $"{{{item.Argument}{alignment}{format}}}";
		}

		/// <summary>
		///     The braces <paramref name="text"/> writes that open nothing, each once as
		///     written: <c>{0:{&gt;fmt}}</c>, <c>{0,x}</c>, <c>{}</c>, a <c>{</c> never closed.
		///     An app shows each as it stands and warns (the runtime spec's <i>One pass for
		///     every brace</i>); <c>{{</c> writes a brace.
		/// </summary>
		public static IReadOnlyList<string> Malformed(string text)
			=> [.. WordsTemplate.Parse(text).OfType<WordsPiece.Malformed>().Select(piece => piece.In(text)).Distinct()];

		/// <summary>
		///     Each value of <paramref name="key"/> that writes a brace opening nothing
		///     (<see cref="Malformed(string)"/>), the default's and its forms' first, then
		///     each language's; none for a constant, whose words are text.
		/// </summary>
		public static IEnumerable<(WordsField Field, IReadOnlyList<string> Braces)> Malformed(WordsKey key)
			=> key.IsConstant ? [] : WordsField.All(key)
				.Where(written => written.Field.Type == "value")
				.Select(written => (written.Field, Braces: Malformed(written.Text)))
				.Where(found => found.Braces.Count != 0);

		/// <summary>
		///     <paramref name="braces"/> named as a gripe names them: <c>`{`, a brace that
		///     opens nothing</c>, or <c>`{`, `{0,x}`, braces that open nothing</c>.
		/// </summary>
		public static string Named(IReadOnlyList<string> braces)
			=> $"{string.Join(", ", braces.Select(brace => $"`{brace}`"))}, {(braces.Count == 1 ? "a brace that opens" : "braces that open")} nothing";

		/// <summary>
		///     Whether <paramref name="name"/> names a parameter a text can use: a number,
		///     <c>{0}</c>, or a name, <c>{Count}</c>, as a format item reads one, and
		///     nothing else. The item's argument is the whole name: <c>Name:</c> reads as
		///     <c>{Name}</c> with an empty spec, and written as <c>param-Name:=</c> would
		///     load back as another parameter.
		/// </summary>
		public static bool IsName(string name)
			=> !name.Any(char.IsWhiteSpace) && WordsTemplate.ParseFormat($"{{{name}}}") is [WordsPiece.Item { Alignment: null, Format: null } item]
				&& item.Length == name.Length + 2 && item.Argument == Normal(name);

		/// <summary>Whether two names are one parameter, as string.Format reads a number: <c>01</c> is <c>1</c>.</summary>
		public static bool SameName(string a, string b) => Normal(a) == Normal(b);

		private static string Normal(string name)
			=> name.Length > 0 && char.IsAsciiDigit(name[0]) && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index.ToString(CultureInfo.InvariantCulture) : name;

		//one key's rendering, followed as Words follows it, the uses gathered on the way
		private sealed class Walk(IWordsProvider provider, string language) {
			private readonly List<string> order = [];
			private readonly HashSet<string> counted = [];
			private bool unfollowed;
			//the entries being rendered, for the circular cut, and whether it was made
			private readonly Stack<string> path = new();
			private bool circular;

			public FoundParameters Found() {
				var numbered = order.Where(name => char.IsAsciiDigit(name[0])).OrderBy(name => name.Length).ThenBy(name => name, StringComparer.Ordinal);
				return new([.. numbered, .. order.Where(name => !char.IsAsciiDigit(name[0]))], counted, unfollowed) { Circular = circular };
			}

			//entry's words, rendered against key, piece by piece: what a reference or a
			//selector brings in, in turn; a constant is text, and brings in no parameter
			public void Words(string text, string entry, string key) {
				path.Push(entry);
				try {
					foreach (WordsPiece piece in WordsTemplate.Parse(text)) {
						switch (piece) {
							case WordsPiece.Item item:
								Use(item.Argument);
								break;
							case WordsPiece.Reference reference:
								Reference(Relative(reference.Key, key));
								break;
							case WordsPiece.Selector selector:
								Use(selector.Argument);
								counted.Add(selector.Argument);
								Select(Relative(selector.Key, key));
								break;
						}
					}
				}
				finally {
					path.Pop();
				}
			}

			//a key's words, rendered against the key they are words of, a form's against its
			//key; a constant's are text
			private void Reference(string entry) {
				if (entry.StartsWith('$')) {
					return;
				}
				if (path.Contains(entry)) {
					circular = true;
					return;
				}
				if (!provider.TryGetValue(entry, out string? words)) {
					unfollowed = true;
				}
				else {
					Words(words, entry, entry.IndexOf('#') is > 0 and var mark ? entry[..mark] : entry);
				}
			}

			//every form of key the language counts by, as a count in each would pick it
			private void Select(string key) {
				if (path.Any(entry => entry == key || entry.StartsWith(key + "#", StringComparison.Ordinal))) {
					circular = true;
					return;
				}
				foreach (string form in PluralRules.Categories(language).Select(category => PatTech.Localization.Words.FormKey(provider, language, key, category)).Distinct()) {
					Reference(form);
				}
			}

			private void Use(string name) {
				if (!order.Contains(name)) {
					order.Add(name);
				}
			}

			//.sub under key, as Words resolves a relative reference
			private static string Relative(string name, string key) => name.StartsWith('.') ? key + name : name;
		}
	}
}
