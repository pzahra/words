namespace PatTech.Text.Ini;

/// <summary>
///     A refusal or a commit left pending (SPEC.md, <i>Failures are data</i>): the library has no
///     words of its own, so each one says what happened in its type and its data, and its caller
///     says it in its own words. The message is for a developer's log. Each is an
///     <see cref="IOException"/>, so a caller that catches what a file throws catches it too.
/// </summary>
public class IniException : IOException {
	/// <summary>Creates one about <paramref name="path"/>, if it is about a file.</summary>
	public IniException(string message, string? path = null, Exception? inner = null) : base(message, inner) {
		Path = path;
	}

	/// <summary>The file it is about; null for a document of no file.</summary>
	public string? Path { get; }
}

/// <summary>Something the check found an edit would change beyond itself: a section, or a key in one, as expected and as it read.</summary>
/// <param name="Section">The section.</param>
/// <param name="Key">The key; null when the section itself differs.</param>
/// <param name="Expected">What the edit should have left: a value, the section's name when it is the section, or null for none.</param>
/// <param name="Actual">What the document read instead, the same way.</param>
public sealed record IniDifference(string Section, string? Key, string? Expected, string? Actual);

/// <summary>The check refused an edit: the document, edited, read otherwise than it should (SPEC.md, <i>Checked before it lands</i>). It stands as it was before the edit.</summary>
public sealed class IniCheckException : IniException {
	/// <summary>Creates one listing <paramref name="differences"/>.</summary>
	public IniCheckException(IReadOnlyList<IniDifference> differences, string? path = null)
		: base($"The edit would change more than itself: {string.Join("; ", differences.Take(5).Select(Describe))}", path) {
		Differences = differences;
	}

	/// <summary>What would have changed.</summary>
	public IReadOnlyList<IniDifference> Differences { get; }

	private static string Describe(IniDifference difference)
		=> $"[{difference.Section}]{(difference.Key is null ? "" : $" {difference.Key}")}: {difference.Expected ?? "none"} expected, {difference.Actual ?? "none"} read";
}

/// <summary>Why an edit could not be made at all.</summary>
public enum IniEditFailure {
	/// <summary>A key or a section's name the dialect cannot write so it reads back.</summary>
	InvalidName,
	/// <summary>A rename onto a name another section has.</summary>
	NameTaken,
	/// <summary>A section the document does not have.</summary>
	NoSuchSection,
	/// <summary>A value no form of the dialect's holds.</summary>
	Unwritable,
	/// <summary>A line given as a comment that the dialect reads otherwise.</summary>
	NotAComment,
	/// <summary>A <c>[]</c> header the dialect does not take.</summary>
	EmptyHeader,
	/// <summary>An element read before an edit that no longer stands where it stood.</summary>
	Moved,
}

/// <summary>An edit refused before it was tried: what it asked for cannot be written in the dialect, or names what is not there.</summary>
public sealed class IniEditException : IniException {
	/// <summary>Creates one for <paramref name="failure"/> over <paramref name="name"/>.</summary>
	public IniEditException(IniEditFailure failure, string? name, string? path = null)
		: base($"{failure}{(name is null ? "" : $": {name}")}", path) {
		Failure = failure;
		Name = name;
	}

	/// <summary>Why.</summary>
	public IniEditFailure Failure { get; }
	/// <summary>The key, section or value it is about, if any.</summary>
	public string? Name { get; }
}

/// <summary>Why a commit, or an open settling a log, was refused (SPEC.md, <i>Outcomes</i>).</summary>
public enum IniCommitFailure {
	/// <summary>The file changed on disk since it was read: another editor, a checkout, another process.</summary>
	Changed,
	/// <summary>Someone else holds the file, past half a second of trying again.</summary>
	Held,
	/// <summary>The file, or its folder, will not be written: read-only, or not the user's to write.</summary>
	Denied,
	/// <summary>Another commit on the file is running: its log is held.</summary>
	Running,
	/// <summary>A log a commit left will not settle, the file held; it is kept for the next open.</summary>
	Unsettled,
	/// <summary>The log would not write or flush; the file was never touched.</summary>
	LogWrite,
}

/// <summary>A commit refused, or an open refused while a log will not settle: nothing written, the file as it was, and no log of its own left.</summary>
public sealed class IniCommitException : IniException {
	/// <summary>Creates one for <paramref name="failure"/> over <paramref name="path"/>.</summary>
	public IniCommitException(IniCommitFailure failure, string path, string? log = null, Exception? inner = null)
		: base($"{failure}{(log is null ? "" : $" (log {log})")}", path, inner) {
		Failure = failure;
		Log = log;
	}

	/// <summary>Why.</summary>
	public IniCommitFailure Failure { get; }
	/// <summary>The log in the way, kept: another commit's, or one that will not settle; null for none.</summary>
	public string? Log { get; }
}

/// <summary>A commit that stands, its log whole, but whose write to the file failed twice: the next open through the library finishes it.</summary>
public sealed class IniPendingException : IniException {
	/// <summary>Creates one for <paramref name="path"/>, finished from <paramref name="log"/>.</summary>
	public IniPendingException(string path, string log, Exception? inner = null) : base($"Pending (log {log})", path, inner) {
		Log = log;
	}

	/// <summary>Where the log sits.</summary>
	public string Log { get; }
}

/// <summary>What went wrong between text and bytes.</summary>
public enum IniEncodingFailure {
	/// <summary>The file holds a byte its encoding does not read: no text to edit, though it can be replaced whole.</summary>
	NotText,
	/// <summary>The file carries a byte-order mark its dialect's rules refuse: no text to edit, though it can be replaced whole.</summary>
	MarkRefused,
	/// <summary>The text holds a character its encoding cannot write.</summary>
	Unencodable,
}

/// <summary>A file that is no text to edit, or text that will not encode.</summary>
public sealed class IniEncodingException : IniException {
	/// <summary>Creates one for <paramref name="failure"/> in <paramref name="encoding"/>.</summary>
	public IniEncodingException(IniEncodingFailure failure, string? encoding, string? path = null, Exception? inner = null)
		: base($"{failure}{(encoding is null ? "" : $" ({encoding})")}", path, inner) {
		Failure = failure;
		Encoding = encoding;
	}

	/// <summary>Why.</summary>
	public IniEncodingFailure Failure { get; }
	/// <summary>The encoding's name, when one is known.</summary>
	public string? Encoding { get; }
}
