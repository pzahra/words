using PatTech.Localization;
using System.Text.Json;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A translator's answer (editor SPEC: Machine translation): the request sent
	///     back with its nulls filled. Only the translator's name, the target's code
	///     and each item's key, default, current words and translation are read, so a
	///     script may drop the rest.
	/// </summary>
	/// <param name="Target">The language the answer is in.</param>
	/// <param name="Items">The items, in the answer's order.</param>
	public sealed record TranslationAnswer(string Target, IReadOnlyList<TranslationItem> Items) {
		/// <summary>The longest name a translator gives itself that the mark carries; past it, the name is cut.</summary>
		public const int TranslatorLength = 80;

		/// <summary>
		///     The name the translator gave itself, as it says, which a model may say
		///     wrong: its runs of white space one space, cut to <see cref="TranslatorLength"/>;
		///     null when it gave none.
		/// </summary>
		public string? Translator { get; init; }

		/// <summary>
		///     The stale mark a key the answer writes takes: <paramref name="mark"/>, and the
		///     translator's name after it where it gave one. The mark still starts the
		///     same, so a selection passing over it passes over this.
		/// </summary>
		public string MarkOf(string mark) => Translator is { } name ? $"{mark}: {name}" : mark;

		/// <summary>
		///     Reads an answer. Text around the document, a model's code fence or a
		///     sentence before it, is dropped: the document runs from the first
		///     <c>{</c> to the last <c>}</c>. Comments and trailing commas are let by.
		/// </summary>
		/// <exception cref="InvalidDataException">
		///     The text holds no JSON object, is no JSON, which says where (<see cref="FaultOf"/>),
		///     or is no translation document, or holds broken Unicode.
		/// </exception>
		public static TranslationAnswer Read(string text) {
			int first = text.IndexOf('{'), last = text.LastIndexOf('}');
			if (first < 0 || last < first) {
				throw new InvalidDataException("the answer holds no JSON object");
			}
			JsonDocument document;
			try {
				document = JsonDocument.Parse(text.AsMemory(first, last - first + 1), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			}
			catch (JsonException ex) {
				throw Faulted(text, first, ex);
			}
			using (document) {
				try {
					return Read(document.RootElement);
				}
				catch (InvalidOperationException ex) {
					throw Broken("the answer", ex);
				}
			}
		}

		/// <summary>
		///     Where <see cref="Read(string)"/> found the answer no JSON, so a person can mend it
		///     there: the character the reader stopped at, counted from 0 in the text as given,
		///     and why, without the place the message gives as a line and a column; null for
		///     any other fault.
		/// </summary>
		public static (int At, string Reason)? FaultOf(InvalidDataException refused)
			=> refused.Data[FaultAt] is int at && refused.Data[FaultReason] is string reason ? (at, reason) : null;

		private const string FaultAt = "TranslationAnswer.At", FaultReason = "TranslationAnswer.Reason";

		//where the reader stopped, counted in the text as given: the reader counts its lines
		//from 0 and its columns in UTF-8 bytes, both from the first {, which a model's
		//sentence or code fence may put lines down; its own count leaves the message
		private static InvalidDataException Faulted(string text, int first, JsonException ex) {
			int start = first;
			for (long line = 0; line < (ex.LineNumber ?? 0); line++) {
				int next = text.IndexOf('\n', start);
				if (next < 0) {
					break;
				}
				start = next + 1;
			}
			int end = text.IndexOf('\n', start);
			end = end < 0 ? text.Length : end;
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text[start..end]);
			int at = start + System.Text.Encoding.UTF8.GetCharCount(bytes, 0, (int)Math.Min(ex.BytePositionInLine ?? 0, bytes.Length));
			int lineStart = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
			int number = 1 + text.AsSpan(0, at).Count('\n'), column = at - lineStart + 1;
			int count = ex.Message.IndexOf(" LineNumber:", StringComparison.Ordinal);
			string what = count >= 0 ? ex.Message[..count] : ex.Message;
			return new($"the answer is no JSON at line {number}, column {column}: {what}", ex) {
				Data = { [FaultAt] = at, [FaultReason] = what },
			};
		}

		//JSON escapes a lone surrogate (\ud800) as readily as a character, but it is no
		//text: reading it throws, and the answer is refused for it, as for any other fault
		private static InvalidDataException Broken(string where, InvalidOperationException ex)
			=> new($"{where} holds broken Unicode: {ex.Message}", ex);

		private static TranslationAnswer Read(JsonElement root) {
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("words", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != TranslationRequest.Kind) {
				throw new InvalidDataException($"the answer is no translation document: its \"words\" is not \"{TranslationRequest.Kind}\"");
			}
			if (!root.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object
				|| !target.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String) {
				throw new InvalidDataException("the answer names no target.code");
			}
			if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) {
				throw new InvalidDataException("the answer has no items");
			}
			List<TranslationItem> read = [];
			int index = 0;
			foreach (var item in items.EnumerateArray()) {
				try {
					read.Add(Item(item, index));
				}
				catch (InvalidOperationException ex) {
					throw Broken($"the answer's item {index}", ex);
				}
				index++;
			}
			return new(code.GetString()!, read) { Translator = Name(root) };
		}

		//the name the translator gave itself, on one line and not too long for a mark; none
		//for a null, a blank or anything but text, which costs the answer nothing
		private static string? Name(JsonElement root) {
			if (!root.TryGetProperty("translator", out var translator) || translator.ValueKind != JsonValueKind.String) {
				return null;
			}
			string name = string.Join(' ', translator.GetString()!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
			return name == "" ? null : name.Length > TranslatorLength ? name[..TranslatorLength].TrimEnd() : name;
		}

		/// <summary>
		///     Checks the whole answer against <paramref name="keys"/>, one file's keys by
		///     name, as the batch would leave them (editor SPEC: Machine translation →
		///     Nothing is written unchecked). Each item is checked as
		///     <see cref="TranslationItem.Check"/> checks it: against the keys as they are,
		///     for what changed since the request, and against the words the batch leaves
		///     for the references it follows, so a key referring to another the answer
		///     translates reads that translation. A key skipped writes nothing, so the rest
		///     are checked again without it, until no more are skipped. A key answered
		///     twice keeps its first answer. A key the batch does not write whose
		///     translation it leaves at odds with its default, where it was not, is named
		///     too (<see cref="TranslationBatch.Broken"/>).
		/// </summary>
		/// <param name="keys">The file's keys by name, as they are now.</param>
		/// <param name="code">The answer's language.</param>
		/// <param name="defaultLanguage">The language the default is written in; English when the file declares none.</param>
		public TranslationBatch Check(IReadOnlyDictionary<string, WordsKey> keys, string code, string? defaultLanguage) {
			var defaults = new DefaultWordsProvider(keys);
			//the keys the batch writes, as it leaves them, over the file's
			Dictionary<string, WordsKey> written = [];
			var after = new LanguageWordsProvider([keys, written], code);
			var verdicts = new TranslationVerdict[Items.Count];
			List<int> standing = [];
			HashSet<string> answered = [];
			for (int i = 0; i < Items.Count; i++) {
				if (answered.Add(Items[i].Key)) {
					standing.Add(i);
					if (Items[i].Trial(keys.GetValueOrDefault(Items[i].Key), code) is { } trial) {
						written[Items[i].Key] = trial;
					}
				}
			}
			//each answer against what the others write, until none more is skipped:
			//a key skipped goes out of what the batch leaves, so it only ever shrinks
			for (bool skipped = true; skipped;) {
				skipped = false;
				foreach (int i in standing.ToList()) {
					TranslationItem item = Items[i];
					verdicts[i] = item.Check(keys.GetValueOrDefault(item.Key), code, defaults, defaultLanguage, after);
					if (verdicts[i].Writes.Count == 0) {
						standing.Remove(i);
						skipped |= written.Remove(item.Key);
					}
				}
			}
			//a second answer for a key stands only where it says nothing
			for (int i = 0; i < Items.Count; i++) {
				if (verdicts[i] is null) {
					var again = Items[i].Check(keys.GetValueOrDefault(Items[i].Key), code, defaults, defaultLanguage, after);
					verdicts[i] = again.Writes.Count != 0 || again.Flags.Count != 0 ? new([], ["answered twice, and the first answer stands"], again.Left, []) : again;
				}
			}

			//a key not written that reads one written, its translation at odds now where it was not
			var before = new LanguageWordsProvider(keys, code);
			List<string> broken = [];
			foreach (var (name, key) in keys) {
				if (written.ContainsKey(name) || !ParameterUse.Follows(key, code, after)) {
					continue;
				}
				var now = ParameterUse.Check(key, code, defaults, defaultLanguage, after);
				var was = ParameterUse.Check(key, code, defaults, defaultLanguage, before);
				if (now.Any && !(now.Dropped.SequenceEqual(was.Dropped) && now.Extra.SequenceEqual(was.Extra))) {
					broken.Add($"{name}: through the words written, {string.Join("; ", TranslationItem.Mismatched(now, code))}");
				}
			}
			return new(verdicts, broken);
		}

		private static TranslationItem Item(JsonElement item, int index) {
			if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String) {
				throw new InvalidDataException($"the answer's item {index} names no key");
			}
			string name = key.GetString()!;
			return new(name,
				item.TryGetProperty("default", out var words) ? Slots(words, name, "default") : null,
				item.TryGetProperty("current", out var current) ? Slots(current, name, "current") : new Dictionary<string, string>(),
				item.TryGetProperty("translation", out var translation) ? Filled(translation, name) : new Dictionary<string, string?>());
		}

		private static Dictionary<string, string> Slots(JsonElement slots, string key, string name) {
			if (slots.ValueKind != JsonValueKind.Object) {
				throw new InvalidDataException($"{key}: \"{name}\" is no object of slots");
			}
			Dictionary<string, string> read = [];
			foreach (var slot in slots.EnumerateObject()) {
				read[slot.Name] = slot.Value.ValueKind == JsonValueKind.String ? slot.Value.GetString()! : throw new InvalidDataException($"{key}: {name}'s {slot.Name} is no text");
			}
			return read;
		}

		private static Dictionary<string, string?> Filled(JsonElement slots, string key) {
			if (slots.ValueKind != JsonValueKind.Object) {
				throw new InvalidDataException($"{key}: \"translation\" is no object of slots");
			}
			Dictionary<string, string?> read = [];
			foreach (var slot in slots.EnumerateObject()) {
				read[slot.Name] = slot.Value.ValueKind switch {
					JsonValueKind.String => slot.Value.GetString(),
					JsonValueKind.Null => null,
					_ => throw new InvalidDataException($"{key}: the translation's {slot.Name} is no text and no null"),
				};
			}
			return read;
		}
	}

	/// <summary>What an answer may write for one key, or why it writes nothing (<see cref="TranslationItem.Check"/>).</summary>
	/// <param name="Writes">The fields to write, each a slot in the target's language, in the answer's order.</param>
	/// <param name="Flags">Why the key is skipped: none when it is written.</param>
	/// <param name="Left">The slots the answer left null or empty.</param>
	/// <param name="Raised">
	///     Why the key's hand goes up as it is written, for a person to check: a link
	///     whose target the answer changed. None when it does not, and none for a key
	///     skipped.
	/// </param>
	public sealed record TranslationVerdict(IReadOnlyList<(WordsField Field, string Text)> Writes, IReadOnlyList<string> Flags, int Left, IReadOnlyList<string> Raised);

	/// <summary>What a whole answer may write (<see cref="TranslationAnswer.Check(IReadOnlyDictionary{string, WordsKey}, string, string?)"/>).</summary>
	/// <param name="Verdicts">Each item's verdict, in the answer's order.</param>
	/// <param name="Broken">
	///     The keys the batch does not write whose translation it leaves at odds with
	///     their default, where it was not: a key whose words read one written, each
	///     named and why. Nothing is skipped for them; a person looks.
	/// </param>
	public sealed record TranslationBatch(IReadOnlyList<TranslationVerdict> Verdicts, IReadOnlyList<string> Broken);

	/// <summary>One key of an answer.</summary>
	/// <param name="Key">The key, by the file's own name.</param>
	/// <param name="Default">The default's slots as the request carried them; null when the answer dropped them.</param>
	/// <param name="Current">The target's slots as the request carried them; none when it had no words.</param>
	/// <param name="Translation">The slots asked for, filled with text or left null.</param>
	public sealed record TranslationItem(string Key, IReadOnlyDictionary<string, string>? Default, IReadOnlyDictionary<string, string> Current, IReadOnlyDictionary<string, string?> Translation) {
		/// <summary>
		///     Checks the answer for <paramref name="key"/> as it is now (editor SPEC:
		///     Machine translation → Nothing is written unchecked), across all its slots:
		///     it keeps the parameters the default uses and adds none no definition
		///     names (<see cref="ParameterUse"/>), keeps every reference and constant
		///     the default holds and holds no reference, constant or selector the
		///     default does not, keeps each format item's alignment and format as the
		///     default writes them (<see cref="ParameterUse.Items"/>), writes no brace
		///     that opens nothing (<see cref="ParameterUse.Malformed(string)"/>), keeps the
		///     default's code spans, and gives each slot as many links as its default's,
		///     read as the app's markdown reads them. A changed link target is written,
		///     and raises the key's hand (<see cref="TranslationVerdict.Raised"/>). A slot the key has no use for in
		///     the language (<see cref="TranslationRequest.Slots"/>) or that the request
		///     did not ask for (<see cref="TranslationRequest.Asked"/>), and a default or
		///     a translation that changed since the request, or a key that is gone, flag
		///     it too. A flagged key writes none of its slots; an
		///     answer with nothing filled writes nothing and is flagged for nothing.
		/// </summary>
		/// <param name="key">The key now; null when the file has it no more.</param>
		/// <param name="code">The answer's language.</param>
		/// <param name="defaults">The default's words, for the references.</param>
		/// <param name="defaultLanguage">The language the default is written in; English when the file declares none.</param>
		/// <param name="translations">
		///     The language's words, for the references: as the batch leaves them, where
		///     the answer is checked whole (<see cref="TranslationAnswer.Check(IReadOnlyDictionary{string, WordsKey}, string, string?)"/>).
		/// </param>
		public TranslationVerdict Check(WordsKey? key, string code, IWordsProvider defaults, string? defaultLanguage, IWordsProvider translations) {
			var filled = Filled();
			int left = Translation.Count - filled.Count;
			if (filled.Count == 0) {
				return new([], [], left, []);
			}
			List<string> flags = [];
			if (key is null) {
				flags.Add("no such key now");
				return new([], flags, left, []);
			}
			if (key.IsConstant) {
				flags.Add("a constant, which is not translated");
				return new([], flags, left, []);
			}
			if (Default is null) {
				flags.Add("the answer dropped its default, so whether it changed is unknown");
			}
			else if (!Same(Default, TranslationRequest.Held(key))) {
				flags.Add("its default changed since the request");
			}
			var entry = key.Entries.GetValueOrDefault(code);
			if (!Same(Current, TranslationRequest.Held(entry))) {
				flags.Add($"its {code} words changed since the request");
			}
			//what the request asked for, as it would ask now: the words being as they were,
			//a slot it did not ask for has some, which the answer may not overwrite
			var slots = TranslationRequest.Slots(key, code);
			var asked = TranslationRequest.Asked(key, code);
			foreach (var (slot, _) in filled) {
				if (!slots.Contains(slot)) {
					flags.Add($"{slot} is no slot it has in {code}");
				}
				else if (!asked.Contains(slot)) {
					flags.Add($"{slot} was not asked for: it has {code} words already");
				}
			}
			if (flags.Count != 0) {
				return new([], flags, left, []);
			}

			//the key as the answer would leave it, checked as the editor checks a translation
			var (trial, writes) = Written(key, code, filled);
			var translated = trial.Entries[code];
			flags.AddRange(Mismatched(ParameterUse.Check(trial, code, defaults, defaultLanguage, translations), code));
			//words that come back round to themselves, through the words the batch leaves,
			//show # ∞ # where the default's do not: a reference moved from a form into the
			//plain value, say, now reached from the key it reaches
			if (ParameterUse.InLanguage(trial, code, translations).Circular && !ParameterUse.InDefault(key, defaults, defaultLanguage).Circular) {
				flags.Add($"the {code} words come back round to themselves, which an app shows as # ∞ #");
			}
			//a brace that opens nothing shows as written in the app, as set refuses to write one
			foreach (var (slot, text) in filled) {
				if (ParameterUse.Malformed(text) is { Count: > 0 } braces) {
					flags.Add($"the {code} {slot} holds {ParameterUse.Named(braces)}");
				}
			}
			var said = TranslationRequest.Held(key).Values.ToList();
			var answered = TranslationRequest.Held(translated).Values.ToList();
			var held = said.SelectMany(ParameterUse.Tokens).ToHashSet();
			var holds = answered.SelectMany(ParameterUse.Tokens).Select(token => token.Token).ToHashSet();
			Compare(held.Where(token => !token.Selects).Select(token => token.Token), holds, held.Select(token => token.Token), "");
			//a parameter's format items are kept as the default writes them, since
			//the app marks a format its argument does not take ({0:Q} for a
			//count); one the default does not use may come bare, as its definition lets
			//the parameter check pass it, and a parameter dropped whole is that check's
			var formats = said.SelectMany(ParameterUse.Items).ToHashSet();
			var formatting = answered.SelectMany(ParameterUse.Items).ToHashSet();
			var formatted = formats.Select(item => item.Name).ToHashSet();
			var formattedNow = formatting.Select(item => item.Name).ToHashSet();
			Compare(formats.Where(item => formattedNow.Contains(item.Name)).Select(item => item.Item),
				formatting.Where(item => formatted.Contains(item.Name) || item.Item != FoundParameters.Placeholder(item.Name)).Select(item => item.Item).ToHashSet(),
				formats.Select(item => item.Item), "");
			var raised = Markdown();
			return flags.Count != 0 ? new([], flags, left, []) : new(writes, [], left, raised);

			//code spans and links as the app's markdown reads them (MarkdownMarks): every
			//code span is kept as written; a link's target may change, since some pages
			//have one per language, but no service decides that alone, so the key goes in
			//with its hand raised, the change named; a slot with more or fewer links than
			//its default's, and markdown the app cannot read, are flagged. Why the hand goes up
			List<string> Markdown() {
				var before = TranslationRequest.Held(key).ToDictionary(slot => slot.Key, slot => MarkdownMarks.Read(slot.Value));
				var after = TranslationRequest.Held(translated).ToDictionary(slot => slot.Key, slot => MarkdownMarks.Read(slot.Value));
				var unread = after.Where(slot => slot.Value is null).Select(slot => slot.Key).ToList();
				foreach (string slot in unread) {
					flags.Add($"the {code} {slot} holds markdown the app cannot read");
				}
				//a default the app cannot read leaves nothing to hold the answer to
				if (unread.Count != 0 || before.Values.Any(marks => marks is null)) {
					return [];
				}
				Compare(before.Values.SelectMany(marks => marks!.Codes).Select(MarkdownMarks.Fenced),
					after.Values.SelectMany(marks => marks!.Codes).Select(MarkdownMarks.Fenced).ToHashSet(), null, "the code span ");
				//a filled slot's links beside its default's: a form's, else other's, else the plain value's
				List<string> changed = [], replaced = [];
				foreach (var (slot, _) in filled) {
					var was = before.GetValueOrDefault(slot) ?? before.GetValueOrDefault(TranslationRequest.Slot("other")) ?? before.GetValueOrDefault(TranslationRequest.Plain) ?? new([], []);
					var now = after[slot]!;
					if (now.Links.Count != was.Links.Count) {
						flags.Add($"the {code} {slot} holds {Links(now.Links.Count)} where the default holds {Links(was.Links.Count)}");
						continue;
					}
					//each target counted, so /a and /b answered /b twice is a change
					var left = was.Links.GroupBy(link => link).ToDictionary(same => same.Key, same => same.Count());
					foreach (string link in now.Links) {
						if (left.TryGetValue(link, out int count) && count > 0) {
							left[link] = count - 1;
						}
						else if (!changed.Contains(link)) {
							changed.Add(link);
						}
					}
					replaced.AddRange(left.Where(link => link.Value > 0).Select(link => link.Key).Except(replaced));
				}
				if (changed.Count == 0) {
					return [];
				}
				return [replaced.Count != 0
					? $"the {code} words link to {Joined(changed)} in place of {Joined(replaced)}"
					: $"the {code} words link to {Joined(changed)}, which the default does not"];

				static string Links(int count) => count switch { 0 => "no link", 1 => "1 link", _ => $"{count} links" };
			}

			//what the default holds and the answer must keep, and what the answer holds beyond what it may
			void Compare(IEnumerable<string> kept, HashSet<string> answer, IEnumerable<string>? allowed, string what) {
				var keep = kept.ToHashSet();
				var may = allowed?.ToHashSet() ?? keep;
				if (keep.Except(answer).ToList() is { Count: > 0 } dropped) {
					flags.Add($"the {code} words drop {what}{Joined(dropped)}, which the default holds");
				}
				if (answer.Except(may).ToList() is { Count: > 0 } added) {
					flags.Add($"the {code} words hold {what}{Joined(added)}, which the default does not");
				}
			}
		}

		//the slots the answer filled, in its order; blank text is none
		private List<(string Slot, string Text)> Filled()
			=> [.. Translation.Where(slot => !string.IsNullOrWhiteSpace(slot.Value)).Select(slot => (slot.Key, slot.Value!))];

		//the key as the answer leaves it, its filled slots written in code; null where it
		//fills none, or the key is gone or a constant, which it never writes, or it fills a
		//slot the key has no use for in the language, which Check flags before it writes
		internal WordsKey? Trial(WordsKey? key, string code)
			=> key is null || key.IsConstant || Filled() is not { Count: > 0 } filled
				|| filled.Any(slot => !TranslationRequest.Slots(key, code).Contains(slot.Slot)) ? null : Written(key, code, filled).Trial;

		private static (WordsKey Trial, List<(WordsField Field, string Text)> Writes) Written(WordsKey key, string code, List<(string Slot, string Text)> filled) {
			var trial = new WordsKey(key);
			if (!trial.Entries.ContainsKey(code)) {
				trial.Entries[code] = new WordsEntry();
			}
			List<(WordsField Field, string Text)> writes = [];
			foreach (var (slot, text) in filled) {
				string form = slot == TranslationRequest.Plain ? "" : slot[(TranslationRequest.Plain.Length + 1)..];
				var field = new WordsField("value", code, form);
				field.Write(trial, text);
				writes.Add((field, text));
			}
			return (trial, writes);
		}

		//a parameter check's findings, as a flag and a gripe name them
		internal static IEnumerable<string> Mismatched(ParameterMismatch mismatch, string code) {
			if (mismatch.Dropped.Count != 0) {
				yield return $"the {code} words drop {Joined(mismatch.Dropped.Select(FoundParameters.Placeholder))}, which the default uses";
			}
			if (mismatch.Extra.Count != 0) {
				yield return $"the {code} words use {Joined(mismatch.Extra.Select(FoundParameters.Placeholder))}, which the default does not and no param- defines";
			}
		}

		private static string Joined(IEnumerable<string> items) => string.Join(", ", items);

		private static bool Same(IReadOnlyDictionary<string, string> sent, IReadOnlyDictionary<string, string> now)
			=> sent.Where(slot => slot.Value != "").Count() == now.Count && now.All(slot => sent.TryGetValue(slot.Key, out string? text) && text == slot.Value);
	}
}
