namespace PatTech.Text.Ini;

/// <summary>A commit landed: the file holds the new bytes.</summary>
/// <param name="Log">A log that would not delete, left for the next open to find the file written and delete it; null when it went.</param>
public sealed record IniLanded(string? Log);

/// <summary>What an open found in a log a commit left, and did (SPEC.md, <i>Recovery</i>).</summary>
public enum IniRecoveryKind {
	/// <summary>The log was never whole, so the file was never touched: the log went.</summary>
	Unwhole,
	/// <summary>The file was already written: the log went.</summary>
	Finished,
	/// <summary>The write never began: the file was written, or made, from the log.</summary>
	Replayed,
	/// <summary>The file was at neither hash, a write broken off or someone's change: its bytes were kept, then it was written from the log.</summary>
	Kept,
}

/// <summary>A log an open settled.</summary>
/// <param name="Kind">What it found and did.</param>
/// <param name="Kept">Where the file's bytes were kept, for <see cref="IniRecoveryKind.Kept"/> over a file there was; null otherwise.</param>
public sealed record IniRecovery(IniRecoveryKind Kind, string? Kept);
