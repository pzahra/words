using System.Text;

namespace PatTech.Text.Ini;

/// <summary>
///     A value that is a list, <c>"comma separated", arrays</c> (SPEC.md, <i>Lists</i>): items
///     parted by commas and trimmed, an item in double quotes taking commas, spaces and doubled
///     quotes as its own.
/// </summary>
public static class IniList {
	/// <summary>The items <paramref name="value"/> lists; none for a value of nothing but spaces.</summary>
	public static IReadOnlyList<string> Split(string value) {
		ArgumentNullException.ThrowIfNull(value);
		var items = new List<string>();
		if (value.Trim().Length == 0) {
			return items;
		}
		int at = 0;
		while (true) {
			while (at < value.Length && IsSpace(value[at])) {
				at++;
			}
			var item = new StringBuilder();
			if (at < value.Length && value[at] == '"') {
				for (at++; at < value.Length; at++) {
					if (value[at] != '"') {
						item.Append(value[at]);
					}
					else if (at + 1 < value.Length && value[at + 1] == '"') {
						item.Append('"');
						at++;
					}
					else {
						at++;
						break;
					}
				}
				//whatever stands between its closing quote and the comma is its own too
				int comma = value.IndexOf(',', at);
				item.Append(value[at..(comma < 0 ? value.Length : comma)].Trim());
				at = comma < 0 ? value.Length : comma;
			}
			else {
				int comma = value.IndexOf(',', at);
				item.Append(value[at..(comma < 0 ? value.Length : comma)].Trim());
				at = comma < 0 ? value.Length : comma;
			}
			items.Add(item.ToString());
			if (at == value.Length) {
				return items;
			}
			at++;
		}
	}

	/// <summary><paramref name="items"/> as a list, parted by <paramref name="separator"/>, an item quoted only where it needs to be.</summary>
	public static string Join(IEnumerable<string> items, string separator = ", ") {
		ArgumentNullException.ThrowIfNull(items);
		List<string> given = [.. items];
		return string.Join(separator, given.Select(item => NeedsQuotes(item, alone: given.Count == 1) ? $"\"{item.Replace("\"", "\"\"")}\"" : item));
	}

	//an item that would not split back as itself bare: a comma or a quote in it, spaces at its
	//ends, or nothing at all where it is the list's only item
	private static bool NeedsQuotes(string item, bool alone)
		=> item.Contains(',') || item.Contains('"') || item != item.Trim() || (alone && item.Length == 0);

	private static bool IsSpace(char c) => c is ' ' or '\t';
}
