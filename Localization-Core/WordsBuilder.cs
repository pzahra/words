using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PatTech.Localization {
	/// <summary>
	/// Loads one or more <c>words.ini</c> sources and turns them into an
	/// <see cref="IWords"/> dictionary for a chosen language. Stack as many
	/// <c>Load</c> calls as you like, flip <see cref="Debug"/> or
	/// <see cref="UseSystemNumbers"/> if you need them, then finish with
	/// <see cref="Digest(string)"/> to install the result as <see cref="Words.Known"/>
	/// (or <see cref="ToWords(string)"/> to only build it).
	/// </summary>
	public class WordsBuilder {
		/// <summary>
		/// Creates a fresh builder with its own parser and empty language store.
		/// </summary>
		/// <param name="logger">Receives warnings about overwritten keys and unknown fields; <see langword="null"/> discards them.</param>
		public static WordsBuilder Create(ITakeException? logger = null) {
			var builder = new WordsParserToWordsProvider(logger);
			var parser = new WordsParser(builder);
			return new WordsBuilder(builder, parser);
		}

		private readonly WordsParserToWordsProvider _builder;
		private readonly WordsParser _parser;
		private bool _showFallback;
		private bool _useSystemNumbers;

		private WordsBuilder(WordsParserToWordsProvider builder, WordsParser parser) {
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(parser);

			_builder = builder;
			_parser = parser;
		}

		/// <summary>
		/// Load a language file as an EmbeddedResource from the specified assembly.
		/// </summary>
		/// <param name="path">The manifest resource name, e.g. <c>"MyApp.Assets.words.ini"</c>.</param>
		/// <param name="assembly">The assembly containing the resource.</param>
		/// <exception cref="FileNotFoundException">No resource with that name exists in <paramref name="assembly"/>.</exception>
		public WordsBuilder LoadResource(string path, Assembly assembly) {
			ArgumentNullException.ThrowIfNull(path);
			if (path is "") {
				throw new ArgumentException("path is empty", nameof(path));
			}
			ArgumentNullException.ThrowIfNull(assembly);

			Stream? stream = null;
			try {
				stream = assembly.GetManifestResourceStream(path)
					?? throw new FileNotFoundException(path);
				return Load(stream);
			}
			finally {
				stream?.Dispose();
			}
		}
		/// <summary>
		/// Called at the very beginning of application runtime. Multiple files can be loaded,
		/// and any duplicate keys will favour last-in, so the official default should be last.
		/// </summary>
		/// <param name="filename">Path to a <c>words.ini</c> file on disk.</param>
		public WordsBuilder Load(string filename) {
			ArgumentNullException.ThrowIfNull(filename);
			if (filename is "") {
				throw new ArgumentException("path is empty", nameof(filename));
			}

			using var stream = File.OpenRead(filename);
			return Load(stream);
		}
		/// <inheritdoc cref="Load(string)"/>
		public WordsBuilder Load(Stream stream) {
			ArgumentNullException.ThrowIfNull(stream);
			if (!stream.CanRead) {
				throw new ArgumentException("stream is not readable", nameof(stream));
			}

			return Load(new StreamReader(stream));
		}
		/// <summary>
		/// Parses <c>words.ini</c> content held directly in a string, no file required.
		/// Same last-in-wins rules as <see cref="Load(string)"/>.
		/// </summary>
		/// <param name="wordsScript">The <c>words.ini</c>-formatted text.</param>
		public WordsBuilder LoadString(string wordsScript) {
			ArgumentNullException.ThrowIfNull(wordsScript);

			return Load(new StringReader(wordsScript));
		}
		/// <inheritdoc cref="Load(string)"/>
		public WordsBuilder Load(TextReader reader) {
			_parser.Load(reader);
			return this;
		}

		/// <summary>
		/// Enumerates the display languages declared in the loaded files, as
		/// language-code/label pairs, in the order the codes were first seen.
		/// A language is listed when its file header declares a label
		/// (a top-of-file <c>value-xx=</c> line before any <c>[block]</c>); labels that
		/// are empty or start with <c>!</c> are hidden from the list.
		/// </summary>
		public IEnumerable<KeyValuePair<string, string>> GetLanguages() {
			var codes = _builder.LanguageCodes;
			for (var i = 0; i < codes.Count; ++i) {
				var code = codes[i];
				var label = _builder.Languages[code].GetValueOrDefault("", "");
				if (!string.IsNullOrEmpty(label) && !label.StartsWith('!')) {
					yield return new KeyValuePair<string, string>(code, label);
				}
			}
		}

		/// <summary>
		/// The language the default is written in, as a top-of-file <c>value=!xx</c>
		/// declares it (the <c>!</c> keeps it off <see cref="GetLanguages"/>), or
		/// <see langword="null"/> when no file declares one; the last file loaded wins.
		/// </summary>
		public string? DefaultLanguage
			=> _builder.Languages.GetValueOrDefault("")?.GetValueOrDefault("", "") is { Length: > 1 } label && label[0] == '!' ? label[1..] : null;

		/// <summary>
		/// Brands values that fell back to another language, so missing translations
		/// stand out: 🕮 for a shorter code's (<c>en</c> for <c>en-GB</c>), 📚 for the
		/// default's. Constants (<c>$</c> keys) are language-less and never branded, and neither is the default
		/// where it speaks the language (<see cref="DefaultLanguage"/>,
		/// <see cref="WordsParser.DefaultSpeaks"/>). A debugging aid, off by
		/// default; it applies to every dictionary this builder then produces, so chain
		/// it before <see cref="Digest(string)"/> or leave it out.
		/// </summary>
		/// <param name="showFallback"><see langword="true"/> to brand fallbacks; <see langword="false"/> to switch it back off.</param>
		public WordsBuilder Debug(bool showFallback = true) {
			_showFallback = showFallback;
			return this;
		}

		/// <summary>
		/// Keeps numbers and dates in the system's regional format — the culture the
		/// process started in, <see cref="Words.SystemCulture"/> — while the words follow
		/// the selected language. Off by default, where the formatting culture is the
		/// language's own (<c>de</c> words come with decimal commas). Like
		/// <see cref="Debug"/>, it applies to every dictionary this builder then produces,
		/// so chain it before <see cref="Digest(string)"/>.
		/// </summary>
		/// <param name="useSystemNumbers"><see langword="true"/> for the system's formatting; <see langword="false"/> to switch back to the language's.</param>
		public WordsBuilder UseSystemNumbers(bool useSystemNumbers = true) {
			_useSystemNumbers = useSystemNumbers;
			return this;
		}

		/// <summary>
		/// Opts in to live language switching (SPEC: Live language switching): keeps this
		/// builder — its loaded sources, its <see cref="Debug"/> and
		/// <see cref="UseSystemNumbers"/> settings — alive as <see cref="Words.Live"/>, so
		/// <see cref="Words.SwitchLanguage"/> can re-flatten for another language without
		/// touching disk, and arms the registry that refreshes what is on screen. Off by
		/// default, where a change of language is a restart. Chain it before
		/// <see cref="Digest(string)"/>.
		/// </summary>
		/// <param name="live"><see langword="true"/> to keep this builder live; <see langword="false"/> to let it go again.</param>
		public WordsBuilder Live(bool live = true) {
			if (live) {
				Words.Live = this;
			}
			else if (ReferenceEquals(Words.Live, this)) {
				Words.Live = null;
			}
			return this;
		}

		/// <summary>
		/// Merges the loaded languages into a single read-only provider for
		/// <paramref name="languageCode"/>. Per key, the value comes from the exact
		/// language (e.g. <c>zh-Hant-TW</c>) first, then each shorter code it falls back
		/// to (<c>zh-Hant</c>, <c>zh</c>: <see cref="LanguageCode.Chain"/>), then the
		/// language-less default; a key's plural forms come whole from the first of
		/// those with any of its words. Passing <c>""</c> returns the raw default
		/// dictionary directly. Fallbacks are branded when <see cref="Debug"/> is on.
		/// </summary>
		/// <param name="languageCode">The language to flatten, e.g. <c>"en"</c> or <c>"en-GB"</c>; casing is normalized for you, and a culture's name that says more than a code (<c>ca-ES-valencia</c>) reads as the code it starts with (<see cref="LanguageCode.TryRead"/>).</param>
		/// <returns>The flattened provider; an empty provider if nothing was loaded at all.</returns>
		/// <exception cref="ArgumentException"><paramref name="languageCode"/> does not start with a language code.</exception>
		public IWordsProvider Flatten(string languageCode) {
			if (languageCode is "") {
				//a file can hold no default value at all
				return _builder.Languages.TryGetValue("", out var defaults) ? defaults : WordsProvider.Empty();
			}
			if (!LanguageCode.TryRead(languageCode, out var code)) {
				throw new ArgumentException($"'{languageCode}' is no language code: language(-Script)?(-REGION)?, as en, ceb, es-419, zh-Hans-CN", nameof(languageCode));
			}

			var fallback = _builder.Languages.GetValueOrDefault("");
			//where the default speaks the language, falling back to it misses nothing
			bool brandDefault = _showFallback && !WordsParser.DefaultSpeaks(DefaultLanguage, code.ToString());

			//the first level found is the language's own; each one after it fell back
			Dictionary<string, string>? words = null;
#pragma warning disable IDE0028 // Simplify collection initialization (with unsupported syntax!)
			foreach (var level in code.Chain) {
				if (_builder.Languages.GetValueOrDefault(level.ToString()) is { } source) {
					if (words is null) {
						words = new(source);
					}
					else {
						patch(words, source, "🕮", _showFallback);
					}
				}
			}
			if (fallback != null) {
				if (words is null) {
					words = new(fallback);
				}
				else {
					patch(words, fallback, "📚", brandDefault);
				}
			}
#pragma warning restore IDE0028 // Simplify collection initialization
			return words is null ? WordsProvider.Empty() : new ReadOnlyWordsProvider(words);

			static void patch(IDictionary<string, string> target, DictionaryWordsProvider source, string fallbackPrefix, bool showFallbackPrefix) {
				//a key's forms come from the first level with any of its words (SPEC: Plural
				//forms): beside a translation's own words, no form flattens in
				var owned = new HashSet<string>(target.Keys.Select(key => key.IndexOf('#') is > 0 and var mark ? key[..mark] : key));
				foreach (var (key, value) in source) {
					if (target.ContainsKey(key) || key.IndexOf('#') is > 0 and var mark && owned.Contains(key[..mark])) {
						continue;
					}
					//a constant is language-less, so it never fell back from anything; branded,
					//it would corrupt whatever it is spliced into (an image base, a unit)
					else if (showFallbackPrefix && !key.StartsWith('$')) {
						target.Add(key, fallbackPrefix + value);
					}
					else {
						target.Add(key, value);
					}
				}
			}
		}

		/// <inheritdoc cref="ToWords(string, out IEnumerable{KeyValuePair{string, string}})"/>
		public IWords ToWords(string languageCode) {
			var uiCulture = CultureInfo.CreateSpecificCulture(languageCode);
			var culture = _useSystemNumbers ? Words.SystemCulture : uiCulture;
			//the code picks the plural forms: the culture may be the invariant one for a language .NET does not know
			var words = Flatten(languageCode);
			return new CulturedWords(words, culture, uiCulture) { Language = LanguageCode.TryRead(languageCode, out var code) ? code.ToString() : languageCode };
		}

		/// <summary>
		/// <see cref="Flatten(string)"/> plus cultures: builds the final
		/// <see cref="IWords"/> for <paramref name="languageCode"/>, carrying the language's
		/// <see cref="CultureInfo"/> (with the system's for formatting after
		/// <see cref="UseSystemNumbers"/>) so assigning it to <see cref="Words.Known"/> also
		/// sets the thread cultures. Builds only; <see cref="Digest(string)"/> is the
		/// same thing installed as the process-wide dictionary in one call.
		/// </summary>
		/// <param name="languageCode">The language to select, e.g. <c>"en"</c> or <c>"en-GB"</c>.</param>
		/// <param name="languages">Return a list of available languages (see <see cref="GetLanguages"/>)</param>
		public IWords ToWords(string languageCode, out IEnumerable<KeyValuePair<string, string>> languages) {
			languages = GetLanguages();
			return ToWords(languageCode);
		}

		/// <inheritdoc cref="Digest(string, out IEnumerable{KeyValuePair{string, string}})"/>
		public IWords Digest(string languageCode) {
			var words = ToWords(languageCode);
			Words.Known = words;
			return words;
		}

		/// <summary>
		/// The one-call startup: builds the dictionary for <paramref name="languageCode"/>
		/// and installs it as <see cref="Words.Known"/>, which applies its cultures to this
		/// thread and to threads yet to come. Typically the last call in the builder
		/// chain. <see cref="ToWords(string)"/> is the same build without the install, for
		/// a dictionary that is not the process-wide one.
		/// </summary>
		/// <param name="languageCode">The language to select, e.g. <c>"en"</c> or <c>"en-GB"</c>.</param>
		/// <param name="languages">Return a list of available languages (see <see cref="GetLanguages"/>)</param>
		/// <returns>The installed dictionary, the same object now in <see cref="Words.Known"/>.</returns>
		public IWords Digest(string languageCode, out IEnumerable<KeyValuePair<string, string>> languages) {
			languages = GetLanguages();
			return Digest(languageCode);
		}
	}
}
