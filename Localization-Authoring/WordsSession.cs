using PatTech.Localization.Authoring.Codecs;
using System.Text;

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

		/// <summary>
		///     Reads and <see cref="Load(TextReader, string, Encoding?)">loads</see> the
		///     file at <paramref name="path"/>, keeping its line break and encoding for
		///     Save. I/O failures propagate.
		/// </summary>
		public WordsFile Load(string path) => Load(path, path);

		//reads source and loads it as path; a StreamReader decodes it as File.OpenText
		//does, and the BOM it went by is the one Save writes back
		private WordsFile Load(string source, string path) {
			byte[] bytes = File.ReadAllBytes(source);
			using var reader = new StreamReader(new MemoryStream(bytes));
			return Load(reader, path, EncodingOf(bytes));
		}

		//a StreamReader's BOMs (UTF-32 LE first: its BOM starts with UTF-16 LE's),
		//else UTF-8 without one; each throws on text it can't encode, where a
		//replacement character would quietly lose a word
		internal static Encoding EncodingOf(ReadOnlySpan<byte> bytes) => bytes switch {
			[0xFF, 0xFE, 0, 0, ..] => new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true),
			[0, 0, 0xFE, 0xFF, ..] => new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true),
			[0xEF, 0xBB, 0xBF, ..] => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
			[0xFF, 0xFE, ..] => new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
			[0xFE, 0xFF, ..] => new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true),
			_ => IniWriter.Utf8,
		};

		/// <summary>
		///     Loads one ini file: the parser reads it and <see cref="Load(ILoadedWords, string, string?, Encoding?)"/>
		///     takes it from there. Bad content never throws; the parser's gripes land
		///     in <see cref="WordsFile.Errors"/>.
		/// </summary>
		/// <param name="reader">The file's text.</param>
		/// <param name="path">Where it came from — the file's identity and its save target.</param>
		/// <param name="encoding">The encoding Save writes it in; <see langword="null"/> for UTF-8 without a BOM.</param>
		public WordsFile Load(TextReader reader, string path, Encoding? encoding = null) {
			//read whole first: the parser's lines lose their breaks, and saving keeps the file's
			string text = reader.ReadToEnd();
			var loaded = new WordsParserToLocalizationProvider();
			new WordsParser(loaded).Load(new StringReader(text));
			return Load(loaded, path, NewLineOf(text), encoding);
		}

		//the first line break decides; a file with none takes the system's
		private static string? NewLineOf(string text) {
			int lf = text.IndexOf('\n');
			return lf < 0 ? null : lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
		}

		/// <summary>
		///     Imports: <paramref name="importer"/> reads <paramref name="paths"/> — what
		///     its <see cref="IWordsImporter.Discover"/> returned, or what the user kept
		///     of it — and the result loads as a native file, the one the importer
		///     <see cref="IWordsImporter.NativePath">names</see>, so Save writes ini and
		///     the foreign files are never written back. Through the ini importer this
		///     is <see cref="Load(string)"/>, line break and encoding kept.
		/// </summary>
		/// <exception cref="ArgumentException">No paths.</exception>
		public WordsFile Import(IWordsImporter importer, IReadOnlyList<string> paths, FormatOptions? options = null) {
			if (paths.Count == 0) {
				throw new ArgumentException("nothing to import", nameof(paths));
			}
			if (importer is IniCodec && paths.Count == 1) {
				return Load(paths[0], importer.NativePath(paths));
			}
			return Load(importer.Read(paths, options), importer.NativePath(paths));
		}

		/// <summary>
		///     The one load path — the ini reader and every importer come through
		///     here. Loads one file: its keys join the store prefixed with the file's
		///     label (empty keys — bare headers — are dropped, so a bare <c>[group]</c>
		///     round-trips as a group), its languages join the table and every key
		///     is backfilled for them. Loading a path already loaded replaces that
		///     file in place: its old keys go first, so a key deleted on disk does
		///     not survive the reload. The reader's gripes land in
		///     <see cref="WordsFile.Errors"/>.
		/// </summary>
		/// <param name="loaded">The file, read into the document surface.</param>
		/// <param name="path">Where it came from — the file's identity and its save target.</param>
		/// <param name="newLine">The line break the file was written with, which saving keeps; <see langword="null"/> for the system's.</param>
		/// <param name="encoding">The encoding the file was written in, which saving keeps; <see langword="null"/> for UTF-8 without a BOM.</param>
		public WordsFile Load(ILoadedWords loaded, string path, string? newLine = null, Encoding? encoding = null) {
			WordsFile? previous = FileAt(path);
			int position = previous is null ? files.Count : files.IndexOf(previous);
			string label = previous?.Label ?? UniqueLabel(System.IO.Path.GetFileNameWithoutExtension(path));
			if (previous is not null) {
				Unload(previous, prune: false);
			}
			var file = new WordsFile(path, label, loaded, newLine ?? Environment.NewLine, encoding ?? IniWriter.Utf8);
			files.Insert(position, file);
			foreach (WordsKey read in loaded.WordKeys.Values) {
				//a copy: the caller's document stays as read, so loading it twice
				//gives each file keys of its own
				var key = new WordsKey(read) { BlockKey = $"{label}.{read.BlockKey}" };
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
			settingsCache.Clear();
			layeredCache.Clear();
			Languages.Reset();
		}

		/// <summary>
		///     Writes <paramref name="file"/> to its <see cref="WordsFile.Path"/>:
		///     its own language table, preamble and settings references, and its
		///     keys in the order <paramref name="tree"/> walks them, in its line break
		///     and encoding. Atomic — a failure leaves the file on disk untouched. I/O
		///     failures propagate, and so does <see cref="EncoderFallbackException"/>
		///     for text the encoding can't hold, such as a lone surrogate.
		/// </summary>
		/// <param name="file">The file to write.</param>
		/// <param name="tree">The file's node: the walk decides block order, comments write themselves in place.</param>
		public void Save(WordsFile file, IKeyTreeNode tree)
			=> IniWriter.WriteAtomic(file.Path, writer => Save(file, tree, writer), file.Encoding);

		/// <summary>
		///     <see cref="Save(WordsFile, IKeyTreeNode)"/> to a writer instead of the
		///     file's path — for tests, and for writing the file elsewhere.
		/// </summary>
		/// <param name="file">The file to write.</param>
		/// <param name="tree">The file's node: the walk decides block order, comments write themselves in place.</param>
		/// <param name="writer">Where the text goes; it takes the file's <see cref="WordsFile.NewLine"/>.</param>
		/// <exception cref="InvalidOperationException">The tree is not <paramref name="file"/>'s, or does not cover exactly its keys — writing it would drop or misplace data.</exception>
		public void Save(WordsFile file, IKeyTreeNode tree, TextWriter writer) {
			EnsureTreeCovers(file, tree);
			writer.NewLine = file.NewLine;
			IniWriter.WriteFile(tree, writer, keys, Languages.For(file), preamble: file.Preamble, settings: file.Settings, languageSettings: file.LanguageSettings, defaultLanguage: file.DefaultLanguage);
		}

		//the writer only emits keys the walk reaches, so a stale or wrong tree would
		//quietly drop the rest: the tree must be the file's and its keyed nodes must
		//be exactly the file's keys, no more, no fewer. An exporter walks the same
		//tree, so an ExportSource checks the same way
		internal void EnsureTreeCovers(WordsFile file, IKeyTreeNode tree) {
			if (tree.FullLabel != file.Label) {
				throw new InvalidOperationException($"tree root '{tree.FullLabel}' does not own file '{file.Label}'");
			}
			var covered = new HashSet<string>();
			Collect(tree);
			var owned = new HashSet<string>(KeysOf(file).Select(key => key.BlockKey));
			if (!covered.SetEquals(owned)) {
				string missing = string.Join(", ", owned.Except(covered));
				string foreign = string.Join(", ", covered.Except(owned));
				throw new InvalidOperationException($"tree for '{file.Label}' does not match its keys"
					+ (missing != "" ? $"; missing: {missing}" : "")
					+ (foreign != "" ? $"; not the file's: {foreign}" : ""));
			}

			void Collect(IKeyTreeNode node) {
				if (node is not ICommentNode && keys.ContainsKey(node.FullLabel)) {
					covered.Add(node.FullLabel);
				}
				foreach (IKeyTreeNode child in node.Children) {
					Collect(child);
				}
			}
		}

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
		///     ones and keeping the base file's preamble, settings references and default's language; then
		///     loads it. Returns <see langword="null"/> — and writes nothing — when the files
		///     disagree on their key sets; the disagreements come back in
		///     <paramref name="conflicts"/>.
		/// </summary>
		/// <param name="baseFile">The file providing the keys and unlocalised fields.</param>
		/// <param name="languageSources">Language code to the file providing that language's entries.</param>
		/// <param name="baseTree">The base file's node; the merged file keeps its shape and comments.</param>
		/// <param name="outPath">Where the merged file is written (and loaded from).</param>
		/// <param name="conflicts">Key suffixes the involved files disagree on.</param>
		/// <exception cref="InvalidOperationException"><paramref name="baseTree"/> does not cover exactly the base file's keys.</exception>
		/// <exception cref="ArgumentException">A source does not declare the language mapped to it: its entries are only backfill, and would empty the base's.</exception>
		public WordsFile? Merge(WordsFile baseFile, IReadOnlyDictionary<string, WordsFile> languageSources, IKeyTreeNode baseTree, string outPath, out HashSet<string> conflicts) {
			foreach (var (code, source) in languageSources) {
				if (!source.Languages.Contains(code)) {
					throw new ArgumentException($"'{source.Label}' does not declare '{code}'", nameof(languageSources));
				}
			}
			//the writer walks the tree, so one that misses a key would merge without it
			EnsureTreeCovers(baseFile, baseTree);
			string outLabel = UniqueLabel(System.IO.Path.GetFileNameWithoutExtension(outPath));
			var sources = languageSources.ToDictionary(pair => pair.Key, pair => pair.Value.Label);
			var merged = WordsOperations.Merge(keys, baseFile.Label, sources, outLabel, out conflicts);
			if (merged is null) {
				return null;
			}
			//each language labelled as the file it comes from labels it: the base's own, else its source's
			List<LanguageEntry> languages = [.. Languages.For(baseFile)];
			foreach (var (code, source) in languageSources) {
				if (languages.All(language => language.Code != code) && Languages.For(source).FirstOrDefault(language => language.Code == code) is { } label) {
					languages.Add(label);
				}
			}
			IniWriter.WriteFile(KeyTree.Relabel(baseTree, outLabel), outPath, merged, languages,
				preamble: baseFile.Preamble, settings: baseFile.Settings, languageSettings: baseFile.LanguageSettings, newLine: baseFile.NewLine, defaultLanguage: baseFile.DefaultLanguage, encoding: baseFile.Encoding);
			return Load(outPath);
		}

		/// <summary>
		///     The inverse of <see cref="Merge"/>: writes <paramref name="languageCode"/>'s
		///     entries from <paramref name="source"/> into their own file at
		///     <paramref name="outPath"/> — unlocalised fields kept for reference, that
		///     one language declared as the source labels it, the source's shape,
		///     preamble, default's language, settings reference and that language's
		///     own — and loads it. Exactly what <see cref="Merge"/> consumes back.
		/// </summary>
		/// <exception cref="InvalidOperationException"><paramref name="sourceTree"/> does not cover exactly the source's keys.</exception>
		public WordsFile Split(WordsFile source, string languageCode, IKeyTreeNode sourceTree, string outPath) {
			EnsureTreeCovers(source, sourceTree);
			string outLabel = UniqueLabel(System.IO.Path.GetFileNameWithoutExtension(outPath));
			var split = WordsOperations.Split(keys, source.Label, languageCode, outLabel);
			//the language as the source labels it, and only its own settings reference
			List<LanguageEntry> languages = (Languages.For(source).FirstOrDefault(language => language.Code == languageCode) ?? Languages.Find(languageCode)) is { } label ? [label] : [];
			Dictionary<string, string> languageSettings = source.LanguageSettings.TryGetValue(languageCode, out string? path) ? new() { [languageCode] = path } : [];
			IniWriter.WriteFile(KeyTree.Relabel(sourceTree, outLabel), outPath, split, languages,
				preamble: source.Preamble, settings: source.Settings, languageSettings: languageSettings, newLine: source.NewLine, defaultLanguage: source.DefaultLanguage, encoding: source.Encoding);
			return Load(outPath);
		}

		//the project settings files by path, as last read; re-read when the file
		//on disk changes, so an edit made elsewhere shows on the next render
		private readonly Dictionary<string, (ProjectSettings settings, DateTime stamp)> settingsCache = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<(string, string), (ProjectSettings over, ProjectSettings under, ProjectSettings layered)> layeredCache = [];

		/// <summary>
		///     The preview rules for <paramref name="file"/> (SPEC: Markdown previews):
		///     its settings file's, with the file it names for
		///     <paramref name="languageCode"/> layered over them when there is one;
		///     <see cref="ProjectSettings.Empty"/> when it names none. Read on first
		///     use and again whenever a settings file changes on disk; the same
		///     instance comes back in between, so a caller can tell nothing moved.
		/// </summary>
		public ProjectSettings SettingsFor(WordsFile file, string? languageCode = null) {
			ProjectSettings settings = file.SettingsPath() is { } path ? Cached(path) : ProjectSettings.Empty;
			if (languageCode is null || file.SettingsPath(languageCode) is not { } languagePath) {
				return settings;
			}
			ProjectSettings language = Cached(languagePath);
			var key = (languagePath, settings.Path);
			if (!layeredCache.TryGetValue(key, out var entry) || entry.over != language || entry.under != settings) {
				entry = (language, settings, language.Over(settings));
				layeredCache[key] = entry;
			}
			return entry.layered;
		}

		private ProjectSettings Cached(string path) {
			DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
			if (!settingsCache.TryGetValue(path, out var entry) || entry.stamp != stamp) {
				entry = (ProjectSettings.Load(path), stamp);
				settingsCache[path] = entry;
			}
			return entry.settings;
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
