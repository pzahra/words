using System.Text;

namespace PatTech.Text.Ini;

/// <summary>
///     A file loaded whole (SPEC.md, <i>A transaction</i>): its lines, each with its own break,
///     in its own encoding and byte-order mark, cut by its dialect into elements. An edit
///     replaces only the lines that hold what it names, and is checked before it stands: the
///     document parsed again must read as it did, the edit applied, or the edit is refused
///     and the document stays as it was. Every other line stays byte for byte, but the one a
///     line added after an unbroken last line gives a break, and, in the dotted dialect, the
///     headers the cut of a group the edit touched changes (SPEC.md, <i>Cutting</i>).
/// </summary>
public sealed class IniDocument {
	private readonly byte[] read;
	private List<IniLine> lines = [];
	private List<IniElement> elements = [];
	private IniContent content;
	//what a batch's edits so far mean, while one runs
	private IniContent? batched;
	private byte[] mark = [];
	private IniEncodingFailure? failure;

	private IniDocument(IniDialect dialect, string? path, byte[] read, bool isNew) {
		Dialect = dialect;
		Path = path;
		this.read = read;
		IsNew = isNew;
		content = new IniContent(dialect.Names, dialect.KeyNames);
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
	/// <summary>Why it is no text; null while it is text.</summary>
	public IniEncodingFailure? Failure => failure;
	/// <summary>Whether an edit or a replacement has changed it since it was read.</summary>
	public bool IsEdited { get; private set; }

	/// <summary>Its text, every line with its break.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public string Text {
		get {
			Readable();
			return Join(lines);
		}
	}

