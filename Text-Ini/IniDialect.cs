using System.Text;

namespace PatTech.Text.Ini;

/// <summary>
///     A file's grammar and its rules (SPEC.md, <i>Its dialect</i>, <i>Values and comments</i>):
///     it cuts a document's lines into elements, reads a value through its form, writes one
///     back in a form that holds it, and says what the check compares. <see cref="Sections"/>
///     is the plain one; a dialect of a user's own extends it.
/// </summary>
public class IniDialect {
	/// <summary>Creates a dialect over <paramref name="rules"/>.</summary>
	protected IniDialect(IniRules rules) {
		ArgumentNullException.ThrowIfNull(rules);
		Rules = rules;
	}

	/// <summary>The sections dialect: <c>[section]</c> headers and <c>key=value</c> lines, read by <paramref name="rules"/>.</summary>
	public static IniDialect Sections(IniRules rules) => new(rules);

	/// <summary>
	///     The dotted dialect: the sections dialect's lines, its section names in dotted segments,
	///     a <c>[.child]</c> header chaining off the last full one, its chains cut by
	///     <paramref name="cuts"/>, <see cref="GroupCuts"/> when none is given.
	/// </summary>
	public static IniDottedDialect Dotted(IniRules rules, ICutStrategy? cuts = null) => new(rules, cuts);

	/// <summary>Its rules.</summary>
	public IniRules Rules { get; }

	/// <summary>How names compare among its sections, and among a section's keys unless <see cref="KeyNames"/> says otherwise.</summary>
	public StringComparer Names => Rules.Names;

	/// <summary>How keys compare among a section's.</summary>
	public StringComparer KeyNames => Rules.KeyNames ?? Rules.Names;

	/// <summary>Whether <paramref name="line"/> is a comment line: a marker first, after any spaces.</summary>
	public virtual bool IsComment(string line) {
		ArgumentNullException.ThrowIfNull(line);
		string trimmed = line.TrimStart();
		return trimmed.Length > 0 && IsCommentMarker(trimmed[0]);
	}

	/// <summary>
	///     Whether <paramref name="key"/>, as a line writes it before its separator, is a key: any
	///     is, unless a dialect that names its keys more narrowly says not. A line whose key is
	///     none is no field.
	/// </summary>
	protected virtual bool IsKey(string key) => true;

	/// <summary>
	///     The section a header names, from the name it writes and the base the headers above
	///     it left, and whether it chains off that base; the base it leaves for those below.
	///     The sections dialect names each as written; the dotted one chains.
	/// </summary>
	internal virtual (string Name, bool Chained) Resolve(string written, ref string chainBase) => (written, false);

	/// <summary>
	///     Where a key new to <paramref name="section"/> goes: after its first occurrence's last
	///     field, else under its header; the head with neither starts the file, a blank line after it.
	/// </summary>
	protected internal virtual IniPlacement PlaceKey(IniDocument document, string section, string key) {
		ArgumentNullException.ThrowIfNull(document);
		return document.KeyPlace(section);
	}

	/// <summary>Where <paramref name="section"/>, new to the document, goes with its <paramref name="pairs"/>: at the end after a blank line, the head at the head.</summary>
	protected internal virtual IniPlacement PlaceSection(IniDocument document, string section, IReadOnlyList<KeyValuePair<string, string>> pairs) {
		ArgumentNullException.ThrowIfNull(document);
		return document.SectionPlace(section);
	}

	/// <summary>
	///     A field's lines as the dialect writes it, key and all, each without its break, for a
	///     new field and for one set anew; null to write it in the form it was written in while
	///     that holds the value, else in the first form that does (SPEC.md, <i>Values and comments</i>).
	/// </summary>
	protected internal virtual IReadOnlyList<string>? WriteField(string key, string value) => null;

	/// <summary>What the check finds beyond every section's pairs, for a dialect that compares more: nothing, here.</summary>
	protected internal virtual IEnumerable<IniDifference> CheckMore(IniChange change) => [];

