namespace WordsEdit.Tests;

/// <summary>A clock a test moves by hand: when keystrokes land, and the stale stamps typing writes.</summary>
internal sealed class FakeTime : TimeProvider {
	public DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
	public override DateTimeOffset GetUtcNow() => Now;
}
