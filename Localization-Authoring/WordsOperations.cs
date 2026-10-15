using System.Globalization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     Document-level operations over key stores, each a file's keys by the
	///     names it writes (<see cref="WordsFile.Keys"/>). These do the processing;
	///     the editor's ViewModels only gather the intent.
	/// </summary>
	public static class WordsOperations {

		/// <summary>
		///     True when every set holds the same keys; the odd ones out come back in
		///     <paramref name="conflicts"/>.
		/// </summary>
		public static bool HaveSameKeys(
				IEnumerable<IReadOnlyDictionary<string, WordsKey>> keySets,
				out HashSet<string> conflicts) {
			HashSet<string>? firstKeySet = null;
			bool haveSameKeys = true;
			conflicts = [];

			foreach (var keySet in keySets) {
				HashSet<string> currentKeySet = [.. keySet.Keys];
				if (firstKeySet is null) {
					firstKeySet = currentKeySet;
				}
				else if (!firstKeySet.SetEquals(currentKeySet)) {
					haveSameKeys = false;
					currentKeySet.SymmetricExceptWith(firstKeySet);
					conflicts.UnionWith(currentKeySet);
				}
			}
			return haveSameKeys;
		}

		/// <summary>
		///     The translator round trip in bulk: the merged file takes every key
		///     from <paramref name="baseKeys"/> and each language's entries from
		///     the file mapped to it in <paramref name="languageSources"/>.
		///     Returns <see langword="null"/> when the files disagree on their key
		///     sets; the disagreements come back in <paramref name="conflicts"/>.
		/// </summary>
		/// <param name="baseKeys">The keys of the file providing the keys and unlocalised fields.</param>
		/// <param name="languageSources">Language code to the keys of the file providing that language's entries.</param>
		/// <param name="conflicts">Keys the involved files disagree on.</param>
		public static Dictionary<string, WordsKey>? Merge(
				IReadOnlyDictionary<string, WordsKey> baseKeys,
				IReadOnlyDictionary<string, IReadOnlyDictionary<string, WordsKey>> languageSources,
				out HashSet<string> conflicts) {
			Dictionary<string, WordsKey> merged = baseKeys.Values.ToDictionary(key => key.BlockKey, key => new WordsKey(key));
			if (!HaveSameKeys([merged, .. languageSources.Values], out conflicts)) {
				return null;
			}
			foreach (var (language, keys) in languageSources) {
				foreach (var key in keys.Values) {
					if (key.Entries.TryGetValue(language, out var entry)) {
						merged[key.BlockKey].Entries[language] = new WordsEntry(entry);
					}
				}
			}
			return merged;
		}

		/// <summary>
		///     The inverse of <see cref="Merge"/>: one language's entries split
		///     into their own file, keeping the unlocalised fields for reference,
		///     so a translator can work on it separately. The result is exactly
		///     the shape <see cref="Merge"/> consumes back.
		/// </summary>
		/// <param name="sourceKeys">The keys of the file being split.</param>
		/// <param name="languageCode">The one language the split file carries.</param>
		public static Dictionary<string, WordsKey> Split(IReadOnlyDictionary<string, WordsKey> sourceKeys, string languageCode) {
			Dictionary<string, WordsKey> split = [];
			foreach (var key in sourceKeys.Values) {
				var copy = new WordsKey(key);
				foreach (var code in copy.Entries.Keys.Where(code => code != languageCode).ToArray()) {
					copy.Entries.Remove(code);
				}
				split[copy.BlockKey] = copy;
			}
			return split;
		}

		/// <summary>
		///     Re-codes a language: each key's entry moves from
		///     <paramref name="fromCode"/> to <paramref name="toCode"/>. An empty
		///     target adopts it whole; otherwise the two merge field by field with
		///     nothing occupied overwritten. On a value clash the target's wins and
		///     the displaced value is appended to its <c>context</c> with a stale
		///     marker; the source's context, comment and stale marker fold into any
		///     of the target's still empty. Plural forms merge as the value does, form
		///     by form, a displaced form noted with its category (<c>#few: …</c>).
		/// </summary>
		public static void Shift(IEnumerable<WordsKey> keys, string fromCode, string toCode) {
			foreach (var key in keys) {
				if (!key.Entries.Remove(fromCode, out var moved)) {
					continue;
				}
				if (!key.Entries.TryGetValue(toCode, out var target) || target.IsEmpty()) {
					key.Entries[toCode] = moved;
					continue;
				}
				bool clash = target.Value != "" && moved.Value != "" && moved.Value != target.Value;
				if (target.Value == "") {
					target.Value = moved.Value;
				}
				target.Context = Fold(target.Context, moved.Context);
				if (clash) {
					target.Context = Fold(target.Context, moved.Value);
				}
				foreach (var (form, text) in moved.Forms.Written()) {
					string held = target.Forms.GetValueOrDefault(form, "");
					if (held == "") {
						target.Forms[form] = text;
					}
					else if (text != held) {
						target.Context = Fold(target.Context, $"#{form}: {text}");
						clash = true;
					}
				}
				if (clash) {
					target.Stale = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
				}
				else {
					target.Stale ??= moved.Stale;
				}
				target.Comment = Fold(target.Comment, moved.Comment);
			}
		}

		/// <summary><paramref name="addition"/> onto <paramref name="existing"/> on a new line; an empty or duplicate addition adds nothing.</summary>
		internal static string Fold(string existing, string addition)
			=> addition == "" || existing == addition ? existing
			 : existing == "" ? addition
			 : existing + "\n" + addition;

		/// <summary>
		///     Renames <paramref name="oldKey"/> and every key below it — <c>a</c>
		///     to <c>b</c> also takes <c>a.x</c> to <c>b.x</c>; a prefix with no
		///     key of its own (a group) renames its descendants alone. All or
		///     nothing: when any target already exists, and is not itself being
		///     vacated by this rename, nothing changes and the occupied targets come
		///     back in <paramref name="collisions"/>. Nothing is ever overwritten.
		/// </summary>
		public static bool TryRename(IDictionary<string, WordsKey> keys, string oldKey, string newKey, out HashSet<string> collisions)
			=> TryRename(keys, oldKey, keys, newKey, out collisions);

		/// <summary>
		///     <see cref="TryRename(IDictionary{string, WordsKey}, string, string, out HashSet{string})"/>
		///     from one file's store into another's: the keys leave <paramref name="from"/>
		///     and join <paramref name="to"/> under their new names, on the same terms;
		///     the collisions are names in <paramref name="to"/>.
		/// </summary>
		public static bool TryRename(IDictionary<string, WordsKey> from, string oldKey, IDictionary<string, WordsKey> to, string newKey, out HashSet<string> collisions) {
			collisions = [];
			if (from == to && oldKey == newKey) {
				return true;
			}
			string oldPrefix = oldKey + ".";
			List<(string From, string To)> moves = [];
			foreach (string key in from.Keys) {
				if (key == oldKey) {
					moves.Add((key, newKey));
				}
				else if (key.StartsWith(oldPrefix, StringComparison.Ordinal)) {
					moves.Add((key, newKey + key[oldKey.Length..]));
				}
			}
			HashSet<string> vacated = from == to ? [.. moves.Select(move => move.From)] : [];
			foreach (var (_, target) in moves) {
				if (to.ContainsKey(target) && !vacated.Contains(target)) {
					collisions.Add(target);
				}
			}
			if (collisions.Count > 0) {
				return false;
			}
			//all out, then all in: a rename that shifts keys within their own prefix
			//(a → a.x) must not land on a key still waiting to move
			List<WordsKey> moving = [];
			foreach (var (source, target) in moves) {
				WordsKey key = from[source];
				from.Remove(source);
				key.BlockKey = target;
				moving.Add(key);
			}
			foreach (WordsKey key in moving) {
				to.Add(key.BlockKey, key);
			}
			return true;
		}

		/// <summary>
		///     Moves <paramref name="key"/> (and everything below it) under
		///     <paramref name="newParent"/>, keeping its last segment — marker and
		///     all; an empty parent is the top of the file. Same all-or-nothing
		///     contract as <see cref="TryRename(IDictionary{string, WordsKey}, string, string, out HashSet{string})"/>.
		/// </summary>
		public static bool TryMove(IDictionary<string, WordsKey> keys, string key, string newParent, out HashSet<string> collisions)
			=> TryRename(keys, key, KeyTree.Join(newParent, LastSegment(key)), out collisions);

		/// <summary>The text after the last dot: <c>group.$key</c> gives <c>$key</c>, and a name without one is its own.</summary>
		public static string LastSegment(string key) => key[(key.LastIndexOf('.') + 1)..];

		/// <summary>
		///     Adds or strips the constant marker on the last segment only:
		///     <c>view.key</c> ↔ <c>view.$key</c>.
		/// </summary>
		public static string SetConstantMarker(string key, bool isConstant) {
			int start = key.LastIndexOf('.') + 1;
			string name = key[start..].TrimStart('$');
			return key[..start] + (isConstant ? "$" + name : name);
		}

		/// <summary>
		///     Makes the key at <paramref name="blockKey"/> a constant, or a regular
		///     key again: it moves to its marked (or unmarked) name, descendants
		///     following as in <see cref="TryRename(IDictionary{string, WordsKey}, string, string, out HashSet{string})"/>, and its
		///     <see cref="WordsKey.IsConstant"/> follows. Translations are kept
		///     unless <paramref name="clearEntries"/> — a constant reads the same in
		///     every language, so a caller normally asks before discarding them.
		///     Returns the key's new block key; <see langword="null"/> when there is
		///     no such key or the marked name is already taken.
		/// </summary>
		public static string? SetConstant(IDictionary<string, WordsKey> keys, string blockKey, bool isConstant, bool clearEntries = false) {
			if (!keys.TryGetValue(blockKey, out var key)) {
				return null;
			}
			string marked = SetConstantMarker(blockKey, isConstant);
			if (!TryRename(keys, blockKey, marked, out _)) {
				return null;
			}
			key.IsConstant = isConstant;
			if (clearEntries) {
				foreach (string code in key.Entries.Keys.ToArray()) {
					key.Entries[code] = new WordsEntry();
				}
			}
			return marked;
		}

		/// <summary>
		///     Try <paramref name="values"/> on <paramref name="text"/> (a rendered
		///     value) exactly the way a host app formats it: numbered parameters
		///     (<c>0</c>, <c>1</c>…) fill the positional slots, the rest go by name
		///     through <see cref="Words.FormatByName(IFormatProvider?, string, object?, object?[])"/>, which reads a dictionary by name.
		///     An item with no value shows marked, <c>#{3}#</c>, a malformed brace as
		///     written, each with a warning, and a format its value rejects throws
		///     <see cref="FormatException"/>: what the host would see.
		/// </summary>
		/// <param name="text">The template.</param>
		/// <param name="values">What the code would pass, by parameter name (<see cref="ReadInputs"/>).</param>
		/// <param name="provider">The culture to format in.</param>
		public static string FormatSample(string text, IReadOnlyDictionary<string, object?> values, IFormatProvider? provider = null) {
			var (named, positional) = Arguments(values);
			return Words.FormatByName(provider, text, named, positional);
		}

		/// <summary>
		///     The same, looked up in <paramref name="words"/> by <paramref name="key"/>,
		///     so a plural selector (<c>{0#word}</c>) picks its form by the values, in
		///     the dictionary's language, as a host app's <c>Format</c> would.
		/// </summary>
		public static string FormatSample(IWords words, string key, IReadOnlyDictionary<string, object?> values, IFormatProvider? provider = null) {
			var (named, positional) = Arguments(values);
			return words.FormatByName(provider, key, named, positional);
		}

		/// <summary>
		///     The values an editor's inputs stand for (editor SPEC: Parameters → The
		///     inputs), each read as its parameter's type. An input left empty stands
		///     as its placeholder written out, <c>{0}</c>, so the text still formats and
		///     shows what is left to fill; so does one its type cannot read, whose
		///     complaint comes back beside the values.
		/// </summary>
		/// <param name="inputs">What was typed, by parameter name, with the type it is read as (<see cref="ParameterUse.Slots"/>).</param>
		public static (Dictionary<string, object?> Values, IReadOnlyList<string> Complaints) ReadInputs(IEnumerable<(string Name, WordsParameterType Type, string Input)> inputs) {
			var values = new Dictionary<string, object?>();
			List<string> complaints = [];
			foreach (var (name, type, input) in inputs) {
				object value = FoundParameters.Placeholder(name);
				if (input != "") {
					try {
						value = type.Read(name, input);
					}
					catch (FormatException ex) {
						complaints.Add(ex.Message);
					}
				}
				values[name] = value;
			}
			return (values, complaints);
		}

		//a describable's slots beside its key, which are the member's and no member
		private static readonly HashSet<string> slotSuffixes = ["desc", "sub", "tooltip"];

		/// <summary>
		///     The members an <c>enum</c> parameter offers (editor SPEC: Parameters →
		///     Types): the keys directly under <paramref name="prefix"/>, found as a
		///     reference finds one, through every store in <paramref name="stores"/>,
		///     each by its last segment, once, in the order the files hold them. A key
		///     beside a member, its <c>.tooltip</c> and kin, is the member's own and
		///     lies deeper; the prefix's own slots and its constants are no members.
		/// </summary>
		/// <param name="stores">Each loaded file's keys, in tree order.</param>
		/// <param name="prefix">The prefix, as the type names it: <c>enums.brew</c>.</param>
		public static IReadOnlyList<string> MembersUnder(IEnumerable<IReadOnlyDictionary<string, WordsKey>> stores, string prefix) {
			List<string> members = [];
			string under = prefix + ".";
			foreach (var store in stores) {
				foreach (string name in store.Keys) {
					if (name.StartsWith(under, StringComparison.Ordinal) && name[under.Length..] is var member
							&& WordsParser.IsKeySegment(member) && !member.StartsWith('$') && !slotSuffixes.Contains(member) && !members.Contains(member)) {
						members.Add(member);
					}
				}
			}
			return members;
		}

		//the values as a host app would hand them: numbered ones in their slots, the rest by name
		private static (Dictionary<string, object?> Named, object?[] Positional) Arguments(IReadOnlyDictionary<string, object?> values) {
			var named = new Dictionary<string, object?>();
			var positional = new List<object?>();
			foreach (var (name, value) in values) {
				if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) {
					while (positional.Count <= index) {
						positional.Add(null);
					}
					positional[index] = value;
				}
				else {
					named[name] = value;
				}
			}
			return (named, [.. positional]);
		}

		/// <summary>
		///     The culture a language code formats in — for a translation preview in
		///     the shape its readers will see. A code the platform does not know (a
		///     made-up one, or a bare <c>!</c>-declared library code) formats invariant
		///     rather than not at all; so does <see langword="null"/>, the default text's
		///     culture.
		/// </summary>
		public static CultureInfo CultureFor(string? languageCode) {
			if (languageCode is null) {
				return CultureInfo.InvariantCulture;
			}
			try {
				return CultureInfo.GetCultureInfo(languageCode, predefinedOnly: true);
			}
			catch (CultureNotFoundException) {
				return CultureInfo.InvariantCulture;
			}
		}
	}
}
