namespace WordsEdit.Utils;

/// <summary>The list helpers the editor uses.</summary>
public static class Extensions {
	public static bool IsNullOrEmpty<T>(this IList<T>? list) => list is not { Count: > 0 };

	/// <summary>The index of the first item <paramref name="match"/> accepts, or -1.</summary>
	public static int FindIndex<T>(this IList<T> list, Predicate<T> match) {
		for (int i = 0; i < list.Count; ++i) {
			if (match(list[i])) {
				return i;
			}
		}
		return -1;
	}

	public static void ForEach<T>(this IEnumerable<T> list, Action<T> action) {
		foreach (T item in list) {
			action(item);
		}
	}
}
