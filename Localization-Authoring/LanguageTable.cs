using System.Collections.ObjectModel;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The languages of a <see cref="WordsSession"/>. Each file keeps its own
	///     table, its codes in <see cref="WordsFile.Languages"/> and its labels in
	///     <see cref="WordsFile.Labels"/>, and writes back only those; every change
	///     here is to one file's, since a library declares what its hosts need of it
	///     and no more, and its <c>!</c> is its own. <see cref="Known"/> is the union
	///     the files contribute to, what a language dropdown shows, in the files'
	///     order. Every change keeps the session's invariant: each key has an entry
	///     for each known language.
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
		///     Each file declares its own.
		/// </summary>
		public string? DefaultLanguage => session.Files.Select(file => file.DefaultLanguage).FirstOrDefault(code => code is not null);

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
		//replaces the placeholder default; a real label upgrades a !code placeholder,
		//and a listed one an unlisted, so a host names what a library hides;
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
				else if ((known.IsPlaceholder && !language.IsPlaceholder) || (Unlisted(known) && !Unlisted(language))) {
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
		///     <paramref name="file"/> declares a language, labelled as given: it
		///     joins the file's table, <see cref="Known"/> when it is new there, and
		///     every key. Nothing happens when the file declares the code already.
		/// </summary>
		public bool Add(WordsFile file, LanguageEntry language) {
			if (file.Languages.Contains(language.Code)) {
				return false;
			}
			file.Languages.Add(language.Code);
			file.Labels[language.Code] = new LanguageEntry(language);
			if (Find(language.Code) is null) {
				Known.Add(new LanguageEntry(language));
			}
			Settle(language.Code);
			return true;
		}

		/// <summary>
		///     <paramref name="file"/> stops declaring a language: the code leaves its
		///     table, labels and settings references, and its default's language, and
		///     the file's keys lose their words in it. The session keeps the language
		///     while another file declares it or a key has words in it. A file's last
		///     language stays, so a manager always has one to edit in.
		/// </summary>
		public bool Remove(WordsFile file, string code) {
			if (!file.Languages.Contains(code) || file.Languages.Count <= 1) {
				return false;
			}
			file.Languages.Remove(code);
			file.Labels.Remove(code);
			file.LanguageSettings.Remove(code);
			if (file.DefaultLanguage == code) {
				file.DefaultLanguage = null;
			}
			foreach (WordsKey key in session.KeysOf(file)) {
				if (key.Entries.ContainsKey(code)) {
					key.Entries[code] = new WordsEntry();
				}
			}
			Settle(code);
			return true;
		}

		/// <summary>
		///     Replaces <paramref name="file"/>'s language at <paramref name="code"/>
		///     with <paramref name="replacement"/>, its label as given, the <c>!</c>
		///     included; no other file's label changes. A changed code re-codes the
		///     file's keys' entries (<see cref="WordsOperations.Shift"/>: the target's
		///     values win, displaced ones park in context, stale-marked), and the
		///     file's settings reference and default's language follow. Re-coding onto
		///     a code the file declares already absorbs into it, keeping that one's
		///     label and reference. Another file declaring the old code keeps it.
		///     Returns the session's entry now standing for the language.
		/// </summary>
		public LanguageEntry Rename(WordsFile file, string code, LanguageEntry replacement) {
			int i = file.Languages.IndexOf(code);
			if (i < 0) {
				throw new ArgumentException($"{file.Label} declares no language '{code}'", nameof(code));
			}
			if (replacement.Code == code) {
				file.Labels[code] = new LanguageEntry(replacement);
				Settle(code);
				return Find(code)!;
			}
			WordsOperations.Shift([.. session.KeysOf(file)], code, replacement.Code);
			if (file.DefaultLanguage == code) {
				file.DefaultLanguage = replacement.Code;
			}
			file.Labels.Remove(code);
			bool hasSettings = file.LanguageSettings.Remove(code, out string? path);
			if (file.Languages.Contains(replacement.Code)) {
				file.Languages.RemoveAt(i);
				if (hasSettings) {
					file.LanguageSettings.TryAdd(replacement.Code, path!);
				}
			}
			else {
				file.Languages[i] = replacement.Code;
				file.Labels[replacement.Code] = new LanguageEntry(replacement);
				if (hasSettings) {
					file.LanguageSettings[replacement.Code] = path!;
				}
			}
			if (Find(replacement.Code) is null) {
				Known.Add(new LanguageEntry(replacement));
			}
			Settle(code, replacement.Code);
			return Find(replacement.Code)!;
		}

		/// <summary>Moves a language in <paramref name="file"/>'s table; <see cref="Known"/> follows the files' order.</summary>
		public void Reorder(WordsFile file, int from, int to) {
			if (from == to) {
				return;
			}
			string code = file.Languages[from];
			file.Languages.RemoveAt(from);
			file.Languages.Insert(to, code);
			Resort();
		}

		/// <summary>
		///     What <paramref name="library"/> lacks that a host offers: each code a
		///     file that is no library lists (no <c>!</c>) which the library neither
		///     declares, nor falls back to (<see cref="LanguageCode.Chain"/>: a library's
		///     <c>en</c> covers a host's <c>en-AU</c>), nor writes its default in, with
		///     the labels of the files that list it. An app offering the language reads
		///     the library's default there. Empty for a file that is no library.
		/// </summary>
		public IReadOnlyList<(string Code, IReadOnlyList<string> Hosts)> Lacks(WordsFile library) {
			if (!library.IsLibrary) {
				return [];
			}
			bool Covers(string code)
				=> WordsParser.DefaultSpeaks(library.DefaultLanguage, code)
				|| (LanguageCode.TryParse(code, out var parsed)
					? parsed.Chain.Any(level => library.Languages.Contains(level.ToString()))
					: library.Languages.Contains(code));
			List<WordsFile> hosts = [.. session.Files.Where(file => !file.IsLibrary)];
			return [.. hosts
				.SelectMany(host => host.Languages.Where(code => host.Labels.GetValueOrDefault(code) is { } label && !Unlisted(label)))
				.Distinct()
				.Where(code => !Covers(code))
				.Select(code => (code, (IReadOnlyList<string>)[.. hosts.Where(host => host.Languages.Contains(code)).Select(host => host.Label)]))];
		}

		private static bool Unlisted(LanguageEntry label) => label.NativeName.StartsWith('!');

		//after a file's table changed: each code the change touched leaves the union
		//when no file declares it and no key has words in it, and otherwise reads the
		//name the files give it; the union takes the files' order, and every key an
		//entry for each of it
		private void Settle(params string[] codes) {
			foreach (string code in codes) {
				if (Find(code) is not { } known) {
					continue;
				}
				if (Held(code)) {
					Relist(known);
				}
				else {
					Known.Remove(known);
					foreach (WordsKey key in session.Keys.Values) {
						key.Entries.Remove(code);
					}
				}
			}
			if (Known.Count == 0) {
				Known.Add(Default());
			}
			Resort();
			Backfill();
		}

		private bool Held(string code)
			=> session.Files.Any(file => file.Languages.Contains(code))
			|| session.Keys.Values.Any(key => key.Entries.TryGetValue(code, out WordsEntry? entry) && !entry.IsEmpty());

		//the union's name for a language is a file's label for it, a listed one before
		//one a library hides, so a dropdown names what a host offers; its exonym is the
		//first any of them gives
		private void Relist(LanguageEntry known) {
			List<LanguageEntry> labels = [.. session.Files
				.Where(file => file.Languages.Contains(known.Code))
				.Select(file => file.Labels.GetValueOrDefault(known.Code))
				.OfType<LanguageEntry>()];
			if ((labels.FirstOrDefault(label => !Unlisted(label)) ?? labels.FirstOrDefault()) is not { } shown) {
				return;
			}
			known.NativeName = shown.NativeName;
			known.EnglishName = shown.EnglishName != "" ? shown.EnglishName : labels.Select(label => label.EnglishName).FirstOrDefault(name => name != "") ?? "";
		}

		//the union in the files' order, each file's table in turn, the first file's
		//first; a language no file declares keeps its place after them
		private void Resort() {
			List<string> order = [.. session.Files.SelectMany(file => file.Languages).Distinct()];
			List<LanguageEntry> sorted = [.. Known.OrderBy(known => order.IndexOf(known.Code) is var at and >= 0 ? at : order.Count)];
			for (int i = 0; i < sorted.Count; i++) {
				int at = Known.IndexOf(sorted[i]);
				if (at != i) {
					Known.Move(at, i);
				}
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