	/// <summary>How many lines it has, the last counted with or without its break.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public int LineCount {
		get {
			Readable();
			return lines.Count;
		}
	}

	/// <summary>Its elements, in order; lines it does not recognise are none.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public IReadOnlyList<IniElement> Elements {
		get {
			Readable();
			return Recognised(elements);
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
	///     go; a new key goes where its dialect places it, after the section's last field, and a
	///     new section where its dialect places it, at the end after a blank line, the section
	///     <c>""</c> at the head.
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
		IniContent expected = Expecting();
		expected.Set(section, key, value);
		List<string> touched = [];
		if (declared.Count > 0) {
			plan.Replace(declared[0], Rewrite(declared[0], key, value));
			if (DropFields(plan, occurrences, declared.Skip(1)).Dropped) {
				touched.Add(Parent(section));
			}
		}
		else if (content.Has(section)) {
			Insert(plan, Dialect.PlaceKey(this, section, key), FieldLines(key, value));
		}
		else {
			AddSection(plan, section, [new(key, value)], FieldLines(key, value));
			touched.Add(Parent(section));
		}
		Land(plan, expected, touched);
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
		IniContent expected = Expecting();
		(bool stands, bool dropped) = DropFields(plan, occurrences, declared);
		if (stands) {
			expected.Remove(section, key);
		}
		else {
			expected.RemoveSection(section);
		}
		Land(plan, expected, dropped ? [Parent(section)] : []);
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
		var keys = new HashSet<string>(Dialect.KeyNames);
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
		List<Written> written = [.. given.SelectMany(pair => FieldLines(pair.Key, pair.Value))];
		var plan = new Plan();
		IniContent expected = Expecting();
		expected.Replace(section, given);
		if (!content.Has(section)) {
			AddSection(plan, section, given, written);
			Land(plan, expected, [Parent(section)]);
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
			(int at, bool parted) = Into(occurrences[0]);
			Insert(plan, new IniPlacement(at, BlankAfter: parted), written);
		}
		List<string> touched = [];
		foreach (Occurrence occurrence in occurrences.Skip(1).Where(Stands)) {
			Drop(plan, occurrence);
			touched = [Parent(section)];
		}
		Land(plan, expected, touched);
	}

	/// <summary>
	///     Removes <paramref name="section"/>, every occurrence of it: its header, its fields and
	///     the blank lines that part nothing that stays. Comments and lines it does not recognise
	///     stand where they stand, and so, in the dotted dialect, does a header that bases a chain.
	/// </summary>
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
		IniContent expected = Expecting();
		expected.RemoveSection(section);
		Land(plan, expected, [Parent(section)]);
	}

	/// <summary>
	///     Renames <paramref name="section"/> to <paramref name="name"/> in every occurrence's
	///     header, its keys going with it; the head, renamed, gains a header above its first field.
	///     In the dotted dialect a section chained off it keeps its own name, the chain cut anew.
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
				plan.Replace(header, [new Written(Rehead(header, name), lines[header.Line].Break, name, Renamed: true)]);
			}
			else {
				plan.Insert(Fields(occurrence)[0].Line, [new Written($"[{name}]", Name: name, Renamed: true)]);
			}
		}
		IniContent expected = Expecting();
		expected.Rename(section, name);
		Land(plan, expected, [Parent(section), Parent(name), section, name]);
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
		var gathered = new List<Written> {
			new(""),
			new(moving[0].Header is { } header ? lines[header.Line].Text : "[]", Name: section.Length == 0 ? null : section, Renamed: true),
		};
		var plan = new Plan();
		foreach (Occurrence occurrence in moving) {
			foreach (IniField field in Fields(occurrence)) {
				gathered.AddRange(lines.Skip(field.Line).Take(field.Count).Select(line => new Written(line.Text, line.Break)));
			}
			Drop(plan, occurrence);
		}
		Occurrence target = Occurrences(after).Where(Stands).Last();
		(int at, _) = Into(target);
		if (at < lines.Count && !string.IsNullOrWhiteSpace(lines[at].Text)) {
			gathered.Add(new(""));
		}
		plan.Insert(at, gathered);
		Land(plan, Expecting(), [Parent(section), section]);
	}

	/// <summary>Replaces a comment block's lines with <paramref name="comment"/>'s; none removes it.</summary>
	/// <exception cref="IniEditException">The block no longer stands where it stood, or a line the dialect reads as no comment.</exception>
	/// <exception cref="IniCheckException">The edit would change more than itself.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void SetComment(IniComment block, IEnumerable<string> comment) {
		ArgumentNullException.ThrowIfNull(block);
		ArgumentNullException.ThrowIfNull(comment);
		Editable();
		List<Written> written = Commentable(comment);
		IniElement located = Locate(block);
		var plan = new Plan();
		plan.Replace(located, written);
		Land(plan, Expecting(), []);
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
		List<Written> written = Commentable(comment);
		int at = before is null ? lines.Count : Locate(before).Line;
		if (written.Count == 0) {
			return;
		}
		var plan = new Plan();
		plan.Insert(at, written);
		Land(plan, Expecting(), []);
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
		plan.Insert(at, [.. lines.Skip(located.Line).Take(located.Count).Select(line => new Written(line.Text, line.Break))]);
		Land(plan, Expecting(), []);
	}

	/// <summary>
	///     Makes <paramref name="edits"/> as one edit (SPEC.md, <i>Checked before it lands</i>): each
	///     placed and cut as it would be alone, and the document checked once, after the last,
	///     against what it was before the first plus what they all mean. Refused, by the check or
	///     by an edit, none of them stands. A batch made inside one is part of it.
	/// </summary>
	/// <exception cref="IniCheckException">The edits would change more than themselves.</exception>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public void Batch(Action edits) {
		ArgumentNullException.ThrowIfNull(edits);
		Editable();
		if (batched is not null) {
			edits();
			return;
		}
		(List<IniLine> wasLines, List<IniElement> wasElements, IniContent wasContent, bool wasEdited) = (lines, elements, content, IsEdited);
		batched = content.Clone();
		try {
			edits();
			if (lines != wasLines) {
				List<IniDifference> differences = Dialect.Check(batched, content, wasContent);
				if (differences.Count == 0) {
					differences.AddRange(Dialect.CheckMore(new IniChange(
						new IniSnapshot(Join(wasLines), Recognised(wasElements), wasContent),
						new IniSnapshot(Join(lines), Recognised(elements), content),
						batched)));
				}
				if (differences.Count > 0) {
					throw new IniCheckException(differences, Path);
				}
			}
		}
		catch {
			(lines, elements, content, IsEdited) = (wasLines, wasElements, wasContent, wasEdited);
			throw;
		}
		finally {
			batched = null;
		}
	}

	/// <summary>
	///     Replaces the document whole with <paramref name="text"/>, written in
	///     <paramref name="encoding"/>, for a file written in a form of the caller's own: no
	///     check, and a document that was no text is text again.
	/// </summary>
	/// <exception cref="InvalidOperationException">A batch is running: it edits the document, which is replaced whole outside one.</exception>
	public void Replace(string text, Encoding encoding, bool byteOrderMark = false) {
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(encoding);
		Unfrozen();
		if (batched is not null) {
			throw new InvalidOperationException("A batch is running: the document is replaced whole outside one.");
		}
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

	//where a key new to section goes, as the plain dialect places it: after its first occurrence's last
	//field, else under its header; the head with neither starts the file, a blank line after it
	internal IniPlacement KeyPlace(string section) {
		List<Occurrence> occurrences = Occurrences(section);
		(int at, bool parted) = Into(occurrences.Count > 0 ? occurrences[0] : new Occurrence(null, 0, 0));
		return new IniPlacement(at, BlankAfter: parted);
	}

	//where a section new to the document goes, as the plain dialect places it: at the end after a
	//blank line, the head at the head
	internal IniPlacement SectionPlace(string section) {
		if (section.Length == 0) {
			(int at, bool parted) = Into(new Occurrence(null, 0, 0));
			return new IniPlacement(at, BlankAfter: parted);
		}
		return new IniPlacement(lines.Count, BlankBefore: lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1].Text));
	}

	/// <summary>Whether the line at <paramref name="line"/>, counted from 0, is blank.</summary>
	/// <exception cref="IniEncodingException">It is no text.</exception>
	public bool IsBlank(int line) {
		Readable();
		return string.IsNullOrWhiteSpace(lines[line].Text);
	}

	private void Load(string text) {
		lines = Split(text);
		elements = Dialect.Parse(lines);
		content = IniContent.Of(elements, Dialect.Names, Dialect.KeyNames);
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

	private static string Join(IEnumerable<IniLine> lines) {
		var text = new StringBuilder();
		foreach (IniLine line in lines) {
			text.Append(line.Text).Append(line.Break);
		}
		return text.ToString();
	}

	private static List<IniElement> Recognised(IEnumerable<IniElement> elements) => [.. elements.Where(element => element is not IniOther)];

	//a prepared transaction has made its bytes: no edit until it ends
	internal bool Frozen { get; set; }

	//what the document is expected to read as once an edit lands: as it reads, or as a batch's
	//edits so far mean it to
	private IniContent Expecting() => (batched ?? content).Clone();

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

	private List<Written> Commentable(IEnumerable<string> comment) {
		List<Written> written = [];
		foreach (string line in comment) {
			ArgumentNullException.ThrowIfNull(line, nameof(comment));
			if (line.Contains('\r') || line.Contains('\n') || !Dialect.IsComment(line)) {
				throw new IniEditException(IniEditFailure.NotAComment, line, Path);
			}
			written.Add(new(line));
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

	//the group a section is in, in the dotted dialect: its name to its last dot, "" for none
	private static string Parent(string section) => section.LastIndexOf('.') is int dot and > 0 ? section[..dot] : "";

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
		=> [.. occurrences.SelectMany(Fields).Where(field => Dialect.KeyNames.Equals(field.Key, key))];

	//an occurrence that makes its section one: a header, or the head with a field
	private bool Stands(Occurrence occurrence) => occurrence.Header is not null || Fields(occurrence).Count > 0;

	//fields going from a section's occurrences, an occurrence they leave with none going whole:
	//whether the section still stands, and whether an occurrence went
	private (bool Stands, bool Dropped) DropFields(Plan plan, List<Occurrence> occurrences, IEnumerable<IniField> going) {
		var gone = going.ToHashSet();
		bool stands = false, dropped = false;
		foreach (Occurrence occurrence in occurrences) {
			List<IniField> fields = Fields(occurrence);
			if (fields.Count > 0 && fields.All(gone.Contains)) {
				//a header that bases a chain stays, bare, and with it the section
				stands |= Drop(plan, occurrence);
				dropped = true;
				continue;
			}
			foreach (IniField field in fields.Where(gone.Contains)) {
				plan.Remove(field);
			}
			stands |= occurrence.Header is not null || fields.Count > 0;
		}
		return (stands, dropped);
	}

	//an occurrence going: its header, its fields, and the blank runs that part nothing left
	//standing; comment blocks and lines no element stand, and so, in the dotted dialect, does a
	//header that bases a chain. One ending the file takes the blank run above its header too.
	//Whether its header stayed
	private bool Drop(Plan plan, Occurrence occurrence) {
		bool keeps = occurrence.Header is { } kept && Bases(kept);
		if (occurrence.Header is { } header && !keeps) {
			plan.Remove(header);
		}
		for (int at = occurrence.First; at < occurrence.End; at++) {
			if (elements[at] is IniField) {
				plan.Remove(elements[at]);
			}
		}
		for (int at = occurrence.First; at < occurrence.End; at++) {
			if (elements[at] is IniBlank && !Parts(plan, at)) {
				plan.Remove(elements[at]);
			}
		}
		//a blank run left ending the file parts nothing either
		if (occurrence.Header is { } dropped && !keeps) {
			int above = occurrence.First - 2;
			if (above >= 0 && elements[above] is IniBlank && !Parts(plan, above)) {
				plan.Remove(elements[above]);
			}
		}
		return keeps;
	}

	//whether the blank run at elements[at] parts two things left standing, neither of them blank
	private bool Parts(Plan plan, int at) {
		IniElement? before = Standing(plan, at, -1), after = Standing(plan, at, +1);
		return before is not null and not IniBlank && after is not null and not IniBlank;
	}

	//the nearest element the plan leaves standing from elements[at], going step's way
	private IniElement? Standing(Plan plan, int at, int step) {
		for (int next = at + step; next >= 0 && next < elements.Count; next += step) {
			if (!plan.Removes(elements[next])) {
				return elements[next];
			}
		}
		return null;
	}

	//whether a header bases a chain in the dotted dialect: a full header the next header chains off
	private bool Bases(IniHeader header) {
		if (Dialect is not IniDottedDialect || header.IsChained) {
			return false;
		}
		for (int at = elements.IndexOf(header) + 1; at < elements.Count; at++) {
			switch (elements[at]) {
				case IniHeader next:
					return next.IsChained;
				case IniOther { IsEmptyHeader: true }:
					return false;
			}
		}
		return false;
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

	private static void Insert(Plan plan, IniPlacement place, List<Written> written)
		=> plan.Insert(place.Line, [.. place.BlankBefore ? [new Written("")] : (Written[])[], .. written, .. place.BlankAfter ? [new Written("")] : (Written[])[]]);

	//a section new to the document, where its dialect places it, under a header of its own but the head
	private void AddSection(Plan plan, string section, IReadOnlyList<KeyValuePair<string, string>> pairs, List<Written> written) {
		if (section.Length > 0) {
			Headable(section);
		}
		IniPlacement place = Dialect.PlaceSection(this, section, pairs);
		List<Written> added = section.Length == 0 ? written : [new Written($"[{section}]", Name: section), .. written];
		Insert(plan, place, added);
	}

	private List<Written> FieldLines(string key, string value) {
		if (Dialect.WriteField(key, value) is { } own) {
			return [.. own.Select(line => new Written(line))];
		}
		IniValueForm form = Dialect.TryFirstChoice(value) ?? throw new IniEditException(IniEditFailure.Unwritable, key, Path);
		List<(string Text, string? Break)> written = Dialect.Write(form, value);
		written[0] = (key + Dialect.Separator + written[0].Text, written[0].Break);
		return [.. written.Select(line => new Written(line.Text, line.Break))];
	}

	//a field's value written again in its own form while that holds it, between what stood
	//either side of it, each line keeping the break its line had; a dialect that writes its
	//fields its own way writes it whole again, its lines taking the breaks the old ones had
	private List<Written> Rewrite(IniField field, string key, string value) {
		if (Dialect.WriteField(key, value) is { } own) {
			List<Written> whole = [.. own.Select(line => new Written(line))];
			for (int at = 0; at < whole.Count - 1 && at < field.Count - 1; at++) {
				whole[at] = whole[at] with { Break = lines[field.Line + at].Break };
			}
			whole[^1] = whole[^1] with { Break = lines[field.Line + field.Count - 1].Break };
			return whole;
		}
		IniValueForm form = Dialect.CanHold(field.Form, value)
			? field.Form
			: Dialect.TryFirstChoice(value) ?? throw new IniEditException(IniEditFailure.Unwritable, field.Key, Path);
		List<(string Text, string? Break)> written = Dialect.Write(form, value);
		for (int at = 0; at < written.Count - 1 && at < field.Count - 1; at++) {
			written[at] = (written[at].Text, written[at].Break ?? lines[field.Line + at].Break);
		}
		written[0] = (field.Prefix + written[0].Text, written[0].Break);
		written[^1] = (written[^1].Text + field.Suffix, written[^1].Break ?? lines[field.Line + field.Count - 1].Break);
		return [.. written.Select(line => new Written(line.Text, line.Break))];
	}

	//a header naming the section anew, the spaces in and around its brackets kept
	private static string Rehead(IniHeader header, string name) {
		string written = header.Written;
		int lead = written.Length - written.TrimStart().Length;
		int trail = lead == written.Length ? 0 : written.Length - written.TrimEnd().Length;
		return $"{header.Indent}[{written[..lead]}{name}{written[(written.Length - trail)..]}]{header.Suffix}";
	}

	//the edit made to a copy, cut anew in the dotted dialect, parsed again and checked, in a batch
	//by the batch's check: refused, the document stands as it was
	private void Land(Plan plan, IniContent expected, IReadOnlyCollection<string> touched) {
		(List<IniLine> wasLines, List<IniElement> wasElements) = (lines, elements);
		List<IniLine> edited;
		try {
			List<Placed> placed = Apply(plan, null);
			if (Dialect is IniDottedDialect dotted) {
				placed = Recut(dotted, placed, touched);
			}
			edited = [.. placed.Select(line => line.Line)];
		}
		finally {
			(lines, elements) = (wasLines, wasElements);
		}
		List<IniElement> parsed = Dialect.Parse(edited);
		var actual = IniContent.Of(parsed, Dialect.Names, Dialect.KeyNames);
		if (batched is not null) {
			//checked once the batch's last edit lands
			batched = expected;
		}
		else {
			List<IniDifference> differences = Dialect.Check(expected, actual, content);
			if (differences.Count == 0) {
				var change = new IniChange(
					new IniSnapshot(Join(lines), Recognised(elements), content),
					new IniSnapshot(Join(edited), Recognised(parsed), actual),
					expected);
				differences.AddRange(Dialect.CheckMore(change));
			}
			if (differences.Count > 0) {
				throw new IniCheckException(differences, Path);
			}
		}
		lines = edited;
		elements = parsed;
		content = actual;
		IsEdited = true;
	}

	//the lines with the plan's splices made, a line added taking the file's first break, else the
	//dialect's, else the system's; every line but the last keeps or gains one, and the last goes
	//without where the file's did. Each line keeps the section it heads, if it heads one, and
	//whether the cut may write it afresh: a line taken from current where one is given
	private List<Placed> Apply(Plan plan, List<Placed>? current) {
		string lineBreak = FirstBreak(lines);
		string?[]? heads = current is null ? Heads() : null;
		var placed = new List<Placed>(lines.Count + 8);
		int at = 0;
		foreach (Splice splice in plan.Splices.OrderBy(splice => splice.At).ThenBy(splice => splice.Count > 0)) {
			while (at < splice.At) {
				placed.Add(Kept(at++, current, heads));
			}
			placed.AddRange(splice.Lines.Select(line => new Placed(
				new IniLine(line.Text, line.Break ?? lineBreak), line.Name, Free: line.Name is not null, New: line.Name is not null && !line.Renamed)));
			at = Math.Max(at, splice.At + splice.Count);
		}
		while (at < lines.Count) {
			placed.Add(Kept(at++, current, heads));
		}
		for (int line = 0; line < placed.Count - 1; line++) {
			if (placed[line].Line.Break.Length == 0) {
				placed[line] = placed[line] with { Line = placed[line].Line with { Break = lineBreak } };
			}
		}
		if (lines.Count > 0 && lines[^1].Break.Length == 0 && placed.Count > 0) {
			placed[^1] = placed[^1] with { Line = placed[^1].Line with { Break = "" } };
		}
		return placed;
	}

	//a line the plan keeps: what current says of it, else the section it heads as it reads
	private Placed Kept(int at, List<Placed>? current, string?[]? heads) {
		if (current is not null) {
			return current[at] with { Line = lines[at] };
		}
		return new Placed(lines[at], heads![at], Free: false, New: false);
	}

	//the section each line heads, "" for a [] that ends a chain, by line
	private string?[] Heads() {
		var heads = new string?[lines.Count];
		foreach (IniElement element in elements) {
			heads[element.Line] = element switch {
				IniHeader header => header.Name,
				IniOther { IsEmptyHeader: true } => "",
				_ => null,
			};
		}
		return heads;
	}

	private string FirstBreak(IEnumerable<IniLine> from)
		=> from.FirstOrDefault(line => line.Break.Length > 0).Break is { Length: > 0 } first ? first : Dialect.Rules.LineBreak ?? Environment.NewLine;

	//the dotted dialect's cut (SPEC.md, Cutting): every header written so it names the section it
	//means; a base the strategy wants added for a group the edit touched, its sections chained off
	//it; and a bare base of such a group the edit left basing nothing gone. A header the edit wrote,
	//broke or the strategy chains is written chained where the base above it allows, every other
	//one as it stands
	private List<Placed> Recut(IniDottedDialect dotted, List<Placed> placed, IReadOnlyCollection<string> touched) {
		var groups = new HashSet<string>(touched.Where(group => group.Length > 0), Dialect.Names);
		//an edit that writes no header and touches no group leaves every header naming what it
		//named: a header it drops bases no chain, or it stays
		if (groups.Count == 0 && !placed.Any(line => line.Free)) {
			return placed;
		}
		for (int pass = 0; pass < 8; pass++) {
			List<IniElement> parsed = Dialect.Parse([.. placed.Select(line => line.Line)]);
			bool changed = Rechain(placed, parsed, out Dictionary<int, string> bases);
			if (changed) {
				continue;
			}
			string lineBreak = FirstBreak(placed.Select(line => line.Line));
			foreach (string group in groups) {
				if (TryBase(dotted, placed, parsed, bases, group, lineBreak, out string? displaced)) {
					//the base its run chained off may now base nothing
					if (displaced is { Length: > 0 }) {
						groups.Add(displaced);
					}
					changed = true;
					break;
				}
			}
			if (changed) {
				continue;
			}
			//bare bases the edit left basing nothing: dropped as any occurrence going
			(lines, elements) = ([.. placed.Select(line => line.Line)], parsed);
			var plan = new Plan();
			foreach (Occurrence occurrence in BareBases(parsed, groups)) {
				Drop(plan, occurrence);
			}
			if (plan.Splices.Count == 0) {
				break;
			}
			placed = Apply(plan, placed);
		}
		return placed;

		//the occurrences of bare bases, of touched groups, the edit did not write, that base nothing
		IEnumerable<Occurrence> BareBases(List<IniElement> parsed, HashSet<string> named) {
			for (int at = 0; at < parsed.Count; at++) {
				if (parsed[at] is not IniHeader header || header.IsChained || !named.Contains(header.Name) || placed[header.Line].New) {
					continue;
				}
				int end = at + 1;
				while (end < parsed.Count && parsed[end] is not IniHeader && parsed[end] is not IniOther { IsEmptyHeader: true }) {
					end++;
				}
				bool bare = !parsed.Skip(at + 1).Take(end - at - 1).OfType<IniField>().Any();
				bool basing = end < parsed.Count && parsed[end] is IniHeader { IsChained: true };
				if (bare && !basing) {
					yield return new Occurrence(header, at + 1, end);
				}
			}
		}
	}

	//every header written so it names the section it means: one free to write, or no longer
	//naming it as written, chained where the base above it allows and full where not; bases
	//says the base above each header's line. Whether a line changed
	private bool Rechain(List<Placed> placed, List<IniElement> parsed, out Dictionary<int, string> bases) {
		bases = [];
		bool changed = false;
		string chainBase = "";
		foreach (IniElement element in parsed) {
			if (element is IniOther { IsEmptyHeader: true }) {
				chainBase = "";
				continue;
			}
			if (element is not IniHeader header) {
				continue;
			}
			Placed line = placed[header.Line];
			string name = line.Name ?? header.Name;
			bases[header.Line] = chainBase;
			if (name.Length == 0) {
				chainBase = "";
				continue;
			}
			string written = header.Written.Trim();
			string probe = chainBase;
			(string resolved, _) = Dialect.Resolve(written, ref probe);
			if (!line.Free && Dialect.Names.Equals(resolved, name)) {
				chainBase = probe;
				continue;
			}
			string rewritten = !line.Base && ChainsOff(chainBase, name) ? name[chainBase.Length..] : name;
			if (rewritten != written) {
				placed[header.Line] = line with { Line = line.Line with { Text = Rehead(header, rewritten) } };
				changed = true;
			}
			if (!rewritten.StartsWith('.')) {
				chainBase = name;
			}
		}
		return changed;
	}

	//a base for group where the strategy cuts it and nothing heads it: a bare header above the
	//run of its sections, which must stand together, even where a base above them, an ancestor's,
	//takes them now, as the writer cuts the deeper group; the run is then free to chain off it,
	//and displaced is that ancestor. Whether one was added
	private bool TryBase(IniDottedDialect dotted, List<Placed> placed, List<IniElement> parsed, Dictionary<int, string> bases, string group, string lineBreak, out string? displaced) {
		displaced = null;
		List<IniElement> heads = [.. parsed.Where(element => element is IniHeader or IniOther { IsEmptyHeader: true })];
		string NameOf(IniElement element) => element is IniHeader header ? placed[header.Line].Name ?? header.Name : "";
		var headed = new HashSet<string>(heads.Select(NameOf).Where(name => name.Length > 0), Dialect.Names);
		//the strategy counts the sections holding keys: a bare base is structure, which it decides
		var keyed = new HashSet<string>(parsed.OfType<IniField>().Select(field => field.Section).Where(name => name.Length > 0), Dialect.Names);
		if (headed.Contains(group) || !dotted.Cuts.Cuts(group, keyed, Dialect.Names)) {
			return false;
		}
		List<int> run = [.. Enumerable.Range(0, heads.Count).Where(at => ChainsOff(group, NameOf(heads[at])))];
		if (run.Count == 0 || run[^1] - run[0] + 1 != run.Count) {
			return false;
		}
		IniElement first = heads[run[0]];
		string above = bases.GetValueOrDefault(first.Line, "");
		if (ChainsOff(above, group)) {
			displaced = above;
		}
		foreach (int at in run) {
			placed[heads[at].Line] = placed[heads[at].Line] with { Free = true };
		}
		placed.Insert(first.Line, new Placed(new IniLine($"[{group}]", lineBreak), group, Free: true, New: true, Base: true));
		return true;
	}

	//whether name chains off chainBase: under it, a dot after it
	private bool ChainsOff(string chainBase, string name)
		=> chainBase.Length > 0 && name.Length > chainBase.Length + 1 && name[chainBase.Length] == '.' && Dialect.Names.Equals(name[..chainBase.Length], chainBase);

	//a section's occurrence: its header, none for the head, and the elements under it
	private sealed record Occurrence(IniHeader? Header, int First, int End);

	//a line an edit writes: its text, its break where it keeps one of its own, and for a header the
	//section it names, which the cut may write afresh; a header renamed or moved is no new one
	private sealed record Written(string Text, string? Break = null, string? Name = null, bool Renamed = false);

	//a line as an edit leaves it: the section it heads, whether the cut may write it afresh,
	//whether the edit wrote it new, and whether it is a base the cut added, written full, as a
	//chained header bases nothing
	private sealed record Placed(IniLine Line, string? Name, bool Free, bool New, bool Base = false);

	private sealed record Splice(int At, int Count, List<Written> Lines);

	//the lines an edit replaces, by where they stand before it, and the elements it takes away
	private sealed class Plan {
		private readonly HashSet<IniElement> removed = [];

		public List<Splice> Splices { get; } = [];

		public bool Removes(IniElement element) => removed.Contains(element);

		public void Remove(IniElement element) {
			if (removed.Add(element)) {
				Splices.Add(new Splice(element.Line, element.Count, []));
			}
		}

		//what replaces an element stands where it stood
		public void Replace(IniElement element, List<Written> written) => Splices.Add(new Splice(element.Line, element.Count, written));

		public void Insert(int at, List<Written> written) => Splices.Add(new Splice(at, 0, written));
	}
}
