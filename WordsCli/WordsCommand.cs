using PatTech.Localization.Authoring;
using System.Reflection;
using System.Text;

namespace PatTech.Localization.Cli {
	/// <summary>
	///     The <c>words</c> command (editor SPEC: A command line for tools): one verb,
	///     one file per call, over <see cref="IniPatcher"/>. Values go to the output
	///     and gripes to the error stream; the exit code is 0 for done, 1 when the
	///     key or field is not there, 2 for a bad call, a file that is missing, can't
	///     be read or written or does not parse, or a refused edit.
	/// </summary>
	public static class WordsCommand {
		public const int Done = 0, NotThere = 1, BadCall = 2;

		private const string Usage = """
			words edits a words.ini one field at a time, and leaves every other byte alone.

			  words get <file> <key> [field]          a field's value, or the key's whole block
			  words set <file> <key> <field> <value>  sets a field, adding it, or the key
			            [--stale [text]]              and marks the language's entry stale
			  words remove <file> <key> [field]       drops a field, or the whole key
			  words list <file> [prefix]              the keys, in the file's order
			            [--missing <code>]            only those missing words in a language
			  words --version

			A field is named as in the file: value, value-fr, value-mt#few, context-fr,
			comment-fr, stale-fr, param-count. A value of - is read from stdin as UTF-8,
			its last line break dropped. A key is its full dotted name, menu.file-open,
			whatever [.child] header holds it: segments of letters, digits, _ and -, or
			a $constant of one segment. set writes no other name.

			Exit codes: 0 done, 1 the key or field is not there, 2 a bad call, a file
			that is missing, can't be read or written or does not parse, or an edit
			refused because it would change more than asked.
			""";

		/// <summary>
		///     Reads a redirected stdin as UTF-8 exactly: a leading U+FEFF is the
		///     value's own, not taken for a BOM, and bytes that are no UTF-8 fail the
		///     read rather than turn into replacement characters.
		/// </summary>
		public static TextReader Piped(Stream stdin) => new StreamReader(stdin, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);

		/// <summary>Runs one call; <paramref name="args"/> is the command line after the program's name.</summary>
		/// <returns>The exit code.</returns>
		public static int Run(IReadOnlyList<string> args, TextReader input, TextWriter output, TextWriter error) {
			try {
				return Dispatch(args, input, output, error);
			}
			catch (UsageException ex) {
				error.WriteLine($"words: {ex.Message}");
				error.WriteLine("words --help shows the calls");
				return BadCall;
			}
			catch (Exception ex) when (ex is InvalidDataException or IniPatchException or IOException or UnauthorizedAccessException or ArgumentException) {
				error.WriteLine($"words: {ex.Message}");
				return BadCall;
			}
		}

		private static int Dispatch(IReadOnlyList<string> args, TextReader input, TextWriter output, TextWriter error) {
			switch (args.Count == 0 ? null : args[0]) {
				case null:
					error.WriteLine(Usage);
					return BadCall;
				case "--help" or "-h" or "-?" or "help":
					output.WriteLine(Usage);
					return Done;
				case "--version":
					output.WriteLine(Version);
					return Done;
				case "get":
					return Get(Call.Parse(args, 2, 3), output, error);
				case "set":
					return Set(Call.Parse(args, 4, 4), input, error);
				case "remove":
					return Remove(Call.Parse(args, 2, 3), error);
				case "list":
					return List(Call.Parse(args, 1, 2), output, error);
				default:
					throw new UsageException($"'{args[0]}' is no call: get, set, remove or list");
			}
		}

