namespace PatTech.Localization.Authoring {
	/// <summary>
	///     One loaded <c>words.ini</c>: everything of the file's that is not a key.
	///     The keys themselves live in the session's store, prefixed with this
	///     file's <see cref="Label"/>; the file is identified by its
	///     <see cref="Path"/>, so two <c>strings.ini</c> in different folders are two
	///     files (the second gets a disambiguated label).
	/// </summary>
	public sealed class WordsFile {
		/// <summary>The path the file was loaded from — its identity, and where it saves.</summary>
		public string Path { get; }
		/// <summary>
		///     The tree prefix its keys carry (<c>label.group.key</c>): the file name
		///     without extension, dots replaced and a <c>-2</c>, <c>-3</c>… suffix
		///     when another loaded file already took it. Dot-free by construction,
		///     since the writer drops exactly one leading segment.
		/// </summary>
		public string Label { get; }
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
		///     Saving writes exactly these (with the session's current labels), so a
		///     main file never absorbs a library's extras.
		/// </summary>
		public List<string> Languages { get; }
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
		/// <summary>What the parser griped about while loading; the file loaded regardless.</summary>
		public IReadOnlyList<string> Errors { get; }
		/// <summary>
		///     A library file lists nothing: every label it declares is a <c>!Label</c>
		///     (or it declares none at all).
		/// </summary>
		public bool IsLibrary { get; }
		/// <summary>
		///     Comment runs by the (prefixed) block key they sat above, as loaded —
		///     <see cref="KeyTree.Build(WordsSession, WordsFile)"/> anchors them. After
		///     that the tree is the truth; this is not updated.
		/// </summary>
		public IReadOnlyDictionary<string, string> BlockComments { get; }

		internal WordsFile(string path, string label, WordsParserToLocalizationProvider loaded) {
			Path = path;
			Label = label;
			Preamble = loaded.Preamble;
			Trailer = loaded.Trailer;
			Languages = [.. loaded.DeclaredLanguages];
			Settings = loaded.Settings;
			LanguageSettings = new(loaded.LanguageSettings);
			List<string> errors = [.. loaded.Errors];
			foreach (string code in loaded.LanguageSettings.Keys) {
				if (!loaded.DeclaredLanguages.Contains(code)) {
					errors.Add($"param-{code} names a settings file for a language the file does not declare");
				}
			}
			Errors = errors;
			IsLibrary = loaded.DeclaredLanguages.All(code => loaded.KnownLanguages[code].NativeName.StartsWith('!'));
			BlockComments = loaded.BlockComments.ToDictionary(pair => $"{label}.{pair.Key}", pair => pair.Value);
		}

		/// <summary>The folder the file sits in — the working directory for a bare name.</summary>
		public string Directory
			=> System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? "";

		/// <summary>The settings file <see cref="Settings"/> names, as an absolute path; <see langword="null"/> when it names none.</summary>
		public string? SettingsPath() => Resolve(Settings);

		/// <summary>The settings file <see cref="LanguageSettings"/> names for <paramref name="languageCode"/>, as an absolute path; <see langword="null"/> when it names none.</summary>
		public string? SettingsPath(string languageCode) => Resolve(LanguageSettings.GetValueOrDefault(languageCode, ""));

		private string? Resolve(string relative)
			=> relative == "" ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, relative));

		/// <summary>The label, for the debugger and for tests that print one.</summary>
		public override string ToString() => Label;
	}
}
