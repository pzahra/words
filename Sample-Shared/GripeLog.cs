using PatTech.Localization;
using System.Collections.ObjectModel;

namespace Sample_Shared;

/// <summary>
///     The samples' <see cref="Words.Logger"/> (SPEC: Pages): keeps what Words gripes
///     about — a missing key, a loop, a picture that would not load — newest first, for
///     the Diagnostics page to list. Make it on the UI thread; a gripe from another
///     thread is posted back to it.
/// </summary>
public sealed class GripeLog : ITakeException {
	private const int Kept = 100;
	private readonly SynchronizationContext? home = SynchronizationContext.Current;
	private readonly int homeThread = Environment.CurrentManagedThreadId;

	/// <summary>The gripes, newest first, the oldest dropped past a hundred.</summary>
	public ObservableCollection<string> Entries { get; } = [];

	public void Warn(string text) => Add(text);

	public void Error(Exception exception, string message) => Add($"{message}: {exception.Message}");

	private void Add(string text) {
		string entry = $"{DateTime.Now:HH:mm:ss}  {text}";
		if (home is null || Environment.CurrentManagedThreadId == homeThread) {
			Insert(entry);
		}
		else {
			home.Post(_ => Insert(entry), null);
		}
	}

	private void Insert(string entry) {
		Entries.Insert(0, entry);
		while (Entries.Count > Kept) {
			Entries.RemoveAt(Entries.Count - 1);
		}
	}
}
