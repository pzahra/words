using System.Text;

namespace PatTech.Text.Ini;

/// <summary>
///     A file loaded whole (SPEC.md, <i>A transaction</i>): its lines, each with its own break,
///     in its own encoding and byte-order mark, cut by its dialect into elements. An edit
///     replaces only the lines that hold what it names, and is checked before it stands: the
///     document parsed again must read as it did, the edit applied, or the edit is refused
///     and the document stays as it was. Every other line stays byte for byte, but the one a
///     line added after an unbroken last line gives a break.
/// </summary>
public sealed class IniDocument {
	private readonly byte[] read;
	private List<IniLine> lines = [];
	private List<IniElement> elements = [];
	private IniContent content;
	private byte[] mark = [];
	private IniEncodingFailure? failure;

	private IniDocument(IniDialect dialect, string? path, byte[] read, bool isNew) {
		Dialect = dialect;
		Path = path;
		this.read = read;
		IsNew = isNew;
		content = new IniContent(dialect.Names);
	}

	/// <summary>
	///     The document <paramref name="bytes"/> hold, decoded strictly: in the encoding a
	///     byte-order mark names, or the first of its rules' that reads them. Bytes no encoding
	///     reads make a document that is no text, refused for edits and still replaceable whole.
	/// </summary>
	public static IniDocument Read(byte[] bytes, IniDialect dialect, string? path = null) {
		ArgumentNullException.ThrowIfNull(bytes);
		ArgumentNullException.ThrowIfNull(dialect);
		(string? text, Encoding? encoding, byte[] mark, IniEncodingFailure? failure) = IniEncodings.Decode(bytes, dialect.Rules);
		var document = new IniDocument(dialect, path, [.. bytes], isNew: false) {
			Encoding = encoding,
			mark = mark,
			failure = failure,
		};
		if (text is not null) {
			document.Load(text);
		}
		return document;
	}

	/// <summary>A document of no file yet, in its rules' first encoding: built from edits on nothing, or replaced whole.</summary>
	public static IniDocument New(IniDialect dialect, string? path = null) {
		ArgumentNullException.ThrowIfNull(dialect);
		return new IniDocument(dialect, path, [], isNew: true) {
			Encoding = dialect.Rules.Encodings.FirstOrDefault() ?? new UTF8Encoding(false),
		};
	}

	/// <summary>Its dialect.</summary>
	public IniDialect Dialect { get; }
	/// <summary>The file it is, for the exceptions it throws; null for none.</summary>
	public string? Path { get; }
	/// <summary>The encoding it reads and writes in; null for bytes no encoding read.</summary>
	public Encoding? Encoding { get; private set; }
	/// <summary>Whether it carries a byte-order mark.</summary>
	public bool ByteOrderMark => mark.Length > 0;
	/// <summary>Whether it is of no file yet.</summary>
	public bool IsNew { get; }
	/// <summary>Whether it is text to edit; one that is not can still be replaced whole.</summary>
	public bool IsText => failure is null;
	/// <summary>Whether an edit or a replacement has changed it since it was read.</summary>
	public bool IsEdited { get; private set; }

