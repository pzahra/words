using PatTech.Localization.Authoring.Codecs;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The formats an editor knows, in registration order: the built-ins, then
	///     whatever a third-party assembly's entry point adds at startup. The
	///     Import and Export lists are built from here, and so are the formats'
	///     words.
	/// </summary>
	public sealed class WordsFormats {
		private readonly List<IWordsFormat> formats = [];

		/// <summary>Every registered format.</summary>
		public IReadOnlyList<IWordsFormat> All => formats;
		/// <summary>The formats that read.</summary>
		public IEnumerable<IWordsImporter> Importers => formats.OfType<IWordsImporter>();
		/// <summary>The formats that write.</summary>
		public IEnumerable<IWordsExporter> Exporters => formats.OfType<IWordsExporter>();

		/// <summary>The built-in formats, registered: <see cref="IniCodec"/>, <see cref="ResxCodec"/>, <see cref="XliffCodec"/>.</summary>
		public static WordsFormats BuiltIn() {
			var registry = new WordsFormats();
			registry.Add(new IniCodec());
			registry.Add(new ResxCodec());
			registry.Add(new XliffCodec());
			return registry;
		}

		/// <summary>Registers <paramref name="format"/>.</summary>
		/// <exception cref="ArgumentException">A format with the same id is already registered.</exception>
		public void Add(IWordsFormat format) {
			if (Find(format.Info.Id) is not null) {
				throw new ArgumentException($"a format with id '{format.Info.Id}' is already registered", nameof(format));
			}
			formats.Add(format);
		}

		/// <summary>The format with <paramref name="id"/>, if any.</summary>
		public IWordsFormat? Find(string id) => formats.FirstOrDefault(format => format.Info.Id == id);

		/// <summary>The first importer claiming <paramref name="path"/>'s extension, if any.</summary>
		public IWordsImporter? ImporterFor(string path) => Importers.FirstOrDefault(format => format.Info.Claims(path));

		/// <summary>The first exporter claiming <paramref name="path"/>'s extension, if any.</summary>
		public IWordsExporter? ExporterFor(string path) => Exporters.FirstOrDefault(format => format.Info.Claims(path));

		/// <summary>The manifest name of the seam's own words: the names <see cref="WordsFeatures"/> carries by key.</summary>
		public const string WordsResource = "PatTech.Localization.Authoring.authoring-words.ini";

		/// <summary>
		///     Loads the seam's own words — the feature names — then every format's
		///     into <paramref name="builder"/>. Call it before loading the host's own,
		///     which then win, or add: a host's <c>feature.x.sub</c> stacks on the
		///     seam's <c>feature.x</c>.
		/// </summary>
		public WordsBuilder LoadWords(WordsBuilder builder) {
			using (var seam = new StreamReader(typeof(WordsFormats).Assembly.GetManifestResourceStream(WordsResource) ?? throw new FileNotFoundException(WordsResource))) {
				builder.Load(seam);
			}
			foreach (IWordsFormat format in formats) {
				using TextReader? words = format.Init();
				if (words is not null) {
					builder.Load(words);
				}
			}
			return builder;
		}
	}
}
