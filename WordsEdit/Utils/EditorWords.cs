using System.Globalization;
using System.IO;

namespace WordsEdit.Utils;

/// <summary>
///     Wordsmith's own words (SPEC: Wordsmith's own words): the embedded
///     <c>Resources/words.ini</c>, loaded into <see cref="Words.Known"/> before the
///     first window, in the language the command line asks for or, failing that,
///     the saved setting (<see cref="EditorConfig"/>) or the one the OS speaks.
///     <c>{l:Words}</c> resolves when a window loads, so a change of language is
///     saved and takes effect when the editor restarts.
/// </summary>
public static class EditorWords {
	/// <summary>The manifest name of the embedded file.</summary>
	public const string ResourceName = "WordsEdit.Resources.words.ini";
	/// <summary>The command-line switch naming the language, e.g. <c>--lang=it</c>.</summary>
	public const string LanguageSwitch = "--lang=";
	/// <summary>The language loaded when the one asked for is no culture at all.</summary>
	public const string Fallback = "en";

	/// <summary>The languages the file labels, code and label, in file order: the language menu.</summary>
	public static IReadOnlyList<KeyValuePair<string, string>> Languages { get; private set; } = [];
	/// <summary>The language <see cref="Words.Known"/> was last loaded in.</summary>
	public static string Current { get; private set; } = "";

	/// <summary>The file, parsed; <paramref name="logger"/> hears what the parser griped about.</summary>
	public static WordsBuilder Builder(ITakeException? logger = null)
		=> WordsBuilder.Create(logger).LoadResource(ResourceName, typeof(EditorWords).Assembly);

	/// <summary>The file's text as embedded, for the round trip through the editor.</summary>
	public static string Text() {
		using Stream stream = typeof(EditorWords).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new FileNotFoundException(ResourceName);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	/// <summary>
	///     Loads the words in <paramref name="languageCode"/>: <see cref="Words.Known"/>
	///     resolves in it and the thread cultures follow. A code no culture answers
	///     to loads <see cref="Fallback"/> instead.
	/// </summary>
	public static void Load(string languageCode, ITakeException? logger = null) {
		WordsBuilder builder = Builder(logger);
		Languages = [.. builder.GetLanguages()];
		try {
			Words.Known = builder.ToWords(languageCode);
			Current = languageCode;
		}
		catch (CultureNotFoundException) {
			Words.Known = builder.ToWords(Fallback);
			Current = Fallback;
		}
	}

	/// <summary>
	///     The menu entry <paramref name="languageCode"/> reads in: its own, else its
	///     family's (<c>en-GB</c> reads in <c>en</c>), else none.
	/// </summary>
	public static string? MenuCode(string languageCode) {
		foreach (var language in Languages) {
			if (string.Equals(language.Key, languageCode, StringComparison.OrdinalIgnoreCase)) {
				return language.Key;
			}
		}
		foreach (var language in Languages) {
			if (languageCode.StartsWith(language.Key + "-", StringComparison.OrdinalIgnoreCase)) {
				return language.Key;
			}
		}
		return null;
	}

	/// <summary>The language the command line asks for (the last <c>--lang=xx</c>), or null when it does not.</summary>
	public static string? AskedLanguage(IEnumerable<string> args) {
		string? asked = null;
		foreach (string arg in args) {
			if (arg.StartsWith(LanguageSwitch, StringComparison.Ordinal)) {
				asked = arg[LanguageSwitch.Length..];
			}
		}
		return asked is { Length: > 0 } ? asked : null;
	}

	/// <summary>The language to start in: the command line for this run, else the saved setting, else the OS.</summary>
	public static string StartupLanguage(IEnumerable<string> args)
		=> AskedLanguage(args) ?? EditorConfig.Language ?? CultureInfo.CurrentUICulture.Name;
}
