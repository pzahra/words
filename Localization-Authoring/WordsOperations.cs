using System.Globalization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     Document-level operations over key dictionaries (keys prefixed with
	///     their file's label, e.g. <c>File.group.key</c>). These do the
	///     processing; the editor's ViewModels only gather the intent.
	/// </summary>
	public static class WordsOperations {

		/// <summary>All keys belonging to <paramref name="fileLabel"/>.</summary>
		public static Dictionary<string, WordsKey> KeysOf(IReadOnlyDictionary<string, WordsKey> allKeys, string fileLabel)
			=> allKeys.Where(pair => pair.Key.StartsWith(fileLabel + ".", StringComparison.Ordinal))
				.ToDictionary(pair => pair.Key, pair => pair.Value);

		/// <summary>
		///     True when every set holds the same keys once the file prefix is
		///     stripped; the odd ones out come back in <paramref name="conflicts"/>.
		/// </summary>
		public static bool HaveSameKeys(
				IEnumerable<IReadOnlyDictionary<string, WordsKey>> keySets,
				out HashSet<string> conflicts) {
			HashSet<string>? firstKeySet = null;
			bool haveSameKeys = true;
			conflicts = [];

			foreach (var keySet in keySets) {
				HashSet<string> currentKeySet = [];
				foreach (var key in keySet.Keys) {
					currentKeySet.Add(key[(key.IndexOf('.') + 1)..]);
				}
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
		///     from <paramref name="baseFile"/> and each language's entries from
		///     the file mapped to it in <paramref name="languageSources"/>.
		///     Returns <see langword="null"/> when the files disagree on their key
		///     sets; the disagreements come back in <paramref name="conflicts"/>.
		/// </summary>
		/// <param name="allKeys">Every loaded key, file-prefixed.</param>
		/// <param name="baseFile">The label of the file providing the keys and unlocalised fields.</param>
		/// <param name="languageSources">Language code to the label of the file providing that language's entries.</param>
		/// <param name="mergedFile">The label the merged keys are prefixed with.</param>
		/// <param name="conflicts">Key suffixes the involved files disagree on.</param>
		public static Dictionary<string, WordsKey>? Merge(
				IReadOnlyDictionary<string, WordsKey> allKeys,
				string baseFile,
				IReadOnlyDictionary<string, string> languageSources,
				string mergedFile,
				out HashSet<string> conflicts) {
			Dictionary<string, WordsKey> merged = [];
			foreach (var key in KeysOf(allKeys, baseFile).Values) {
				var copy = new WordsKey(key) {
					BlockKey = mergedFile + key.BlockKey[key.BlockKey.IndexOf('.')..]
				};
				merged[copy.BlockKey] = copy;
			}
			var sources = languageSources.ToDictionary(
				pair => pair.Key,
				pair => KeysOf(allKeys, pair.Value));
			if (!HaveSameKeys([merged, .. sources.Values], out conflicts)) {
				return null;
			}
			foreach (var (language, keys) in sources) {
				foreach (var key in keys.Values) {
					if (!key.Entries.TryGetValue(language, out var entry)) {
						continue;
					}
					string suffix = key.BlockKey[(key.BlockKey.IndexOf('.') + 1)..];
					merged[$"{mergedFile}.{suffix}"].Entries[language] = new WordsEntry(entry);
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
		/// <param name="allKeys">Every loaded key, file-prefixed.</param>
		/// <param name="sourceFile">The label of the file being split.</param>
		/// <param name="languageCode">The one language the split file carries.</param>
		/// <param name="splitFile">The label the split keys are prefixed with.</param>
		public static Dictionary<string, WordsKey> Split(
				IReadOnlyDictionary<string, WordsKey> allKeys,
				string sourceFile,
				string languageCode,
				string splitFile) {
			Dictionary<string, WordsKey> split = [];
			foreach (var key in KeysOf(allKeys, sourceFile).Values) {
				var copy = new WordsKey(key) {
					BlockKey = splitFile + key.BlockKey[key.BlockKey.IndexOf('.')..]
				};
				foreach (var code in copy.Entries.Keys.Where(code => code != languageCode).ToArray()) {
					copy.Entries.Remove(code);
				}
				split[copy.BlockKey] = copy;
			}
			return split;
		}

		/// <summary>
		///     Re-codes a language: every key's entry moves from
		///     <paramref name="fromCode"/> to <paramref name="toCode"/>. Where both
		///     codes hold a value the target's wins, the displaced source value
		///     parks in the target's <c>context</c> field where the translator can
		///     copy from it, and the entry is stale-marked so the review filter
		///     surfaces the collision.
		/// </summary>
		public static void Shift(IEnumerable<WordsKey> keys, string fromCode, string toCode) {
			foreach (var key in keys) {
				if (!key.Entries.Remove(fromCode, out var moved)) {
					continue;
				}
				if (key.Entries.TryGetValue(toCode, out var target) && target.Value != "") {
					if (moved.Value != "" && moved.Value != target.Value) {
						target.Context = moved.Value;
						target.Stale = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
					}
				}
				else {
					key.Entries[toCode] = moved;
				}
			}
		}

		/// <summary>
		///     Renames <paramref name="oldKey"/> and every key below it — <c>F.a</c>
		///     to <c>F.b</c> also takes <c>F.a.x</c> to <c>F.b.x</c>; a prefix with no
		///     key of its own (a group) renames its descendants alone. All or
		///     nothing: when any target already exists, and is not itself being
		///     vacated by this rename, nothing changes and the occupied targets come
		///     back in <paramref name="collisions"/>. Nothing is ever overwritten.
		/// </summary>
		public static bool TryRename(IDictionary<string, WordsKey> keys, string oldKey, string newKey, out HashSet<string> collisions) {
			collisions = [];
			if (oldKey == newKey) {
				return true;
			}
			string oldPrefix = oldKey + ".";
			List<(string From, string To)> moves = [];
			foreach (string key in keys.Keys) {
				if (key == oldKey) {
					moves.Add((key, newKey));
				}
				else if (key.StartsWith(oldPrefix, StringComparison.Ordinal)) {
					moves.Add((key, newKey + key[oldKey.Length..]));
				}
			}
			HashSet<string> vacated = [.. moves.Select(move => move.From)];
			foreach (var (_, to) in moves) {
				if (keys.ContainsKey(to) && !vacated.Contains(to)) {
					collisions.Add(to);
				}
			}
			if (collisions.Count > 0) {
				return false;
			}
			//all out, then all in: a rename that shifts keys within their own prefix
			//(a → a.x) must not land on a key still waiting to move
			List<WordsKey> moving = [];
			foreach (var (from, to) in moves) {
				WordsKey key = keys[from];
				keys.Remove(from);
				key.BlockKey = to;
				moving.Add(key);
			}
			foreach (WordsKey key in moving) {
				keys.Add(key.BlockKey, key);
			}
			return true;
		}

		/// <summary>
		///     Moves <paramref name="key"/> (and everything below it) under
		///     <paramref name="newParent"/>, keeping its last segment — marker and
		///     all. Same all-or-nothing contract as <see cref="TryRename"/>.
		/// </summary>
		public static bool TryMove(IDictionary<string, WordsKey> keys, string key, string newParent, out HashSet<string> collisions)
			=> TryRename(keys, key, $"{newParent}.{LastSegment(key)}", out collisions);

		/// <summary>The text after the last dot: <c>F.group.$key</c> gives <c>$key</c>.</summary>
		public static string LastSegment(string key) => key[(key.LastIndexOf('.') + 1)..];

		/// <summary>
		///     Adds or strips the constant marker on the last segment only:
		///     <c>F.view.key</c> ↔ <c>F.view.$key</c>.
		/// </summary>
		public static string SetConstantMarker(string key, bool isConstant) {
			int start = key.LastIndexOf('.') + 1;
			string name = key[start..].TrimStart('$');
			return key[..start] + (isConstant ? "$" + name : name);
		}

		/// <summary>
		///     Makes the key at <paramref name="blockKey"/> a constant, or a regular
		///     key again: it moves to its marked (or unmarked) name, descendants
		///     following as in <see cref="TryRename"/>, and its
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
		///     Try the key's sample parameters on <paramref name="text"/> (its
		///     rendered value) exactly the way a host app formats it: numbered
		///     parameters (<c>0</c>, <c>1</c>…) fill the positional slots, the rest go
		///     by name through <see cref="Words.FormatByName(IFormatProvider?, string, IReadOnlyDictionary{string, object?}, object?[])"/>.
		///     A sample that doesn't parse as its declared type throws
		///     <see cref="FormatException"/>, as does a template
		///     <see cref="string.Format(string, object[])"/> rejects — the same
		///     failure the host would see.
		/// </summary>
		public static string FormatSample(WordsKey key, string text, IFormatProvider? provider = null) {
			var named = new Dictionary<string, object?>();
			var positional = new List<object?>();
			foreach (WordsParameter parameter in key.Parameters) {
				object value = parameter.ToObject();
				if (int.TryParse(parameter.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) {
					while (positional.Count <= index) {
						positional.Add(null);
					}
					positional[index] = value;
				}
				else {
					named[parameter.Key] = value;
				}
			}
			return Words.FormatByName(provider, text, named, [.. positional]);
		}
	}
}
