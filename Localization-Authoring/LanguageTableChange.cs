namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A change to the session's language tables, its operations made through here
	///     so it can be put back whole either way (the editor's SPEC: Undo →
	///     Languages): every file's table, with the language its default is written
	///     in, the session's language list, its order and names, and every key's
	///     entries, in the order the key writes its fields, copies for each code the
	///     change touched, as they stood before it and after. Never by replaying
	///     inverses, which can write a key's fields back in another order. A re-code
	///     onto a code the file declares merges two languages' entries and has no
	///     tidy way back: <see cref="Merged"/> says the change made one.
	/// </summary>
	public sealed class LanguageTableChange : ILanguageEdits {
		private readonly WordsSession session;
		private readonly Dictionary<WordsFile, FileTable> tablesBefore;
		private Dictionary<WordsFile, FileTable> tablesAfter = [];
		private readonly KnownList knownBefore;
		private KnownList? knownAfter;
		//every key's languages in its order, which its fields are written in, and each
		//touched code's entries, a key by its file and its name, as they stood before the
		//change first touched it, and after the change
		private readonly Dictionary<(WordsFile, string), string[]> ordersBefore;
		private Dictionary<(WordsFile, string), string[]> ordersAfter = [];
		private readonly Dictionary<string, Dictionary<(WordsFile, string), WordsEntry>> entriesBefore = [];
		private Dictionary<string, Dictionary<(WordsFile, string), WordsEntry>> entriesAfter = [];

		//the session's list as it stands: its entries in order, each with its names
		private sealed record KnownList((LanguageEntry Entry, string NativeName, string EnglishName)[] Languages) {
			public static KnownList Of(LanguageTable table) => new([.. table.Known.Select(known => (known, known.NativeName, known.EnglishName))]);

			public void PutBack(LanguageTable table) {
				if (!table.Known.SequenceEqual(Languages.Select(language => language.Entry))) {
					table.Known.Clear();
					foreach (var (entry, _, _) in Languages) {
						table.Known.Add(entry);
					}
				}
				foreach (var (entry, nativeName, englishName) in Languages) {
					entry.NativeName = nativeName;
					entry.EnglishName = englishName;
				}
			}
		}

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

		/// <summary>Starts a change: the session's tables and entries as they stand are what putting it back before returns to.</summary>
		public LanguageTableChange(WordsSession session) {
			this.session = session;
			tablesBefore = Tables();
			knownBefore = KnownList.Of(Table);
			ordersBefore = Orders();
		}

		private LanguageTable Table => session.Languages;
		/// <summary>An operation changed a table.</summary>
		public bool Changed { get; private set; }
		/// <summary>A re-code merged two languages' entries.</summary>
		public bool Merged { get; private set; }

		/// <inheritdoc cref="LanguageTable.Add"/>
		public bool Add(WordsFile file, LanguageEntry language) {
			Keep(language.Code);
			return Note(Table.Add(file, language));
		}

		/// <inheritdoc cref="LanguageTable.Remove"/>
		public bool Remove(WordsFile file, string code) {
			Keep(code);
			return Note(Table.Remove(file, code));
		}

		/// <inheritdoc cref="LanguageTable.Rename"/>
		public LanguageEntry Rename(WordsFile file, string code, LanguageEntry replacement) {
			Keep(code);
			Keep(replacement.Code);
			Merged |= replacement.Code != code && file.Languages.Contains(replacement.Code);
			Note(true);
			return Table.Rename(file, code, replacement);
		}

		/// <inheritdoc cref="LanguageTable.Reorder"/>
		public void Reorder(WordsFile file, int from, int to) {
			if (from != to) {
				Table.Reorder(file, from, to);
				Note(true);
			}
		}

		/// <summary>Declares the language <paramref name="file"/>'s default is written in; nothing happens when the file says so already.</summary>
		public void Declare(WordsFile file, string? code) {
			if (file.DefaultLanguage != code) {
				Table.Declare(file, code);
				Note(true);
			}
		}

		//an operation's own answer, a refusal kept a refusal however much changed before it
		private bool Note(bool changed) {
			Changed |= changed;
			return changed;
		}

		//a code's entries as they stand, the first time the change touches it
		private void Keep(string code) => entriesBefore.TryAdd(code, Entries(code));

		private Dictionary<(WordsFile, string), WordsEntry> Entries(string code) => Placed()
			.Where(placed => placed.Key.Entries.ContainsKey(code))
			.ToDictionary(placed => (placed.File, placed.Key.BlockKey), placed => new WordsEntry(placed.Key.Entries[code]));

		private Dictionary<(WordsFile, string), string[]> Orders() => Placed().ToDictionary(placed => (placed.File, placed.Key.BlockKey), placed => placed.Key.Entries.Keys.ToArray());

		//every key with the file holding it
		private IEnumerable<(WordsFile File, WordsKey Key)> Placed() => session.Files.SelectMany(file => file.Keys.Values.Select(key => (file, key)));

		private Dictionary<WordsFile, FileTable> Tables() => session.Files.ToDictionary(file => file, FileTable.Of);

		/// <summary>The change is done: every file's table, the session's list and the touched entries as they now stand are what putting it back after returns to.</summary>
		public void Close() {
			tablesAfter = Tables();
			knownAfter = KnownList.Of(Table);
			ordersAfter = Orders();
			entriesAfter = entriesBefore.Keys.ToDictionary(code => code, Entries);
		}

		/// <summary>Puts the tables, the list and the touched entries back as they stood <paramref name="before"/> the change, or after it.</summary>
		/// <exception cref="InvalidOperationException">After, before the change was closed.</exception>
		public void PutBack(bool before) {
			if (!before && knownAfter is null) {
				throw new InvalidOperationException("the change is not closed: there is no after to put back");
			}
			foreach (var (file, table) in before ? tablesBefore : tablesAfter) {
				table.PutBack(file);
			}
			(before ? knownBefore : knownAfter!).PutBack(Table);
			//each key's entries rebuilt in its order then: a touched code's from the copy,
			//any other the one it holds, which the change left alone
			var (orders, kept) = before ? (ordersBefore, entriesBefore) : (ordersAfter, entriesAfter);
			foreach (var (file, key) in Placed()) {
				if (!orders.TryGetValue((file, key.BlockKey), out string[]? codes)) {
					continue;
				}
				List<(string Code, WordsEntry Entry)> entries = [.. codes.Select(code =>
					(code, kept.TryGetValue(code, out var copies) && copies.TryGetValue((file, key.BlockKey), out WordsEntry? copy) ? new WordsEntry(copy) : key.Entries[code]))];
				key.Entries.Clear();
				foreach (var (code, entry) in entries) {
					key.Entries[code] = entry;
				}
			}
		}
	}
}
