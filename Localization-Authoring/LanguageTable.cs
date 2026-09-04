using System.Collections.ObjectModel;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The languages of a <see cref="WordsSession"/>: <see cref="Known"/> is the
	///     union every loaded file contributes to (what a language dropdown and a
	///     language manager show), while each file keeps its own declared codes in
	///     <see cref="WordsFile.Languages"/> and writes back only those. Every change
	///     here keeps the session's invariant — each key has an entry for each known
	///     language — and applies to every file's table, since a manager edits the
	///     session, not one file.
	/// </summary>
	public sealed class LanguageTable {
		private readonly WordsSession session;

		/// <summary>The session union, in dropdown order. Never empty: a session with no languages shows <see cref="Default"/>.</summary>
		public ObservableCollection<LanguageEntry> Known { get; } = [];

		/// <summary>The language an empty session offers, so there is always one to edit in.</summary>
		public static LanguageEntry Default() => new("en", "!English (common)");

		internal LanguageTable(WordsSession session) {
			this.session = session;
			Reset();
		}

		internal void Reset() {
			Known.Clear();
			Known.Add(Default());
		}

		/// <summary>The known language with <paramref name="code"/>, if any.</summary>
		public LanguageEntry? Find(string code) => Known.FirstOrDefault(language => language.Code == code);

		/// <summary>
		///     The file's own table: its declared codes, in its order, carrying the
		///     session's current labels. A file declaring nothing writes no table.
		/// </summary>
		public IReadOnlyList<LanguageEntry> For(WordsFile file)
			=> [.. file.Languages.Select(Find).OfType<LanguageEntry>()];

		//a freshly parsed file's languages join the union: the first file's table
		//replaces the placeholder default; a real label upgrades a !code placeholder;
		//an English name fills in where the union only had the native one
		internal void Absorb(WordsParserToLocalizationProvider loaded, bool firstFile) {
			if (firstFile) {
				Known.Clear();
			}
			foreach (LanguageEntry language in loaded.KnownLanguages.Values) {
				LanguageEntry? known = Find(language.Code);
				if (known is null) {
					Known.Add(language);
				}
				else if (known.IsPlaceholder && !language.IsPlaceholder) {
					known.NativeName = language.NativeName;
					known.EnglishName = language.EnglishName;
				}
				else if (language.EnglishName != language.NativeName && known.EnglishName == known.NativeName) {
					known.EnglishName = language.EnglishName;
				}
			}
			if (Known.Count == 0) {
				//a file with keys and no labels still needs a language to edit in
				Known.Add(Default());
			}
			Backfill();
		}

		//the invariant: every key, every known language
		private void Backfill() {
			foreach (WordsKey key in session.Keys.Values) {
				foreach (LanguageEntry language in Known) {
					key.Entries.TryAdd(language.Code, new WordsEntry());
				}
			}
		}

		/// <summary>
		///     A new language for the session: it joins <see cref="Known"/>, every
		///     file's table and every key. Nothing happens when its code is taken.
		/// </summary>
		public bool Add(LanguageEntry language) {
			if (Find(language.Code) is not null) {
				return false;
			}
			Known.Add(language);
			foreach (WordsFile file in session.Files) {
				if (!file.Languages.Contains(language.Code)) {
					file.Languages.Add(language.Code);
				}
			}
			Backfill();
			return true;
		}

		/// <summary>
		///     Removes a language and its entries from every key and every file's
		///     table. The last language stays: a session always has one.
		/// </summary>
		public bool Remove(string code) {
			LanguageEntry? known = Find(code);
			if (known is null || Known.Count <= 1) {
				return false;
			}
			Known.Remove(known);
			foreach (WordsFile file in session.Files) {
				file.Languages.Remove(code);
			}
			foreach (WordsKey key in session.Keys.Values) {
				key.Entries.Remove(code);
			}
			return true;
		}

		/// <summary>
		///     Replaces the language at <paramref name="code"/> with
		///     <paramref name="replacement"/>. A changed code re-codes the entries
		///     (<see cref="WordsOperations.Shift"/>: the target's values win, displaced
		///     ones park in context, stale-marked) and every file's table follows.
		///     Re-coding onto a language that already exists absorbs into it. Returns
		///     the entry now standing for the language.
		/// </summary>
		public LanguageEntry Rename(string code, LanguageEntry replacement) {
			LanguageEntry edited = Find(code) ?? throw new ArgumentException($"no language '{code}'", nameof(code));
			if (replacement.Code != code) {
				WordsOperations.Shift(session.Keys.Values, code, replacement.Code);
				foreach (WordsFile file in session.Files) {
					int i = file.Languages.IndexOf(code);
					if (i < 0) {
						continue;
					}
					if (file.Languages.Contains(replacement.Code)) {
						file.Languages.RemoveAt(i);
					}
					else {
						file.Languages[i] = replacement.Code;
					}
				}
			}
			LanguageEntry? absorbedInto = Known.FirstOrDefault(known => known.Code == replacement.Code && known != edited);
			if (absorbedInto is not null) {
				Known.Remove(edited);
				Backfill();
				return absorbedInto;
			}
			Known[Known.IndexOf(edited)] = replacement;
			Backfill();
			return replacement;
		}

		/// <summary>
		///     Moves a language in <see cref="Known"/>, and makes that order every
		///     file's order — so a reorder in a manager reaches the files it writes.
		/// </summary>
		public void Reorder(int from, int to) {
			if (from == to) {
				return;
			}
			Known.Move(from, to);
			var order = Known.Select(language => language.Code).ToList();
			foreach (WordsFile file in session.Files) {
				file.Languages.Sort((a, b) => order.IndexOf(a).CompareTo(order.IndexOf(b)));
			}
		}

		//after a file leaves: a language no remaining file declares, and no
		//remaining key has words in, leaves too (with its empty entries)
		internal void Prune() {
			HashSet<string> keep = [.. session.Files.SelectMany(file => file.Languages)];
			foreach (WordsKey key in session.Keys.Values) {
				foreach (var (code, entry) in key.Entries) {
					if (!entry.IsEmpty()) {
						keep.Add(code);
					}
				}
			}
			foreach (LanguageEntry known in Known.ToArray()) {
				if (keep.Contains(known.Code)) {
					continue;
				}
				Known.Remove(known);
				foreach (WordsKey key in session.Keys.Values) {
					key.Entries.Remove(known.Code);
				}
			}
			if (Known.Count == 0) {
				Known.Add(Default());
			}
		}
	}
}
