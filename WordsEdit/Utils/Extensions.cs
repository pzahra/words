using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WordsEdit.Utils;

/// <summary>
/// Extension methods for improving code readability.
/// </summary>
public static class Extensions {
	private const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

	/// <summary>
	///     Returns the <see cref="MemberInfo"/> associated with <paramref name="value"/> if it
	///     is defined; otherwise, returns <see langword="null"/>.
	/// </summary>
	public static MemberInfo? GetEnumMemberInfo(this Enum value) {
		Debug.Assert(value != null);
		var type = value.GetType();
		return Enum.GetNames(type)
			.Where(n => Enum.Parse(type, n).Equals(value))
			.Select(n => GetEnumMemberInfo(type, n))
			.OfType<MemberInfo>()
			.OrderBy(m => m.GetCustomAttribute<ObsoleteAttribute>() != null)
			.FirstOrDefault();
	}
	/// <summary>
	///     Returns the <see cref="MemberInfo"/> associated with <paramref name="name"/> from
	///     the <see langword="enum"/> <paramref name="type"/>.
	/// </summary>
	/// <param name="type">The enum type.</param>
	/// <param name="name">The name of the field.</param>
	public static MemberInfo? GetEnumMemberInfo(Type type, string name) {
		Debug.Assert(type != null);
		Debug.Assert(type.IsEnum);
		return type.GetMember(name, MemberTypes.Field, flags)
			.SingleOrDefault();
	}
	/// <summary>
	///     Returns the <see cref="MemberInfo"/> associated with <paramref name="name"/> from
	///     the <see langword="enum"/> <typeparamref name="TEnum"/>.
	/// </summary>
	/// <typeparam name="TEnum">The enum type.</typeparam>
	/// <param name="name">The name of the field.</param>
	public static MemberInfo? GetEnumMemberInfo<TEnum>(string name) where TEnum : Enum {
		return typeof(TEnum)
			.GetMember(name, MemberTypes.Field, flags)
			.SingleOrDefault();
	}
	/// <summary>
	/// Search for the first matching Attribute on the given enum value.
	/// </summary>
	/// <typeparam name="T">The attribute you seek.</typeparam>
	/// <param name="value">The enum value.</param>
	/// <returns>Matching attribute or <see langword="null"/> if not present.</returns>
	public static T? GetEnumMemberAttribute<T>(this Enum value)
		where T : Attribute {
		Debug.Assert(value != null);
		return value.GetEnumMemberInfo()
			?.GetCustomAttribute<T>(false);
	}

	/// <summary>
	/// Execute a Regular Expression and output the <see cref="Match"/>, returning true if successful.
	/// </summary>
	/// <param name="pattern">The Regex pattern.</param>
	/// <param name="subject">The string to search.</param>
	/// <param name="match">The search result, successful or not.</param>
	/// <returns>True if the match succeeded.</returns>
	public static bool TryMatch(this Regex pattern, string subject, out Match match) {
		match = pattern.Match(subject);
		return match.Success;
	}

	/// <summary>
	/// Retrieve and output a named group from a Regex result, returning true if successful.
	/// </summary>
	/// <param name="match">The regex result.</param>
	/// <param name="key">The name of the match group.</param>
	/// <param name="group">The match group if found; or null if not found.</param>
	/// <returns>True if the group was found.</returns>
	public static bool TryGetGroup(this Match match, string key, [NotNullWhen(true)] out Group? group)
		=> match.Groups.TryGetValue(key, out group) && group.Success;

	public static bool IsNullOrEmpty<T>(this IList<T> list) => list?.Any() is not true;

	/// <summary>
	/// Filter a nullable sequence of all null values, informing a null-aware
	/// linter that the sequence is now null-free.
	/// </summary>
	/// <typeparam name="T">The sequence type.</typeparam>
	/// <param name="list">The sequence.</param>
	/// <returns>The sequence without nulls.</returns>
	public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> list) {
		foreach (var item in list) {
			if (item is not null) {
				yield return item;
			}
		}
	}
	/// <summary>
	/// Filter a nullable sequence of all null values, informing a null-aware
	/// linter that the sequence is now null-free.
	/// </summary>
	/// <typeparam name="T">The sequence type.</typeparam>
	/// <param name="list">The sequence.</param>
	/// <returns>The sequence without nulls.</returns>
	public static IEnumerable<T> WhereNotNull<T>(this IList<T?> list) {
		for (int i = 0; i < list.Count; ++i) {
			if (list[i] is { } item) {
				yield return item;
			}
		}
	}

	/// <summary>
	/// Scan a list for a matching item according to the callback and return its index,
	/// or -1 if not found.
	/// </summary>
	/// <typeparam name="T">The type of item in the list.</typeparam>
	/// <param name="list">The list to search.</param>
	/// <param name="match">The comparison callback.</param>
	/// <returns>The index of the matching item; otherwise, -1.</returns>
	public static int FindIndex<T>(this IList<T> list, Predicate<T> match) {
		for (int i = 0; i < list.Count; ++i) {
			if (match(list[i])) {
				return i;
			}
		}
		return -1;
	}

	/// <summary>
	/// Fire-and-forget a task, optionally handling any exception via the provided callback.
	/// </summary>
	public static async void SafeFireAndForget(this Task task, Action<Exception> onException) {
		ArgumentNullException.ThrowIfNull(onException);
		if (task is null) return;
		try {
			await task.ConfigureAwait(false);
		}
		catch (Exception ex) {
			try { onException(ex); } catch { /* swallow exceptions from the handler */ }
		}
	}

	public static void ForEach<T>(this IEnumerable<T> list, Action<T> action) {
		foreach (T item in list) { action(item); }
	}
}

