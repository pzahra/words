namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The document an authoring session holds: every loaded file
	///     (<see cref="Files"/>), the one store of their keys (<see cref="Keys"/>,
	///     prefixed with each file's label, in load order) and the language table
	///     (<see cref="Languages"/>). This is where the processing lives; a
	///     ViewModel over it only gathers intent. Everything here runs on a
	///     <see cref="TextReader"/>, so it is testable without a UI.
	///     <para>
	///     Invariant kept throughout: every key carries an entry for every known
	///     language, so <c>key.Entries[code]</c> is safe for any code in
	///     <see cref="LanguageTable.Known"/>. Empty entries write nothing.
	///     </para>
	/// </summary>
	public sealed class WordsSession {
		//insertion-ordered on purpose: the tree is built from a file's keys in the
		//order they were read, and a plain Dictionary re-uses freed slots, so a
		//reload (remove all, add all) would come back reversed
		private readonly OrderedDictionary<string, WordsKey> keys = new();
		private readonly List<WordsFile> files = [];

		/// <summary>Every loaded key by its prefixed block key, in load order. Change it through the session.</summary>
		public IReadOnlyDictionary<string, WordsKey> Keys => keys;
		/// <summary>The loaded files, in load order (a reload keeps its place).</summary>
		public IReadOnlyList<WordsFile> Files => files;
		/// <summary>The session union of languages and each file's own table.</summary>
		public LanguageTable Languages { get; }

		public WordsSession() {
			Languages = new LanguageTable(this);
		}

		/// <summary>The file loaded from <paramref name="path"/>, if any (paths compare in full, case-insensitively).</summary>
		public WordsFile? FileAt(string path) {
			string full = System.IO.Path.GetFullPath(path);
			return files.FirstOrDefault(file => string.Equals(System.IO.Path.GetFullPath(file.Path), full, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>The file whose keys carry <paramref name="label"/> as their prefix, if any.</summary>
		public WordsFile? FileOf(string label) => files.FirstOrDefault(file => file.Label == label);

		/// <summary>The file a prefixed block key belongs to, if it is loaded.</summary>
		public WordsFile? FileOfKey(string blockKey) {
			int dot = blockKey.IndexOf('.');
			return FileOf(dot < 0 ? blockKey : blockKey[..dot]);
		}

		/// <summary>Reads and <see cref="Load(TextReader, string)">loads</see> the file at <paramref name="path"/>. I/O failures propagate.</summary>
		public WordsFile Load(string path) {
			using var reader = File.OpenText(path);
			return Load(reader, path);
		}

		/// <summary>
		///     Loads one file: its keys join the store prefixed with the file's label
		///     (empty keys — bare headers — are dropped, so a bare <c>[group]</c>
		///     round-trips as a group), its languages join the table and every key
		///     is backfilled for them. Loading a path already loaded replaces that
		///     file in place: its old keys go first, so a key deleted on disk does
		///     not survive the reload. Bad content never throws; the parser's gripes
		///     land in <see cref="WordsFile.Errors"/>.
		/// </summary>
		/// <param name="reader">The file's text.</param>
		/// <param name="path">Where it came from — the file's identity and its save target.</param>
		public WordsFile Load(TextReader reader, string path) {
			var loaded = new WordsParserToLocalizationProvider();
			new WordsParser(loaded).Load(reader);

			WordsFile? previous = FileAt(path);
			int position = previous is null ? files.Count : files.IndexOf(previous);
			string label = previous?.Label ?? UniqueLabel(System.IO.Path.GetFileNameWithoutExtension(path));
			if (previous is not null) {
				Unload(previous, prune: false);
			}
			var file = new WordsFile(path, label, loaded);
			files.Insert(position, file);
			foreach (WordsKey key in loaded.WordKeys.Values) {
				key.BlockKey = $"{label}.{key.BlockKey}";
				if (!key.IsEmpty()) {
					keys.Add(key.BlockKey, key);
				}
			}
			Languages.Absorb(loaded, firstFile: files.Count == 1);
			if (previous is not null) {
				//languages the file stopped declaring, and nobody else has, go now
				Languages.Prune();
			}
			return file;
		}

		//the file name, dots replaced (the writer strips one leading segment), and
		//suffixed past any label another loaded file already carries
		private string UniqueLabel(string fileName) {
			string name = fileName.Replace('.', '-');
			if (name == "") {
				name = "file";
			}
			string label = name;
			for (int i = 2; FileOf(label) is not null; i++) {
				label = $"{name}-{i}";
			}
			return label;
		}

		/// <summary>
		///     Forgets a file: its keys leave the store and languages no remaining
		///     file declares (and no remaining key has words in) leave the table.
		/// </summary>
		public bool Unload(WordsFile file) => Unload(file, prune: true);

		private bool Unload(WordsFile file, bool prune) {
			if (!files.Remove(file)) {
				return false;
			}
			RemoveKeysUnder(file.Label);
			if (prune) {
				Languages.Prune();
			}
			return true;
		}

		/// <summary>Back to an empty session with the default language.</summary>
		public void Reset() {
			keys.Clear();
			files.Clear();
			Languages.Reset();
		}

		/// <summary>
		///     Writes <paramref name="file"/> to its <see cref="WordsFile.Path"/>:
		///     its own language table, preamble and image schemes, and its keys in
		///     the order <paramref name="tree"/> walks them. I/O failures propagate.
		/// </summary>
		/// <param name="file">The file to write.</param>
		/// <param name="tree">The file's node: the walk decides block order, comments write themselves in place.</param>
		public void Save(WordsFile file, IKeyTreeNode tree) {
			using var writer = new StreamWriter(file.Path);
			Save(file, tree, writer);
		}

		/// <summary>
		///     <see cref="Save(WordsFile, IKeyTreeNode)"/> to a writer instead of the
		///     file's path — for tests, and for writing the file elsewhere.
		/// </summary>
		/// <param name="file">The file to write.</param>
		/// <param name="tree">The file's node: the walk decides block order, comments write themselves in place.</param>
		/// <param name="writer">Where the text goes.</param>
		public void Save(WordsFile file, IKeyTreeNode tree, TextWriter writer)
			=> IniWriter.WriteFile(tree, writer, keys, Languages.For(file), preamble: file.Preamble, imageSchemes: file.ImageSchemes);

		/// <summary>The keys of <paramref name="file"/>, in store order (document order after a load).</summary>
		public IEnumerable<WordsKey> KeysOf(WordsFile file) {
			string prefix = file.Label + ".";
			return keys.Values.Where(key => key.BlockKey.StartsWith(prefix, StringComparison.Ordinal));
		}

		/// <summary>
		///     A new key at <paramref name="blockKey"/>, carrying an empty entry for
		///     every known language; the existing key when there already is one.
		/// </summary>
		public WordsKey AddKey(string blockKey) {
			if (keys.TryGetValue(blockKey, out var existing)) {
				return existing;
			}
			var key = new WordsKey(blockKey);
			foreach (LanguageEntry language in Languages.Known) {
				key.Entries[language.Code] = new WordsEntry();
			}
			keys.Add(blockKey, key);
			return key;
		}

		/// <summary>Removes the key at <paramref name="blockKey"/> alone; descendants stay.</summary>
		public bool RemoveKey(string blockKey) => keys.Remove(blockKey);

		/// <summary>
		///     Removes the key at <paramref name="blockKey"/> and every key below it —
		///     exact-or-prefix, so <c>view</c> never catches <c>viewer</c>. Returns how
		///     many went.
		/// </summary>
		public int RemoveKeysUnder(string blockKey) {
			string prefix = blockKey + ".";
			int removed = 0;
			for (int i = keys.Count - 1; i >= 0; i--) {
				string candidate = keys.GetAt(i).Key;
				if (candidate == blockKey || candidate.StartsWith(prefix, StringComparison.Ordinal)) {
					keys.RemoveAt(i);
					removed++;
				}
			}
			return removed;
		}

		/// <inheritdoc cref="WordsOperations.TryRename"/>
		public bool TryRename(string oldKey, string newKey, out HashSet<string> collisions)
			=> WordsOperations.TryRename(keys, oldKey, newKey, out collisions);

		/// <inheritdoc cref="WordsOperations.TryMove"/>
		public bool TryMove(string key, string newParent, out HashSet<string> collisions)
			=> WordsOperations.TryMove(keys, key, newParent, out collisions);

		/// <inheritdoc cref="WordsOperations.SetConstant"/>
		public string? SetConstant(string blockKey, bool isConstant, bool clearEntries = false)
			=> WordsOperations.SetConstant(keys, blockKey, isConstant, clearEntries);

		/// <summary>True when the files hold the same keys (file prefix aside); the odd ones out come back.</summary>
		public bool HaveSameKeys(IEnumerable<WordsFile> files, out HashSet<string> conflicts)
			=> WordsOperations.HaveSameKeys(files.Select(file => WordsOperations.KeysOf(keys, file.Label)), out conflicts);

		/// <summary>
		///     The translator round trip in bulk: writes a file at
		///     <paramref name="outPath"/> taking every key and the unlocalised fields
		///     from <paramref name="baseFile"/> and each language's entries from the
		///     file mapped to it, declaring the base file's languages plus the merged
		///     ones and keeping the base file's preamble and image schemes; then loads
		///     it. Returns <see langword="null"/> — and writes nothing — when the files
		///     disagree on their key sets; the disagreements come back in
		///     <paramref name="conflicts"/>.
		/// </summary>
		/// <param name="baseFile">The file providing the keys and unlocalised fields.</param>
		/// <param name="languageSources">Language code to the file providing that language's entries.</param>
		/// <param name="baseTree">The base file's node; the merged file keeps its shape and comments.</param>
		/// <param name="outPath">Where the merged file is written (and loaded from).</param>
		/// <param name="conflicts">Key suffixes the involved files disagree on.</param>
		public WordsFile? Merge(WordsFile baseFile, IReadOnlyDictionary<string, WordsFile> languageSources, IKeyTreeNode baseTree, string outPath, out HashSet<string> conflicts) {
			string outLabel = UniqueLabel(System.IO.Path.GetFileNameWithoutExtension(outPath));
			var sources = languageSources.ToDictionary(pair => pair.Key, pair => pair.Value.Label);
			var merged = WordsOperations.Merge(keys, baseFile.Label, sources, outLabel, out conflicts);
			if (merged is null) {
				return null;
			}
			List<LanguageEntry> languages = [.. Languages.For(baseFile).Select(language => language.Code)
				.Concat(languageSources.Keys)
				.Distinct()
				.Select(Languages.Find)
				.OfType<LanguageEntry>()];
			IniWriter.WriteFile(KeyTree.Relabel(baseTree, outLabel), outPath, merged, languages,
				preamble: baseFile.Preamble, imageSchemes: baseFile.ImageSchemes);
			return Load(outPath);
		}

		/// <summary>
		///     The inverse of <see cref="Merge"/>: writes <paramref name="languageCode"/>'s
		///     entries from <paramref name="source"/> into their own file at
		///     <paramref name="outPath"/> — unlocalised fields kept for reference, that
		///     one language declared, the source's shape, preamble and image schemes —
		///     and loads it. Exactly what <see cref="Merge"/> consumes back.
		/// </summary>
		public WordsFile Split(WordsFile source, string languageCode, IKeyTreeNode sourceTree, string outPath) {
			string outLabel = UniqueLabel(System.IO.Path.GetFileNameWithoutExtension(outPath));
			var split = WordsOperations.Split(keys, source.Label, languageCode, outLabel);
			List<LanguageEntry> languages = Languages.Find(languageCode) is { } language ? [language] : [];
			IniWriter.WriteFile(KeyTree.Relabel(sourceTree, outLabel), outPath, split, languages,
				preamble: source.Preamble, imageSchemes: source.ImageSchemes);
			return Load(outPath);
		}

		/// <summary>
		///     A provider over every loaded file, later files winning bare-reference
		///     lookups like a host app stacking dictionaries — for previews.
		/// </summary>
		/// <param name="fileLabels">The files in precedence order (the tree's order, typically).</param>
		/// <param name="languageCode">A language for its values with fallback to the defaults, or <see langword="null"/> for the defaults alone.</param>
		public IWordsProvider Provider(IEnumerable<string> fileLabels, string? languageCode = null)
			=> languageCode is null
				? new DefaultWordsProvider(keys, fileLabels)
				: new LanguageWordsProvider(keys, languageCode, fileLabels);
	}
}