		/// <summary>The version, without the commit Source Link appends.</summary>
		public static string Version {
			get {
				string version = typeof(WordsCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
				int plus = version.IndexOf('+');
				return plus < 0 ? version : version[..plus];
			}
		}

		private static int Get(Call call, TextWriter output, TextWriter error) {
			var patcher = Open(call.File);
			string key = call.Positionals[1];
			if (patcher.Find(key) is not { } found) {
				error.WriteLine($"words: {call.File} has no key {key}");
				return NotThere;
			}
			if (call.Positionals.Count == 2) {
				output.WriteLine(patcher.Block(key));
				return Done;
			}
			var field = Field(call.Positionals[2]);
			if (field.Read(found) is not { } text) {
				error.WriteLine($"words: {key} has no {field}");
				return NotThere;
			}
			output.WriteLine(text);
			return Done;
		}

		private static int Set(Call call, TextReader input, TextWriter error) {
			var patcher = Open(call.File);
			string key = call.Positionals[1];
			var field = Field(call.Positionals[2]);
			string text = call.Positionals[3];
			List<string> gripes = [];
			if (text == "-") {
				text = Stdin(input);
				if (text.StartsWith('﻿')) {
					gripes.Add("the value from stdin starts with U+FEFF, kept as its own; a tool that writes a BOM may have put it there");
				}
			}
			gripes.AddRange(patcher.Set(key, field, text));
			if (call.Stale is { } stale) {
				if (field.Type is "param" or "stale") {
					throw new UsageException($"--stale marks a language's entry, and {field} is none");
				}
				gripes.AddRange(patcher.Set(key, new WordsField("stale", field.Language, ""), stale));
			}
			Save(patcher, call.File);
			Gripe(error, gripes);
			return Done;
		}

		//all of stdin but its last line break, \r\n, \n or \r
		private static string Stdin(TextReader input) {
			string text;
			try {
				text = input.ReadToEnd();
			}
			catch (DecoderFallbackException) {
				throw new InvalidDataException("stdin is not UTF-8 text");
			}
			return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') || text.EndsWith('\r') ? text[..^1] : text;
		}

		private static int Remove(Call call, TextWriter error) {
			var patcher = Open(call.File);
			string key = call.Positionals[1];
			IReadOnlyList<string> gripes;
			if (call.Positionals.Count == 2) {
				if (patcher.Find(key) is null) {
					error.WriteLine($"words: {call.File} has no key {key}");
					return NotThere;
				}
				gripes = patcher.Remove(key);
			}
			else {
				//an empty field reads as none, but its line is there to drop
				var field = Field(call.Positionals[2]);
				if (!patcher.Declares(key, field)) {
					error.WriteLine(patcher.Find(key) is null ? $"words: {call.File} has no key {key}" : $"words: {key} has no {field}");
					return NotThere;
				}
				gripes = patcher.Remove(key, field);
			}
			Save(patcher, call.File);
			Gripe(error, gripes);
			return Done;
		}

		private static int List(Call call, TextWriter output, TextWriter error) {
			var patcher = Open(call.File);
			string prefix = call.Positionals.Count > 1 ? call.Positionals[1] : "";
			var keys = patcher.Keys.Where(key => key.BlockKey.StartsWith(prefix, StringComparison.Ordinal));
			if (call.Missing is { } missing) {
				if (!LanguageCode.TryParse(missing, out var parsed)) {
					throw new UsageException($"--missing {missing}: {WordsParserToLocalizationProvider.LanguageCodeRule}");
				}
				string code = parsed.ToString();
				if (!MissingWords.Wants(patcher.Languages, patcher.DefaultLanguage, code)) {
					error.WriteLine(patcher.Languages.Contains(code)
						? $"words: the default speaks {code}, so an empty entry reads it and misses nothing"
						: $"words: {call.File} does not declare {code}, so nothing misses it");
				}
				keys = keys.Where(key => MissingWords.Wants(patcher.Languages, patcher.DefaultLanguage, code) && MissingWords.InLanguage(key, code));
			}
			foreach (var key in keys) {
				output.WriteLine(key.BlockKey);
			}
			return Done;
		}

		//a file's failures name the file, which the system's messages may not
		private static IniPatcher Open(string path) {
			if (!File.Exists(path)) {
				throw new FileNotFoundException($"{path}: no such file", path);
			}
			try {
				return IniPatcher.Open(path);
			}
			catch (InvalidDataException ex) {
				throw new InvalidDataException($"{path}: {ex.Message}", ex);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				throw new IOException($"{path}: can't be read: {ex.Message}", ex);
			}
		}

		private static void Save(IniPatcher patcher, string path) {
			try {
				patcher.Save(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				throw new IOException($"{path}: can't be written: {ex.Message}", ex);
			}
		}

		private static WordsField Field(string name)
			=> WordsField.TryParse(name, out var field, out string? problem) ? field : throw new UsageException(problem);

		private static void Gripe(TextWriter error, IEnumerable<string> gripes) {
			foreach (string gripe in gripes) {
				error.WriteLine($"words: {gripe}");
			}
		}

		//a call's file and other positionals, and its options
		private sealed class Call {
			public List<string> Positionals { get; } = [];
			public string File => Positionals[0];
			public string? Stale { get; private set; }
			public string? Missing { get; private set; }

			//options go anywhere after the verb; --stale takes the next argument as its
			//words only once every positional is in, and -- ends the options
			public static Call Parse(IReadOnlyList<string> args, int least, int most) {
				string verb = args[0];
				var call = new Call();
				bool options = true;
				for (int i = 1; i < args.Count; i++) {
					string arg = args[i];
					if (options && arg == "--") {
						options = false;
						continue;
					}
					if (!options || !arg.StartsWith("--", StringComparison.Ordinal)) {
						call.Positionals.Add(arg);
						continue;
					}
					int equals = arg.IndexOf('=');
					string name = equals < 0 ? arg : arg[..equals];
					string? value = equals < 0 ? null : arg[(equals + 1)..];
					switch (name) {
						case "--stale" when verb == "set":
							call.Stale = value ?? (call.Positionals.Count >= most && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "");
							break;
						case "--missing" when verb == "list":
							call.Missing = value ?? (i + 1 < args.Count ? args[++i] : throw new UsageException("--missing wants a language code"));
							break;
						default:
							throw new UsageException($"{verb} takes no {name}");
					}
				}
				if (call.Positionals.Count < least || call.Positionals.Count > most) {
					throw new UsageException(verb switch {
						"get" => "get <file> <key> [field]",
						"set" => "set <file> <key> <field> <value>",
						"remove" => "remove <file> <key> [field]",
						_ => "list <file> [prefix]",
					});
				}
				return call;
			}
		}

		private sealed class UsageException(string message) : Exception(message);
	}
}
