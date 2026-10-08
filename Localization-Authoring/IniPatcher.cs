using PatTech.Localization;
using System.Text;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A <c>words.ini</c> edited in place (editor SPEC: A command line for tools).
	///     A change replaces only the lines of the field it names with what
	///     <see cref="IniWriter"/> writes for that one pair, in the file's own line
	///     ending, encoding and BOM, and every other byte stays. Each change is read
	///     back and compared with the model before it: anything but the asked-for
	///     change refuses it, with an <see cref="IniPatchException"/>, and leaves the
	///     patcher as it was. Keys are the file's own, without a session's label.
	/// </summary>
	public sealed class IniPatcher {
		private readonly Encoding encoding;
		private readonly byte[] bom;
		private readonly string newLine;
		private readonly bool endsWithBreak;
		private List<Line> lines;
		private Reading reading;

		private IniPatcher(Encoding encoding, byte[] bom, string text) {
			this.encoding = encoding;
			this.bom = bom;
			lines = Split(text);
			newLine = lines.Select(line => line.Break).FirstOrDefault(lineBreak => lineBreak != "") ?? Environment.NewLine;
			endsWithBreak = lines.Count == 0 || lines[^1].Break != "";
			reading = Read(lines);
		}

		/// <summary>Reads the file at <paramref name="path"/>.</summary>
		/// <exception cref="InvalidDataException">The file is not UTF-8, UTF-16 or UTF-32 text, or does not parse.</exception>
		public static IniPatcher Open(string path) => FromBytes(File.ReadAllBytes(path));

		/// <summary>
		///     Reads a file's bytes: UTF-8, with or without a BOM, or UTF-16 or UTF-32
		///     with one, by the BOMs Wordsmith reads. Text holding a NUL is none:
		///     UTF-16 without its BOM reads as UTF-8 with a NUL in every other byte.
		/// </summary>
		/// <exception cref="InvalidDataException">The bytes are no such text, or do not parse.</exception>
		public static IniPatcher FromBytes(byte[] bytes) {
			var encoding = WordsSession.EncodingOf(bytes);
			int bomLength = encoding.Preamble.Length;
			string name = bomLength == 0 ? "UTF-8" : encoding.WebName;
			string text;
			try {
				text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
			}
			catch (DecoderFallbackException) {
				throw new InvalidDataException($"not {name} text");
			}
			if (text.Contains('\0')) {
				throw new InvalidDataException(bomLength == 0 ? "not UTF-8 text: it holds NULs, as UTF-16 without a BOM does" : $"not {name} text: it holds a NUL");
			}
			return new IniPatcher(encoding, bytes[..bomLength], text);
		}

		/// <summary>The file as it now reads, BOM first.</summary>
		public byte[] ToBytes() => [.. bom, .. encoding.GetBytes(Text)];

		/// <summary>The file's text as it now reads.</summary>
		public string Text => string.Concat(lines.Select(line => line.Text + line.Break));

		/// <summary>
		///     Writes the file to <paramref name="path"/>: to a temporary sibling first,
		///     moved over the original once written, so a failure leaves it whole. A
		///     link is followed to its file, and a Unix file keeps its mode.
		/// </summary>
		public void Save(string path) {
			string full = Path.GetFullPath(path);
			if (File.Exists(full) && File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target) {
				full = target.FullName;
			}
			string temp = Path.Combine(Path.GetDirectoryName(full) ?? ".", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
			try {
				File.WriteAllBytes(temp, ToBytes());
				if (!OperatingSystem.IsWindows() && File.Exists(full)) {
					File.SetUnixFileMode(temp, File.GetUnixFileMode(full));
				}
				File.Move(temp, full, overwrite: true);
			}
			catch {
				try { File.Delete(temp); } catch { /* the write's own error is the one worth raising */ }
				throw;
			}
		}

		/// <summary>What the parser gripes about in the file as it now reads.</summary>
		public IReadOnlyList<string> Errors => reading.Words.Errors;
		/// <summary>The file's own language table, in its order.</summary>
		public IReadOnlyList<string> Languages => reading.Words.DeclaredLanguages;
		/// <summary>The language the default is written in, or <see langword="null"/>.</summary>
		public string? DefaultLanguage => reading.Words.DefaultLanguage;

		/// <summary>
		///     The file's keys in the order their blocks first appear. A bare header
		///     is a group, not a key, and is left out, as the editor drops it.
		/// </summary>
		public IEnumerable<WordsKey> Keys => reading.Order.Select(key => reading.Words.WordKeys[key]).Where(key => !key.IsEmpty());

		/// <summary>The key named <paramref name="key"/>, or <see langword="null"/> when the file has none.</summary>
		public WordsKey? Find(string key) => reading.Words.WordKeys.TryGetValue(key, out var found) && !found.IsEmpty() ? found : null;

		/// <summary>The key's whole block as Save writes it, under a full header and broken with <c>\n</c>, or <see langword="null"/> when there is none.</summary>
		public string? Block(string key) {
			if (Find(key) is not { } found) {
				return null;
			}
			var writer = new StringWriter { NewLine = "\n" };
			using (var ini = new IniWriter(writer)) {
				//the writer drops a leading label segment from the header
				ini.WriteBlock(new WordsKey(found) { BlockKey = "_." + found.BlockKey });
				return writer.ToString().TrimEnd('\r', '\n');
			}
		}

		/// <summary>
		///     Sets <paramref name="field"/> of <paramref name="key"/> to
		///     <paramref name="text"/>, adding the field, or the key, where it is
		///     missing. A field already holding the text is left alone; a field declared
		///     more than once keeps its first place, and its others go. A new field goes
		///     after the last of its block, a new key after the header chain holding
		///     its nearest sibling, as a full header. A parameter whose text names no
		///     type before its first <c>:</c> is written as a String's, behind
		///     <c>String:</c>, so its words read back whole.
		/// </summary>
		/// <returns>The gripes the change adds to the file's.</returns>
		/// <exception cref="ArgumentException"><paramref name="key"/> is no key name (<see cref="WordsParser.IsKeyName"/>), or <paramref name="text"/> holds a NUL.</exception>
		/// <exception cref="IniPatchException">The change would change anything else.</exception>
		public IReadOnlyList<string> Set(string key, WordsField field, string text) {
			CheckKey(key);
			if (text.Contains('\0')) {
				throw new ArgumentException($"{key}: {field} can't hold a NUL, which would leave the file no text");
			}
			text = text.Replace("\r\n", "\n").Replace('\r', '\n');
			if (field is { Type: "stale", Language: "" }) {
				text = ""; //the default's stale mark keeps no words
			}
			List<string> notes = [];
			if (field.Type == "param" && text.Split(':', count: 2) is [var type, _] && !WordsParameterType.All.Any(known => known.Name == type)) {
				//read as written, the words before the first ':' would go as the type
				text = $"{WordsParameterType.String.Name}:{text}";
				notes.Add($"{key}: {field} names no type {type} ({string.Join(", ", WordsParameterType.All.Select(known => known.Name))}), so it is written as a String's words: {text}");
			}
			var own = Declarations(key, field);
			if (own.Count == 1 && Find(key) is { } found && field.Read(found) == text) {
				return notes;
			}
			var edit = new Edit(lines);
			List<Line> pair = Pair(field.ToString(), text);
			if (own.Count != 0) {
				edit.Replace(own[0].Start, own[0].End, pair);
				foreach (var other in own.Skip(1)) {
					edit.Delete(other.Start, other.End);
				}
				if (own.Count > 1) {
					notes.Add($"{key}: {field} was declared {own.Count} times, now once");
				}
			}
			else if (reading.Headers.Any(header => header.Key == key)) {
				//after the last field of its block, or right under its header
				int last = reading.Declarations.Where(declaration => declaration.InBlock && declaration.Key == key).Select(declaration => declaration.End)
					.DefaultIfEmpty(reading.Headers.Last(header => header.Key == key).Line).Max();
				edit.Insert(last + 1, pair);
			}
			else {
				int at = NewKeyLine(key);
				bool spaced = at == 0 || lines[at - 1].Text.Trim() == "";
				edit.Insert(at, [.. spaced ? [] : (Line[])[new("", newLine)], new($"[{key}]", newLine), .. pair]);
			}
			return [.. notes, .. Commit(edit.Apply(), keys => {
				if (!keys.TryGetValue(key, out var changed)) {
					keys[key] = changed = new WordsKey(key) { IsConstant = key[0] == '$' };
				}
				field.Write(changed, text);
			})];
		}

		/// <summary>Drops <paramref name="field"/> of <paramref name="key"/>, every declaration of it, with words or empty.</summary>
		/// <returns>The gripes the change adds to the file's.</returns>
		/// <exception cref="KeyNotFoundException">The key's blocks declare no such field.</exception>
		/// <exception cref="IniPatchException">The change would change anything else.</exception>
		public IReadOnlyList<string> Remove(string key, WordsField field) {
			var own = Declarations(key, field);
			if (own.Count == 0) {
				throw new KeyNotFoundException($"{key} has no {field}");
			}
			var edit = new Edit(lines);
			foreach (var declaration in own) {
				edit.Delete(declaration.Start, declaration.End);
			}
			return Commit(Collapse(edit), keys => field.Write(keys[key], null));
		}

		/// <summary>
		///     True when a block of <paramref name="key"/> declares
		///     <paramref name="field"/>, with words or empty: an empty field reads as
		///     none (<see cref="WordsField.Read"/>), but its line is there to remove.
		/// </summary>
		public bool Declares(string key, WordsField field) => Declarations(key, field).Count != 0;

		/// <summary>
		///     Drops the whole of <paramref name="key"/>: its headers and its fields. A
		///     header that bases <c>[.child]</c> headers stays, bare, which reads back as
		///     a group; the comments above it stand where they are.
		/// </summary>
		/// <returns>The gripes the change adds to the file's.</returns>
		/// <exception cref="KeyNotFoundException">The file has no such key.</exception>
		/// <exception cref="IniPatchException">The change would change anything else, or the key is a constant that bases <c>[.child]</c> headers.</exception>
		public IReadOnlyList<string> Remove(string key) {
			if (Find(key) is null) {
				throw new KeyNotFoundException($"no key {key}");
			}
			var edit = new Edit(lines);
			var headers = reading.Headers;
			for (int i = 0; i < headers.Count; i++) {
				bool bases = headers[i].IsFull && i + 1 < headers.Count && !headers[i + 1].IsFull;
				if (headers[i].Key != key) {
					continue;
				}
				if (bases && key.StartsWith('$')) {
					//its bare header would still be the constant, and without it the children re-base
					throw new IniPatchException($"refused: {key} bases [.child] headers, which no constant can; give them full headers first");
				}
				if (!bases) {
					edit.Delete(headers[i].Line, headers[i].Line);
				}
			}
			foreach (var declaration in reading.Declarations.Where(declaration => declaration.InBlock && declaration.Key == key)) {
				edit.Delete(declaration.Start, declaration.End);
			}
			return Commit(Collapse(edit), keys => keys.Remove(key));
		}

		//a key's name, which a runtime reads (runtime SPEC: Key names); a file's own
		//invalid block can still be read and removed, but not written to
		private static void CheckKey(string key) {
			if (!WordsParser.IsKeyName(key)) {
				throw new ArgumentException($"'{key}' is no key name: {WordsParserToLocalizationProvider.KeyNameRule}");
			}
		}

		private List<Declaration> Declarations(string key, WordsField field)
			=> [.. reading.Declarations.Where(declaration => declaration.InBlock && declaration.Key == key && declaration.Field == field)];

		//the pair's lines as the writer writes them, folded and escaped
		private List<Line> Pair(string name, string text) {
			var writer = new StringWriter { NewLine = newLine };
			using var ini = new IniWriter(writer);
			ini.WritePair(name, text);
			return Split(writer.ToString());
		}

		//where a new key's block goes: after the header chain holding the key that
		//shares the most leading segments with it, the last of them; after the last
		//chain when none shares any, and at the end of a file with no blocks
		private int NewKeyLine(string key) {
			var headers = reading.Headers;
			if (headers.Count == 0) {
				return lines.Count;
			}
			int nearest = headers.Count - 1, most = 0;
			for (int i = 0; i < headers.Count; i++) {
				int shared = Shared(key, headers[i].Key);
				if (shared > 0 && shared >= most) {
					(nearest, most) = (i, shared);
				}
			}
			//the chain runs from its full header to the next one; it ends at its last
			//header or field line, so the comments above the next block stay with it
			int first = nearest, end = nearest + 1;
			while (first > 0 && !headers[first].IsFull) {
				first--;
			}
			while (end < headers.Count && !headers[end].IsFull) {
				end++;
			}
			int last = headers[end - 1].Line;
			foreach (var declaration in reading.Declarations) {
				if (declaration.Header >= first && declaration.Header < end) {
					last = Math.Max(last, declaration.End);
				}
			}
			return last + 1;
		}

		//the dotted segments two keys start with alike
		private static int Shared(string a, string b) {
			string[] left = a.Split('.'), right = b.Split('.');
			int count = 0;
			while (count < left.Length && count < right.Length && left[count] == right[count]) {
				count++;
			}
			return count;
		}

		//a removal leaves no doubled blank line where its lines were, nor a blank
		//one at the end of the file; a blank line inside a kept field is its text
		private List<Line> Collapse(Edit edit) {
			var (result, origins, gaps) = edit.ApplyTracked();
			bool Blank(int index) => origins[index] >= 0 && result[index].Text.Trim() == ""
				&& !reading.Declarations.Any(declaration => origins[index] >= declaration.Start && origins[index] <= declaration.End);
			foreach (int gap in gaps.OrderDescending()) {
				if (gap < result.Count && Blank(gap) && (gap == 0 || Blank(gap - 1))) {
					result.RemoveAt(gap);
					origins.RemoveAt(gap);
				}
				else if (gap == result.Count && gap > 0 && Blank(gap - 1)) {
					result.RemoveAt(gap - 1);
					origins.RemoveAt(gap - 1);
				}
			}
			return result;
		}

		//reads the edit back and compares it with the change asked for; only a match is kept
		private List<string> Commit(List<Line> edited, Action<Dictionary<string, WordsKey>> change) {
			for (int i = 0; i < edited.Count; i++) {
				bool last = i == edited.Count - 1;
				string lineBreak = last && !endsWithBreak ? "" : edited[i].Break == "" ? newLine : edited[i].Break;
				edited[i] = edited[i] with { Break = lineBreak };
			}
			var after = Read(edited);
			var expected = reading.Words.WordKeys.ToDictionary(pair => pair.Key, pair => new WordsKey(pair.Value));
			change(expected);
			if (Difference(reading, after, expected) is { } difference) {
				throw new IniPatchException($"refused: the edit would also change {difference}; the file is left as it was");
			}
			List<string> gripes = [.. after.Words.Errors];
			foreach (string known in reading.Words.Errors) {
				gripes.Remove(known);
			}
			(lines, reading) = (edited, after);
			return gripes;
		}

		//the first way the read-back differs from what was asked, or null
		private static string? Difference(Reading before, Reading after, Dictionary<string, WordsKey> expected) {
			var wanted = Flatten(expected);
			var got = Flatten(after.Words.WordKeys);
			foreach (string key in wanted.Keys.Concat(got.Keys).Distinct()) {
				if (!got.TryGetValue(key, out var gotFields)) {
					return $"{key}, which would vanish";
				}
				if (!wanted.TryGetValue(key, out var wantedFields)) {
					return $"{key}, which would appear";
				}
				foreach (string field in wantedFields.Keys.Concat(gotFields.Keys).Distinct()) {
					string? want = wantedFields.GetValueOrDefault(field), have = gotFields.GetValueOrDefault(field);
					if (want != have) {
						return $"{key} {field}, which would read {(have is null ? "nothing" : $"'{have}'")}";
					}
				}
			}
			var (was, now) = (before.Words, after.Words);
			if (!was.DeclaredLanguages.SequenceEqual(now.DeclaredLanguages)
				|| was.DeclaredLanguages.Any(code => was.KnownLanguages[code].NativeName != now.KnownLanguages[code].NativeName
					|| was.KnownLanguages[code].EnglishName != now.KnownLanguages[code].EnglishName)) {
				return "the language table";
			}
			if (was.DefaultLanguage != now.DefaultLanguage || was.Settings != now.Settings
				|| !was.LanguageSettings.OrderBy(pair => pair.Key).SequenceEqual(now.LanguageSettings.OrderBy(pair => pair.Key))) {
				return "the file's settings";
			}
			if (!before.Comments.SequenceEqual(after.Comments)) {
				return "a comment";
			}
			return null;
		}

		//key → field → text, for every key that is not a bare group
		private static Dictionary<string, Dictionary<string, string>> Flatten(IReadOnlyDictionary<string, WordsKey> keys) {
			Dictionary<string, Dictionary<string, string>> flat = [];
			foreach (var (name, key) in keys) {
				if (!key.IsEmpty()) {
					flat[name] = WordsField.All(key).ToDictionary(pair => pair.Field.ToString(), pair => pair.Text);
				}
			}
			return flat;
		}

		private static Reading Read(List<Line> lines) {
			var reading = new Reading();
			try {
				new WordsParser(reading).Load(new StringReader(string.Concat(lines.Select(line => line.Text + "\n"))));
			}
			catch (Exception ex) when (ex is not OutOfMemoryException) {
				throw new InvalidDataException($"does not parse: {ex.Message}", ex);
			}
			return reading;
		}

		//lines as a TextReader reads them, each with its own break: \r\n, \n or \r,
		//and none on a last line that has none
		private static List<Line> Split(string text) {
			List<Line> lines = [];
			int start = 0;
			for (int i = 0; i < text.Length; i++) {
				if (text[i] is '\r' or '\n') {
					int width = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
					lines.Add(new(text[start..i], text.Substring(i, width)));
					i += width - 1;
					start = i + 1;
				}
			}
			if (start < text.Length) {
				lines.Add(new(text[start..], ""));
			}
			return lines;
		}

		private readonly record struct Line(string Text, string Break);

		/// <summary>A <c>[header]</c> line: the key it opens, and whether it is a full one or <c>[.child]</c>.</summary>
		private readonly record struct Header(int Line, string Key, bool IsFull);

		/// <summary>A field's lines, its declaration and its continuations; <see cref="Header"/> is the one it sits under, -1 in the language section.</summary>
		private readonly record struct Declaration(string Key, int Header, WordsField Field, int Start, int End) {
			public bool InBlock => Header >= 0;
		}

		//the ini reader's model, with where each header and field sits
		private sealed class Reading : IWordsParserConsumer {
			public WordsParserToLocalizationProvider Words { get; } = new();
			public List<Header> Headers { get; } = [];
			public List<Declaration> Declarations { get; } = [];
			public List<string> Comments { get; } = [];
			public List<string> Order { get; } = [];
			private readonly HashSet<string> seen = [];
			private int line;

			public void VisitLine(int number) => line = number - 1;

			public void VisitBlock(string baseKey, string key) {
				string name = key[0] == '.' ? baseKey + key : key;
				Headers.Add(new(line, name, key[0] != '.'));
				if (seen.Add(name)) {
					Order.Add(name);
				}
				((IWordsParserConsumer)Words).VisitBlock(baseKey, key);
			}

			public void VisitFieldDeclaration(FieldKey key, string text) {
				int mark = key.FieldType.IndexOf('#');
				var field = mark < 0 ? new WordsField(key.FieldType, key.LanguageCode, "") : new WordsField(key.FieldType[..mark], key.LanguageCode, key.FieldType[(mark + 1)..]);
				Declarations.Add(new(key.BlockKey, Headers.Count - 1, field, line, line));
				Words.VisitFieldDeclaration(key, text);
			}

			public void VisitFieldContinuation(FieldKey key, string value) {
				Declarations[^1] = Declarations[^1] with { End = line };
				Words.VisitFieldContinuation(key, value);
			}

			public void VisitComment(string text) {
				Comments.Add(text);
				Words.VisitComment(text);
			}
		}

		//changes to a file's lines, by their place in it, applied in one pass
		private sealed class Edit(IReadOnlyList<Line> lines) {
			private readonly Dictionary<int, List<Line>> inserts = [];
			private readonly HashSet<int> deleted = [];

			public void Insert(int at, IEnumerable<Line> added) {
				if (!inserts.TryGetValue(at, out var list)) {
					inserts[at] = list = [];
				}
				list.AddRange(added);
			}

			public void Delete(int start, int end) {
				for (int i = start; i <= end; i++) {
					deleted.Add(i);
				}
			}

			public void Replace(int start, int end, IEnumerable<Line> added) {
				Delete(start, end);
				Insert(start, added);
			}

			public List<Line> Apply() => ApplyTracked().Lines;

			//the lines, where each came from (-1 for a new one), and where a run of deleted ones was
			public (List<Line> Lines, List<int> Origins, List<int> Gaps) ApplyTracked() {
				List<Line> result = [];
				List<int> origins = [], gaps = [];
				bool skipping = false;
				for (int i = 0; i <= lines.Count; i++) {
					if (inserts.TryGetValue(i, out var added)) {
						result.AddRange(added);
						origins.AddRange(added.Select(_ => -1));
						skipping = false;
					}
					if (i == lines.Count) {
						break;
					}
					if (deleted.Contains(i)) {
						if (!skipping) {
							gaps.Add(result.Count);
						}
						skipping = true;
					}
					else {
						result.Add(lines[i]);
						origins.Add(i);
						skipping = false;
					}
				}
				return (result, origins, gaps);
			}
		}
	}

	/// <summary>An <see cref="IniPatcher"/> change refused: reading it back showed more than the change asked for.</summary>
	public sealed class IniPatchException(string message) : Exception(message);
}
