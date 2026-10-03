namespace WordsEdit.ViewModels;

/// <summary>
///     Where the selection has been (SPEC: Navigation): node labels, oldest first, and
///     the one the selection stands on. A browser's history, except that selecting a
///     neighbour by hand is a step onto it, not a push, so walking back and forth
///     between two nodes piles nothing up and keeps what lies ahead.
/// </summary>
/// <param name="capacity">How many entries it keeps; the oldest go first.</param>
public sealed class SelectionHistory(int capacity = 50) {
	private readonly List<string> entries = [];
	private int current = -1;

	public bool CanGoBack => current > 0;
	public bool CanGoForward => current < entries.Count - 1;
	/// <summary>The entries, oldest first, for tests.</summary>
	public IReadOnlyList<string> Entries => entries;
	/// <summary>The label the selection stands on; null while empty.</summary>
	public string? Current => current < 0 ? null : entries[current];

	/// <summary>
	///     The selection moved to <paramref name="label"/> by any way but Back and
	///     Forward: the node it stands on, or a neighbour stepped onto, or a push that
	///     drops whatever lay ahead.
	/// </summary>
	public void Visit(string label) {
		if (Current == label) {
			return;
		}
		if (CanGoBack && entries[current - 1] == label) {
			current--;
			return;
		}
		if (CanGoForward && entries[current + 1] == label) {
			current++;
			return;
		}
		entries.RemoveRange(current + 1, entries.Count - current - 1);
		entries.Add(label);
		if (entries.Count > capacity) {
			entries.RemoveAt(0);
		}
		current = entries.Count - 1;
	}

	/// <summary>Steps back to the nearest entry that <paramref name="resolves"/>; null, and no step, when none does.</summary>
	public string? Back(Func<string, bool> resolves) => Step(-1, resolves);

	/// <summary>Steps forward to the nearest entry that <paramref name="resolves"/>; null, and no step, when none does.</summary>
	public string? Forward(Func<string, bool> resolves) => Step(+1, resolves);

	private string? Step(int direction, Func<string, bool> resolves) {
		while (direction < 0 ? CanGoBack : CanGoForward) {
			int next = current + direction;
			string label = entries[next];
			if (label != entries[current] && resolves(label)) {
				current = next;
				return label;
			}
			//gone (removed, renamed, its file unloaded), or the same node again once a gone
			//one between them went: dropped as it is reached
			entries.RemoveAt(next);
			if (direction < 0) {
				current--;
			}
		}
		return null;
	}

	public void Clear() {
		entries.Clear();
		current = -1;
	}
}
