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

	/// <summary>
	///     The file, parsed, over every format's own words (SPEC: Import and
	///     export — each format brings its own words): the formats load first and
	///     the editor's file last, so the editor's words win. <paramref name="logger"/>
	///     hears what the parser griped about.
	/// </summary>
	/// <param name="logger">Where the parser's gripes go.</param>
	/// <param name="formats">The formats whose words come along; the built-ins when unsaid.</param>
	public static WordsBuilder Builder(ITakeException? logger = null, WordsFormats? formats = null)
		=> (formats ?? WordsFormats.BuiltIn()).LoadWords(WordsBuilder.Create(logger)).LoadResource(ResourceName, typeof(EditorWords).Assembly);

	/// <summary>The file's text as embedded, for the round trip through the editor.</summary>
	public static string Text() {
		using Stream stream = typeof(EditorWords).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new FileNotFoundException(ResourceName);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	/// <summary>
	///     Loads the words in <paramref name="languageCode"/>, read as far as it is a
	///     code (<see cref="Readable"/>): <see cref="Words.Known"/> resolves in it,
	///     falling back through its shorter codes (<c>sr-Latn-RS</c>, <c>sr-Latn</c>,
	///     <c>sr</c>) to the default, and the thread cultures follow. One no culture
	///     answers to loads <see cref="Fallback"/> instead; startup never throws on a code.
	/// </summary>
	public static void Load(string languageCode, ITakeException? logger = null, WordsFormats? formats = null) {
		WordsBuilder builder = Builder(logger, formats);
		Languages = [.. builder.GetLanguages()];
		string code = Readable(languageCode);
		try {
			builder.Digest(code);
			Current = code;
		}
		catch (ArgumentException) {
			builder.Digest(Fallback);
			Current = Fallback;
		}
	}

	/// <summary>
	///     The code <paramref name="languageCode"/> loads as: itself, cased by kind,
	///     or the code a culture's name starts with (<c>ca-ES-valencia</c> loads
	///     <c>ca-ES</c>), else <see cref="Fallback"/>.
	/// </summary>
	public static string Readable(string languageCode)
		=> LanguageCode.TryRead(languageCode, out var code) ? code.ToString() : Fallback;

	/// <summary>
	///     The menu entry <paramref name="languageCode"/> reads in: its own, else the
	///     first of its shorter codes the menu has (<c>en-GB</c> reads in <c>en</c>),
	///     else none.
	/// </summary>
	public static string? MenuCode(string languageCode) {
		if (!LanguageCode.TryRead(languageCode, out var code)) {
			return null;
		}
		foreach (var level in code.Chain) {
			foreach (var language in Languages) {
				if (string.Equals(language.Key, level.ToString(), StringComparison.OrdinalIgnoreCase)) {
					return language.Key;
				}
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
