using PatTech.Localization.Authoring;
using PatTech.Text.Ini;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PatTech.Localization.Cli {
	/// <summary>
	///     The <c>words</c> command (command line SPEC: Calls): one verb,
	///     one file per call, over <see cref="IniPatcher"/>. Values go to the output
	///     and gripes to the error stream; the exit code is 0 for done, 1 when the
	///     key or field is not there, a request finds nothing to ask or an answer
	///     has a key skipped, 2 for a bad call, a file that is missing, can't be read
	///     or written or does not parse, an answer that is no translation document,
	///     or a refused edit.
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
			            [--missing <code>]            only those missing words in a language,
			            [--stale <code>]              or marked stale in it,
			            [--stale-without <text>]      but for a mark that starts with text
			  words request <file> [prefix]           a translation request, as JSON, for the
			            --missing/--stale <code>      keys list would list
			            [--stale-without <text>]
			            [--limit <n>] [--brief <text>]
			  words apply <file> <answer>             writes the translations a filled request
			            [--stale [text]]              holds, marked "machine translated" or text
			  words --version

			A field is named as in the file: value, value-fr, value-mt#few, context-fr,
			comment-fr, stale-fr, param-count. A value of - is read from stdin as UTF-8,
			its last line break dropped. A key is its full dotted name, menu.file-open,
			whatever [.child] header holds it: segments of letters, digits, _ and -, or
			a $constant of one segment. set writes no other name.

			A request explains itself: hand it to a language model, or to a script for a
			translation service, and give apply the same document back, its nulls filled
			(an answer of - is read from stdin). apply writes a key only when its answer
			keeps the default's {...} tokens, code spans and links, and neither the
			default nor the translation changed since the request. A changed link target
			is written, and raises the key's hand (stale=) for a person to check. The name
			a translator gives itself in "translator" follows the mark, after a colon. A script that loops
			request and apply over --stale passes over apply's own marks with
			--stale-without "machine translated", or the words it gave --stale.

			Exit codes: 0 done, 1 the key or field is not there, request found nothing
			to ask or apply skipped a key, 2 a bad call, a file that is missing, can't be
			read or written or does not parse, an answer that is no translation
			document, or an edit refused because it would change more than asked or
			write a value with a brace that opens nothing ({{ writes a brace).
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
				case "request":
					return Request(Call.Parse(args, 1, 2), output, error);
				case "apply":
					return Apply(Call.Parse(args, 2, 2), input, error);
				default:
					throw new UsageException($"'{args[0]}' is no call: get, set, remove, list, request or apply");
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
			using var patcher = Open(call.File, error);
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
			using var patcher = Open(call.File, error);
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
			//a brace that opens nothing, which an app shows as written with a warning, is
			//never written; a constant's words are text
			if (field.Type == "value" && !key.StartsWith('$') && ParameterUse.Malformed(text) is { Count: > 0 } braces) {
				error.WriteLine($"words: {key}: {field} would hold {ParameterUse.Named(braces)}, shown as written in the app; {{{{ writes a brace, and the file is left as it was");
				return BadCall;
			}
			//a default that changes leaves its translations stale, as Wordsmith's typing does;
			//its line breaks as the file keeps them, so \r\n for \n changes nothing
			List<string> staling = field is { Type: "value", Language: "" } && patcher.Find(key) is { } before && (field.Read(before) ?? "") != IniPatcher.Kept(text)
				? [.. before.TranslationsToStale()]
				: [];
			gripes.AddRange(patcher.Set(key, field, text));
			string stamp = WordsKey.StaleStamp(DateTimeOffset.Now);
			foreach (string code in staling) {
				gripes.AddRange(patcher.Set(key, new WordsField("stale", code, ""), stamp));
			}
			if (call.Stale is { } stale) {
				if (field.Type is "param" or "stale") {
					throw new UsageException($"--stale marks a language's entry, and {field} is none");
				}
				gripes.AddRange(patcher.Set(key, new WordsField("stale", field.Language, ""), stale));
			}
			if (field is { Type: "value", Language: not "" }) {
				gripes.AddRange(Mismatch(patcher, key, field.Language));
			}
			Save(patcher, call.File);
			Gripe(error, gripes);
			return Done;
		}

		//what a translation just written drops or adds beside its default, by the
		//editor's rule; the file is all it sees, so a reference into another is one it
		//cannot follow
		private static IEnumerable<string> Mismatch(IniPatcher patcher, string key, string code) {
			var keys = patcher.Keys.ToDictionary(found => found.BlockKey);
			if (!keys.TryGetValue(key, out var written)) {
				yield break;
			}
			var mismatch = ParameterUse.Check(written, code, new DefaultWordsProvider(keys), patcher.DefaultLanguage, new LanguageWordsProvider(keys, code));
			if (mismatch.Dropped.Count != 0) {
				yield return $"{key}: the {code} words drop {Placeholders(mismatch.Dropped)}, which the default uses, so the app's value never shows";
			}
			if (mismatch.Extra.Count != 0) {
				yield return $"{key}: the {code} words use {Placeholders(mismatch.Extra)}, which the default does not and no param- defines";
			}

			static string Placeholders(IEnumerable<string> names) => string.Join(", ", names.Select(FoundParameters.Placeholder));
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
			using var patcher = Open(call.File, error);
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
			using var patcher = Open(call.File, error);
			foreach (var key in Selected(patcher, call, error, out _)) {
				output.WriteLine(key.BlockKey);
			}
			return Done;
		}

		//the keys under the prefix, in the file's order, narrowed to those --missing
		//or --stale selects in their one language, either taking a key; code is that
		//language, null when neither is given. --stale passes over a mark that starts
		//with a --stale-without, so a loop of request and apply ends on apply's marks
		private static IEnumerable<WordsKey> Selected(IniPatcher patcher, Call call, TextWriter error, out string? code) {
			string prefix = call.Positionals.Count > 1 ? call.Positionals[1] : "";
			var keys = patcher.Keys.Where(key => key.BlockKey.StartsWith(prefix, StringComparison.Ordinal));
			string? missing = call.Missing is { } m ? Code("--missing", m) : null;
			string? stale = call.StaleIn is { } s ? Code("--stale", s) : null;
			if (missing is not null && stale is not null && missing != stale) {
				throw new UsageException($"--missing and --stale name one language, not {missing} and {stale}");
			}
			if (call.StaleWithout.Count != 0 && stale is null) {
				throw new UsageException("--stale-without narrows --stale <code>, which is not given");
			}
			code = missing ?? stale;
			if (code is not { } language) {
				return keys;
			}
			bool wants = MissingWords.Wants(patcher.Languages, patcher.DefaultLanguage, language);
			if (missing is not null && !wants) {
				error.WriteLine(patcher.Languages.Contains(language)
					? $"words: the default speaks {language}, so an empty entry reads it and misses nothing"
					: $"words: {call.File} does not declare {language}, so nothing misses it");
			}
			return keys.Where(key => missing is not null && wants && MissingWords.InLanguage(key, language) || stale is not null && TranslationRequest.Stale(key, language) && !PassedOver(key.Entries[language].Stale!));

			bool PassedOver(string mark) => call.StaleWithout.Any(start => mark.StartsWith(start, StringComparison.Ordinal));
		}

		private static string Code(string option, string text)
			=> LanguageCode.TryParse(text, out var parsed) ? parsed.ToString() : throw new UsageException($"{option} {text}: {WordsParserToLocalizationProvider.LanguageCodeRule}");

		private static int Request(Call call, TextWriter output, TextWriter error) {
			if (call.Missing is null && call.StaleIn is null) {
				throw new UsageException("request wants --missing <code>, --stale <code> or both");
			}
			using var patcher = Open(call.File, error);
			var keys = Selected(patcher, call, error, out string? code)
				.Where(key => TranslationRequest.HasWords(key) && TranslationRequest.Asked(key, code!).Count != 0);
			var selected = (call.Limit is { } limit ? keys.Take(limit) : keys).ToList();
			if (selected.Count == 0) {
				error.WriteLine($"words: nothing in {call.File} to ask for in {code}");
				return NotThere;
			}
			string source = patcher.DefaultLanguage ?? "en";
			output.WriteLine(TranslationRequest.Write(Path.GetFileName(call.File), new(source, patcher.LanguageName(source)), new(code!, patcher.LanguageName(code!)), selected, call.Brief ?? ""));
			return Done;
		}

		//a filled request's translations, each key's checked and written with its stale
		//mark, in one patch saved once; what is skipped is named, and all is counted
		private static int Apply(Call call, TextReader input, TextWriter error) {
			using var patcher = Open(call.File, error);
			string document = call.Positionals[1] == "-" ? Stdin(input) : Read(call.Positionals[1]);
			var answer = TranslationAnswer.Read(document);
			string code = LanguageCode.TryParse(answer.Target, out var parsed) ? parsed.ToString()
				: throw new InvalidDataException($"the answer's target.code {answer.Target}: {WordsParserToLocalizationProvider.LanguageCodeRule}");
			if (!patcher.Languages.Contains(code)) {
				throw new InvalidDataException($"{call.File} does not declare {code}, so it takes no {code} words");
			}
			//checked whole: a key reads the words the batch writes for the keys it refers to
			var batch = answer.Check(patcher.Keys.ToDictionary(key => key.BlockKey), code, patcher.DefaultLanguage);
			var mark = new WordsField("stale", code, "");
			var hand = new WordsField("stale", "", "");
			List<string> gripes = [];
			int written = 0, left = 0, skipped = 0, raised = 0;
			foreach (var (item, verdict) in answer.Items.Zip(batch.Verdicts)) {
				left += verdict.Left;
				if (verdict.Flags.Count != 0) {
					skipped += item.Translation.Count - verdict.Left;
					gripes.Add($"{item.Key}: skipped: {string.Join("; ", verdict.Flags)}");
					continue;
				}
				foreach (var (field, text) in verdict.Writes) {
					gripes.AddRange(patcher.Set(item.Key, field, text));
					written++;
				}
				if (verdict.Writes.Count != 0) {
					gripes.AddRange(patcher.Set(item.Key, mark, answer.MarkOf(call.Stale ?? "machine translated")));
				}
				//a changed link target goes in, and the key's hand up, so a person checks the URL;
				//a hand up already keeps its words
				if (verdict.Raised.Count != 0) {
					if (patcher.Find(item.Key) is not { NeedsReview: true }) {
						gripes.AddRange(patcher.Set(item.Key, hand, ""));
					}
					gripes.Add($"{item.Key}: written, its hand raised: {string.Join("; ", verdict.Raised)}");
					raised++;
				}
			}
			gripes.AddRange(batch.Broken);
			if (written != 0) {
				Save(patcher, call.File);
			}
			string hands = raised switch { 0 => "", 1 => ", 1 hand raised", _ => $", {raised} hands raised" };
			Gripe(error, [.. gripes, $"{written} written, {left} left, {skipped} skipped{hands}"]);
			return skipped != 0 ? NotThere : Done;
		}

		//a file's failures name the file, which the system's messages may not; a write a call
		//before left pending is finished first, and said
		private static IniPatcher Open(string path, TextWriter error) {
			if (!File.Exists(path)) {
				throw new FileNotFoundException($"{path}: no such file", path);
			}
			IniPatcher patcher;
			try {
				patcher = IniPatcher.Open(path);
			}
			catch (InvalidDataException ex) {
				throw new InvalidDataException($"{path}: {ex.Message}", ex);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				throw new IOException($"{path}: can't be read: {Say(ex)}", ex);
			}
			if (Recovered(patcher.Recovery) is { } note) {
				error.WriteLine($"words: {path}: {note}");
			}
			return patcher;
		}

		//what settling a write a call before left pending did, when it changed the file or dropped a write
		private static string? Recovered(IniRecovery? recovery) => recovery switch {
			{ Kind: IniRecoveryKind.Replayed } => "a write a call before left pending was finished first",
			{ Kind: IniRecoveryKind.Kept, Kept: { } kept } => $"a write a call before left pending was finished first, over a change made since; the file as it was is kept in {kept}",
			{ Kind: IniRecoveryKind.Kept } => "a write a call before left pending was finished first, the file made again",
			{ Kind: IniRecoveryKind.Unwhole } => "a write a call before broke off before it was logged, and was dropped",
			_ => null,
		};

		//why a file was refused, in the command's words: the ini driver's are a type and its data
		private static string Say(Exception failure) => failure switch {
			IniCommitException { Failure: IniCommitFailure.Changed } => "it changed since it was read; run the call again",
			IniCommitException { Failure: IniCommitFailure.Held } => "another program holds it",
			IniCommitException { Failure: IniCommitFailure.Denied } => "it is read-only, or not yours to write",
			IniCommitException { Failure: IniCommitFailure.Running } => "another write to it is running",
			IniCommitException { Failure: IniCommitFailure.Unsettled, Log: var log } => $"a write a call before left pending in {log} will not finish, and is kept for the next call",
			IniCommitException { Failure: IniCommitFailure.LogWrite } refused => $"its log would not write: {refused.InnerException?.Message}",
			IniEncodingException { Failure: IniEncodingFailure.Unencodable, Encoding: var encoding } => $"its {encoding} has no bytes for a character the edit writes",
			_ => failure.Message,
		};

		//a text file the call reads, by its BOM or as UTF-8, its failures naming it
		private static string Read(string path) {
			if (!File.Exists(path)) {
				throw new FileNotFoundException($"{path}: no such file", path);
			}
			try {
				return File.ReadAllText(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				throw new IOException($"{path}: can't be read: {ex.Message}", ex);
			}
		}

		//committed through the file's log: refused, the file is as it was; pending, the next call finishes it
		private static void Save(IniPatcher patcher, string path) {
			try {
				patcher.Save(path);
			}
			catch (IniPendingException ex) {
				throw new IOException($"{path}: the write stands in its log, {ex.Log}, but the file's own failed; the next call that opens it finishes it", ex);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				throw new IOException($"{path}: can't be written: {Say(ex)}", ex);
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
			public string? StaleIn { get; private set; }
			public List<string> StaleWithout { get; } = [];
			public int? Limit { get; private set; }
			public string? Brief { get; private set; }

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
						case "--stale" when verb is "set" or "apply":
							call.Stale = value ?? (call.Positionals.Count >= most && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "");
							break;
						case "--stale" when verb is "list" or "request":
							call.StaleIn = value ?? (i + 1 < args.Count ? args[++i] : throw new UsageException("--stale wants a language code"));
							break;
						case "--stale-without" when verb is "list" or "request":
							string start = value ?? (i + 1 < args.Count ? args[++i] : throw new UsageException("--stale-without wants the words a mark starts with"));
							call.StaleWithout.Add(start != "" ? start : throw new UsageException("--stale-without \"\" would pass over every mark: give the words one starts with"));
							break;
						case "--missing" when verb is "list" or "request":
							call.Missing = value ?? (i + 1 < args.Count ? args[++i] : throw new UsageException("--missing wants a language code"));
							break;
						case "--limit" when verb == "request":
							string limit = value ?? (i + 1 < args.Count ? args[++i] : throw new UsageException("--limit wants a count"));
							call.Limit = int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0 ? count
								: throw new UsageException($"--limit {limit}: a count from 1 up");
							break;
						case "--brief" when verb == "request":
							call.Brief = value ?? (i + 1 < args.Count ? args[++i] : throw new UsageException("--brief wants its words"));
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
						"request" => "request <file> [prefix] --missing <code> | --stale <code>",
						"apply" => "apply <file> <answer>",
						_ => "list <file> [prefix]",
					});
				}
				return call;
			}
		}

		private sealed class UsageException(string message) : Exception(message);
	}
}
