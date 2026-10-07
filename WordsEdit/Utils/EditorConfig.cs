using System.Globalization;
using System.IO;

namespace WordsEdit.Utils;

/// <summary>
///     Wordsmith's own settings — the language it speaks, and the main window as it
///     last closed — as <c>key=value</c> lines in <c>%LocalAppData%\Wordsmith\config.ini</c>.
///     Read on demand, written as soon as a value is set, missing file allowed; a
///     file that won't read or write costs the settings, never the editor.
/// </summary>
public static class EditorConfig {
	/// <summary>Where the settings live; tests point it elsewhere.</summary>
	public static string Path { get; set; } = System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wordsmith", "config.ini");

	/// <summary>The language Wordsmith speaks, or null when the OS decides.</summary>
	public static string? Language {
		get => Read().GetValueOrDefault("language");
		set => Write(("language", value));
	}

	/// <summary>The main window's size and state as it last closed, or null before it ever has.</summary>
	public static WindowPlace? Window {
		get {
			var values = Read();
			return Number(values, "window-width") is { } width && Number(values, "window-height") is { } height
				? new WindowPlace(width, height, string.Equals(values.GetValueOrDefault("window-state"), "maximized", StringComparison.OrdinalIgnoreCase))
				: null;
		}
		set => Write(
			("window-width", value?.Width.ToString("0", CultureInfo.InvariantCulture)),
			("window-height", value?.Height.ToString("0", CultureInfo.InvariantCulture)),
			("window-state", value is null ? null : value.Value.Maximized ? "maximized" : "normal"));
	}

	private static double? Number(Dictionary<string, string> values, string key)
		=> double.TryParse(values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number > 0 ? number : null;

	//the last line naming a key wins; a file that won't read is no settings at all
	private static Dictionary<string, string> Read() {
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in Lines()) {
			if (KeyOf(line) is { } key) {
				values[key] = line[(line.IndexOf('=') + 1)..].Trim();
			}
		}
		return values;
	}

	//each change replaces its key's line where it stands, or joins the end; a
	//cleared one goes. Comments and lines that name no key stay as they were.
	//A config that won't write (read-only, locked) keeps what it had: the
	//setting is lost, never the close it was saved on
	private static void Write(params (string Key, string? Value)[] changes) {
		try {
			List<string> lines = [.. Lines()];
			foreach ((string key, string? value) in changes) {
				int at = -1;
				for (int i = lines.Count - 1; i >= 0; i--) {
					if (string.Equals(KeyOf(lines[i]), key, StringComparison.OrdinalIgnoreCase)) {
						lines.RemoveAt(i);
						at = i;
					}
				}
				if (!string.IsNullOrWhiteSpace(value)) {
					lines.Insert(at < 0 ? lines.Count : at, $"{key}={value.Trim()}");
				}
			}
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? "");
			File.WriteAllLines(Path, lines);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
		}
	}

	private static string[] Lines() {
		try {
			return File.Exists(Path) ? File.ReadAllLines(Path) : [];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return [];
		}
	}

	private static string? KeyOf(string line) {
		int equals = line.IndexOf('=');
		return equals > 0 && !line.StartsWith(';') ? line[..equals].Trim() : null;
	}
}
