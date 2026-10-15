using PatTech.Localization;
using PatTech.Utils;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The parameters a key's words use, found as the runtime renders and then
	///     formats them (editor SPEC: Parameters → Defined by hand, found to help).
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
	///     and every form. The words are read as the runtime read them before it read each
	///     brace once, in two passes: rendering expands each reference and collapses Words'
	///     escapes, then string.Format reads what is left, so <c>{{0}</c> renders to
	///     <c>{0}</c> and is a use, and <c>{{{{0}}</c> is not; the finder keeps that until it
	///     reads the runtime's pieces (runtime SPEC: One pass for every brace). A reference's words are its own and their uses
	///     are the key's; a selector may pick any form of its key the language counts
	///     by, so the uses of every one of them are.
	/// </summary>
	public static class ParameterUse {
		//the rendering pass as Words' own matches it: an escaped pair, a {$constant} or
		//{>key} reference, or a {0#word} / {Count#word} selector
		private static readonly Regex rxRender = new(
				@"(?<1>[\\'""{])\1|\{[$>](?<2>[^}]+)\}|\{(?<3>\d+|(?=[_a-zA-Z])\w+)#(?<4>[^{}#\s]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		//the inside of a format item: a number, with an alignment, as string.Format reads
		//one, or a name, as FormatByName does; either with a format after a ':'
		private static readonly Regex rxItem = new(
				@"^(?:(?<1>\d+) *(?:, *(?<2>-?\d+) *)?|[\s-[\r\n]]*(?<1>(?=[_a-zA-Z])\w+)[\s-[\r\n]]*)(?::(?<3>.*))?\z",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);

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
			string plain = entry?.Value is { Length: > 0 } value ? value : provider.TryGetValue(key.BlockKey, out string? fallback) ? fallback : "";
			return Find(key, provider, code, plain, entry?.Forms ?? []);
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
			if (key.IsConstant || key.Entries.GetValueOrDefault(code) is not { } entry || (entry.Value == "" && !entry.Forms.HasWords())) {
				return ParameterMismatch.None;
			}
			return Compare(InDefault(key, defaults, defaultLanguage), InLanguage(key, code, translations), key.Parameters.Select(parameter => parameter.Key));
		}

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
		///     <c>Selects</c>; read as rendering reads them, so an escaped brace holds none.
		/// </summary>
		public static IEnumerable<(string Token, bool Selects)> Tokens(string text) {
			for (Match match = rxRender.Match(text); match.Success; match = match.NextMatch()) {
				if (!match.Groups[1].Success) {
					yield return (match.Value, match.Groups[3].Success);
				}
			}
		}

		/// <summary>
		///     The format items <paramref name="text"/> holds, each by its parameter and
		///     written one way: <c>{0,5:N2}</c>, the number as string.Format reads it
		///     (<c>{00}</c> is <c>{0}</c>), no spaces around the alignment, and an empty
		///     format none. Read as rendering leaves them, so an escaped brace holds none,
		///     and what a reference or a selector brings in is not the text's own.
		/// </summary>
		public static IEnumerable<(string Name, string Item)> Items(string text) {
			List<(string Name, string Item)> items = [];
			var stretch = new StringBuilder();
			int start = 0;
			for (Match match = rxRender.Match(text); match.Success; match = match.NextMatch()) {
				stretch.Append(text, start, match.Index - start);
				start = match.Index + match.Length;
				if (match.Groups[1].Success) {
					stretch.Append(match.Groups[1].Value);
					continue;
				}
				Read(stretch.ToString());
				stretch.Clear();
			}
			stretch.Append(text, start, text.Length - start);
			Read(stretch.ToString());
			return items;

			void Read(string words) {
				foreach (Match item in FormatItems(words)) {
					string name = Normal(item.Groups[1].Value);
					string alignment = item.Groups[2].Success ? $",{item.Groups[2].Value}" : "";
					string format = item.Groups[3].Value is { Length: > 0 } spec ? $":{spec}" : "";
					items.Add((name, $"{{{name}{alignment}{format}}}"));
				}
			}
		}

		//string.Format's items in text: {{ is a brace, and an item runs to the first }
		private static IEnumerable<Match> FormatItems(string text) {
			for (int i = text.IndexOf('{'); i >= 0 && i < text.Length; i = text.IndexOf('{', i)) {
				if (i + 1 < text.Length && text[i + 1] == '{') {
					i += 2;
					continue;
				}
				int close = text.IndexOf('}', i + 1);
				if (close < 0) {
					yield break;
				}
				if (rxItem.TryMatch(text[(i + 1)..close], out var item)) {
					yield return item;
				}
				i = close + 1;
			}
		}

		//a number as string.Format reads it, 01 being 1; a name as written
		/// <summary>Whether <paramref name="name"/> names a parameter a text can use: a number, <c>{0}</c>, or a name, <c>{Count}</c>, as FormatByName reads one.</summary>
		public static bool IsName(string name) => rxName.IsMatch(name);
		private static readonly Regex rxName = new(@"^(?:\d+|(?=[_a-zA-Z])\w+)\z", RegexOptions.Compiled);

		/// <summary>Whether two names are one parameter, as string.Format reads a number: <c>01</c> is <c>1</c>.</summary>
		public static bool SameName(string a, string b) => Normal(a) == Normal(b);

		private static string Normal(string name)
			=> name.Length > 0 && char.IsAsciiDigit(name[0]) && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index.ToString(CultureInfo.InvariantCulture) : name;

		//one key's rendering, followed as Words follows it, the uses gathered on the way
		private sealed class Walk(IWordsProvider provider, string language) {
			private readonly List<string> order = [];
			private readonly HashSet<string> counted = [];
			private bool unfollowed;
			//the entries being rendered, for the circular cut
			private readonly Stack<string> path = new();

			public FoundParameters Found() {
				var numbered = order.Where(name => char.IsAsciiDigit(name[0])).OrderBy(name => name.Length).ThenBy(name => name, StringComparer.Ordinal);
				return new([.. numbered, .. order.Where(name => !char.IsAsciiDigit(name[0]))], counted, unfollowed);
			}

			//entry's words, rendered against key: each stretch between references and
			//selectors is read for format items once its escapes collapse, and what a
			//reference or a selector brings in, in turn
			public void Words(string text, string entry, string key) {
				path.Push(entry);
				try {
					var stretch = new StringBuilder();
					int start = 0;
					for (Match match = rxRender.Match(text); match.Success; match = match.NextMatch()) {
						stretch.Append(text, start, match.Index - start);
						start = match.Index + match.Length;
						if (match.Groups[1].Success) {
							stretch.Append(match.Groups[1].Value);
							continue;
						}
						Items(stretch.ToString());
						stretch.Clear();
						if (match.Groups[2].Success) {
							Reference(match.Value[1] == '$' ? "$" + match.Groups[2].Value : Relative(match.Groups[2].Value, key));
						}
						else {
							string count = Normal(match.Groups[3].Value);
							Use(count);
							counted.Add(count);
							Select(Relative(match.Groups[4].Value, key));
						}
					}
					stretch.Append(text, start, text.Length - start);
					Items(stretch.ToString());
				}
				finally {
					path.Pop();
				}
			}

			//a constant's words go to string.Format as they are; a key's are rendered
			//against the key they are words of, a form's against its key
			private void Reference(string entry) {
				if (path.Contains(entry)) {
					return;
				}
				if (!provider.TryGetValue(entry, out string? words)) {
					unfollowed = true;
				}
				else if (entry.StartsWith('$')) {
					Items(words);
				}
				else {
					Words(words, entry, entry.IndexOf('#') is > 0 and var mark ? entry[..mark] : entry);
				}
			}

			//every form of key the language counts by, as a count in each would pick it
			private void Select(string key) {
				if (path.Any(entry => entry == key || entry.StartsWith(key + "#", StringComparison.Ordinal))) {
					return;
				}
				foreach (string form in PluralRules.Categories(language).Select(category => PatTech.Localization.Words.FormKey(provider, language, key, category)).Distinct()) {
					Reference(form);
				}
			}

			private void Items(string text) {
				foreach (Match item in FormatItems(text)) {
					Use(Normal(item.Groups[1].Value));
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
