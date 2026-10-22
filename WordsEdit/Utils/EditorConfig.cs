using System.Globalization;
using System.IO;

namespace WordsEdit.Utils;

/// <summary>
///     Wordsmith's own settings — the language it speaks, the main window as it
///     last closed, and the help bubbles — in <c>%LocalAppData%\Wordsmith\config.ini</c>,
///     in sections: <c>[editor]</c> for the language and the tips switch,
///     <c>[window]</c> for the window, <c>[seen]</c> for each bubble closed, each a
///     group of <c>key=value</c> lines, their names matched without case. Read on
///     demand, written as soon as a value is set, missing file allowed; a file
///     that won't read or write costs the settings, never the editor.
/// </summary>
public static class EditorConfig {
	/// <summary>Where the settings live; tests point it elsewhere.</summary>
	public static string Path { get; set; } = System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wordsmith", "config.ini");

	private const string EditorSection = "editor";
	private const string WindowSection = "window";
	private const string SeenSection = "seen";

	/// <summary>The language Wordsmith speaks, or null when the OS decides.</summary>
	public static string? Language {
		get => Read(EditorSection).GetValueOrDefault("language");
		set => Write(EditorSection, ("language", value));
	}

	/// <summary>The main window's size and state as it last closed, or null before it ever has.</summary>
	public static WindowPlace? Window {
		get {
			var values = Read(WindowSection);
			return Number(values, "width") is { } width && Number(values, "height") is { } height
				? new WindowPlace(width, height, string.Equals(values.GetValueOrDefault("state"), "maximized", StringComparison.OrdinalIgnoreCase))
				: null;
		}
		set => Write(WindowSection,
			("width", value?.Width.ToString("0", CultureInfo.InvariantCulture)),
			("height", value?.Height.ToString("0", CultureInfo.InvariantCulture)),
			("state", value is null ? null : value.Value.Maximized ? "maximized" : "normal"));
	}

	/// <summary>Whether the help bubbles show: on unless <c>[editor]</c> says <c>tips=off</c>.</summary>
	public static bool Tips {
		get => !string.Equals(Read(EditorSection).GetValueOrDefault("tips"), "off", StringComparison.OrdinalIgnoreCase);
		set => Write(EditorSection, ("tips", value ? null : "off"));
	}

	/// <summary>The revision of the help bubble <paramref name="topic"/> last closed, from <c>[seen]</c>; 0 when it never was.</summary>
	public static int Seen(string topic)
		=> int.TryParse(Read(SeenSection).GetValueOrDefault(topic), NumberStyles.None, CultureInfo.InvariantCulture, out int revision) ? revision : 0;

	/// <summary>Remembers the help bubble <paramref name="topic"/> closed at <paramref name="revision"/>.</summary>
	public static void Remember(string topic, int revision) => Write(SeenSection, (topic, revision.ToString(CultureInfo.InvariantCulture)));

	/// <summary>Forgets every help bubble closed: the <c>[seen]</c> section goes.</summary>
	public static void ForgetSeen() => Remove(SeenSection);

	private static double? Number(Dictionary<string, string> values, string key)
		=> double.TryParse(values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number > 0 ? number : null;

	//a section's values, the last line naming a key winning. Lines above the first
	//section are in none, so a config from before the sections reads as empty; a
	//file that won't read is no settings at all
	private static Dictionary<string, string> Read(string section) {
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] lines = Lines();
		if (Find(lines, section) is { } found) {
			for (int i = found.Start + 1; i < found.End; i++) {
				if (KeyOf(lines[i]) is { } key) {
					values[key] = lines[i][(lines[i].IndexOf('=') + 1)..].Trim();
				}
			}
		}
		return values;
	}

	//each change replaces its key's line in its section where it stands, or joins the
	//section's last value; a cleared one goes. A section the file lacks is appended
	//after a blank line. Comments, lines that name no key and other sections stay as
	//they were. A config that won't write (read-only, locked) keeps what it had: the
	//setting is lost, never the close it was saved on
	private static void Write(string section, params (string Key, string? Value)[] changes) {
		try {
			List<string> lines = [.. Lines()];
			if (Find(lines, section) is null) {
				if (changes.All(change => string.IsNullOrWhiteSpace(change.Value))) {
					return; //nothing to clear, and no section to make for it
				}
				if (lines.Count > 0 && lines[^1].Trim() != "") {
					lines.Add("");
				}
				lines.Add($"[{section}]");
			}
			foreach ((string key, string? value) in changes) {
				var (start, end) = Find(lines, section)!.Value;
				int at = -1;
				for (int i = end - 1; i > start; i--) {
					if (string.Equals(KeyOf(lines[i]), key, StringComparison.OrdinalIgnoreCase)) {
						lines.RemoveAt(i);
						at = i;
					}
				}
				if (!string.IsNullOrWhiteSpace(value)) {
					lines.Insert(at < 0 ? LastValue(lines, section) + 1 : at, $"{key}={value.Trim()}");
				}
			}
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? "");
			File.WriteAllLines(Path, lines);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
		}
	}

	//a section gone whole, its header to the next; one that ended the file takes the blank
	//lines before it too
	private static void Remove(string section) {
		try {
			List<string> lines = [.. Lines()];
			if (Find(lines, section) is not { } found) {
				return;
			}
			lines.RemoveRange(found.Start, found.End - found.Start);
			if (found.Start == lines.Count) {
				while (lines.Count > 0 && lines[^1].Trim() == "") {
					lines.RemoveAt(lines.Count - 1);
				}
			}
			File.WriteAllLines(Path, lines);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
		}
	}

	//the section's header line, and the line its section ends before: the next header's, or the end
	private static (int Start, int End)? Find(IReadOnlyList<string> lines, string section) {
		for (int start = 0; start < lines.Count; start++) {
			if (string.Equals(SectionOf(lines[start]), section, StringComparison.OrdinalIgnoreCase)) {
				int end = start + 1;
				while (end < lines.Count && SectionOf(lines[end]) is null) {
					end++;
				}
				return (start, end);
			}
		}
		return null;
	}

	//the line a new key goes after: the section's last that isn't blank, so the blank
	//line before the next section stays where it is
	private static int LastValue(IReadOnlyList<string> lines, string section) {
		var (start, end) = Find(lines, section)!.Value;
		int last = end - 1;
		while (last > start && lines[last].Trim() == "") {
			last--;
		}
		return last;
	}

	private static string[] Lines() {
		try {
			return File.Exists(Path) ? File.ReadAllLines(Path) : [];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return [];
		}
	}

	private static string? SectionOf(string line) {
		string trimmed = line.Trim();
		return trimmed.Length > 1 && trimmed[0] == '[' && trimmed[^1] == ']' ? trimmed[1..^1].Trim() : null;
	}

	private static string? KeyOf(string line) {
		int equals = line.IndexOf('=');
		return equals > 0 && !line.TrimStart().StartsWith(';') && SectionOf(line) is null ? line[..equals].Trim() : null;
	}
}
