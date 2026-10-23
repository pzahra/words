namespace PatTech.Localization.Authoring {
	/// <summary>
	///     One loaded <c>words.ini</c>: its keys (<see cref="Keys"/>), by the names
	///     the file writes, and everything else of the file's. The file is
	///     identified by its <see cref="Path"/>, so two <c>strings.ini</c> in
	///     different folders are two files, each holding its own keys, which the
	///     editor tells apart by the folder (<see cref="WordsSession.ShownName"/>).
	/// </summary>
	public sealed class WordsFile {
		/// <summary>
		///     The path the file was loaded from, or first saved to — its identity, and
		///     where it saves; <see langword="null"/> for a file started in the editor
		///     and not saved yet (<see cref="WordsSession.New(string, string, LanguageEntry)"/>), which asks where at
		///     its first Save (<see cref="WordsSession.MoveTo"/>).
		/// </summary>
		public string? Path { get; internal set; }
		/// <summary>
		///     The file's true name: its file name without the extension, dots and all
		///     (<c>Strings.Designer</c>), or the name a file not saved yet was given,
		///     which its first Save proposes. It follows the file
		///     (<see cref="WordsSession.Rename"/>, <see cref="WordsSession.MoveTo"/>), is
		///     never numbered, and no key carries it; two loaded files may share it, and
		///     <see cref="WordsSession.ShownName"/> tells them apart.
		/// </summary>
		public string Name { get; internal set; }
		/// <summary>
		///     The file's keys by the names it writes (<c>view.key</c>, <c>$constant</c>),
		///     in store order: document order after a load, a key added since at the
		///     end. Change them through the session.
		/// </summary>
		public IReadOnlyDictionary<string, WordsKey> Keys => Store;
		//insertion-ordered on purpose: the tree is built from the keys in the order they
		//were read, and a plain Dictionary re-uses freed slots
		internal OrderedDictionary<string, WordsKey> Store { get; } = new();
		/// <summary>The comment run above the language table; written back above it.</summary>
		public string Preamble { get; set; }
		/// <summary>
		///     The comment run after the last block, as loaded. It is presented as a
		///     tree comment at the file's end and written from the tree, so this is
		///     the load-time value only.
		/// </summary>
		public string Trailer { get; }
		/// <summary>
		///     The file's own language table: the codes it declares, in its order.
		///     Saving writes exactly these (with their <see cref="Labels"/>), so a
		///     main file never absorbs a library's extras.
		/// </summary>
		public List<string> Languages { get; }
		/// <summary>
		///     The file's own labels for the codes it declares, as it wrote them: the
		///     endonym with its <c>!</c> where the file declares the language unlisted,
		///     and the exonym only where the file or a manager gave one. A relabel is
		///     the file's alone (<see cref="LanguageTable.Rename"/>); a code without a
		///     label here takes the session's.
		/// </summary>
		public Dictionary<string, LanguageEntry> Labels { get; }
		/// <summary>
		///     The language the default is written in, declared by <c>value=!xx</c>
		///     atop the file, or <see langword="null"/>. Where the default speaks a
		///     language (<see cref="WordsParser.DefaultSpeaks"/>), an empty entry
		///     falls back to it and misses nothing. It need not be in <see cref="Languages"/>.
		/// </summary>
		public string? DefaultLanguage { get; set; }
		/// <summary>
		///     The project settings file named by <c>param=</c>, as written (relative
		///     to the file), or empty; <see cref="SettingsPath()"/> resolves it.
		/// </summary>
		public string Settings { get; set; }
		/// <summary>
		///     The per-language settings files named by <c>param-xx=</c>, code → path
		///     as written; <see cref="SettingsPath(string)"/> resolves one.
		/// </summary>
		public Dictionary<string, string> LanguageSettings { get; }
		/// <summary>
		///     What the parser griped about while loading, and each value holding a brace
		///     that opens nothing (<see cref="ParameterUse.Malformed(WordsKey)"/>); the
		///     file loaded regardless.
		/// </summary>
		public IReadOnlyList<string> Errors { get; }
		/// <summary>
		///     A library file lists nothing: every label it declares is a <c>!Label</c>
		///     (or it declares none at all). Read from the table as it stands, which
		///     a manager may change.
		/// </summary>
		public bool IsLibrary => Languages.All(code => Labels.TryGetValue(code, out LanguageEntry? label) && label.NativeName.StartsWith('!'));
		/// <summary>
		///     Comment runs by the name of the block they sat above, as loaded —
		///     <see cref="KeyTree.Build(WordsFile)"/> anchors them. After that the tree
		///     is the truth; this is not updated.
		/// </summary>
		public IReadOnlyDictionary<string, string> BlockComments { get; }
		/// <summary>
		///     The line break the file was written with, <c>\n</c> or <c>\r\n</c>, which
		///     saving keeps so the file round-trips byte for byte; the system's for a
		///     file read without one, or imported.
		/// </summary>
		public string NewLine { get; }
		/// <summary>
		///     The encoding the file was read in, BOM and all, which saving keeps
		///     like <see cref="NewLine"/>: UTF-8, -16 or -32 by its BOM, UTF-8
		///     without one. Text it can't encode makes Save throw
		///     <see cref="System.Text.EncoderFallbackException"/>.
		/// </summary>
		public System.Text.Encoding Encoding { get; }
		//what the file was read as, or last written as, which Save patches (the editor's SPEC: Save as a patch)
		internal ReadAs? ReadAs { get; set; }

		internal WordsFile(string? path, string name, ILoadedWords loaded, string newLine, System.Text.Encoding encoding) {
			Path = path;
			Name = name;
			NewLine = newLine;
			Encoding = encoding;
			Preamble = loaded.Preamble;
			Trailer = loaded.Trailer;
			Languages = [.. loaded.DeclaredLanguages];
			Labels = [];
			foreach (string code in Languages) {
				if (loaded.KnownLanguages.TryGetValue(code, out LanguageEntry? written)) {
					Labels[code] = new LanguageEntry(written);
				}
			}
			DefaultLanguage = loaded.DefaultLanguage;
			Settings = loaded.Settings;
			LanguageSettings = new(loaded.LanguageSettings);
			List<string> errors = [.. loaded.Errors];
			foreach (string code in loaded.LanguageSettings.Keys) {
				if (!loaded.DeclaredLanguages.Contains(code)) {
					errors.Add($"param-{code} names a settings file for a language the file does not declare");
				}
			}
			//a brace that opens nothing, which an app shows as written with a warning, is
			//said here; the editor badges it and saves it, since a file it loaded must save
			foreach (var (keyName, key) in loaded.WordKeys) {
				foreach (var (field, braces) in ParameterUse.Malformed(key)) {
					errors.Add($"{keyName}: {field} holds {ParameterUse.Named(braces)}, shown as written in the app; {{{{ writes a brace");
				}
			}
			Errors = errors;
			BlockComments = loaded.BlockComments.ToDictionary();
		}

		/// <summary>The folder the file sits in — the working directory for a bare name, or a file not saved yet.</summary>
		public string Directory
			=> Path is null ? System.IO.Directory.GetCurrentDirectory() : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? "";

		/// <summary>The settings file <see cref="Settings"/> names, as an absolute path; <see langword="null"/> when it names none.</summary>
		public string? SettingsPath() => Resolve(Settings);

		/// <summary>The settings file <see cref="LanguageSettings"/> names for <paramref name="languageCode"/>, as an absolute path; <see langword="null"/> when it names none.</summary>
		public string? SettingsPath(string languageCode) => Resolve(LanguageSettings.GetValueOrDefault(languageCode, ""));

		private string? Resolve(string relative)
			=> relative == "" ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, relative));

		/// <summary>The name, for the debugger and for tests that print one.</summary>
		public override string ToString() => Name;
	}

	/// <summary>
	///     What a file was read as at <paramref name="Path"/>, or last written as: its bytes,
	///     which the file on disk must still be for Save to patch it, and the name each key had
	///     then, so a key renamed since is a rename (the editor's SPEC: Save as a patch).
	/// </summary>
	internal sealed record ReadAs(string Path, byte[] Bytes, IReadOnlyDictionary<WordsKey, string> Names) {
		//the file's keys by their names as they stand
		public static ReadAs Of(WordsFile file, string path, byte[] bytes)
			=> new(path, bytes, file.Store.Values.ToDictionary<WordsKey, WordsKey, string>(key => key, key => key.BlockKey, ReferenceEqualityComparer.Instance));

		//each key renamed since, from its name then to its name now
		public List<(string From, string To)> Renames(WordsFile file)
			=> [.. file.Store.Values.Where(key => Names.TryGetValue(key, out string? was) && was != key.BlockKey).Select(key => (Names[key], key.BlockKey))];
	}

	/// <summary>
	///     A file's settings references as written, <c>param=</c> and its
	///     <c>param-xx=</c> (<see cref="WordsFile.Settings"/>,
	///     <see cref="WordsFile.LanguageSettings"/>), copied: what a change to them is
	///     made against, and put back.
	/// </summary>
	public sealed record SettingsSlots(string Settings, IReadOnlyList<KeyValuePair<string, string>> Languages) {
		/// <summary>The file's slots as they stand.</summary>
		public static SettingsSlots Of(WordsFile file) => new(file.Settings, [.. file.LanguageSettings]);

		/// <summary>Whether the two name the same files, in the same order.</summary>
		public bool Matches(SettingsSlots other) => Settings == other.Settings && Languages.SequenceEqual(other.Languages);

		/// <summary>Puts the slots back in <paramref name="file"/>, in place of its own.</summary>
		public void PutBack(WordsFile file) {
			file.Settings = Settings;
			file.LanguageSettings.Clear();
			foreach (var (code, path) in Languages) {
				file.LanguageSettings[code] = path;
			}
		}
	}
}
