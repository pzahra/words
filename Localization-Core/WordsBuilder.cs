using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace PatTech.Localization {
	public class WordsBuilder {
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
		/// <param name="path"></param>
		/// <param name="assembly"></param>
		/// <exception cref="FileNotFoundException"></exception>
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
		/// <param name="filename"></param>
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
		public WordsBuilder LoadString(string wordsScript) {
			ArgumentNullException.ThrowIfNull(wordsScript);

			return Load(new StringReader(wordsScript));
		}
		/// <inheritdoc cref="Load(string)"/>
		public WordsBuilder Load(TextReader reader) {
			_parser.Load(reader);
			return this;
		}

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

		public IWords ToWords(string languageCode, bool showFallback = false) {
			var cultureInfo = CultureInfo.CreateSpecificCulture(languageCode);
			return new Wordsmith(Flatten(languageCode, showFallback), cultureInfo);
		}
	}
}