	//every line of the document in exactly one element, in order
	internal List<IniElement> Parse(IReadOnlyList<IniLine> lines) {
		var elements = new List<IniElement>();
		string section = "";
		string chainBase = "";
		//under a [] the dialect does not take, nothing is an element until the next header
		bool readingPast = false;
		int at = 0;
		while (at < lines.Count) {
			string text = lines[at].Text;
			if (ReadBrackets(text) is { } brackets) {
				string written = brackets.Written.Trim();
				readingPast = written == "" && !Rules.EmptyHeader;
				if (written == "") {
					chainBase = "";
				}
				if (readingPast) {
					elements.Add(new IniOther(at, 1, section, text, isEmptyHeader: true));
				}
				else {
					(string name, bool chained) = written == "" ? ("", false) : Resolve(written, ref chainBase);
					section = name;
					elements.Add(new IniHeader(at, name, chained, brackets.Written, brackets.Indent, brackets.Suffix, brackets.Comment, text));
				}
				at++;
			}
			else if (readingPast) {
				//a field read past still runs on, whatever its next line holds
				int count = ReadField(lines, at, section)?.Count ?? 1;
				elements.Add(new IniOther(at, count, section, Snapshot(lines, at, count)));
				at += count;
			}
			else if (string.IsNullOrWhiteSpace(text)) {
				int start = at;
				while (at < lines.Count && string.IsNullOrWhiteSpace(lines[at].Text)) {
					at++;
				}
				elements.Add(new IniBlank(start, at - start, section, Snapshot(lines, start, at - start)));
			}
			else if (IsComment(text)) {
				int start = at;
				while (at < lines.Count && IsComment(lines[at].Text)) {
					at++;
				}
				elements.Add(new IniComment(start, section, [.. lines.Skip(start).Take(at - start).Select(line => line.Text)]));
			}
			else if (ReadField(lines, at, section) is { } field) {
				elements.Add(field);
				at += field.Count;
			}
			else {
				elements.Add(new IniOther(at, 1, section, text));
				at++;
			}
		}
		return elements;
	}

	//[name], any spaces either side where the rules let a header stand indented, and after it a
	//comment where the rules take one, or anything where they read past it
	private (string Written, string Indent, string Suffix, string? Comment)? ReadBrackets(string text) {
		int open = 0;
		while (open < text.Length && IsSpace(text[open])) {
			open++;
		}
		if (open == text.Length || text[open] != '[' || (open > 0 && !Rules.Indents)) {
			return null;
		}
		int close = text.IndexOf(']', open + 1);
		if (close < 0) {
			return null;
		}
		string? comment = null;
		if (!Rules.HeaderTails && !TryReadSuffix(text[(close + 1)..], out comment)) {
			return null;
		}
		return (text[(open + 1)..close], text[..open], text[(close + 1)..], comment);
	}

	//key, separator, value: the value plain, run on, or quoted
	private IniField? ReadField(IReadOnlyList<IniLine> lines, int at, string section) {
		string text = lines[at].Text;
		int separator = SeparatorIn(text);
		if (separator <= 0 || text[..separator].Trim() is not { Length: > 0 } key || text.TrimStart()[0] == '['
			|| (!Rules.Indents && IsSpace(text[0])) || !IsKey(key)) {
			return null;
		}
		int start = separator + 1;
		while (start < text.Length && IsSpace(text[start])) {
			start++;
		}
		string prefix = text[..start];
		if (start < text.Length && IsQuote(text[start]) && ReadQuoted(lines, at, start) is { } quoted) {
			return new IniField(at, quoted.Count, section, key, quoted.Value, quoted.Form, prefix, quoted.Suffix, quoted.Comment, Snapshot(lines, at, quoted.Count));
		}
		var value = new StringBuilder();
		var form = IniValueForm.Plain;
		int line = at;
		string part = text[start..];
		while (true) {
			(string body, string suffix, string? comment) = SplitComment(part);
			if (comment is null && Continues(body, out char marker)) {
				if (form == IniValueForm.Plain) {
					form = marker == '\\' ? IniValueForm.Backslash : IniValueForm.Underscore;
				}
				value.Append(Unescape(body[..^1]));
				if (marker == '\\') {
					value.Append('\n');
				}
				if (++line == lines.Count) {
					//it runs on to nothing
					return new IniField(at, line - at, section, key, value.ToString(), form, prefix, "", null, Snapshot(lines, at, line - at));
				}
				part = lines[line].Text;
				continue;
			}
			//spaces after the value are its line's, not the value's, unless the rules say they are
			string trimmed = Rules.TrailingSpaces ? body : body.TrimEnd(' ', '\t');
			value.Append(Unescape(trimmed));
			int count = line - at + 1;
			return new IniField(at, count, section, key, value.ToString(), form, prefix, body[trimmed.Length..] + suffix, comment, Snapshot(lines, at, count));
		}
	}

	//a quoted value runs on, its line breaks raw, until its closing quote; null when it never closes,
	//or something other than spaces and a comment follows it
	private (string Value, int Count, IniValueForm Form, string Suffix, string? Comment)? ReadQuoted(IReadOnlyList<IniLine> lines, int at, int start) {
		char quote = lines[at].Text[start];
		var value = new StringBuilder();
		int line = at;
		string text = lines[at].Text;
		int position = start + 1;
		while (true) {
			for (; position < text.Length; position++) {
				if (text[position] != quote) {
					value.Append(text[position]);
				}
				else if (position + 1 < text.Length && text[position + 1] == quote) {
					value.Append(quote);
					position++;
				}
				else {
					string rest = text[(position + 1)..];
					return TryReadSuffix(rest, out string? comment)
						? (value.ToString(), line - at + 1, quote == '"' ? IniValueForm.DoubleQuoted : IniValueForm.SingleQuoted, rest, comment)
						: null;
				}
			}
			if (line + 1 == lines.Count) {
				return null;
			}
			value.Append(lines[line].Break);
			text = lines[++line].Text;
			position = 0;
		}
	}

