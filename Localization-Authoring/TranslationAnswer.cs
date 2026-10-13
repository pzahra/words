using PatTech.Localization;
using System.Text.Json;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A translator's answer (editor SPEC: Machine translation): the request sent
	///     back with its nulls filled. Only the target's code and each item's key,
	///     default, current words and translation are read, so a script may drop the
	///     rest.
	/// </summary>
	/// <param name="Target">The language the answer is in.</param>
	/// <param name="Items">The items, in the answer's order.</param>
	public sealed record TranslationAnswer(string Target, IReadOnlyList<TranslationItem> Items) {
		/// <summary>
		///     Reads an answer. Text around the document, a model's code fence or a
		///     sentence before it, is dropped: the document runs from the first
		///     <c>{</c> to the last <c>}</c>. Comments and trailing commas are let by.
		/// </summary>
		/// <exception cref="InvalidDataException">The text is no JSON, or no translation document, or holds broken Unicode.</exception>
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
				throw new InvalidDataException($"the answer is no JSON: {ex.Message}", ex);
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
			return new(code.GetString()!, read);
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
		///     default writes them (<see cref="ParameterUse.Items"/>), keeps the
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
		/// <param name="translations">The language's words, for the references.</param>
		public TranslationVerdict Check(WordsKey? key, string code, IWordsProvider defaults, string? defaultLanguage, IWordsProvider translations) {
			var filled = Translation.Where(slot => !string.IsNullOrWhiteSpace(slot.Value)).Select(slot => (Slot: slot.Key, Text: slot.Value!)).ToList();
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
			var trial = new WordsKey(key);
			if (!trial.Entries.TryGetValue(code, out var translated)) {
				trial.Entries[code] = translated = new WordsEntry();
			}
			List<(WordsField Field, string Text)> writes = [];
			foreach (var (slot, text) in filled) {
				string form = slot == TranslationRequest.Plain ? "" : slot[(TranslationRequest.Plain.Length + 1)..];
				var field = new WordsField("value", code, form);
				field.Write(trial, text);
				writes.Add((field, text));
			}
			var mismatch = ParameterUse.Check(trial, code, defaults, defaultLanguage, translations);
			if (mismatch.Dropped.Count != 0) {
				flags.Add($"the {code} words drop {Joined(mismatch.Dropped.Select(FoundParameters.Placeholder))}, which the default uses");
			}
			if (mismatch.Extra.Count != 0) {
				flags.Add($"the {code} words use {Joined(mismatch.Extra.Select(FoundParameters.Placeholder))}, which the default does not and no param- defines");
			}
			var said = TranslationRequest.Held(key).Values.ToList();
			var answered = TranslationRequest.Held(translated).Values.ToList();
			var held = said.SelectMany(ParameterUse.Tokens).ToHashSet();
			var holds = answered.SelectMany(ParameterUse.Tokens).Select(token => token.Token).ToHashSet();
			Compare(held.Where(token => !token.Selects).Select(token => token.Token), holds, held.Select(token => token.Token), "");
			//a parameter's format items are kept as the default writes them, since
			//string.Format throws on a format its argument does not take ({0:Q} for a
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
					changed.AddRange(now.Links.Except(was.Links).Except(changed));
					replaced.AddRange(was.Links.Except(now.Links).Except(replaced));
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

		private static string Joined(IEnumerable<string> items) => string.Join(", ", items);

		private static bool Same(IReadOnlyDictionary<string, string> sent, IReadOnlyDictionary<string, string> now)
			=> sent.Where(slot => slot.Value != "").Count() == now.Count && now.All(slot => sent.TryGetValue(slot.Key, out string? text) && text == slot.Value);
	}
}
