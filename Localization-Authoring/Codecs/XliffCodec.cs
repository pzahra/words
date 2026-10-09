using System.Text;
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
	///     a translation tool. XLIFF 2.0 is another shape, refused with a gripe. On
	///     the way in a unit is its <c>file</c>'s <c>original</c> and its <c>id</c>,
	///     so two never become one key, and its key is the <c>resname</c> any file
	///     of the set gives it, else its <c>id</c>; inline codes read as the text
	///     they stand for.
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
			List<(string File, XElement Root)> documents = [];
			foreach (string path in paths) {
				string file = Path.GetFileName(path);
				XDocument document;
				try {
					//whitespace between two inline codes is text, xml:space or not
					document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
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
				documents.Add((file, root));
			}
			var names = Names(documents, loaded.Errors);
			HashSet<(Unit Unit, string Code)> read = [];
			foreach (var (file, root) in documents) {
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
					string original = Original(fileElement);
					foreach (XElement unit in TransUnits(fileElement)) {
						ReadUnit(loaded, unit, code, file, original, names, read, ref targetsIgnored);
					}
					if (targetsIgnored) {
						loaded.Errors.Add($"{file}: targets in a file with no target-language were ignored");
					}
				}
			}
			return loaded;
		}

		//a trans-unit is its <file>'s original and its id, since an id is only its
		//file's own; one with no id is known by its resname, kept apart from any id
		private readonly record struct Unit(string Original, string Id, string Resname);

		private static Unit? UnitOf(XElement unit, string original) {
			string id = (string?)unit.Attribute("id") ?? "";
			string resname = (string?)unit.Attribute("resname") ?? "";
			return id == "" && resname == "" ? null : new Unit(original, id, id == "" ? resname : "");
		}

		private static string Original(XElement fileElement) => (string?)fileElement.Attribute("original") ?? "";

		private static IEnumerable<XElement> TransUnits(XElement fileElement)
			=> fileElement.Descendants().Where(element => element.Name.LocalName == "trans-unit");

		//each unit's name, the resname any file of the set gives it, read ahead of the
		//units, so a file without one doesn't name the key by its id first; a unit the
		//set names two ways keeps the first, with a gripe
		private static Dictionary<Unit, string> Names(List<(string File, XElement Root)> documents, List<string> gripes) {
			Dictionary<Unit, string> names = [];
			foreach (var (file, root) in documents) {
				foreach (XElement fileElement in XmlText.Children(root, "file")) {
					string original = Original(fileElement);
					foreach (XElement unit in TransUnits(fileElement)) {
						if (UnitOf(unit, original) is not { } which || (string?)unit.Attribute("resname") is not { Length: > 0 } resname) {
							continue;
						}
						if (!names.TryAdd(which, resname) && names[which] != resname) {
							gripes.Add($"{file}: {Shown(which, names[which])} is named '{resname}' here, and keeps the name it was given first");
						}
					}
				}
			}
			return names;
		}

		//a unit as a gripe names it
		private static string Shown(Unit unit, string name)
			=> $"'{name}'" + (unit.Id != "" && unit.Id != name ? $" (id {unit.Id})" : "") + (unit.Original == "" ? "" : $" of {unit.Original}");

		//a unit's key is its name, the resource's own, else its id. A unit met again
		//for the same language is a duplicate, and the first stands
		private static void ReadUnit(LoadedWords loaded, XElement unit, string code, string file, string original, Dictionary<Unit, string> names, HashSet<(Unit, string)> read, ref bool targetsIgnored) {
			if (UnitOf(unit, original) is not { } which) {
				loaded.Errors.Add($"{file}: a trans-unit without an id was skipped");
				return;
			}
			string name = names.GetValueOrDefault(which) ?? which.Id;
			string shown = Shown(which, name);
			if (!read.Add((which, code))) {
				loaded.Errors.Add($"{file}: trans-unit {shown} came twice{(code == "" ? "" : $" for {code}")}, and the first stands");
				return;
			}
			WordsKey key = loaded.Key(FileNames.BlockKey(loaded, name, file, which, shown));
			if ((string?)unit.Attribute("translate") == "no" && !key.IsConstant) {
				loaded.Errors.Add($"{file}: translate=\"no\" on '{name}' ignored: a Words constant is a $key");
			}
			if ((string?)unit.Attribute("approved") == "no") {
				key.NeedsReview = true;
			}
			if (XmlText.Child(unit, "source") is { } source) {
				key.DefaultValue = Text(source, file, shown, loaded.Errors);
			}
			WordsEntry? entry = code == "" ? null : key.Entries[code];
			if (XmlText.Child(unit, "target") is { } target) {
				if (entry is null) {
					targetsIgnored = true;
				}
				else {
					entry.Value = Text(target, file, shown, loaded.Errors);
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
				string named = (string?)parameter.Attribute("name") ?? "";
				if (named != "" && !key.Parameters.Any(existing => existing.Key == named)) {
					key.Parameters.Add(new WordsParameter(named, WordsParameterType.Select((string?)parameter.Attribute("type") ?? "String"), parameter.Value));
				}
			}
		}

		//a source's or target's text, its inline codes read as what they stand for: the
		//native code a ph, bpt, ept or it holds, else the equiv-text it or an x, bx or
		//ex carries; a mrk is a mark on the text inside it, and a g, or what is no
		//inline code at all, keeps its text and drops its own codes. What is dropped is a gripe
		private static string Text(XElement element, string file, string identity, List<string> gripes) {
			List<string> dropped = [];
			string text = Inline(element, dropped);
			if (dropped.Count != 0) {
				gripes.Add($"{file}: {identity} {element.Name.LocalName}: dropped the inline codes {string.Join(", ", dropped)}, which carry no text");
			}
			return text;
		}

		private static string Inline(XElement element, List<string> dropped) {
			var text = new StringBuilder();
			foreach (XNode node in element.Nodes()) {
				if (node is XText run) {
					text.Append(run.Value); //CDATA too
				}
				else if (node is XElement code) {
					switch (code.Name.LocalName) {
						case "ph" or "bpt" or "ept" or "it" when code.Value != "":
							text.Append(code.Value); //a sub inside is part of the code
							break;
						case "ph" or "bpt" or "ept" or "it" or "x" or "bx" or "ex":
							if ((string?)code.Attribute("equiv-text") is { } equivalent) {
								text.Append(equivalent);
							}
							else {
								dropped.Add(Tag(code));
							}
							break;
						case "mrk":
							text.Append(Inline(code, dropped));
							break;
						default:
							text.Append(Inline(code, dropped));
							dropped.Add(Tag(code));
							break;
					}
				}
			}
			return text.ToString();
		}

		private static string Tag(XElement code)
			=> $"<{code.Name.LocalName}{(code.Attribute("id") is { } id ? $" id=\"{id.Value}\"" : "")}{(code.IsEmpty ? "/" : "")}>";

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
