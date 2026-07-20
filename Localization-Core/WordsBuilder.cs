using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace PatTech.Localization {
	/// <summary>
	/// Loads one or more <c>words.ini</c> sources and turns them into an
	/// <see cref="IWords"/> dictionary for a chosen language. Stack as many
	/// <c>Load</c> calls as you like, then finish with <see cref="ToWords(string, bool)"/>.
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
		/// Merges the loaded languages into a single read-only provider for
		/// <paramref name="languageCode"/>. Per key, the value comes from the exact
		/// language (e.g. <c>en-GB</c>) first, then its language family (<c>en</c>),
		/// then the language-less default. Passing <c>""</c> returns the raw default
		/// dictionary directly.
		/// </summary>
		/// <param name="languageCode">The language to flatten, e.g. <c>"en"</c> or <c>"en-GB"</c>; casing is normalized for you.</param>
		/// <param name="showFallback">When <see langword="true"/>, values that fell back are visibly branded: 🕮 for family fallback, 📚 for default fallback. Handy for spotting missing translations.</param>
		/// <returns>The flattened provider; an empty provider if nothing was loaded at all.</returns>
		public IWordsProvider Flatten(string languageCode, bool showFallback = false) {
			if (languageCode is "") {
				return _builder.Languages[""];
			}

			languageCode = WordsParser.NormalizeLanguageCasing(languageCode);

			var separator = languageCode.IndexOf('-');

			DictionaryWordsProvider? primary;
			DictionaryWordsProvider? secondary = null;
			var fallback = _builder.Languages.GetValueOrDefault("");
			if (separator > 0) {
				primary = _builder.Languages.GetValueOrDefault(languageCode);
				secondary = _builder.Languages.GetValueOrDefault(languageCode[..separator]);
			}
			else {
				primary = _builder.Languages.GetValueOrDefault(languageCode);
			}

			Dictionary<string, string> words;
#pragma warning disable IDE0028 // Simplify collection initialization (with unsupported syntax!)
			if (primary != null) {
				words = new(primary);
				if (secondary != null) {
					patch(words, secondary, "🕮", showFallback);
				}
				if (fallback != null) {
					patch(words, fallback, "📚", showFallback);
				}
			}
			else if (secondary != null) {
				words = new(secondary);
				if (fallback != null) {
					patch(words, fallback, "📚", showFallback);
				}
			}
			else if (fallback != null) {
				words = new(fallback);
			}
			else {
				return WordsProvider.Empty();
			}
#pragma warning restore IDE0028 // Simplify collection initialization
			return new ReadOnlyWordsProvider(words);

			static void patch(IDictionary<string, string> target, DictionaryWordsProvider source, string fallbackPrefix, bool showFallbackPrefix) {
				foreach (var (key, value) in source) {
					if (target.ContainsKey(key)) {
						continue;
					}
					else if (showFallbackPrefix) {
						target.Add(key, fallbackPrefix + value);
					}
					else {
						target.Add(key, value);
					}
				}
			}
		}

		/// <inheritdoc cref="ToWords(string, out IEnumerable{KeyValuePair{string, string}}, bool)"/>
		public IWords ToWords(string languageCode, bool showFallback = false) {
			var cultureInfo = CultureInfo.CreateSpecificCulture(languageCode);
			return new Wordsmith(Flatten(languageCode, showFallback), cultureInfo);
		}

		/// <summary>
		/// <see cref="Flatten(string, bool)"/> plus a culture: builds the final
		/// <see cref="IWords"/> for <paramref name="languageCode"/>, carrying the matching
		/// <see cref="CultureInfo"/> so assigning it to <see cref="Words.Known"/> also
		/// sets the thread cultures. Typically the last call in the builder chain.
		/// </summary>
		/// <param name="languageCode">The language to select, e.g. <c>"en"</c> or <c>"en-GB"</c>.</param>
		/// <param name="languages">Return a list of available languages (see <see cref="GetLanguages"/>)</param>
		/// <param name="showFallback">See <see cref="Flatten(string, bool)"/>.</param>
		public IWords ToWords(string languageCode, out IEnumerable<KeyValuePair<string, string>> languages, bool showFallback = false) {
			var cultureInfo = CultureInfo.CreateSpecificCulture(languageCode);
			languages = GetLanguages();
			return new Wordsmith(Flatten(languageCode, showFallback), cultureInfo);
		}
	}
}