	/// <summary>Its text, every line with its break.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public string Text {
		get {
			Readable();
			var text = new StringBuilder();
			foreach (IniLine line in lines) {
				text.Append(line.Text).Append(line.Break);
			}
			return text.ToString();
		}
	}

	/// <summary>Its elements, in order; lines it does not recognise are none.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public IReadOnlyList<IniElement> Elements {
		get {
			Readable();
			return [.. elements.Where(element => element is not IniOther)];
		}
	}

	/// <summary>Its sections, in the order they first stand, each named as it first is.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public IReadOnlyList<string> Sections {
		get {
			Readable();
			return content.Sections;
		}
	}

	/// <summary>Whether it has <paramref name="section"/>.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public bool HasSection(string section) {
		ArgumentNullException.ThrowIfNull(section);
		Readable();
		return content.Has(section);
	}

	/// <summary><paramref name="section"/>'s keys, in the order they are first declared; none for no such section.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public IReadOnlyList<string> Keys(string section) {
		ArgumentNullException.ThrowIfNull(section);
		Readable();
		return content.Keys(section);
	}

	/// <summary>The value of <paramref name="key"/> in <paramref name="section"/>, its last declaration's; null for none.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public string? Get(string section, string key) {
		ArgumentNullException.ThrowIfNull(section);
		ArgumentNullException.ThrowIfNull(key);
		Readable();
		return content.Get(section, key);
	}

	/// <summary>
	///     Sets <paramref name="key"/> in <paramref name="section"/>, or clears it for null. Its
	///     first declaration takes the value, in its own form while that holds it, and the others
	///     go; a new key goes after the section's last field, and a new section at the end after
	///     a blank line, the section <c>""</c> at the head.
	/// </summary>
	/// <exception cref="IniEditException">The key or the section's name will not write, or the value.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void Set(string section, string key, string? value) {
		ArgumentNullException.ThrowIfNull(section);
		ArgumentNullException.ThrowIfNull(key);
		if (value is null) {
			Remove(section, key);
			return;
		}
		Editable();
		Nameable(key);
		List<Occurrence> occurrences = Occurrences(section);
		List<IniField> declared = Declared(occurrences, key);
		if (declared.Count == 1 && declared[0].Value == value) {
			return;
		}
		var plan = new Plan();
		IniContent expected = content.Clone();
		expected.Set(section, key, value);
		if (declared.Count > 0) {
			plan.Replace(declared[0], Rewrite(declared[0], value));
			DropFields(plan, occurrences, declared.Skip(1));
		}
		else if (content.Has(section)) {
			Add(plan, occurrences[0], FieldLines(key, value));
		}
		else {
			AddSection(plan, section, FieldLines(key, value));
		}
		Land(plan, expected);
	}

	/// <summary>Clears <paramref name="key"/> in <paramref name="section"/>, every declaration of it; an occurrence of the section it leaves with no field goes.</summary>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void Remove(string section, string key) {
		ArgumentNullException.ThrowIfNull(section);
		ArgumentNullException.ThrowIfNull(key);
		Editable();
		List<Occurrence> occurrences = Occurrences(section);
		List<IniField> declared = Declared(occurrences, key);
		if (declared.Count == 0) {
			return;
		}
		var plan = new Plan();
		IniContent expected = content.Clone();
		if (DropFields(plan, occurrences, declared)) {
			expected.Remove(section, key);
		}
		else {
			expected.RemoveSection(section);
		}
		Land(plan, expected);
	}

	/// <summary>
	///     Replaces <paramref name="section"/>'s fields with <paramref name="pairs"/>, in their
	///     order, where its first field stood; comments and lines it does not recognise stay where
	///     they stand. A section it does not have is added.
	/// </summary>
	/// <exception cref="ArgumentException">A key given twice.</exception>
	/// <exception cref="IniEditException">A key or the section's name will not write, or a value.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void ReplaceSection(string section, IEnumerable<KeyValuePair<string, string>> pairs) {
		ArgumentNullException.ThrowIfNull(section);
		ArgumentNullException.ThrowIfNull(pairs);
		Editable();
		List<KeyValuePair<string, string>> given = [.. pairs];
		var keys = new HashSet<string>(Dialect.Names);
		foreach ((string key, string value) in given) {
			ArgumentNullException.ThrowIfNull(value, nameof(pairs));
			Nameable(key);
			if (!keys.Add(key)) {
				throw new ArgumentException($"{key} is given twice", nameof(pairs));
			}
		}
		if (section.Length == 0 && given.Count == 0) {
			//the head with no fields is no section
			RemoveSection(section);
			return;
		}
		List<(string Text, string? Break)> written = [.. given.SelectMany(pair => FieldLines(pair.Key, pair.Value))];
		var plan = new Plan();
		IniContent expected = content.Clone();
		expected.Replace(section, given);
		if (!content.Has(section)) {
			AddSection(plan, section, written);
			Land(plan, expected);
			return;
		}
		List<Occurrence> occurrences = Occurrences(section);
		List<IniField> fields = Fields(occurrences[0]);
		if (fields.Count > 0) {
			plan.Replace(fields[0], written);
			foreach (IniField field in fields.Skip(1)) {
				plan.Remove(field);
			}
		}
		else {
			Add(plan, occurrences[0], written);
		}
		foreach (Occurrence occurrence in occurrences.Skip(1).Where(Stands)) {
			Drop(plan, occurrence);
		}
		Land(plan, expected);
	}

	/// <summary>Removes <paramref name="section"/>, every occurrence of it: its header, its fields and the blank lines that part nothing that stays. Comments and lines it does not recognise stay where they stand.</summary>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void RemoveSection(string section) {
		ArgumentNullException.ThrowIfNull(section);
		Editable();
		if (!content.Has(section)) {
			return;
		}
		var plan = new Plan();
		foreach (Occurrence occurrence in Occurrences(section).Where(Stands)) {
			Drop(plan, occurrence);
		}
		IniContent expected = content.Clone();
		expected.RemoveSection(section);
		Land(plan, expected);
	}

	/// <summary>
	///     Renames <paramref name="section"/> to <paramref name="name"/> in every occurrence's
	///     header, its keys going with it; the head, renamed, gains a header above its first field.
	/// </summary>
	/// <exception cref="IniEditException">No such section, a name another section has, a name that will not write, or <c>""</c> where the dialect takes no <c>[]</c>.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void RenameSection(string section, string name) {
		ArgumentNullException.ThrowIfNull(section);
		ArgumentNullException.ThrowIfNull(name);
		Editable();
		if (!content.Has(section)) {
			throw new IniEditException(IniEditFailure.NoSuchSection, section, Path);
		}
		if (section == name) {
			return;
		}
		Headable(name);
		if (!Dialect.Names.Equals(section, name) && content.Has(name)) {
			throw new IniEditException(IniEditFailure.NameTaken, name, Path);
		}
		var plan = new Plan();
		foreach (Occurrence occurrence in Occurrences(section).Where(Stands)) {
			if (occurrence.Header is { } header) {
				plan.Replace(header, [(Rehead(header, name), lines[header.Line].Break)]);
			}
			else {
				plan.Insert(Fields(occurrence)[0].Line, [($"[{name}]", null)]);
			}
		}
		IniContent expected = content.Clone();
		expected.Rename(section, name);
		Land(plan, expected);
	}

	/// <summary>
	///     Moves <paramref name="section"/> after <paramref name="after"/>'s last field: every
	///     occurrence's fields gathered under one header, the head's under a <c>[]</c>. Comments
	///     and lines it does not recognise stay where they stand.
	/// </summary>
	/// <exception cref="IniEditException">No such section, or the head where the dialect takes no <c>[]</c>.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void MoveSection(string section, string after) {
		ArgumentNullException.ThrowIfNull(section);
		ArgumentNullException.ThrowIfNull(after);
		Editable();
		foreach (string name in (ReadOnlySpan<string>)[section, after]) {
			if (!content.Has(name)) {
				throw new IniEditException(IniEditFailure.NoSuchSection, name, Path);
			}
		}
		if (Dialect.Names.Equals(section, after)) {
			return;
		}
		if (section.Length == 0) {
			Headable(section);
		}
		List<Occurrence> moving = [.. Occurrences(section).Where(Stands)];
		var gathered = new List<(string Text, string? Break)> {
			("", null),
			(moving[0].Header is { } header ? lines[header.Line].Text : "[]", null),
		};
		var plan = new Plan();
		foreach (Occurrence occurrence in moving) {
			foreach (IniField field in Fields(occurrence)) {
				gathered.AddRange(lines.Skip(field.Line).Take(field.Count).Select(line => (line.Text, (string?)line.Break)));
			}
			Drop(plan, occurrence);
		}
		Occurrence target = Occurrences(after).Where(Stands).Last();
		(int at, _) = Into(target);
		if (at < lines.Count && !string.IsNullOrWhiteSpace(lines[at].Text)) {
			gathered.Add(("", null));
		}
		plan.Insert(at, gathered);
		Land(plan, content.Clone());
	}

	/// <summary>Replaces a comment block's lines with <paramref name="comment"/>'s; none removes it.</summary>
	/// <exception cref="IniEditException">The block no longer stands where it stood, or a line the dialect reads as no comment.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void SetComment(IniComment block, IEnumerable<string> comment) {
		ArgumentNullException.ThrowIfNull(block);
		ArgumentNullException.ThrowIfNull(comment);
		Editable();
		List<(string Text, string? Break)> written = Commentable(comment);
		IniElement located = Locate(block);
		var plan = new Plan();
		plan.Replace(located, written);
		Land(plan, content.Clone());
	}

	/// <summary>Removes a comment block.</summary>
	/// <exception cref="IniEditException">The block no longer stands where it stood.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void RemoveComment(IniComment block) => SetComment(block, []);

	/// <summary>Adds <paramref name="comment"/>'s lines above <paramref name="before"/>, or at the end for null.</summary>
	/// <exception cref="IniEditException">The element no longer stands where it stood, or a line the dialect reads as no comment.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void AddComment(IniElement? before, IEnumerable<string> comment) {
		ArgumentNullException.ThrowIfNull(comment);
		Editable();
		List<(string Text, string? Break)> written = Commentable(comment);
		int at = before is null ? lines.Count : Locate(before).Line;
		if (written.Count == 0) {
			return;
		}
		var plan = new Plan();
		plan.Insert(at, written);
		Land(plan, content.Clone());
	}

	/// <summary>Moves a comment block above <paramref name="before"/>, or to the end for null.</summary>
	/// <exception cref="IniEditException">Either no longer stands where it stood.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void MoveComment(IniComment block, IniElement? before) {
		ArgumentNullException.ThrowIfNull(block);
		Editable();
		IniElement located = Locate(block);
		int at = before is null ? lines.Count : Locate(before).Line;
		if (at == located.Line) {
			return;
		}
		var plan = new Plan();
		plan.Remove(located);
		plan.Insert(at, [.. lines.Skip(located.Line).Take(located.Count).Select(line => (line.Text, (string?)line.Break))]);
		Land(plan, content.Clone());
	}

	/// <summary>
	///     Replaces the document whole with <paramref name="text"/>, written in
	///     <paramref name="encoding"/>, for a file written in a form of the caller's own: no
	///     check, and a document that was no text is text again.
	/// </summary>
	public void Replace(string text, Encoding encoding, bool byteOrderMark = false) {
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(encoding);
		Unfrozen();
		Encoding = encoding;
		mark = byteOrderMark ? IniEncodings.MarkOf(encoding) : [];
		failure = null;
		Load(text);
		IsEdited = true;
	}

	/// <summary>Its bytes: as read while nothing changed it, else its text in its encoding behind its mark.</summary>
	/// <exception cref="IniEncodingException">Its text holds a character its encoding cannot write.</exception>
	public byte[] ToBytes() => IsEdited ? IniEncodings.Encode(Text, Encoding!, mark, Path) : [.. read];

	//the bytes it was read from, the file's to be checked against
	internal ReadOnlyMemory<byte> Original => read;

	private void Load(string text) {
		lines = Split(text);
		elements = Dialect.Parse(lines);
		content = IniContent.Of(elements, Dialect.Names);
	}

	//the text in lines, each with its break as read; a last line without one left so
	private static List<IniLine> Split(string text) {
		var split = new List<IniLine>();
		int start = 0;
		for (int at = 0; at < text.Length; at++) {
			if (text[at] is '\r' or '\n') {
				string lineBreak = text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? "\r\n" : text[at].ToString();
				split.Add(new IniLine(text[start..at], lineBreak));
				at += lineBreak.Length - 1;
				start = at + 1;
			}
		}
		if (start < text.Length) {
			split.Add(new IniLine(text[start..], ""));
		}
		return split;
	}

	//a prepared transaction has made its bytes: no edit until it ends
	internal bool Frozen { get; set; }

	private void Readable() {
		if (failure is { } why) {
			throw new IniEncodingException(why, Encoding?.WebName, Path);
		}
	}

	private void Editable() {
		Unfrozen();
		Readable();
	}

	private void Unfrozen() {
		if (Frozen) {
			throw new InvalidOperationException("The transaction is prepared: its bytes are made.");
		}
	}

	private void Nameable(string key) {
		if (!Dialect.CanName(key)) {
			throw new IniEditException(IniEditFailure.InvalidName, key, Path);
		}
	}

	private void Headable(string name) {
		if (name.Length == 0 ? !Dialect.Rules.EmptyHeader : !Dialect.CanHead(name)) {
			throw new IniEditException(name.Length == 0 ? IniEditFailure.EmptyHeader : IniEditFailure.InvalidName, name, Path);
		}
	}

	private List<(string Text, string? Break)> Commentable(IEnumerable<string> comment) {
		List<(string Text, string? Break)> written = [];
		foreach (string line in comment) {
			ArgumentNullException.ThrowIfNull(line, nameof(comment));
			if (line.Contains('\r') || line.Contains('\n') || !Dialect.IsComment(line)) {
				throw new IniEditException(IniEditFailure.NotAComment, line, Path);
			}
			written.Add((line, null));
		}
		return written;
	}

	//the element read before as it stands now; refused when it stands no longer where it stood
	private IniElement Locate(IniElement element) {
		IniElement? found = elements.Find(standing => standing.Line == element.Line);
		if (found is null || found.GetType() != element.GetType() || found.Count != element.Count || found.Snapshot != element.Snapshot) {
			throw new IniEditException(IniEditFailure.Moved, null, Path);
		}
		return found;
	}

	//a section's occurrences: each header named so and the elements under it to the next, the head
	//above the first header the first of the section ""'s, even with nothing in it
	private List<Occurrence> Occurrences(string section) {
		var all = new List<Occurrence>();
		IniHeader? header = null;
		int first = 0;
		for (int at = 0; at < elements.Count; at++) {
			if (elements[at] is IniHeader next) {
				all.Add(new Occurrence(header, first, at));
				header = next;
				first = at + 1;
			}
		}
		all.Add(new Occurrence(header, first, elements.Count));
		return [.. all.Where(occurrence => Dialect.Names.Equals(occurrence.Header?.Name ?? "", section))];
	}

	private List<IniField> Fields(Occurrence occurrence)
		=> [.. elements.Skip(occurrence.First).Take(occurrence.End - occurrence.First).OfType<IniField>()];

	private List<IniField> Declared(List<Occurrence> occurrences, string key)
		=> [.. occurrences.SelectMany(Fields).Where(field => Dialect.Names.Equals(field.Key, key))];

	//an occurrence that makes its section one: a header, or the head with a field
	private bool Stands(Occurrence occurrence) => occurrence.Header is not null || Fields(occurrence).Count > 0;

	//fields going from a section's occurrences, an occurrence they leave with none going whole;
	//true while the section still stands
	private bool DropFields(Plan plan, List<Occurrence> occurrences, IEnumerable<IniField> going) {
		var gone = going.ToHashSet();
		bool stands = false;
		foreach (Occurrence occurrence in occurrences) {
			List<IniField> fields = Fields(occurrence);
			if (fields.Count > 0 && fields.All(gone.Contains)) {
				Drop(plan, occurrence);
				continue;
			}
			foreach (IniField field in fields.Where(gone.Contains)) {
				plan.Remove(field);
			}
			stands |= occurrence.Header is not null || fields.Count > 0;
		}
		return stands;
	}

	//an occurrence going: its header, its fields, and the blank runs that part nothing staying;
	//comment blocks and lines no element stand, and one ending the file takes the blank run
	//above its header too
	private void Drop(Plan plan, Occurrence occurrence) {
		List<IniElement> span = elements.GetRange(occurrence.First, occurrence.End - occurrence.First);
		static bool Kept(IniElement element) => element is IniComment or IniOther;
		for (int at = 0; at < span.Count; at++) {
			bool parts = span.Take(at).Any(Kept) && (span.Skip(at + 1).Any(Kept) || occurrence.End < elements.Count);
			if (span[at] is IniField || (span[at] is IniBlank && !parts)) {
				plan.Remove(span[at]);
			}
		}
		if (occurrence.Header is { } header) {
			plan.Remove(header);
			int above = occurrence.First - 2;
			if (occurrence.End == elements.Count && !span.Any(Kept) && above >= 0 && elements[above] is IniBlank blank) {
				plan.Remove(blank);
			}
		}
	}

	//where a field added to an occurrence goes: after its last field, else after its header; the
	//head with neither starts the file, a blank line parting it from what follows
	private (int At, bool Parted) Into(Occurrence occurrence) {
		List<IniField> fields = Fields(occurrence);
		if (fields.Count > 0) {
			return (fields[^1].Line + fields[^1].Count, false);
		}
		if (occurrence.Header is { } header) {
			return (header.Line + 1, false);
		}
		return (0, lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[0].Text));
	}

	private void Add(Plan plan, Occurrence occurrence, List<(string Text, string? Break)> written) {
		(int at, bool parted) = Into(occurrence);
		plan.Insert(at, parted ? [.. written, ("", null)] : written);
	}

	//a section new to the document: at the end after a blank line, the head at the head
	private void AddSection(Plan plan, string section, List<(string Text, string? Break)> written) {
		if (section.Length == 0) {
			Add(plan, new Occurrence(null, 0, 0), written);
			return;
		}
		Headable(section);
		List<(string Text, string? Break)> added = [];
		if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1].Text)) {
			added.Add(("", null));
		}
		added.Add(($"[{section}]", null));
		added.AddRange(written);
		plan.Insert(lines.Count, added);
	}

	private List<(string Text, string? Break)> FieldLines(string key, string value) {
		IniValueForm form = Dialect.TryFirstChoice(value) ?? throw new IniEditException(IniEditFailure.Unwritable, key, Path);
		List<(string Text, string? Break)> written = Dialect.Write(form, value);
		written[0] = (key + Dialect.Separator + written[0].Text, written[0].Break);
		return written;
	}

	//a field's value written again in its own form while that holds it, between what stood
	//either side of it, each line keeping the break its line had
	private List<(string Text, string? Break)> Rewrite(IniField field, string value) {
		IniValueForm form = Dialect.CanHold(field.Form, value)
			? field.Form
			: Dialect.TryFirstChoice(value) ?? throw new IniEditException(IniEditFailure.Unwritable, field.Key, Path);
		List<(string Text, string? Break)> written = Dialect.Write(form, value);
		for (int at = 0; at < written.Count - 1 && at < field.Count - 1; at++) {
			written[at] = (written[at].Text, written[at].Break ?? lines[field.Line + at].Break);
		}
		written[0] = (field.Prefix + written[0].Text, written[0].Break);
		written[^1] = (written[^1].Text + field.Suffix, written[^1].Break ?? lines[field.Line + field.Count - 1].Break);
		return written;
	}

	//a header naming the section anew, the spaces in and around its brackets kept
	private static string Rehead(IniHeader header, string name) {
		string written = header.Written;
		int lead = written.Length - written.TrimStart().Length;
		int trail = lead == written.Length ? 0 : written.Length - written.TrimEnd().Length;
		return $"{header.Indent}[{written[..lead]}{name}{written[(written.Length - trail)..]}]{header.Suffix}";
	}

	//the edit made to a copy, parsed again and checked: refused, the document stands as it was
	private void Land(Plan plan, IniContent expected) {
		List<IniLine> edited = Apply(plan);
		List<IniElement> parsed = Dialect.Parse(edited);
		var actual = IniContent.Of(parsed, Dialect.Names);
		List<IniDifference> differences = Dialect.Check(expected, actual);
		if (differences.Count > 0) {
			throw new IniCheckException(differences, Path);
		}
		lines = edited;
		elements = parsed;
		content = actual;
		IsEdited = true;
	}

	//the lines with the plan's splices made, a line added taking the file's first break, else the
	//dialect's, else the system's; every line but the last keeps or gains one, and the last goes
	//without where the file's did
	private List<IniLine> Apply(Plan plan) {
		string lineBreak = lines.Find(line => line.Break.Length > 0).Break is { Length: > 0 } first
			? first
			: Dialect.Rules.LineBreak ?? Environment.NewLine;
		var edited = new List<IniLine>(lines.Count + 8);
		int at = 0;
		foreach (Splice splice in plan.Splices.OrderBy(splice => splice.At).ThenBy(splice => splice.Count > 0)) {
			while (at < splice.At) {
				edited.Add(lines[at++]);
			}
			edited.AddRange(splice.Lines.Select(line => new IniLine(line.Text, line.Break ?? lineBreak)));
			at = Math.Max(at, splice.At + splice.Count);
		}
		while (at < lines.Count) {
			edited.Add(lines[at++]);
		}
		for (int line = 0; line < edited.Count - 1; line++) {
			if (edited[line].Break.Length == 0) {
				edited[line] = edited[line] with { Break = lineBreak };
			}
		}
		if (lines.Count > 0 && lines[^1].Break.Length == 0 && edited.Count > 0) {
			edited[^1] = edited[^1] with { Break = "" };
		}
		return edited;
	}

	//a section's occurrence: its header, none for the head, and the elements under it
	private sealed record Occurrence(IniHeader? Header, int First, int End);

	private sealed record Splice(int At, int Count, List<(string Text, string? Break)> Lines);

	//the lines an edit replaces, by where they stand before it
	private sealed class Plan {
		public List<Splice> Splices { get; } = [];

		public void Remove(IniElement element) => Splices.Add(new Splice(element.Line, element.Count, []));

		public void Replace(IniElement element, List<(string Text, string? Break)> written) => Splices.Add(new Splice(element.Line, element.Count, written));

		public void Insert(int at, List<(string Text, string? Break)> written) => Splices.Add(new Splice(at, 0, written));
	}
}
