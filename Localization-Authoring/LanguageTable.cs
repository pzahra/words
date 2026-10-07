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
		///     The language the session's defaults are written in: the first file's that
		///     declares one (<see cref="WordsFile.DefaultLanguage"/>), or <see langword="null"/>.
		///     Setting it declares it in every file, as a manager edits the session.
		/// </summary>
		public string? DefaultLanguage {
			get => session.Files.Select(file => file.DefaultLanguage).FirstOrDefault(code => code is not null);
			set {
				foreach (WordsFile file in session.Files) {
					file.DefaultLanguage = value;
				}
			}
		}

		/// <summary>
		///     The file's own table, as it writes it: its declared codes, in its
		///     order, carrying its own labels (<see cref="WordsFile.Labels"/>) — a
		///     library's <c>!</c>, and an exonym only where it has one — or the
		///     session's for a code it has no label for. A file declaring nothing
		///     writes no table.
		/// </summary>
		public IReadOnlyList<LanguageEntry> For(WordsFile file)
			=> [.. file.Languages.Select(code => file.Labels.GetValueOrDefault(code) ?? Find(code)).OfType<LanguageEntry>()];

		/// <summary>The session's entries for the codes <paramref name="file"/> declares, in its order: what a list of its languages shows.</summary>
		public IReadOnlyList<LanguageEntry> Declared(WordsFile file)
			=> [.. file.Languages.Select(Find).OfType<LanguageEntry>()];

		//a freshly parsed file's languages join the union: the first file's table
		//replaces the placeholder default; a real label upgrades a !code placeholder;
		//an exonym fills in where the union had none
		internal void Absorb(ILoadedWords loaded, bool firstFile) {
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
					if (language.EnglishName != "") {
						known.EnglishName = language.EnglishName;
					}
				}
				else if (known.EnglishName == "") {
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
					file.Labels[language.Code] = new LanguageEntry(language);
				}
			}
			Backfill();
			return true;
		}

		/// <summary>
		///     Removes a language and its entries from every key and every file's
		///     table, labels and settings references, and as any file's default
		///     language. The last language stays: a session always has one.
		/// </summary>
		public bool Remove(string code) {
			LanguageEntry? known = Find(code);
			if (known is null || Known.Count <= 1) {
				return false;
			}
			Known.Remove(known);
			foreach (WordsFile file in session.Files) {
				file.Languages.Remove(code);
				file.Labels.Remove(code);
				file.LanguageSettings.Remove(code);
				if (file.DefaultLanguage == code) {
					file.DefaultLanguage = null;
				}
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
		///     ones park in context, stale-marked) and every file's table, labels,
		///     settings reference and default language follow. Re-coding onto a
		///     language that already exists absorbs into it, and a file declaring both
		///     keeps the target's label and reference. Each file's label takes what
		///     the replacement changes — the <c>!</c>, the endonym, the exonym — and
		///     keeps what it leaves alone. Returns the entry now standing for the language.
		/// </summary>
		public LanguageEntry Rename(string code, LanguageEntry replacement) {
			LanguageEntry edited = Find(code) ?? throw new ArgumentException($"no language '{code}'", nameof(code));
			if (replacement.Code != code) {
				WordsOperations.Shift(session.Keys.Values, code, replacement.Code);
			}
			foreach (WordsFile file in session.Files) {
				if (file.DefaultLanguage == code) {
					file.DefaultLanguage = replacement.Code;
				}
				int i = file.Languages.IndexOf(code);
				if (i < 0) {
					continue;
				}
				LanguageEntry label = Relabeled(file.Labels.GetValueOrDefault(code) ?? edited, edited, replacement);
				if (replacement.Code == code) {
					file.Labels[code] = label;
					continue;
				}
				file.Labels.Remove(code);
				bool hasSettings = file.LanguageSettings.Remove(code, out string? path);
				if (file.Languages.Contains(replacement.Code)) {
					file.Languages.RemoveAt(i);
					if (hasSettings) {
						file.LanguageSettings.TryAdd(replacement.Code, path!);
					}
					continue;
				}
				file.Languages[i] = replacement.Code;
				file.Labels[replacement.Code] = label;
				if (hasSettings) {
					file.LanguageSettings[replacement.Code] = path!;
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

		//a file's label after a relabel from before to after: what the relabel changed
		//reaches the file, and what it left alone stays the file's own — a library
		//keeps its !, a file without an exonym gains none from a recode
		private static LanguageEntry Relabeled(LanguageEntry own, LanguageEntry before, LanguageEntry after) {
			static bool Unlisted(LanguageEntry label) => label.NativeName.StartsWith('!');
			static string Bare(LanguageEntry label) => label.NativeName.TrimStart('!');
			bool unlisted = Unlisted(after) != Unlisted(before) ? Unlisted(after) : Unlisted(own);
			string name = Bare(after) != Bare(before) ? Bare(after) : Bare(own);
			string exonym = after.EnglishName != before.EnglishName ? after.EnglishName : own.EnglishName;
			return new LanguageEntry(after.Code, (unlisted ? "!" : "") + name) { EnglishName = exonym };
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
