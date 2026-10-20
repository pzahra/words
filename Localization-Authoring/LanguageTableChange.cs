namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A change to one file's language table, its operations made through here so
	///     it can be put back whole either way (the editor's SPEC: Undo → Languages):
	///     the file's table, with the language its default is written in, and its
	///     keys' entries, in the order each key writes its fields, copies for each code
	///     the change touched, as they stood before it and after. Never by replaying
	///     inverses, which can write a key's fields back in another order. What the
	///     session holds for every file at once, its language list and the empty entry
	///     each key keeps for each language in it, is never put back from a copy but
	///     read again from the files (the editor's SPEC: Dirty by file, Each file's
	///     history), so a change stands alone once another file goes. A re-code onto a
	///     code the file declares merges two languages' entries and has no tidy way
	///     back: <see cref="Merged"/> says the change made one.
	/// </summary>
	public sealed class LanguageTableChange : ILanguageEdits {
		private readonly WordsSession session;
		private readonly WordsFile file;
		private readonly FileTable tableBefore;
		private FileTable? tableAfter;
		//each key's languages in its order, which its fields are written in, and each
		//touched code's entries, a key by its name, as they stood before the change first
		//touched it, and after the change
		private readonly Dictionary<string, string[]> ordersBefore;
		private Dictionary<string, string[]> ordersAfter = [];
		private readonly Dictionary<string, Dictionary<string, WordsEntry>> entriesBefore = [];
		private Dictionary<string, Dictionary<string, WordsEntry>> entriesAfter = [];

		//what a file's table is: its codes, its labels, its settings references and its
		//default's language, copied so later edits leave the copy alone
		private sealed record FileTable(string[] Codes, (string Code, LanguageEntry Label)[] Labels, (string Code, string Path)[] Settings, string? Default) {
			public static FileTable Of(WordsFile file) => new(
				[.. file.Languages],
				[.. file.Labels.Select(pair => (pair.Key, new LanguageEntry(pair.Value)))],
				[.. file.LanguageSettings.Select(pair => (pair.Key, pair.Value))],
				file.DefaultLanguage);

			public void PutBack(WordsFile file) {
				file.Languages.Clear();
				file.Languages.AddRange(Codes);
				file.Labels.Clear();
				foreach (var (code, label) in Labels) {
					file.Labels[code] = new LanguageEntry(label);
				}
				file.LanguageSettings.Clear();
				foreach (var (code, path) in Settings) {
					file.LanguageSettings[code] = path;
				}
				file.DefaultLanguage = Default;
			}
		}

		/// <summary>Starts a change to <paramref name="file"/>'s table: the table and its keys' entries as they stand are what putting it back before returns to.</summary>
		public LanguageTableChange(WordsSession session, WordsFile file) {
			this.session = session;
			this.file = file;
			tableBefore = FileTable.Of(file);
			ordersBefore = Orders();
		}

		private LanguageTable Table => session.Languages;
		/// <summary>An operation changed the table.</summary>
		public bool Changed { get; private set; }
		/// <summary>A re-code merged two languages' entries.</summary>
		public bool Merged { get; private set; }
		/// <summary>The file whose table the change changed, once an operation did (the editor's SPEC: Dirty by file); none before.</summary>
		public IReadOnlyCollection<WordsFile> Files => Changed ? [file] : [];

		/// <inheritdoc cref="LanguageTable.Add"/>
		public bool Add(WordsFile file, LanguageEntry language) {
			Mine(file);
			Keep(language.Code);
			return Note(Table.Add(file, language));
		}

		/// <inheritdoc cref="LanguageTable.Remove"/>
		public bool Remove(WordsFile file, string code) {
			Mine(file);
			Keep(code);
			return Note(Table.Remove(file, code));
		}

		/// <inheritdoc cref="LanguageTable.Rename"/>
		public LanguageEntry Rename(WordsFile file, string code, LanguageEntry replacement) {
			Mine(file);
			Keep(code);
			Keep(replacement.Code);
			Merged |= replacement.Code != code && file.Languages.Contains(replacement.Code);
			Note(true);
			return Table.Rename(file, code, replacement);
		}

		/// <inheritdoc cref="LanguageTable.Reorder"/>
		public void Reorder(WordsFile file, int from, int to) {
			Mine(file);
			if (from != to) {
				Table.Reorder(file, from, to);
				Note(true);
			}
		}

		/// <summary>Declares the language <paramref name="file"/>'s default is written in; nothing happens when the file says so already.</summary>
		public void Declare(WordsFile file, string? code) {
			Mine(file);
			if (file.DefaultLanguage != code) {
				Table.Declare(file, code);
				Note(true);
			}
		}

		//the change is the one file's: an operation on another is a mistake
		private void Mine(WordsFile file) {
			if (file != this.file) {
				throw new ArgumentException($"the change is {this.file.Name}'s, not {file.Name}'s", nameof(file));
			}
		}

		//an operation's own answer, a refusal kept a refusal however much changed before it
		private bool Note(bool changed) {
			Changed |= changed;
			return changed;
		}

		//a code's entries as they stand, the first time the change touches it
		private void Keep(string code) => entriesBefore.TryAdd(code, Entries(code));

		private Dictionary<string, WordsEntry> Entries(string code) => session.KeysOf(file)
			.Where(key => key.Entries.ContainsKey(code))
			.ToDictionary(key => key.BlockKey, key => new WordsEntry(key.Entries[code]));

		private Dictionary<string, string[]> Orders() => session.KeysOf(file).ToDictionary(key => key.BlockKey, key => key.Entries.Keys.ToArray());

		/// <summary>The change is done: the table and the touched entries as they now stand are what putting it back after returns to.</summary>
		public void Close() {
			tableAfter = FileTable.Of(file);
			ordersAfter = Orders();
			entriesAfter = entriesBefore.Keys.ToDictionary(code => code, Entries);
		}

		/// <summary>
		///     Puts the table and the touched entries back as they stood <paramref name="before"/>
		///     the change, or after it, and reads the session's list again from the files.
		/// </summary>
		/// <exception cref="InvalidOperationException">After, before the change was closed.</exception>
		public void PutBack(bool before) {
			if (!before && tableAfter is null) {
				throw new InvalidOperationException("the change is not closed: there is no after to put back");
			}
			(before ? tableBefore : tableAfter!).PutBack(file);
			//each key's entries rebuilt in its order then: a touched code's from the copy,
			//any other the one it holds, which the change left alone, or none where the
			//session let the language go since
			var (orders, kept) = before ? (ordersBefore, entriesBefore) : (ordersAfter, entriesAfter);
			foreach (WordsKey key in session.KeysOf(file)) {
				if (!orders.TryGetValue(key.BlockKey, out string[]? codes)) {
					continue;
				}
				List<(string Code, WordsEntry Entry)> entries = [];
				foreach (string code in codes) {
					if (kept.TryGetValue(code, out var copies) && copies.TryGetValue(key.BlockKey, out WordsEntry? copy)) {
						entries.Add((code, new WordsEntry(copy)));
					}
					else if (key.Entries.TryGetValue(code, out WordsEntry? held)) {
						entries.Add((code, held));
					}
				}
				key.Entries.Clear();
				foreach (var (code, entry) in entries) {
					key.Entries[code] = entry;
				}
			}
			Table.Reread([.. tableBefore.Codes, .. tableAfter?.Codes ?? [], .. entriesBefore.Keys]);
		}
	}
}
