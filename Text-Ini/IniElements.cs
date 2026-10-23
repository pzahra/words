namespace PatTech.Text.Ini;

/// <summary>A line of the file: its text, and its break as read (<c>\r\n</c>, <c>\n</c>, <c>\r</c>, or none on a last line).</summary>
internal readonly record struct IniLine(string Text, string Break);

/// <summary>
///     A run of the document's lines its dialect recognises as one thing (SPEC.md,
///     <i>Elements</i>): a run of blank lines, a block of comments, a header, or a field.
///     It is a snapshot: an edit cuts the document again, and an element read before it
///     is used only while it still stands where it stood.
/// </summary>
public abstract class IniElement {
	private protected IniElement(int line, int count, string section, string snapshot) {
		Line = line;
		Count = count;
		Section = section;
		Snapshot = snapshot;
	}

	/// <summary>The index of its first line.</summary>
	public int Line { get; }
	/// <summary>How many lines it takes.</summary>
	public int Count { get; }
	/// <summary>The section it stands in: <c>""</c> above the first header.</summary>
	public string Section { get; }
	//its lines as read, to know it again
	internal string Snapshot { get; }
}

/// <summary>A run of blank lines.</summary>
public sealed class IniBlank : IniElement {
	internal IniBlank(int line, int count, string section, string snapshot) : base(line, count, section, snapshot) {
	}
}

/// <summary>A block of comment lines, parted from the next by a blank line or anything else.</summary>
public sealed class IniComment : IniElement {
	internal IniComment(int line, string section, IReadOnlyList<string> lines) : base(line, lines.Count, section, string.Join("\n", lines)) {
		Lines = lines;
	}

	/// <summary>Its lines as written, markers and all.</summary>
	public IReadOnlyList<string> Lines { get; }
}

/// <summary>A section's header.</summary>
public sealed class IniHeader : IniElement {
	internal IniHeader(int line, string name, string written, string indent, string suffix, string? comment, string snapshot)
		: base(line, 1, name, snapshot) {
		Written = written;
		Indent = indent;
		Suffix = suffix;
		Comment = comment;
	}

	/// <summary>The section it names.</summary>
	public string Name => Section;
	/// <summary>The comment after it on its line, from its marker; null when none.</summary>
	public string? Comment { get; }
	//the name between the brackets as written, and what stands either side of them
	internal string Written { get; }
	internal string Indent { get; }
	internal string Suffix { get; }
}

/// <summary>A field: its key, and its value read through its continuations and quotes.</summary>
public sealed class IniField : IniElement {
	internal IniField(int line, int count, string section, string key, string value, IniValueForm form, string prefix, string suffix, string? comment, string snapshot)
		: base(line, count, section, snapshot) {
		Key = key;
		Value = value;
		Form = form;
		Prefix = prefix;
		Suffix = suffix;
		Comment = comment;
	}

	/// <summary>Its key.</summary>
	public string Key { get; }
	/// <summary>The text its value holds.</summary>
	public string Value { get; }
	/// <summary>The form its value is written in.</summary>
	public IniValueForm Form { get; }
	/// <summary>The comment after it on its last line, from its marker; null when none.</summary>
	public string? Comment { get; }
	//its first line up to its value, and what follows its value on its last
	internal string Prefix { get; }
	internal string Suffix { get; }
}

/// <summary>Lines the dialect does not recognise: no element, kept where they stand.</summary>
internal sealed class IniOther : IniElement {
	internal IniOther(int line, int count, string section, string snapshot) : base(line, count, section, snapshot) {
	}
}
