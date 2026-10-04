using System.Globalization;
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

		/// <summary>True when <paramref name="tail"/> names a culture the platform knows; the canonical code comes back.</summary>
		public static bool TryCulture(string tail, out string code) {
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

		/// <summary>The native <c>.ini</c> beside a set: its stem, culture tail dropped, in the first file's folder.</summary>
		public static string NativePath(IReadOnlyList<string> paths) {
			string first = paths[0];
			return Path.Combine(Path.GetDirectoryName(first) ?? "", SplitCulture(Path.GetFileNameWithoutExtension(first)).Stem + ".ini");
		}

		/// <summary>
		///     A foreign name as a block key: anything but <c>]</c> goes, and a leading
		///     dot would read as relative to the block before it. A change is a gripe.
		/// </summary>
		public static string BlockKey(string name, string file, List<string> gripes) {
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
