using System.Globalization;
using System.IO;

namespace WordsEdit.Utils;

/// <summary>
///     Wordsmith's own settings — the language it speaks, and the main window as it
///     last closed — as <c>key=value</c> lines in <c>%LocalAppData%\Wordsmith\config.ini</c>.
///     Read on demand, written as soon as a value is set, missing file allowed.
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

	private static Dictionary<string, string> Read() {
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(Path)) {
			return values;
		}
		foreach (string line in File.ReadAllLines(Path)) {
			int equals = line.IndexOf('=');
			if (equals > 0 && !line.StartsWith(';')) {
				values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
			}
		}
		return values;
	}

	private static void Write(params (string Key, string? Value)[] changes) {
		var values = Read();
		foreach ((string key, string? value) in changes) {
			if (string.IsNullOrWhiteSpace(value)) {
				values.Remove(key);
			}
			else {
				values[key] = value.Trim();
			}
		}
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? "");
		File.WriteAllLines(Path, values.Select(pair => $"{pair.Key}={pair.Value}"));
	}
}
