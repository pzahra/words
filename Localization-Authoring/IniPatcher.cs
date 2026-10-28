using PatTech.Localization;
using PatTech.Text.Ini;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A <c>words.ini</c> edited in place (command line SPEC: Surgical edits), through the
	///     ini driver's words dialect (<see cref="WordsDialect"/>). A change replaces only the
	///     lines of the field it names with what <see cref="IniWriter"/> writes for that one
	///     pair, in the file's own line ending, encoding and BOM, and every other byte stays but
	///     the headers the cut of a group it touched changes. Each change is read back and
	///     compared with the file before it: anything but the asked-for change refuses it, with
	///     an <see cref="IniPatchException"/>, and leaves the patcher as it was. Opened from a
	///     file, it holds that file's transaction, and <see cref="Save"/> commits it. Keys are the
	///     file's own, without a session's label.
	/// </summary>
	public sealed class IniPatcher : IDisposable {
		private readonly IniTransaction? file;
		private readonly IniDocument document;
		private WordsReading reading;

		private IniPatcher(IniTransaction? file, IniDocument document) {
			this.file = file;
			this.document = document;
			reading = WordsReading.Of(document.Text);
		}

		/// <summary>Reads the file at <paramref name="path"/>, a link at its end followed, a write left pending on it finished first.</summary>
		/// <exception cref="InvalidDataException">The file is not UTF-8, UTF-16 or UTF-32 text.</exception>
		/// <exception cref="IniCommitException">The file is held, another write to it is running, or one left pending will not settle.</exception>
		public static IniPatcher Open(string path) {
			IniTransaction opened = IniTransaction.Open(path, WordsDialect.Instance);
			try {
				Readable(opened.Document);
				return new IniPatcher(opened, opened.Document);
			}
			catch {
				opened.Dispose();
				throw;
			}
		}

		/// <summary>
		///     Reads a file's bytes: UTF-8, with or without a BOM, or UTF-16 or UTF-32
		///     with one, by the BOMs Wordsmith reads. Text holding a NUL is none:
		///     UTF-16 without its BOM reads as UTF-8 with a NUL in every other byte.
		/// </summary>
		/// <exception cref="InvalidDataException">The bytes are no such text.</exception>
		public static IniPatcher FromBytes(byte[] bytes) {
			IniDocument read = IniDocument.Read(bytes, WordsDialect.Instance);
			Readable(read);
			return new IniPatcher(null, read);
		}

		//a document the patcher reads, or why it is none
		private static void Readable(IniDocument read) {
			if (read.IsText) {
				return;
			}
			if (!read.ByteOrderMark) {
				throw new InvalidDataException(read.ToBytes().Contains((byte)0) ? "not UTF-8 text: it holds NULs, as UTF-16 without a BOM does" : "not UTF-8 text");
			}
			throw new InvalidDataException($"not {read.Encoding?.WebName ?? "its BOM's"} text, or it holds a NUL");
		}

		/// <summary>What opening the file did to a write left pending on it; null for nothing.</summary>
		public IniRecovery? Recovery => file?.Recovery;

		/// <summary>Whether the file was not there to read, once a write left pending on it was finished.</summary>
		public bool IsNew => document.IsNew;

		/// <summary>The file as it now reads, BOM first.</summary>
		public byte[] ToBytes() => document.ToBytes();

		/// <summary>The file's text as it now reads.</summary>
		public string Text => document.Text;

		/// <summary>
		///     Writes the file to <paramref name="path"/>: the file it was opened from by committing
		///     its transaction, written ahead to a hidden log beside it, so a failure partway is
		///     finished by the next open (the ini driver's SPEC: The commit); any other, or one read
		///     from bytes, replaced whole the same way. Committed, its transaction ends: the patcher
		///     reads on, but takes no more changes.
		/// </summary>
		/// <exception cref="IniCommitException">Refused, nothing written: the file changed since it was read, is held or denied, or another write to it is running.</exception>
		/// <exception cref="IniPendingException">The write stands, written ahead to its log, but the file's own failed: the next open finishes it.</exception>
		/// <exception cref="IniEncodingException">Refused: a character the file's encoding cannot write.</exception>
		public void Save(string path) {
			if (file is not null && IsOwn(path)) {
				file.Commit();
				return;
			}
			using IniTransaction other = IniTransaction.Open(path, WordsDialect.Instance);
			other.Replace(document.Text, document.Encoding!, document.ByteOrderMark);
			other.Commit();
		}

		//whether path is the file the patcher was opened from, a link followed
		private bool IsOwn(string path) {
			string full = Path.GetFullPath(path);
			if (File.Exists(full) && File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target) {
				full = target.FullName;
			}
			return string.Equals(full, file!.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		}

		/// <summary>Lets go of the file without writing, where it was opened from one.</summary>
		public void Dispose() => file?.Dispose();

		/// <summary>What the parser gripes about in the file as it now reads.</summary>
		public IReadOnlyList<string> Errors => reading.Words.Errors;
		/// <summary>The file's own language table, in its order.</summary>
		public IReadOnlyList<string> Languages => reading.Words.DeclaredLanguages;
		/// <summary>The language the default is written in, or <see langword="null"/>.</summary>
		public string? DefaultLanguage => reading.Words.DefaultLanguage;
		/// <summary>What the file's table names <paramref name="code"/>: its English name, else its own; null when the table has neither.</summary>
		public string? LanguageName(string code)
			=> reading.Words.KnownLanguages.TryGetValue(code, out var entry) && !(entry.IsPlaceholder && entry.EnglishName == "") ? entry.DisplayName : null;

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
				ini.WriteBlock(found);
				return writer.ToString().TrimEnd('\r', '\n');
			}
		}

		/// <summary>
		///     Sets <paramref name="field"/> of <paramref name="key"/> to
		///     <paramref name="text"/>, adding the field, or the key, where it is
		///     missing. A field already holding the text is left alone; a field declared
		///     more than once keeps its first place, and its others go. A new field goes
		///     where the writer puts it among fields already in the writer's order, and
		///     last in a block in none; a new key goes beside its nearest sibling, into the
		///     chain whose full header it extends as a <c>[.child]</c>, and a group it makes
		///     worth a header gains one, its keys chained off it (<see cref="WordsDialect"/>).
		///     A parameter's text is written as given: the reader keeps a text whose words
		///     before the first <c>:</c> name no type whole.
		/// </summary>
		/// <returns>The gripes the change adds to the file's.</returns>
		/// <exception cref="ArgumentException"><paramref name="key"/> is no key name (<see cref="WordsParser.IsKeyName"/>), or <paramref name="text"/> holds a NUL.</exception>
		/// <exception cref="IniPatchException">The change would change anything else.</exception>
		public IReadOnlyList<string> Set(string key, WordsField field, string text) {
			CheckKey(key);
			if (text.Contains('\0')) {
				throw new ArgumentException($"{key}: {field} can't hold a NUL, which would leave the file no text");
			}
			text = Kept(text);
			int declared = Declarations(key, field);
			if (declared == 1 && Find(key) is { } found && field.Read(found) == text) {
				return [];
			}
			List<string> notes = declared > 1 ? [$"{key}: {field} was declared {declared} times, now once"] : [];
			return [.. notes, .. Edit(() => document.Set(key, field.ToString(), text))];
		}

		/// <summary>
		///     <paramref name="text"/> as <see cref="Set"/> keeps it, and as a field reads
		///     back: each line break, <c>\r\n</c> or a lone <c>\r</c>, a <c>\n</c>. Compare
		///     a value with a field's by this, or a <c>\r\n</c> of the shell's is a change.
		/// </summary>
		public static string Kept(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

		/// <summary>Drops <paramref name="field"/> of <paramref name="key"/>, every declaration of it, with words or empty; a block it leaves with no field goes, but a header that bases <c>[.child]</c> headers, which stays bare.</summary>
		/// <returns>The gripes the change adds to the file's.</returns>
		/// <exception cref="KeyNotFoundException">The key's blocks declare no such field.</exception>
		/// <exception cref="IniPatchException">The change would change anything else.</exception>
		public IReadOnlyList<string> Remove(string key, WordsField field) {
			if (Declarations(key, field) == 0) {
				throw new KeyNotFoundException($"{key} has no {field}");
			}
			return Edit(() => document.Remove(key, field.ToString()));
		}

		/// <summary>
		///     True when a block of <paramref name="key"/> declares
		///     <paramref name="field"/>, with words or empty: an empty field reads as
		///     none (<see cref="WordsField.Read"/>), but its line is there to remove.
		/// </summary>
		public bool Declares(string key, WordsField field) => Declarations(key, field) != 0;

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
			if (key.StartsWith('$') && Bases(key)) {
				//its bare header would still be the constant, and without it the children re-base
				throw new IniPatchException($"refused: {key} bases [.child] headers, which no constant can; give them full headers first");
			}
			return Edit(() => document.RemoveSection(key));
		}

		//a key's name, which a runtime reads (runtime SPEC: Key names); a file's own
		//invalid block can still be read and removed, but not written to
		private static void CheckKey(string key) {
			if (!WordsParser.IsKeyName(key)) {
				throw new ArgumentException($"'{key}' is no key name: {WordsParserToLocalizationProvider.KeyNameRule}");
			}
		}

		//how many times the key's blocks declare the field
		private int Declarations(string key, WordsField field)
			=> document.Elements.OfType<IniField>().Count(declared => declared.Section == key && document.Dialect.KeyNames.Equals(declared.Key, field.ToString()));

		//whether a full header of the key bases [.child] headers
		private bool Bases(string key) {
			List<IniHeader> headers = [.. document.Elements.OfType<IniHeader>()];
			return headers.Index().Any(header => header.Item.Name == key && !header.Item.IsChained && header.Index + 1 < headers.Count && headers[header.Index + 1].IsChained);
		}

		//an edit made and read back: refused, the file stays as it was; made, the gripes it adds
		private List<string> Edit(Action edit) {
			try {
				edit();
			}
			catch (IniCheckException refused) {
				throw new IniPatchException($"refused: the edit would also change {Say(refused.Differences[0])}; the file is left as it was");
			}
			WordsReading after = WordsReading.Of(document.Text);
			List<string> gripes = [.. after.Words.Errors];
			foreach (string known in reading.Words.Errors) {
				gripes.Remove(known);
			}
			reading = after;
			return gripes;
		}

		//what an edit would have changed besides itself, as the refusal says it
		private static string Say(IniDifference difference) => difference switch {
			{ Key: { } phrase } when phrase.Contains(' ') => phrase,
			{ Key: { } key } => $"{difference.Section} {key}, which would read {(difference.Actual is { } read ? $"'{read}'" : "nothing")}",
			{ Actual: null } => $"{difference.Section}, which would vanish",
			_ => $"{difference.Section}, which would appear",
		};
	}

	/// <summary>An <see cref="IniPatcher"/> change refused: reading it back showed more than the change asked for.</summary>
	public sealed class IniPatchException(string message) : Exception(message);
}
