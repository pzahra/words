namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>
	///     The native format, written to the format interface: the reference
	///     implementation, and the proof the interface is enough. Reading is the
	///     parser, writing is the writer, and nothing is lost either way. Save does
	///     not route through here — <see cref="WordsSession.Save(WordsFile, IKeyTreeNode)"/>
	///     calls the writer itself — but the bytes are the same.
	/// </summary>
	public sealed class IniCodec : IWordsImporter, IWordsExporter {
		/// <summary>The manifest name of the format's words.</summary>
		public const string WordsResource = "PatTech.Localization.Authoring.Codecs.ini.words.ini";

		/// <inheritdoc/>
		public WordsFormatInfo Info { get; } = new("ini", [".ini"], WordsFeatures.All);

		/// <inheritdoc/>
		public TextReader? Init()
			=> new StreamReader(typeof(IniCodec).Assembly.GetManifestResourceStream(WordsResource)
				?? throw new FileNotFoundException(WordsResource));

		/// <summary>One file carries every language: the pick alone.</summary>
		public IReadOnlyList<string> Discover(string path) => [path];

		/// <inheritdoc/>
		/// <exception cref="ArgumentException">Not exactly one path: ini is one file.</exception>
		public ILoadedWords Read(IReadOnlyList<string> paths, FormatOptions? options = null) {
			if (paths.Count != 1) {
				throw new ArgumentException("ini reads one file", nameof(paths));
			}
			var loaded = new WordsParserToLocalizationProvider();
			using var reader = File.OpenText(paths[0]);
			new WordsParser(loaded).Load(reader);
			return loaded;
		}

		/// <summary>One unit at <paramref name="target"/>, carrying the file's own language table.</summary>
		public IReadOnlyList<ExportUnit> Plan(ExportSource source, string target, FormatOptions? options = null)
			=> [new ExportUnit(target, [.. source.File.Languages])];

		/// <summary>Exactly what Save writes; the unit's languages are the file's own, so they are not consulted.</summary>
		public void Write(ExportSource source, ExportUnit unit, TextWriter writer, ICollection<string> gripes, FormatOptions? options = null)
			=> source.Session.Save(source.File, source.Tree, writer);
	}
}
