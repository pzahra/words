using System.Collections.ObjectModel;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The operations on one file's language table: the <see cref="LanguageTable"/>'s
	///     own, or a recorder's standing in front of them, as an editor's undo does.
	/// </summary>
	public interface ILanguageEdits {
		/// <inheritdoc cref="LanguageTable.Add"/>
		bool Add(WordsFile file, LanguageEntry language);
		/// <inheritdoc cref="LanguageTable.Remove"/>
		bool Remove(WordsFile file, string code);
		/// <inheritdoc cref="LanguageTable.Rename"/>
		LanguageEntry Rename(WordsFile file, string code, LanguageEntry replacement);
		/// <inheritdoc cref="LanguageTable.Reorder"/>
		void Reorder(WordsFile file, int from, int to);
		/// <inheritdoc cref="LanguageTable.Declare"/>
		void Declare(WordsFile file, string? code);
	}

	/// <summary>One language of a file's table as a manager wants it (<see cref="LanguageTable.Apply"/>).</summary>
	/// <param name="Origin">The file's label it comes from, as <see cref="LanguageTable.For"/> read it; <see langword="null"/> for one added.</param>
	/// <param name="Label">The label it is to have, its code as the file writes it.</param>
	/// <param name="IsDefault">Whether the default is written in it.</param>
	public sealed record PlannedLanguage(LanguageEntry? Origin, LanguageEntry Label, bool IsDefault = false) {
		//whether applying it changes the file: new, or no longer as its origin reads
		internal bool Changes => Origin is null || Label.Code != Origin.Code || Label.NativeName != Origin.NativeName || Label.EnglishName != Origin.EnglishName;
	}

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
	public sealed class LanguageTable : ILanguageEdits {
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
				if (Find(language.Code) is { } known) {
					Take(known, language);
				}
				else {
					Known.Add(language);
				}
			}
			if (Known.Count == 0) {
				//a file with keys and no labels still needs a language to edit in
				Known.Add(Default());
			}
			Backfill();
		}

		//a later file's label for a language the union names already: a real label
		//upgrades a !code placeholder, and a listed one an unlisted; an exonym fills in
		//where there was none
		private static void Take(LanguageEntry known, LanguageEntry language) {
			if ((known.IsPlaceholder && !language.IsPlaceholder) || (Unlisted(known) && !Unlisted(language))) {
				known.NativeName = language.NativeName;
				if (language.EnglishName != "") {
					known.EnglishName = language.EnglishName;
				}
			}
			else if (known.EnglishName == "") {
				known.EnglishName = language.EnglishName;
			}
		}

		//the invariant: every key, every known language
		private void Backfill() {
			foreach (WordsKey key in session.AllKeys) {
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
				throw new ArgumentException($"{file.Name} declares no language '{code}'", nameof(code));
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

		/// <summary>Declares the language <paramref name="file"/>'s default is written in, or that it says none (<see cref="WordsFile.DefaultLanguage"/>).</summary>
		public void Declare(WordsFile file, string? code) => file.DefaultLanguage = code;

		/// <summary>
		///     Brings <paramref name="file"/>'s table to <paramref name="rows"/>, a
		///     language manager's list (the editor's SPEC: Languages): each row keeps,
		///     re-codes or relabels the language it comes from, or adds one, a language
		///     no row comes from goes, the default is declared in the ticked row's when
		///     the tick moved, and the table takes the rows' order. Every operation goes
		///     through <paramref name="edits"/>, the table itself unless a recorder
		///     stands in front of it, in an order that refuses nothing it need not:
		///     additions whose code is free first, so the last-language rule never
		///     refuses a removal; removals next, freeing codes, one whose code a new row
		///     takes parked on a throwaway code first, so the new row comes in before it
		///     goes; then the re-codes, one whose code is still taken waiting for the
		///     re-code that frees it, a cycle of swaps parking one language on a
		///     throwaway code; then the additions that waited for a code; then the
		///     default's language, every code being final; then the order.
		/// </summary>
		/// <param name="file">The file whose table changes; no other file's does.</param>
		/// <param name="rows">The languages it is to have, in order, their codes unique.</param>
		/// <param name="edits">What makes the changes; <see langword="null"/> for the table.</param>
		/// <returns>The codes the table refused, a file's last language or a code that stayed taken, for the caller to tell.</returns>
		public IReadOnlyList<string> Apply(WordsFile file, IReadOnlyList<PlannedLanguage> rows, ILanguageEdits? edits = null) {
			edits ??= this;
			List<string> refused = [];
			//the tick as the file has it, so the default is declared only when it moved
			PlannedLanguage? declared = file.DefaultLanguage is { } language ? rows.FirstOrDefault(row => row.Origin?.Code == language) : null;
			PlannedLanguage? ticked = rows.FirstOrDefault(row => row.IsDefault);
			List<LanguageEntry> gone = [.. For(file).Where(label => rows.All(row => row.Origin?.Code != label.Code))];
			List<PlannedLanguage> additions = [.. rows.Where(row => row.Origin is null)];
			AddFree();
			foreach (LanguageEntry entry in gone) {
				string code = entry.Code;
				if (additions.Any(row => row.Label.Code == code)) {
					code = Parking();
					edits.Rename(file, entry.Code, new LanguageEntry(code, entry.NativeName) { EnglishName = entry.EnglishName });
					AddFree();
				}
				if (!edits.Remove(file, code)) {
					refused.Add(entry.Code);
				}
			}
			List<PlannedLanguage> renames = [.. rows.Where(row => row.Origin is not null && row.Changes)];
			Dictionary<PlannedLanguage, string> current = renames.ToDictionary(row => row, row => row.Origin!.Code);
			while (renames.Count > 0) {
				PlannedLanguage next = renames.FirstOrDefault(row => row.Label.Code == current[row] || !file.Languages.Contains(row.Label.Code)) ?? renames[0];
				if (next.Label.Code != current[next] && file.Languages.Contains(next.Label.Code)) {
					LanguageEntry blocking = For(file).First(label => label.Code == next.Label.Code);
					PlannedLanguage blocked = renames.First(row => current[row] == blocking.Code);
					current[blocked] = Parking();
					edits.Rename(file, blocking.Code, new LanguageEntry(current[blocked], blocking.NativeName) { EnglishName = blocking.EnglishName });
				}
				edits.Rename(file, current[next], next.Label);
				renames.Remove(next);
			}
			AddFree();
			refused.AddRange(additions.Select(row => row.Label.Code));
			if (!ReferenceEquals(ticked, declared)) {
				edits.Declare(file, ticked?.Label.Code);
			}
			int place = 0;
			foreach (PlannedLanguage row in rows) {
				int at = file.Languages.IndexOf(row.Label.Code);
				if (at >= 0) {
					edits.Reorder(file, at, place++);
				}
			}
			return refused;

			//adds the rows whose code the file does not declare yet, and drops them from the list
			void AddFree() {
				foreach (PlannedLanguage row in additions.Where(row => !file.Languages.Contains(row.Label.Code)).ToList()) {
					if (edits.Add(file, row.Label)) {
						additions.Remove(row);
					}
				}
			}

			static string Parking() => $"zz-{Guid.NewGuid():N}";
		}

		/// <summary>
		///     What <paramref name="library"/> lacks that a host offers: each code a
		///     file that is no library lists (no <c>!</c>) which the library neither
		///     declares, nor falls back to (<see cref="LanguageCode.Chain"/>: a library's
		///     <c>en</c> covers a host's <c>en-AU</c>), nor writes its default in, with
		///     the files that list it, by the names they show (<see cref="WordsSession.ShownName"/>). An app offering the language reads
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
			//a host offers a language it lists, not one it hides behind a !
			static bool Lists(WordsFile host, string code) => host.Labels.GetValueOrDefault(code) is { } label && !Unlisted(label);
			List<WordsFile> hosts = [.. session.Files.Where(file => !file.IsLibrary)];
			return [.. hosts
				.SelectMany(host => host.Languages.Where(code => Lists(host, code)))
				.Distinct()
				.Where(code => !Covers(code))
				.Select(code => (code, (IReadOnlyList<string>)[.. hosts.Where(host => Lists(host, code)).Select(session.ShownName)]))];
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
					foreach (WordsKey key in session.AllKeys) {
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
			|| session.AllKeys.Any(key => key.Entries.TryGetValue(code, out WordsEntry? entry) && !entry.IsEmpty());

		//the union's name for a language is what a load of the files there are now would
		//give it, file by file in their order by Absorb's rule (Take), so a dropdown names
		//what a host offers, and a language the files only use reads !code, as it loads
		private void Relist(LanguageEntry known) {
			LanguageEntry? shown = null;
			foreach (LanguageEntry label in session.Files.SelectMany(AsLoaded).Where(label => label.Code == known.Code)) {
				if (shown is null) {
					shown = new LanguageEntry(label);
				}
				else {
					Take(shown, label);
				}
			}
			if (shown is not null) {
				known.NativeName = shown.NativeName;
				known.EnglishName = shown.EnglishName;
			}
		}

		//the labels a load of file alone reads: its table's, in its order, then a !code
		//placeholder for each language its keys have words in and its table leaves out
		private IEnumerable<LanguageEntry> AsLoaded(WordsFile file) {
			foreach (string code in file.Languages) {
				yield return file.Labels.GetValueOrDefault(code) ?? new LanguageEntry(code);
			}
			foreach (string code in session.KeysOf(file).SelectMany(key => key.Entries.Where(entry => !entry.Value.IsEmpty()).Select(entry => entry.Key)).Distinct()) {
				if (!file.Languages.Contains(code)) {
					yield return new LanguageEntry(code);
				}
			}
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
		//remaining key has words in, leaves too (with its empty entries); the rest
		//read the names the remaining files give them, in their order, as a load of
		//those files alone would
		internal void Prune() {
			HashSet<string> keep = [.. session.Files.SelectMany(file => file.Languages)];
			foreach (WordsKey key in session.AllKeys) {
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
				foreach (WordsKey key in session.AllKeys) {
					key.Entries.Remove(known.Code);
				}
			}
			foreach (LanguageEntry known in Known) {
				Relist(known);
			}
			if (Known.Count == 0) {
				Known.Add(Default());
			}
			Resort();
		}
	}
}
