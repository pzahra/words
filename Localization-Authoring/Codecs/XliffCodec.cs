using System.Xml;
using System.Xml.Linq;

namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>
	///     XLIFF 1.2, the interchange format translation tools speak: one file per
	///     target language, a <c>trans-unit</c> per key with the default text as
	///     <c>source</c> and the entry as <c>target</c>. The one that barely loses.
	///     The four note channels are <c>note</c>s told apart by <c>from</c>
	///     (developer, translator) and <c>annotates</c> (source, target); an
	///     untranslated entry is a target in <c>needs-translation</c>, a stale one
	///     in <c>needs-review-translation</c> with its stale text in a Words
	///     attribute; the review flag is <c>approved="no"</c>; a constant keeps its
	///     <c>$</c> and is marked <c>translate="no"</c>; parameters ride as Words
	///     extension elements. Only the freeform comments, the settings references
	///     and the plural forms have nowhere to go: a form is no unit of its own to
	///     a translation tool. XLIFF 2.0 is another shape, refused with a gripe.
	/// </summary>
	public sealed class XliffCodec : IWordsImporter, IWordsExporter {
		/// <summary>The manifest name of the format's words.</summary>
		public const string WordsResource = "PatTech.Localization.Authoring.Codecs.xliff.words.ini";
		/// <summary>
		///     The option naming the <c>source-language</c> an export declares. Unset,
		///     it is the file's default language (<see cref="WordsFile.DefaultLanguage"/>),
		///     and <c>en</c> for a file that declares none, as XLIFF insists on a code.
		///     An import takes the attribute back as the default's language.
		/// </summary>
		public const string SourceLanguageOption = "source-language";
		private static readonly XNamespace Xliff = "urn:oasis:names:tc:xliff:document:1.2";
		private static readonly XNamespace Ext = "https://github.com/pzahra/words";

		/// <inheritdoc/>
		public WordsFormatInfo Info { get; } = new("xliff", [".xlf", ".xliff"], WordsFeatures.All & ~(WordsFeatures.FreeComments | WordsFeatures.Settings | WordsFeatures.PluralForms));

		/// <inheritdoc/>
		public TextReader? Init()
			=> new StreamReader(typeof(XliffCodec).Assembly.GetManifestResourceStream(WordsResource)
				?? throw new FileNotFoundException(WordsResource));

		/// <summary>
		///     Every culture sibling of the pick, by code: <c>Strings.fr.xlf</c>
		///     gathers <c>Strings.de.xlf</c> and itself. A pick named without a culture
		///     (<c>Strings.xlf</c>) leads its siblings; the file's own
		///     <c>target-language</c> decides the language either way.
		/// </summary>
		public IReadOnlyList<string> Discover(string path) {
			string full = Path.GetFullPath(path);
			string folder = Path.GetDirectoryName(full) ?? full;
			string extension = Path.GetExtension(full);
			var (stem, code) = FileNames.SplitCulture(Path.GetFileNameWithoutExtension(full));
			List<string> set = [];
			if (code == "" && File.Exists(full)) {
				set.Add(full);
			}
			set.AddRange(Directory.EnumerateFiles(folder, $"{stem}.*{extension}")
				.Where(sibling => FileNames.SplitCulture(Path.GetFileNameWithoutExtension(sibling)) is (var siblingStem, not "") && siblingStem == stem)
				.OrderBy(sibling => FileNames.SplitCulture(Path.GetFileNameWithoutExtension(sibling)).Code, StringComparer.Ordinal));
			return set;
		}

		/// <summary>The stem's <c>.ini</c>: <c>Strings.ini</c> for a set of <c>Strings.*.xlf</c>.</summary>
		public string NativePath(IReadOnlyList<string> paths) => FileNames.NativePath(paths);

		/// <inheritdoc/>
		public ILoadedWords Read(IReadOnlyList<string> paths, FormatOptions? options = null) {
			var loaded = new LoadedWords();
			foreach (string path in paths) {
				string file = Path.GetFileName(path);
				XDocument document;
				try {
					document = XDocument.Load(path);
				}
				catch (XmlException e) {
					loaded.Errors.Add($"{file}: not well-formed XML, skipped ({e.Message})");
					continue;
				}
				XElement? root = document.Root;
				if (root is null || root.Name.LocalName != "xliff") {
					loaded.Errors.Add($"{file}: not an XLIFF document, skipped");
					continue;
				}
				string version = (string?)root.Attribute("version") ?? "1.2";
				if (version != "1.2") {
					loaded.Errors.Add($"{file}: XLIFF {version} is not supported, 1.2 only; skipped");
					continue;
				}
				foreach (XElement fileElement in XmlText.Children(root, "file")) {
					//the source text is the default, so its language is the default's
					if (loaded.DefaultLanguage is null && (string?)fileElement.Attribute("source-language") is { Length: > 0 } sourceLanguage) {
						string declared = Code(sourceLanguage, file, loaded.Errors, "source-language");
						loaded.DefaultLanguage = declared == "" ? null : declared;
					}
					string code = Code((string?)fileElement.Attribute("target-language"), file, loaded.Errors);
					if (code != "") {
						FileNames.Declare(loaded, code);
					}
					bool targetsIgnored = false;
					foreach (XElement unit in fileElement.Descendants().Where(element => element.Name.LocalName == "trans-unit")) {
						ReadUnit(loaded, unit, code, file, ref targetsIgnored);
					}
					if (targetsIgnored) {
						loaded.Errors.Add($"{file}: targets in a file with no target-language were ignored");
					}
				}
			}
			return loaded;
		}

		private static void ReadUnit(LoadedWords loaded, XElement unit, string code, string file, ref bool targetsIgnored) {
			string id = (string?)unit.Attribute("id") ?? (string?)unit.Attribute("resname") ?? "";
			if (id == "") {
				loaded.Errors.Add($"{file}: a trans-unit without an id was skipped");
				return;
			}
			WordsKey key = loaded.Key(FileNames.BlockKey(loaded, id, file));
			if ((string?)unit.Attribute("translate") == "no" && !key.IsConstant) {
				loaded.Errors.Add($"{file}: translate=\"no\" on '{id}' ignored: a Words constant is a $key");
			}
			if ((string?)unit.Attribute("approved") == "no") {
				key.NeedsReview = true;
			}
			if (XmlText.Child(unit, "source") is { } source) {
				key.DefaultValue = source.Value;
			}
			WordsEntry? entry = code == "" ? null : key.Entries[code];
			if (XmlText.Child(unit, "target") is { } target) {
				if (entry is null) {
					targetsIgnored = true;
				}
				else {
					entry.Value = target.Value;
					string state = (string?)target.Attribute("state") ?? "";
					if ((string?)target.Attribute(Ext + "stale") is { } stale) {
						entry.Stale = stale;
					}
					else if (state.StartsWith("needs-review", StringComparison.Ordinal)) {
						entry.Stale = "";
					}
				}
			}
			foreach (XElement note in XmlText.Children(unit, "note")) {
				//a bare note is the developer's, on the source: the context channel
				bool translator = (string?)note.Attribute("from") == "translator";
				bool onTarget = entry is not null && (string?)note.Attribute("annotates") == "target";
				if (onTarget) {
					if (translator) {
						entry!.Comment = WordsOperations.Fold(entry.Comment, note.Value);
					}
					else {
						entry!.Context = WordsOperations.Fold(entry.Context, note.Value);
					}
				}
				else if (translator) {
					key.Comment = WordsOperations.Fold(key.Comment, note.Value);
				}
				else {
					key.Context = WordsOperations.Fold(key.Context, note.Value);
				}
			}
			foreach (XElement parameter in unit.Elements(Ext + "param")) {
				string name = (string?)parameter.Attribute("name") ?? "";
				if (name != "" && !key.Parameters.Any(existing => existing.Key == name)) {
					key.Parameters.Add(new WordsParameter(name, WordsParameterType.Select((string?)parameter.Attribute("type") ?? "String"), parameter.Value));
				}
			}
		}

		//the target language in canonical casing; one that says more than a code (a
		//variant, an extension) reads as the code it starts with, and one that starts
		//with none is no language, each with a gripe
		private static string Code(string? raw, string file, List<string> gripes, string attribute = "target-language") {
			if (string.IsNullOrEmpty(raw)) {
				return "";
			}
			if (LanguageCode.TryParse(raw, out var code)) {
				return code.ToString();
			}
			if (LanguageCode.TryRead(raw, out code)) {
				gripes.Add($"{file}: {attribute} '{raw}' read as {code}: {WordsParserToLocalizationProvider.LanguageCodeRule}");
				return code.ToString();
			}
			gripes.Add($"{file}: {attribute} '{raw}' ignored: {WordsParserToLocalizationProvider.LanguageCodeRule}");
			return "";
		}

		/// <summary>One file per declared language beside <paramref name="target"/>, its stem's (a culture tail on it is dropped).</summary>
		public IReadOnlyList<ExportUnit> Plan(ExportSource source, string target, FormatOptions? options = null) {
			string folder = Path.GetDirectoryName(target) ?? "";
			string extension = Info.Claims(target) ? Path.GetExtension(target) : Info.Extensions[0];
			var (stem, _) = FileNames.SplitCulture(Path.GetFileNameWithoutExtension(target));
			return [.. source.File.Languages.Select(code => new ExportUnit(Path.Combine(folder, $"{stem}.{code}{extension}"), [code]))];
		}

		/// <inheritdoc/>
		/// <exception cref="ArgumentException">The unit does not carry exactly one language: an XLIFF file is one target language.</exception>
		public void Write(ExportSource source, ExportUnit unit, TextWriter writer, ICollection<string> gripes, FormatOptions? options = null) {
			if (unit.Languages.Count != 1) {
				throw new ArgumentException("an XLIFF file carries one target language", nameof(unit));
			}
			string code = unit.Languages[0];
			string file = Path.GetFileName(unit.Path);
			string sourceLanguage = options?.GetValueOrDefault(SourceLanguageOption) is { Length: > 0 } asked ? asked : source.File.DefaultLanguage ?? "en";
			WordsFeatures used = source.Used();
			if ((used & WordsFeatures.FreeComments) != 0) {
				gripes.Add($"{file}: dropped the preamble and the comments between blocks: XLIFF has no slot for them");
			}
			if ((used & WordsFeatures.Settings) != 0) {
				gripes.Add($"{file}: dropped the settings references: XLIFF has no slot for them");
			}
			var body = new XElement(Xliff + "body");
			int forms = 0;
			foreach (WordsKey key in source.Keys()) {
				forms += key.Forms.Written().Count() + (key.Entries.GetValueOrDefault(code)?.Forms.Written().Count() ?? 0);
				string name = key.BlockKey[(key.BlockKey.IndexOf('.') + 1)..];
				var trans = new XElement(Xliff + "trans-unit", new XAttribute("id", name), new XAttribute(XNamespace.Xml + "space", "preserve"));
				if (key.IsConstant) {
					trans.Add(new XAttribute("translate", "no"));
				}
				if (key.NeedsReview) {
					trans.Add(new XAttribute("approved", "no"));
				}
				trans.Add(new XElement(Xliff + "source", key.DefaultValue));
				key.Entries.TryGetValue(code, out WordsEntry? entry);
				string value = entry?.Value ?? "";
				var target = new XElement(Xliff + "target", value);
				target.Add(new XAttribute("state", value == "" ? "needs-translation" : entry?.Stale is not null ? "needs-review-translation" : "translated"));
				if (entry?.Stale is { } stale) {
					target.Add(new XAttribute(Ext + "stale", stale));
				}
				trans.Add(target);
				Note(trans, "developer", "source", key.Context);
				Note(trans, "translator", "source", key.Comment);
				if (entry is not null) {
					Note(trans, "developer", "target", entry.Context);
					Note(trans, "translator", "target", entry.Comment);
				}
				foreach (WordsParameter parameter in key.Parameters) {
					trans.Add(new XElement(Ext + "param", new XAttribute("name", parameter.Key), new XAttribute("type", parameter.DataType.Name), parameter.Value));
				}
				body.Add(trans);
			}
			if (forms != 0) {
				gripes.Add($"{file}: dropped the plural forms ({forms}): XLIFF has no slot for them");
			}
			var root = new XElement(Xliff + "xliff",
				new XAttribute("version", "1.2"),
				new XAttribute(XNamespace.Xmlns + "w", Ext.NamespaceName),
				new XElement(Xliff + "file",
					new XAttribute("original", Path.GetFileName(source.File.Path)),
					new XAttribute("source-language", sourceLanguage),
					new XAttribute("target-language", code),
					new XAttribute("datatype", "plaintext"),
					body));
			XmlText.Write(writer, root);
		}

		private static void Note(XElement unit, string from, string annotates, string text) {
			if (text != "") {
				unit.Add(new XElement(Xliff + "note", new XAttribute("from", from), new XAttribute("annotates", annotates), text));
			}
		}
	}
}
