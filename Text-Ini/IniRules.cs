using System.Text;

namespace PatTech.Text.Ini;

/// <summary>What parts a key from its value.</summary>
[Flags]
public enum IniSeparators {
	/// <summary><c>key=value</c>.</summary>
	EqualsSign = 1,
	/// <summary><c>key: value</c>.</summary>
	Colon = 2,
	/// <summary>Either, whichever comes first on the line.</summary>
	Either = EqualsSign | Colon,
}

/// <summary>What starts a comment.</summary>
[Flags]
public enum IniCommentMarkers {
	/// <summary><c>; a comment</c>.</summary>
	Semicolon = 1,
	/// <summary><c># a comment</c>.</summary>
	Hash = 2,
	/// <summary>Either.</summary>
	Either = Semicolon | Hash,
}

/// <summary>The markers that run a value's line on to the next.</summary>
[Flags]
public enum IniContinuations {
	/// <summary>A value ends with its line.</summary>
	None = 0,
	/// <summary><c>\</c>, the line break kept in the value.</summary>
	Backslash = 1,
	/// <summary><c>_</c>, the next line joined on in the same line.</summary>
	Underscore = 2,
	/// <summary>Either.</summary>
	Both = Backslash | Underscore,
}

/// <summary>How a continuation marker meant as itself is written.</summary>
public enum IniEscapes {
	/// <summary>Doubled (<c>\\</c>, <c>__</c>): a line runs on when it ends in an odd run of a marker.</summary>
	Doubled,
	/// <summary>
	///     A marker takes whatever character follows it, so a line runs on only when it ends
	///     in a marker nothing took (<c>one\_</c> and <c>one_\</c> end in a pair); a doubled
	///     marker still reads as one.
	/// </summary>
	TakesNext,
}

/// <summary>The quotes a value may be written in, running on over as many lines as it takes.</summary>
[Flags]
public enum IniQuotes {
	/// <summary>A quote is part of the value.</summary>
	None = 0,
	/// <summary><c>'…'</c>.</summary>
	Single = 1,
	/// <summary><c>"…"</c>.</summary>
	Double = 2,
	/// <summary>Either.</summary>
	Both = Single | Double,
}

/// <summary>What a byte-order mark does.</summary>
public enum IniByteOrderMarks {
	/// <summary>It names the file's encoding, and stays.</summary>
	Kept,
	/// <summary>One naming an encoding the rules list is read past and never written; one naming another, the file is no text to edit.</summary>
	Dropped,
	/// <summary>Any, the file is no text to edit.</summary>
	Refused,
}

/// <summary>The form a value is written in.</summary>
public enum IniValueForm {
	/// <summary>On its line, as it is.</summary>
	Plain,
	/// <summary>Run on with <c>\</c>, a line of the file for each line of the value.</summary>
	Backslash,
	/// <summary>Run on with <c>_</c>, its lines joined in one.</summary>
	Underscore,
	/// <summary>In <c>'…'</c>.</summary>
	SingleQuoted,
	/// <summary>In <c>"…"</c>.</summary>
	DoubleQuoted,
}

/// <summary>
///     A dialect's rules (SPEC.md, <i>Its dialect</i>, <i>Values and comments</i>): how names
///     compare, what parts a key from its value, how a value runs past its line, what a
///     comment is, and the encodings a file may be in.
/// </summary>
public sealed record IniRules {
	/// <summary>How section names and keys compare: without case, or exactly.</summary>
	public StringComparer Names { get; init; } = StringComparer.OrdinalIgnoreCase;
	/// <summary>What parts a key from its value; a new field takes the first.</summary>
	public IniSeparators Separators { get; init; } = IniSeparators.EqualsSign;
	/// <summary>What starts a comment line.</summary>
	public IniCommentMarkers Comments { get; init; } = IniCommentMarkers.Semicolon;
	/// <summary>Whether a comment may follow an entry on its line, its marker after a space and outside quotes.</summary>
	public bool CommentsAfterEntries { get; init; }
	/// <summary>The markers that run a value on.</summary>
	public IniContinuations Continuations { get; init; }
	/// <summary>How a marker meant as itself is written.</summary>
	public IniEscapes Escapes { get; init; }
	/// <summary>The quotes a value may be written in.</summary>
	public IniQuotes Quotes { get; init; }
	/// <summary>Whether a <c>[]</c> header opens the section <c>""</c> again; without, its lines are no elements.</summary>
	public bool EmptyHeader { get; init; } = true;
	/// <summary>The encodings a file without a byte-order mark is read in, tried in order, each strictly: UTF-8 unless a dialect says else.</summary>
	public IReadOnlyList<Encoding> Encodings { get; init; } = [new UTF8Encoding(false)];
	/// <summary>What a byte-order mark does: kept, dropped, or refused.</summary>
	public IniByteOrderMarks ByteOrderMarks { get; init; }
	/// <summary>The break of a line added to a file that has none to copy; null for the system's.</summary>
	public string? LineBreak { get; init; }
}