	//what may follow an entry on its line: spaces, then a comment after one where the rules take it
	private bool TryReadSuffix(string rest, out string? comment) {
		comment = null;
		int at = 0;
		while (at < rest.Length && IsSpace(rest[at])) {
			at++;
		}
		if (at == rest.Length) {
			return true;
		}
		if (Rules.CommentsAfterEntries && at > 0 && IsCommentMarker(rest[at])) {
			comment = rest[at..];
			return true;
		}
		return false;
	}

	//a value's line parted from a comment after it: the comment starts at the spaces before its marker
	private (string Body, string Suffix, string? Comment) SplitComment(string part) {
		if (Rules.CommentsAfterEntries) {
			for (int at = 1; at < part.Length; at++) {
				if (IsCommentMarker(part[at]) && IsSpace(part[at - 1])) {
					int spaces = at;
					while (spaces > 0 && IsSpace(part[spaces - 1])) {
						spaces--;
					}
					return (part[..spaces], part[spaces..], part[at..]);
				}
			}
		}
		return (part, "", null);
	}

	//whether a value's line runs on, and on which marker
	private bool Continues(string body, out char marker) {
		marker = '\0';
		if (body.Length == 0 || !IsMarker(body[^1])) {
			return false;
		}
		if (Rules.Escapes == IniEscapes.Doubled) {
			int run = 0;
			for (int at = body.Length - 1; at >= 0 && body[at] == body[^1]; at--) {
				run++;
			}
			marker = body[^1];
			return run % 2 == 1;
		}
		//each marker takes the character after it; the line runs on when the last one took nothing
		for (int at = 0; at < body.Length; at++) {
			if (IsMarker(body[at])) {
				if (at == body.Length - 1) {
					marker = body[at];
					return true;
				}
				at++;
			}
		}
		return false;
	}

	/// <summary>A value's line as it reads: each doubled continuation marker one. A dialect that escapes more extends it.</summary>
	protected virtual string Unescape(string text) {
		ArgumentNullException.ThrowIfNull(text);
		if (Rules.Continuations == IniContinuations.None) {
			return text;
		}
		var read = new StringBuilder(text.Length);
		for (int at = 0; at < text.Length; at++) {
			read.Append(text[at]);
			if (IsMarker(text[at]) && at + 1 < text.Length && text[at + 1] == text[at]) {
				at++;
			}
		}
		return read.ToString();
	}

	/// <summary>A value's line as it is written: each continuation marker doubled. A dialect that escapes more extends it.</summary>
	protected virtual string Escape(string text) {
		ArgumentNullException.ThrowIfNull(text);
		if (Rules.Continuations == IniContinuations.None) {
			return text;
		}
		var written = new StringBuilder(text.Length);
		foreach (char c in text) {
			written.Append(c);
			if (IsMarker(c)) {
				written.Append(c);
			}
		}
		return written.ToString();
	}

	/// <summary>Whether <paramref name="value"/>, written in <paramref name="form"/>, reads back as itself.</summary>
	internal bool CanHold(IniValueForm form, string value) {
		ArgumentNullException.ThrowIfNull(value);
		return form switch {
			IniValueForm.Plain => LineHolds(value, first: true, last: true),
			IniValueForm.Underscore => Rules.Continuations.HasFlag(IniContinuations.Underscore) && LineHolds(value, first: true, last: true),
			IniValueForm.Backslash => Rules.Continuations.HasFlag(IniContinuations.Backslash) && !value.Contains('\r')
				&& value.Split('\n') is var parts && parts.Select((part, at) => LineHolds(part, at == 0, at == parts.Length - 1)).All(holds => holds),
			IniValueForm.DoubleQuoted => Rules.Quotes.HasFlag(IniQuotes.Double),
			IniValueForm.SingleQuoted => Rules.Quotes.HasFlag(IniQuotes.Single),
			_ => false,
		};
	}

	/// <summary>The form a new value takes, or one its own form cannot hold: plain, else run on, else quoted.</summary>
	/// <exception cref="IniEditException">No form of the dialect's holds <paramref name="value"/>.</exception>
	internal IniValueForm FirstChoice(string value) => TryFirstChoice(value) ?? throw new IniEditException(IniEditFailure.Unwritable, value);

