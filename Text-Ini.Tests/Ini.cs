using System.Text;

namespace PatTech.Text.Ini.Tests;

//a document from text, in UTF-8 with no mark, read by the sections dialect over rules
internal static class Ini {
	public static IniDocument Read(string text, IniRules? rules = null)
		=> IniDocument.Read(new UTF8Encoding(false).GetBytes(text), IniDialect.Sections(rules ?? new IniRules()));
}
