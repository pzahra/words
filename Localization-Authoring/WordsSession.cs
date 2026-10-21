using PatTech.Localization.Authoring.Codecs;
using System.Text;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The document an authoring session holds: every loaded file
	///     (<see cref="Files"/>), each holding its own keys by the names it writes
	///     (<see cref="WordsFile.Keys"/>), and the language table
	///     (<see cref="Languages"/>). This is where the processing lives; a
	///     ViewModel over it only gathers intent. Everything here runs on a
	///     <see cref="TextReader"/>, so it is testable without a UI.
	///     <para>
	///     Invariant kept throughout: every key carries an entry for every known
	///     language, so <c>key.Entries[code]</c> is safe for any code in
	///     <see cref="LanguageTable.Known"/>. Empty entries write nothing.
	///     </para>
	///     <para>
	///     A key is named by its file and its name in it, as the file writes it: no
	///     key carries its file's name (the editor's SPEC: Keys without the file's name).
	///     </para>
	/// </summary>
	public sealed class WordsSession {
		private readonly List<WordsFile> files = [];

		/// <summary>Every loaded key, file by file in load order, each file's in its order.</summary>
		public IEnumerable<WordsKey> AllKeys => files.SelectMany(file => file.Keys.Values);
		/// <summary>The loaded files, in load order (a reload keeps its place).</summary>
		public IReadOnlyList<WordsFile> Files => files;
		/// <summary>The session union of languages and each file's own table.</summary>
		public LanguageTable Languages { get; }

		public WordsSession() {
			Languages = new LanguageTable(this);
		}

		/// <summary>The file loaded from <paramref name="path"/>, if any (<see cref="SamePath"/>); a file not saved yet is at none.</summary>
		public WordsFile? FileAt(string path) {
			string resolved = Resolved(path);
			return files.FirstOrDefault(file => file.Path is { } at && string.Equals(Resolved(at), resolved, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		///     Whether two paths reach one file: compared in full and case-insensitively,
		///     each folder on the way a link or a junction leads through followed to where
		///     it leads, so <c>alias\words.ini</c>, <c>alias</c> a junction to <c>real</c>,
		///     is <c>real\words.ini</c> (the editor's SPEC: Saving). The file's own link is
		///     not followed: Save replaces a link to a file rather than writing through it.
		/// </summary>
		public static bool SamePath(string a, string b) => string.Equals(Resolved(a), Resolved(b), StringComparison.OrdinalIgnoreCase);

		//path in full, each folder on it that is a link or a junction followed as far as the
		//disk has it; what is not there yet, and the file itself, stay as written
		private static string Resolved(string path) {
			string full = System.IO.Path.GetFullPath(path);
			string root = System.IO.Path.GetPathRoot(full) ?? "";
			string[] segments = full[root.Length..].Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
			string at = root;
			for (int i = 0; i < segments.Length; i++) {
				at = System.IO.Path.Combine(at, segments[i]);
				if (i == segments.Length - 1) {
					break;
				}
				try {
					if (new DirectoryInfo(at) is { Exists: true } folder && folder.ResolveLinkTarget(returnFinalTarget: true) is { } target) {
						at = System.IO.Path.GetFullPath(target.FullName);
					}
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
					//a folder it cannot look into is taken as written
				}
			}
			return System.IO.Path.TrimEndingDirectorySeparator(at);
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
			var (loaded, newLine) = Read(reader);
			return Load(loaded, path, newLine, encoding);
		}

		/// <summary>
		///     Starts a file with no path (the editor's SPEC: New): <paramref name="reader"/>
		///     read as <see cref="Load(TextReader, string, Encoding?)"/> reads a file, the
		///     file named <paramref name="name"/>, never numbered, and saved in UTF-8
		///     without a BOM. Nothing is on disk until its first Save asks where
		///     (<see cref="MoveTo"/>), which tells two files of one name apart by refusing
		///     a path another file has.
		/// </summary>
		/// <param name="reader">The file's text.</param>
		/// <param name="name">Its <see cref="WordsFile.Name"/>, which its first Save proposes.</param>
		public WordsFile New(TextReader reader, string name) {
			var (loaded, newLine) = Read(reader);
			return Add(loaded, null, name, newLine, null, files.Count);
		}

		/// <summary>
		///     Starts an empty file with no path, as <see cref="New(TextReader, string)"/>
		///     does: <paramref name="preamble"/> its comment lines above the language
		///     table, written <c>; line</c>, and its default written in
		///     <paramref name="language"/>, which it declares, listed.
		/// </summary>
		/// <param name="name">Its <see cref="WordsFile.Name"/>, which its first Save proposes.</param>
		/// <param name="preamble">What the file is and where it goes, a line of text to a comment line.</param>
		/// <param name="language">The default's language, as the table lists it.</param>
		public WordsFile New(string name, string preamble, LanguageEntry language) {
			var loaded = new LoadedWords {
				Preamble = string.Join('\n', preamble.ReplaceLineEndings("\n").Split('\n').Select(line => line == "" ? "" : " " + line)),
				DefaultLanguage = language.Code,
			};
			loaded.Declare(language.Code, language.NativeName, language.EnglishName is "" ? null : language.EnglishName);
			return Add(loaded, null, name, null, null, files.Count);
		}

		//read whole first: the parser's lines lose their breaks, and saving keeps the file's
		private static (WordsParserToLocalizationProvider Loaded, string? NewLine) Read(TextReader reader) {
			string text = reader.ReadToEnd();
			var loaded = new WordsParserToLocalizationProvider();
			new WordsParser(loaded).Load(new StringReader(text));
			return (loaded, NewLineOf(text));
		}

		/// <summary>
		///     Renames a file not saved yet: <paramref name="name"/> becomes its
		///     <see cref="WordsFile.Name"/>, the name its first Save proposes, another
		///     file's too, and no key changes (the editor's SPEC: New → Renaming a root).
		///     A saved file's name is its file's, and changes only as the file is saved
		///     elsewhere (<see cref="MoveTo"/>, the editor's Save As).
		/// </summary>
		/// <exception cref="ArgumentException">The name is no file name.</exception>
		/// <exception cref="InvalidOperationException">The file is saved.</exception>
		public void Rename(WordsFile file, string name) {
			if (file.Path is { } path) {
				throw new InvalidOperationException($"{file.Name} is saved at {path}, so its name is its file's: it changes as the file is saved elsewhere");
			}
			if (!IsFileName(name)) {
				throw new ArgumentException($"'{name}' is no file name", nameof(name));
			}
			Name(file, name, null);
		}

		/// <summary>True when <paramref name="name"/> can name a file: something besides spaces and dots, and no character a file name refuses.</summary>
		public static bool IsFileName(string name)
			=> name.Trim().Trim('.') != "" && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;

		/// <summary>
		///     Gives <paramref name="file"/> the path it saves to from now on, as its
		///     first Save or a Save As does (the editor's SPEC: New, Saving): it is
		///     named as a file loaded from there would be, and no key changes. Writes
		///     nothing; the caller writes the file there first, and the file at the old
		///     path stays as it is.
		/// </summary>
		/// <exception cref="ArgumentException">Another file has the path.</exception>
		public void MoveTo(WordsFile file, string path) {
			if (FileAt(path) is { } other && other != file) {
				throw new ArgumentException($"'{path}' is the path of {other.Name}", nameof(path));
			}
			Name(file, System.IO.Path.GetFileNameWithoutExtension(path), path);
		}

		//the file's keys are its own, by their names: the name is the file's alone
		private static void Name(WordsFile file, string name, string? path) {
			file.Path = path;
			file.Name = name;
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
		///     here. Loads one file: it holds its keys by their names (empty keys —
		///     bare headers — are dropped, so a bare <c>[group]</c> round-trips as a
		///     group), its languages join the table and every key is backfilled for
		///     them. Loading a path already loaded replaces that file in place, its old
		///     keys with it, so a key deleted on disk does not survive the reload. The
		///     reader's gripes land in <see cref="WordsFile.Errors"/>.
		/// </summary>
		/// <param name="loaded">The file, read into the document surface.</param>
		/// <param name="path">Where it came from — the file's identity and its save target.</param>
		/// <param name="newLine">The line break the file was written with, which saving keeps; <see langword="null"/> for the system's.</param>
		/// <param name="encoding">The encoding the file was written in, which saving keeps; <see langword="null"/> for UTF-8 without a BOM.</param>
		public WordsFile Load(ILoadedWords loaded, string path, string? newLine = null, Encoding? encoding = null) {
			WordsFile? previous = FileAt(path);
			int position = previous is null ? files.Count : files.IndexOf(previous);
			if (previous is not null) {
				Unload(previous, prune: false);
			}
			WordsFile file = Add(loaded, path, System.IO.Path.GetFileNameWithoutExtension(path), newLine, encoding, position);
			if (previous is not null) {
				//languages the file stopped declaring, and nobody else has, go now
				Languages.Prune();
			}
			return file;
		}

		//the file joins the session at position, named name
		private WordsFile Add(ILoadedWords loaded, string? path, string name, string? newLine, Encoding? encoding, int position) {
			var file = new WordsFile(path, name, loaded, newLine ?? Environment.NewLine, encoding ?? IniWriter.Utf8);
			files.Insert(position, file);
			foreach (WordsKey read in loaded.WordKeys.Values) {
				//a copy: the caller's document stays as read, so loading it twice
				//gives each file keys of its own
				var key = new WordsKey(read);
				if (!key.IsEmpty()) {
					file.Store.Add(key.BlockKey, key);
				}
			}
			Languages.Absorb(loaded, firstFile: files.Count == 1);
			return file;
		}

		/// <summary>
		///     The name <paramref name="file"/> shows by (the editor's SPEC: Keys without
		///     the file's name): its <see cref="WordsFile.Name"/>, and where another loaded
		///     file has that name too, in brackets the first folder, walking up from its
		///     own, where its path differs from every other's: <c>strings (ui)</c> beside
		///     <c>strings (core)</c>. The files of one name are told apart together: two
		///     whose folders would read alike each take in the next folder toward its own,
		///     then the one above, until none do, so <c>C:\alpha\ui</c> beside
		///     <c>C:\other\alpha</c> shows <c>(alpha\ui)</c> and <c>(other\alpha)</c>. A
		///     file not saved yet has no folder, and shows its name alone; one whose
		///     folders differ at no single step shows its folder whole.
		/// </summary>
		public string ShownName(WordsFile file) {
			List<WordsFile> named = [.. files.Where(other => string.Equals(other.Name, file.Name, StringComparison.OrdinalIgnoreCase))];
			if (named.Count < 2 || file.Path is null) {
				return file.Name;
			}
			return $"{file.Name} ({Told(named)[file]})";
		}

		//what tells each saved file of one name from the rest: a run of its folders, at
		//first the one where its path differs from every other's, grown where two runs
		//read alike, toward its own folder, then above, until none do
		private static Dictionary<WordsFile, string> Told(List<WordsFile> named) {
			var folders = named.ToDictionary(file => file, file => file.Path is { } path ? Folders(path) : []);
			var runs = new Dictionary<WordsFile, (int Low, int High)>();
			foreach (WordsFile file in named.Where(file => file.Path is not null)) {
				string[] own = folders[file];
				int step = Enumerable.Range(0, own.Length).FirstOrDefault(step => named.All(other => other == file
					|| step >= folders[other].Length || !string.Equals(folders[other][step], own[step], StringComparison.OrdinalIgnoreCase)), -1);
				runs[file] = step < 0 ? (0, Math.Max(own.Length - 1, 0)) : (step, step);
			}
			while (true) {
				var told = runs.ToDictionary(run => run.Key, run => Run(folders[run.Key], run.Value));
				var alike = told.GroupBy(label => label.Value, StringComparer.OrdinalIgnoreCase).Where(same => same.Count() > 1).SelectMany(same => same.Select(label => label.Key)).ToList();
				bool grew = false;
				foreach (WordsFile file in alike) {
					var (low, high) = runs[file];
					if (low > 0) {
						runs[file] = (low - 1, high);
						grew = true;
					}
					else if (high < folders[file].Length - 1) {
						runs[file] = (low, high + 1);
						grew = true;
					}
				}
				if (!grew) {
					return told;
				}
			}

			//the folders a path sits in, its own first
			static string[] Folders(string path)
				=> [.. (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "").Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Reverse()];

			//a run of folders as a path writes them, the outermost first
			static string Run(string[] own, (int Low, int High) run)
				=> own.Length == 0 ? "" : string.Join(System.IO.Path.DirectorySeparatorChar, own[run.Low..(run.High + 1)].Reverse());
		}

		/// <summary>
		///     Forgets a file, its keys with it; languages no remaining file declares
		///     (and no remaining key has words in) leave the table.
		/// </summary>
		public bool Unload(WordsFile file) => Unload(file, prune: true);

		private bool Unload(WordsFile file, bool prune) {
			if (!files.Remove(file)) {
				return false;
			}
			if (prune) {
				Languages.Prune();
			}
			return true;
		}

		/// <summary>Back to an empty session with the default language.</summary>
		public void Reset() {
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
		/// <exception cref="InvalidOperationException">The file has no path yet: its first Save asks where.</exception>
		public void Save(WordsFile file, IKeyTreeNode tree)
			=> Save(file, tree, file.Path ?? throw new InvalidOperationException($"{file.Name} has no path yet: its first Save asks where"));

		/// <summary>
		///     <see cref="Save(WordsFile, IKeyTreeNode)"/> to <paramref name="path"/>,
		///     the file's own path left as it is: a file's first Save, or its move,
		///     writes there before <see cref="MoveTo"/>, so a failure moves nothing.
		/// </summary>
		/// <param name="file">The file to write.</param>
		/// <param name="tree">The file's node: the walk decides block order, comments write themselves in place.</param>
		/// <param name="path">Where it is written.</param>
		public void Save(WordsFile file, IKeyTreeNode tree, string path)
			=> IniWriter.WriteAtomic(path, writer => Save(file, tree, writer), file.Encoding);

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
			IniWriter.WriteFile(tree, writer, file.Keys, Languages.For(file), preamble: file.Preamble, settings: file.Settings, languageSettings: file.LanguageSettings, defaultLanguage: file.DefaultLanguage);
		}

		//the writer only emits keys the walk reaches, so a stale or wrong tree would
		//quietly drop the rest: the tree must be the file's, its root carrying no other
		//file and labelled by nothing, every node by its path, and its keyed nodes must
		//be exactly the file's keys, no more, no fewer; another file's tree with the
		//same keys would write its comments and order. An exporter walks the same
		//tree, so an ExportSource checks the same way, and so do merge and split
		internal static void EnsureTreeCovers(WordsFile file, IKeyTreeNode tree) {
			if (tree.File is { } owner && owner != file) {
				throw new InvalidOperationException($"the tree of '{owner.Name}' was given for '{file.Name}': a file is written from its own tree");
			}
			if (tree.FullLabel != "") {
				throw new InvalidOperationException($"tree root '{tree.FullLabel}' is no file's: a file's tree is labelled by its paths alone");
			}
			var covered = new HashSet<string>();
			foreach (IKeyTreeNode child in tree.Children) {
				Collect(child);
			}
			var owned = new HashSet<string>(file.Keys.Keys);
			if (!covered.SetEquals(owned)) {
				string missing = string.Join(", ", owned.Except(covered));
				throw new InvalidOperationException($"tree for '{file.Name}' does not match its keys"
					+ (missing != "" ? $"; missing: {missing}" : ""));
			}

			void Collect(IKeyTreeNode node) {
				if (node is not ICommentNode && file.Keys.ContainsKey(node.FullLabel)) {
					covered.Add(node.FullLabel);
				}
				foreach (IKeyTreeNode child in node.Children) {
					Collect(child);
				}
			}
		}

		/// <summary>The keys of <paramref name="file"/>, in store order (document order after a load).</summary>
		public IEnumerable<WordsKey> KeysOf(WordsFile file) => file.Keys.Values;

		/// <summary>
		///     A new key in <paramref name="file"/> named <paramref name="name"/>,
		///     carrying an empty entry for every known language; the existing key when
		///     there already is one.
		/// </summary>
		public WordsKey AddKey(WordsFile file, string name) {
			if (file.Store.TryGetValue(name, out var existing)) {
				return existing;
			}
			var key = new WordsKey(name);
			foreach (LanguageEntry language in Languages.Known) {
				key.Entries[language.Code] = new WordsEntry();
			}
			file.Store.Add(name, key);
			return key;
		}

		/// <summary>Removes <paramref name="file"/>'s key named <paramref name="name"/> alone; descendants stay.</summary>
		public bool RemoveKey(WordsFile file, string name) => file.Store.Remove(name);

		/// <summary>
		///     <paramref name="file"/>'s key named <paramref name="name"/> and every key
		///     below it, as <see cref="RemoveKeysUnder(WordsFile, string)"/> takes them;
		///     an empty name is every key of the file.
		/// </summary>
		public IEnumerable<WordsKey> KeysUnder(WordsFile file, string name) {
			string prefix = name + ".";
			return file.Keys.Values.Where(key => name == "" || key.BlockKey == name || key.BlockKey.StartsWith(prefix, StringComparison.Ordinal));
		}

		/// <summary>
		///     Removes <paramref name="file"/>'s key named <paramref name="name"/> and
		///     every key below it — exact-or-prefix, so <c>view</c> never catches
		///     <c>viewer</c>. Returns how many went.
		/// </summary>
		public int RemoveKeysUnder(WordsFile file, string name) {
			List<WordsKey> going = [.. KeysUnder(file, name)];
			foreach (WordsKey key in going) {
				file.Store.Remove(key.BlockKey);
			}
			return going.Count;
		}

		/// <summary>
		///     Puts a kept copy of a key (<c>new WordsKey(key)</c>) back in
		///     <paramref name="file"/> as it was: the session makes the key, as
		///     <see cref="AddKey(WordsFile, string)"/> does, and the copy fills it,
		///     plural forms, parameters, entries and all, so the copy stays as it was
		///     kept and can go back again. Its entries meet the languages there are
		///     now, as <see cref="PutBackEntries"/> puts them.
		/// </summary>
		public WordsKey Restore(WordsFile file, WordsKey kept) {
			WordsKey key = AddKey(file, kept.BlockKey);
			key.IsConstant = kept.IsConstant;
			key.DefaultValue = kept.DefaultValue;
			key.Context = kept.Context;
			key.Comment = kept.Comment;
			key.Review = kept.Review;
			key.Forms.Clear();
			foreach (var (form, text) in kept.Forms) {
				key.Forms[form] = text;
			}
			key.PutBackParameters(kept.Parameters);
			key.Entries.Clear();
			key.PutBackEntries(kept.Entries);
			Reconcile(key);
			return key;
		}

		/// <summary>
		///     Renames <paramref name="file"/>'s key <paramref name="name"/> and every
		///     key below it (<see cref="WordsOperations.TryRename(IDictionary{string, WordsKey}, string, string, out HashSet{string})"/>).
		/// </summary>
		public bool TryRename(WordsFile file, string name, string newName, out HashSet<string> collisions)
			=> WordsOperations.TryRename(file.Store, name, newName, out collisions);

		/// <summary>
		///     Moves <paramref name="file"/>'s key <paramref name="name"/> and every key
		///     below it under <paramref name="newParent"/> in <paramref name="to"/>,
		///     another file or the same, keeping its last segment, marker and all; an
		///     empty parent is the top of the file. All or nothing, as a rename; the
		///     collisions are names in <paramref name="to"/>.
		/// </summary>
		public bool TryMove(WordsFile file, string name, WordsFile to, string newParent, out HashSet<string> collisions)
			=> WordsOperations.TryRename(file.Store, name, to.Store, KeyTree.Join(newParent, WordsOperations.LastSegment(name)), out collisions);

		/// <summary>Makes <paramref name="file"/>'s key <paramref name="name"/> a constant, or a regular key again (<see cref="WordsOperations.SetConstant"/>): its new name, or <see langword="null"/>.</summary>
		public string? SetConstant(WordsFile file, string name, bool isConstant, bool clearEntries = false)
			=> WordsOperations.SetConstant(file.Store, name, isConstant, clearEntries);

		//A key's state set and put back by its file and name, as an editor's history names
		//it (the editor's SPEC: Undo → Where it lives); each is false, and does nothing,
		//where the file has no key of the name

		/// <summary>
		///     Sets one field of <paramref name="file"/>'s key <paramref name="name"/>:
		///     a value, a form, a context or a comment, the default's or a language's,
		///     the raised hand (<see cref="WordsField.Hand"/>) or a language's stale
		///     mark, as <see cref="WordsField.Write"/> writes it, <see langword="null"/>
		///     clearing it, but for a form, where an empty one is none, as the session
		///     holds forms. Words in a language the session let go, a redo after another
		///     file went, bring the language back, as words in it keep it (the editor's
		///     SPEC: Dirty by file); nothing written there is false, and makes no entry.
		/// </summary>
		public bool Set(WordsFile file, string name, WordsField field, string? text) {
			if (!file.Keys.TryGetValue(name, out WordsKey? key)) {
				return false;
			}
			bool lacks = field.Type != "param" && field.Language != "" && !key.Entries.ContainsKey(field.Language);
			if (lacks && string.IsNullOrEmpty(text)) {
				return false;
			}
			if (lacks) {
				key.Entries[field.Language] = new WordsEntry();
			}
			field.Write(key, field is { Type: "value", Form: not "" } && text == "" ? null : text);
			if (lacks) {
				Languages.Reread([field.Language]);
			}
			return true;
		}

		/// <summary>Sets the description of the key's definition of <paramref name="parameter"/>, its type and its place kept; false where it defines none of the name.</summary>
		public bool Describe(WordsFile file, string name, string parameter, string description) {
			if (!file.Keys.TryGetValue(name, out WordsKey? key) || key.Parameters.FirstOrDefault(defined => defined.Key == parameter) is not { } found) {
				return false;
			}
			found.Description = description;
			return true;
		}

		/// <summary>Puts kept parameters (<see cref="WordsKey.CopyParameters"/>) back in place of the key's (<see cref="WordsKey.PutBackParameters"/>).</summary>
		public bool PutBackParameters(WordsFile file, string name, IEnumerable<WordsParameter> kept) {
			if (!file.Keys.TryGetValue(name, out WordsKey? key)) {
				return false;
			}
			key.PutBackParameters(kept);
			return true;
		}

		/// <summary>
		///     Puts kept entries (<see cref="WordsKey.CopyEntries"/>) back in the key
		///     (<see cref="WordsKey.PutBackEntries"/>), meeting the languages there are
		///     now: a language the session let go since they were kept comes back where
		///     they have words in it, and an empty entry in one is dropped; one it took
		///     since gives the key an empty entry, as every key has for each.
		/// </summary>
		public bool PutBackEntries(WordsFile file, string name, IReadOnlyDictionary<string, WordsEntry> kept) {
			if (!file.Keys.TryGetValue(name, out WordsKey? key)) {
				return false;
			}
			key.PutBackEntries(kept);
			Reconcile(key);
			return true;
		}

		//a key's entries put back from a copy, which knew the languages there were then (the
		//editor's SPEC: Dirty by file, Each file's history)
		private void Reconcile(WordsKey key) {
			string[] gone = [.. key.Entries.Keys.Where(code => Languages.Find(code) is null)];
			if (gone.Length > 0) {
				Languages.Reread(gone);
			}
			foreach (string code in gone) {
				if (Languages.Find(code) is null) {
					key.Entries.Remove(code);
				}
			}
			foreach (LanguageEntry language in Languages.Known) {
				key.Entries.TryAdd(language.Code, new WordsEntry());
			}
		}

		/// <summary>
		///     Marks stale with <paramref name="stamp"/> the translations a change to the
		///     key's default leaves behind (<see cref="WordsKey.TranslationsToStale"/>), and
		///     gives back their languages; none where the file has no key of the name.
		/// </summary>
		public IReadOnlyList<string> StaleTranslations(WordsFile file, string name, string stamp) {
			if (!file.Keys.TryGetValue(name, out WordsKey? key)) {
				return [];
			}
			List<string> codes = [.. key.TranslationsToStale()];
			foreach (string code in codes) {
				Set(file, name, WordsField.StaleIn(code), stamp);
			}
			return codes;
		}

		/// <summary>True when the files hold the same keys; the odd ones out come back.</summary>
		public bool HaveSameKeys(IEnumerable<WordsFile> files, out HashSet<string> conflicts)
			=> WordsOperations.HaveSameKeys(files.Select(file => file.Keys), out conflicts);

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
		/// <param name="conflicts">Keys the involved files disagree on.</param>
		/// <exception cref="InvalidOperationException"><paramref name="baseTree"/> does not cover exactly the base file's keys.</exception>
		/// <exception cref="ArgumentException">A source does not declare the language mapped to it: its entries are only backfill, and would empty the base's.</exception>
		public WordsFile? Merge(WordsFile baseFile, IReadOnlyDictionary<string, WordsFile> languageSources, IKeyTreeNode baseTree, string outPath, out HashSet<string> conflicts) {
			foreach (var (code, source) in languageSources) {
				if (!source.Languages.Contains(code)) {
					throw new ArgumentException($"'{source.Name}' does not declare '{code}'", nameof(languageSources));
				}
			}
			//the writer walks the tree, so one that misses a key would merge without it
			EnsureTreeCovers(baseFile, baseTree);
			var sources = languageSources.ToDictionary(pair => pair.Key, pair => pair.Value.Keys);
			var merged = WordsOperations.Merge(baseFile.Keys, sources, out conflicts);
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
			IniWriter.WriteFile(baseTree, outPath, merged, languages,
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
			var split = WordsOperations.Split(source.Keys, languageCode);
			//the language as the source labels it, and only its own settings reference
			List<LanguageEntry> languages = (Languages.For(source).FirstOrDefault(language => language.Code == languageCode) ?? Languages.Find(languageCode)) is { } label ? [label] : [];
			Dictionary<string, string> languageSettings = source.LanguageSettings.TryGetValue(languageCode, out string? path) ? new() { [languageCode] = path } : [];
			IniWriter.WriteFile(sourceTree, outPath, split, languages,
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
		///     A provider over <paramref name="files"/>' keys, stacked as a host app
		///     stacks dictionaries, a later file winning — for previews and checks.
		/// </summary>
		/// <param name="files">The files in precedence order (the tree's order, typically).</param>
		/// <param name="languageCode">A language for its values with fallback to the defaults, or <see langword="null"/> for the defaults alone.</param>
		public IWordsProvider Provider(IEnumerable<WordsFile> files, string? languageCode = null)
			=> Provider(files.Select(file => file.Keys), languageCode);

		/// <summary>
		///     <see cref="Provider(IEnumerable{WordsFile}, string?)"/> with
		///     <paramref name="shown"/> read by its name above every file: the key a
		///     preview shows renders its own words, though a later file holds a key of
		///     its name.
		/// </summary>
		public IWordsProvider Provider(IEnumerable<WordsFile> files, WordsKey shown, string? languageCode = null)
			=> Provider([.. files.Select(file => file.Keys), new Dictionary<string, WordsKey> { [shown.BlockKey] = shown }], languageCode);

		private static IWordsProvider Provider(IEnumerable<IReadOnlyDictionary<string, WordsKey>> stores, string? languageCode)
			=> languageCode is null ? new DefaultWordsProvider(stores) : new LanguageWordsProvider(stores, languageCode);
	}
}