	internal IniValueForm? TryFirstChoice(string value) {
		ArgumentNullException.ThrowIfNull(value);
		foreach (IniValueForm form in (ReadOnlySpan<IniValueForm>)[IniValueForm.Plain, IniValueForm.Backslash, IniValueForm.DoubleQuoted, IniValueForm.SingleQuoted]) {
			if (CanHold(form, value)) {
				return form;
			}
		}
		return null;
	}

	//the value's lines in the form: their text, and a break of their own where a quote keeps it raw
	internal List<(string Text, string? Break)> Write(IniValueForm form, string value) {
		switch (form) {
			case IniValueForm.Backslash:
				string[] parts = value.Split('\n');
				return [.. parts.Select((part, at) => (Escape(part) + (at < parts.Length - 1 ? "\\" : ""), (string?)null))];
			case IniValueForm.DoubleQuoted:
			case IniValueForm.SingleQuoted:
				char quote = form == IniValueForm.DoubleQuoted ? '"' : '\'';
				string quoted = quote + value.Replace(quote.ToString(), new string(quote, 2)) + quote;
				var lines = new List<(string, string?)>();
				int start = 0;
				for (int at = 0; at < quoted.Length; at++) {
					if (quoted[at] is '\r' or '\n') {
						string lineBreak = quoted[at] == '\r' && at + 1 < quoted.Length && quoted[at + 1] == '\n' ? "\r\n" : quoted[at].ToString();
						lines.Add((quoted[start..at], lineBreak));
						at += lineBreak.Length - 1;
						start = at + 1;
					}
				}
				lines.Add((quoted[start..], null));
				return lines;
			default:
				return [(Escape(value), null)];
		}
	}

	//a line of a value that reads back as written: none of its own breaks, no spaces at its ends
	//where they are trimmed, no quote opening it, and no comment where one may follow an entry
	private bool LineHolds(string line, bool first, bool last) {
		if (line.Contains('\n') || line.Contains('\r')) {
			return false;
		}
		if ((first && line.Length > 0 && (IsSpace(line[0]) || IsQuote(line[0]))) || (last && line.Length > 0 && IsSpace(line[^1]) && !Rules.TrailingSpaces)) {
			return false;
		}
		if (Rules.CommentsAfterEntries) {
			for (int at = 1; at < line.Length; at++) {
				if (IsCommentMarker(line[at]) && IsSpace(line[at - 1])) {
					return false;
				}
			}
		}
		return true;
	}

	//a key the dialect writes so it reads back as itself
	internal bool CanName(string key)
		=> key.Length > 0 && key == key.Trim() && !key.Contains('\n') && !key.Contains('\r')
			&& ReadField([new IniLine(key + Separator + "x", "")], 0, "") is { } field && field.Key == key && !IsComment(key);

	//a section's name written in a full header so it reads back as itself
	internal bool CanHead(string name) {
		if (name != name.Trim() || name.Contains(']') || name.Contains('\n') || name.Contains('\r') || ReadBrackets($"[{name}]") is not { } brackets || brackets.Written != name) {
			return false;
		}
		string chainBase = "";
		return Resolve(name, ref chainBase) == (name, false);
	}

	/// <summary>The separator a new field takes: the rules' first.</summary>
	internal char Separator => Rules.Separators.HasFlag(IniSeparators.EqualsSign) ? '=' : ':';

	//the first separator on the line, whichever the rules take
	private int SeparatorIn(string text) {
		for (int at = 0; at < text.Length; at++) {
			if ((text[at] == '=' && Rules.Separators.HasFlag(IniSeparators.EqualsSign)) || (text[at] == ':' && Rules.Separators.HasFlag(IniSeparators.Colon))) {
				return at;
			}
		}
		return -1;
	}

	/// <summary>
	///     What the check finds between the document as its edits should have left it and as it reads
	///     (SPEC.md, <i>Checked before it lands</i>): every section's pairs. A dialect that compares
	///     more extends it.
	/// </summary>
	internal virtual List<IniDifference> Check(IniContent expected, IniContent actual, IniContent before) => expected.Against(actual);

	private bool IsCommentMarker(char c)
		=> (c == ';' && Rules.Comments.HasFlag(IniCommentMarkers.Semicolon)) || (c == '#' && Rules.Comments.HasFlag(IniCommentMarkers.Hash));

	private bool IsMarker(char c)
		=> (c == '\\' && Rules.Continuations.HasFlag(IniContinuations.Backslash)) || (c == '_' && Rules.Continuations.HasFlag(IniContinuations.Underscore));

	private bool IsQuote(char c)
		=> (c == '"' && Rules.Quotes.HasFlag(IniQuotes.Double)) || (c == '\'' && Rules.Quotes.HasFlag(IniQuotes.Single));

	private static bool IsSpace(char c) => c is ' ' or '\t';

	private static string Snapshot(IReadOnlyList<IniLine> lines, int at, int count)
		=> string.Join("\n", lines.Skip(at).Take(count).Select(line => line.Text));
}
