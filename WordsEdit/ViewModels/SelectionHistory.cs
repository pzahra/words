namespace WordsEdit.ViewModels;

/// <summary>
///     Where the selection has been (SPEC: Navigation): the places of nodes, oldest
///     first, and the one the selection stands on. A browser's history, except that
///     selecting a neighbour by hand is a step onto it, not a push, so walking back and
///     forth between two nodes piles nothing up and keeps what lies ahead.
/// </summary>
/// <typeparam name="T">A node's place: the tree keeps its file and its path (<see cref="NodeRef"/>).</typeparam>
/// <param name="capacity">How many entries it keeps; the oldest go first.</param>
public sealed class SelectionHistory<T>(int capacity = 50) where T : class {
	private readonly List<T> entries = [];
	private int current = -1;

	public bool CanGoBack => current > 0;
	public bool CanGoForward => current < entries.Count - 1;
	/// <summary>The entries, oldest first, for tests.</summary>
	public IReadOnlyList<T> Entries => entries;
	/// <summary>The place the selection stands on; null while empty.</summary>
	public T? Current => current < 0 ? null : entries[current];

	/// <summary>
	///     The selection moved to <paramref name="place"/> by any way but Back and
	///     Forward: the node it stands on, or a neighbour stepped onto, or a push that
	///     drops whatever lay ahead.
	/// </summary>
	public void Visit(T place) {
		if (current >= 0 && Same(entries[current], place)) {
			return;
		}
		if (CanGoBack && Same(entries[current - 1], place)) {
			current--;
			return;
		}
		if (CanGoForward && Same(entries[current + 1], place)) {
			current++;
			return;
		}
		entries.RemoveRange(current + 1, entries.Count - current - 1);
		entries.Add(place);
		if (entries.Count > capacity) {
			entries.RemoveAt(0);
		}
		current = entries.Count - 1;
	}

	/// <summary>Steps back to the nearest entry that <paramref name="resolves"/>; null, and no step, when none does.</summary>
	public T? Back(Func<T, bool> resolves) => Step(-1, resolves);

	/// <summary>Steps forward to the nearest entry that <paramref name="resolves"/>; null, and no step, when none does.</summary>
	public T? Forward(Func<T, bool> resolves) => Step(+1, resolves);

	private T? Step(int direction, Func<T, bool> resolves) {
		while (direction < 0 ? CanGoBack : CanGoForward) {
			int next = current + direction;
			T place = entries[next];
			if (!Same(place, entries[current]) && resolves(place)) {
				current = next;
				return place;
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

	private static bool Same(T a, T b) => a.Equals(b);

	/// <summary>
	///     Drops every entry that is <paramref name="gone"/> at once, and one left beside
	///     its own twin once what stood between them went (SPEC: Dirty by file, Each
	///     file's history); the selection stands on the nearest entry left at or before
	///     where it stood.
	/// </summary>
	public void Forget(Func<T, bool> gone) {
		List<T> left = [];
		int standing = -1;
		for (int i = 0; i < entries.Count; i++) {
			if (!gone(entries[i]) && (left.Count == 0 || !Same(left[^1], entries[i]))) {
				left.Add(entries[i]);
			}
			if (i == current) {
				standing = left.Count - 1;
			}
		}
		entries.Clear();
		entries.AddRange(left);
		current = entries.Count == 0 ? -1 : Math.Max(standing, 0);
	}

	public void Clear() {
		entries.Clear();
		current = -1;
	}
}
