using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>What the file-per-culture formats share: telling a culture tail from a stem, and making a name a block key.</summary>
	internal static class FileNames {
		/// <summary>
		///     A name without its extension into stem and culture code:
		///     <c>Strings.fr-CA</c> is (<c>Strings</c>, <c>fr-CA</c>); <c>Strings</c> and
		///     a tail no culture answers to (<c>Strings.Designer</c>) have an empty
		///     code. The code comes back in canonical casing.
		/// </summary>
		public static (string Stem, string Code) SplitCulture(string name) {
			int dot = name.LastIndexOf('.');
			if (dot > 0 && TryCulture(name[(dot + 1)..], out string code)) {
				return (name[..dot], code);
			}
			return (name, "");
		}

		/// <summary>True when <paramref name="tail"/> is a language code that names a culture the platform knows; the canonical code comes back.</summary>
		public static bool TryCulture(string tail, out string code) {
			code = "";
			if (!LanguageCode.TryParse(tail, out var parsed)) {
				return false;
			}
			try {
				CultureInfo.GetCultureInfo(tail, predefinedOnly: true);
				code = parsed.ToString();
				return true;
			}
			catch (CultureNotFoundException) {
				return false;
			}
		}

		/// <summary>The native <c>.ini</c> beside a set: its stem, culture tail dropped, in the first file's folder.</summary>
		public static string NativePath(IReadOnlyList<string> paths) {
			string first = paths[0];
			return Path.Combine(Path.GetDirectoryName(first) ?? "", SplitCulture(Path.GetFileNameWithoutExtension(first)).Stem + ".ini");
		}

		/// <summary>
		///     A foreign name as a block key: a key's name as it is (runtime SPEC: Key
		///     names), otherwise each dotted segment made one — what is no letter, digit,
		///     <c>_</c> or <c>-</c> becomes <c>_</c>, and an empty or dash-led segment
		///     gains one, so <c>$this.Text</c> loads as <c>_this.Text</c>. Two units made
		///     one key are told apart with a number, never overwritten, and a unit met
		///     again in another file of the set comes back to the key it took first. A
		///     change is a gripe.
		/// </summary>
		/// <param name="loaded">The words the set loads into.</param>
		/// <param name="name">The unit's name in its format, the same wherever the set meets the unit.</param>
		/// <param name="file">The file it is read from, for a gripe.</param>
		/// <param name="unit">What tells the unit from another of the same name, compared by value; the name itself when the format has nothing more.</param>
		/// <param name="shown">The unit as a gripe names it; the quoted name when omitted.</param>
		public static string BlockKey(LoadedWords loaded, string name, string file, object? unit = null, string? shown = null) {
			unit ??= name;
			shown ??= $"'{name}'";
			string made = WordsParser.IsKeyName(name) ? name : string.Join('.', name.Split('.').Select(Segment));
			if (!loaded.ForeignUnits.TryGetValue(unit, out string? blockKey)) {
				blockKey = made;
				for (int n = 2; loaded.ForeignNames.ContainsKey(blockKey); n++) {
					blockKey = $"{made}-{n}";
				}
				loaded.ForeignUnits[unit] = blockKey;
				loaded.ForeignNames[blockKey] = shown;
			}
			if (blockKey != made && loaded.ForeignNames.TryGetValue(made, out string? other)) {
				loaded.Errors.Add($"{file}: {shown} would load as '{made}', which {other} already is, so it loads as '{blockKey}'");
			}
			else if (made != name) {
				loaded.Errors.Add($"{file}: '{name}' is no words.ini key, loaded as '{made}'");
			}
			return blockKey;
		}

		//a foreign segment made one by the one check (WordsParser.IsKeySegment), a code
		//point at a time, in NFC: what may not follow becomes _, and what may follow but
		//not start, a mark or a dash, gets a _ before it
		private static string Segment(string segment) {
			var made = new StringBuilder();
			foreach (Rune rune in segment.Normalize().EnumerateRunes()) {
				if (WordsParser.IsKeySegment($"{made}{rune}")) {
					made.Append(rune);
				}
				else if (made.Length == 0 && WordsParser.IsKeySegment($"_{rune}")) {
					made.Append('_').Append(rune);
				}
				else {
					made.Append('_');
				}
			}
			return made.Length == 0 ? "_" : made.ToString();
		}

		/// <summary>
		///     The language a code declares: its culture's native name when the
		///     platform knows it, the code itself when it does not. The English name
		///     comes along only where the default is English (or undeclared), since
		///     exonyms are named in the default's language.
		/// </summary>
		public static LanguageEntry Declare(LoadedWords loaded, string code) {
			try {
				CultureInfo culture = CultureInfo.GetCultureInfo(code, predefinedOnly: true);
				bool english = WordsParser.DefaultSpeaks("en", loaded.DefaultLanguage ?? "en");
				return loaded.Declare(code, culture.NativeName, english ? culture.EnglishName : null);
			}
			catch (CultureNotFoundException) {
				return loaded.Declare(code, code);
			}
		}
	}

	/// <summary>Writing an XML document the way the ini writer writes text: the writer's own line ending, no BOM decisions.</summary>
	internal static class XmlText {
		/// <summary>
		///     <paramref name="root"/> as an indented document with a UTF-8
		///     declaration. The declaration is written by hand: an <see cref="XmlWriter"/>
		///     over a <see cref="TextWriter"/> would name that writer's encoding, which
		///     is UTF-16 for a string.
		/// </summary>
		public static void Write(TextWriter writer, XElement root) {
			writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
			writer.Write(writer.NewLine);
			var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true, NewLineChars = writer.NewLine, CloseOutput = false };
			using (XmlWriter xml = XmlWriter.Create(writer, settings)) {
				root.Save(xml);
			}
			writer.Write(writer.NewLine);
		}

		/// <summary>The children of <paramref name="element"/> named <paramref name="localName"/>, whatever namespace the document put them in.</summary>
		public static IEnumerable<XElement> Children(XElement element, string localName)
			=> element.Elements().Where(child => child.Name.LocalName == localName);

		/// <summary>The first child named <paramref name="localName"/>, if any.</summary>
		public static XElement? Child(XElement element, string localName) => Children(element, localName).FirstOrDefault();
	}
}
