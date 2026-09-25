using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>
	///     .NET resources: one file per culture — <c>Strings.resx</c> the neutral
	///     defaults, <c>Strings.fr.resx</c> French — each <c>data</c> a key with a
	///     <c>value</c> and an optional <c>comment</c>. The comment is the context
	///     as provided: the neutral file's is the key's, a culture file's is that
	///     language's entry's. Constants keep their <c>$</c> in the name, so they
	///     come back as constants. Everything else the model holds — the
	///     translator-facing comment channels, parameters, stale marks, the review
	///     flag, freeform comments, settings references — resx has no slot for and
	///     an export drops with a gripe. Typed and binary resources are skipped on
	///     the way in, with a gripe; a culture file carries only what is
	///     translated, since an empty value in a satellite would shadow the default
	///     rather than fall back to it.
	/// </summary>
	public sealed class ResxCodec : IWordsImporter, IWordsExporter {
		/// <summary>The manifest name of the format's words.</summary>
		public const string WordsResource = "PatTech.Localization.Authoring.Codecs.resx.words.ini";
		private const string Extension = ".resx";

		/// <inheritdoc/>
		public WordsFormatInfo Info { get; } = new("resx", [Extension], WordsFeatures.Context | WordsFeatures.EntryContext | WordsFeatures.Constants);

		/// <inheritdoc/>
		public TextReader? Init()
			=> new StreamReader(typeof(ResxCodec).Assembly.GetManifestResourceStream(WordsResource)
				?? throw new FileNotFoundException(WordsResource));

		/// <summary>
		///     The neutral file first, when it exists, then every culture sibling of
		///     the pick by code: <c>Strings.fr.resx</c> gathers <c>Strings.resx</c>,
		///     <c>Strings.de.resx</c> and itself. A sibling whose tail is no culture
		///     (<c>Strings.Designer.resx</c>) is another resource altogether.
		/// </summary>
		public IReadOnlyList<string> Discover(string path) {
			string full = Path.GetFullPath(path);
			string folder = Path.GetDirectoryName(full) ?? full;
			var (stem, _) = Split(Path.GetFileName(full));
			List<string> set = [];
			string neutral = Path.Combine(folder, stem + Extension);
			if (File.Exists(neutral)) {
				set.Add(neutral);
			}
			set.AddRange(Directory.EnumerateFiles(folder, $"{stem}.*{Extension}")
				.Where(sibling => Split(Path.GetFileName(sibling)) is (var siblingStem, not "") && siblingStem == stem)
				.OrderBy(sibling => Split(Path.GetFileName(sibling)).Code, StringComparer.Ordinal));
			return set;
		}

		/// <inheritdoc/>
		public ILoadedWords Read(IReadOnlyList<string> paths, FormatOptions? options = null) {
			var loaded = new LoadedWords();
			//the neutral file first whatever the order of the set: its keys set the order
			var ordered = paths.OrderBy(path => Split(Path.GetFileName(path)).Code == "" ? 0 : 1).ToList();
			if (ordered.Count == 0 || Split(Path.GetFileName(ordered[0])).Code != "") {
				loaded.Errors.Add("no neutral .resx in the set: the keys have no default text");
			}
			foreach (string path in ordered) {
				string file = Path.GetFileName(path);
				string code = Split(file).Code;
				if (code != "") {
					CultureInfo culture = CultureInfo.GetCultureInfo(code);
					loaded.Declare(code, culture.NativeName, culture.EnglishName);
				}
				XDocument document;
				try {
					document = XDocument.Load(path);
				}
				catch (XmlException e) {
					loaded.Errors.Add($"{file}: not well-formed XML, skipped ({e.Message})");
					continue;
				}
				List<XElement> items = [.. document.Root?.Elements("data") ?? []];
				if (items.Count == 0) {
					loaded.Errors.Add($"{file}: no string resources found");
				}
				foreach (XElement data in items) {
					string name = (string?)data.Attribute("name") ?? "";
					if (name == "") {
						loaded.Errors.Add($"{file}: a data element without a name was skipped");
						continue;
					}
					if (data.Attribute("type") is not null || data.Attribute("mimetype") is not null) {
						loaded.Errors.Add($"{file}: '{name}' is not a string resource, skipped");
						continue;
					}
					string value = (string?)data.Element("value") ?? "";
					string comment = (string?)data.Element("comment") ?? "";
					WordsKey key = loaded.Key(BlockKey(name, file, loaded.Errors));
					if (code == "") {
						key.DefaultValue = value;
						key.Context = comment;
					}
					else {
						WordsEntry entry = key.Entries[code];
						entry.Value = value;
						entry.Context = comment;
					}
				}
			}
			return loaded;
		}

		//a resx name is any string; a block key is anything but `]`, and a leading
		//dot would read as relative to the block before it
		private static string BlockKey(string name, string file, List<string> gripes) {
			string blockKey = name.Replace(']', '_').TrimStart('.');
			if (blockKey == "") {
				blockKey = "_";
			}
			if (blockKey != name) {
				gripes.Add($"{file}: '{name}' is no words.ini key, loaded as '{blockKey}'");
			}
			return blockKey;
		}

		/// <summary>
		///     The neutral file at <paramref name="target"/> (a culture tail on it is
		///     dropped) and one culture file per declared language beside it.
		/// </summary>
		public IReadOnlyList<ExportUnit> Plan(ExportSource source, string target, FormatOptions? options = null) {
			string folder = Path.GetDirectoryName(target) ?? "";
			var (stem, _) = Split(Path.GetFileName(target));
			List<ExportUnit> units = [new ExportUnit(Path.Combine(folder, stem + Extension), [])];
			foreach (string code in source.File.Languages) {
				units.Add(new ExportUnit(Path.Combine(folder, $"{stem}.{code}{Extension}"), [code]));
			}
			return units;
		}

		/// <inheritdoc/>
		/// <exception cref="ArgumentException">The unit carries more than one language: a resx file is one culture.</exception>
		public void Write(ExportSource source, ExportUnit unit, TextWriter writer, ICollection<string> gripes, FormatOptions? options = null) {
			if (unit.Languages.Count > 1) {
				throw new ArgumentException("a resx file carries one culture", nameof(unit));
			}
			string? code = unit.Languages.Count == 0 ? null : unit.Languages[0];
			string file = Path.GetFileName(unit.Path);
			var dropped = new Dictionary<string, int>();
			var root = new XElement("root",
				Header("resmimetype", "text/microsoft-resx"),
				Header("version", "2.0"),
				Header("reader", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
				Header("writer", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"));
			if (code is null) {
				Drop("the preamble and the comments between blocks", source.File.Preamble != "" || (source.Used() & WordsFeatures.FreeComments) != 0 ? 1 : 0);
				Drop("the settings references", source.File.Settings != "" || source.File.LanguageSettings.Values.Any(path => path != "") ? 1 : 0);
			}
			foreach (WordsKey key in source.Keys()) {
				string name = key.BlockKey[(key.BlockKey.IndexOf('.') + 1)..];
				if (code is null) {
					root.Add(Data(name, key.DefaultValue, key.Context));
					Drop("comment=", key.Comment != "" ? 1 : 0);
					Drop("param-x=", key.Parameters.Count);
					Drop("stale=", key.NeedsReview ? 1 : 0);
				}
				else if (key.Entries.TryGetValue(code, out WordsEntry? entry)) {
					if (entry.Value == "") {
						//no value, no data: an empty satellite entry would shadow the default
						Drop($"context-{code}= on an untranslated key", entry.Context != "" ? 1 : 0);
						continue;
					}
					root.Add(Data(name, entry.Value, entry.Context));
					Drop($"comment-{code}=", entry.Comment != "" ? 1 : 0);
					Drop($"stale-{code}=", entry.Stale is not null ? 1 : 0);
				}
			}
			foreach (var (what, count) in dropped) {
				gripes.Add($"{file}: dropped {what} ({count}): resx has no slot for it");
			}

			writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
			writer.Write(writer.NewLine);
			var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true, NewLineChars = writer.NewLine, CloseOutput = false };
			using (XmlWriter xml = XmlWriter.Create(writer, settings)) {
				root.Save(xml);
			}
			writer.Write(writer.NewLine);

			void Drop(string what, int count) {
				if (count != 0) {
					dropped[what] = dropped.GetValueOrDefault(what) + count;
				}
			}
		}

		private static XElement Header(string name, string value)
			=> new("resheader", new XAttribute("name", name), new XElement("value", value));

		private static XElement Data(string name, string value, string comment) {
			var data = new XElement("data",
				new XAttribute("name", name),
				new XAttribute(XNamespace.Xml + "space", "preserve"),
				new XElement("value", value));
			if (comment != "") {
				data.Add(new XElement("comment", comment));
			}
			return data;
		}

		/// <summary>
		///     A file name into its stem and culture code: <c>Strings.fr-CA.resx</c>
		///     is (<c>Strings</c>, <c>fr-CA</c>); <c>Strings.resx</c> and a tail no
		///     culture answers to (<c>Strings.Designer.resx</c>) have an empty code.
		///     The code comes back in canonical casing.
		/// </summary>
		public static (string Stem, string Code) Split(string fileName) {
			string name = fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? fileName[..^Extension.Length] : fileName;
			int dot = name.LastIndexOf('.');
			if (dot > 0 && TryCulture(name[(dot + 1)..], out string code)) {
				return (name[..dot], code);
			}
			return (name, "");
		}

		private static bool TryCulture(string tail, out string code) {
			code = "";
			if (tail == "" || !tail.All(c => char.IsLetterOrDigit(c) || c == '-')) {
				return false;
			}
			try {
				CultureInfo.GetCultureInfo(tail, predefinedOnly: true);
				code = WordsParser.NormalizeLanguageCasing(tail);
				return true;
			}
			catch (Exception e) when (e is CultureNotFoundException or ArgumentException) {
				return false;
			}
		}
	}
}
