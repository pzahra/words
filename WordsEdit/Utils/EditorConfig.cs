using System.IO;

namespace WordsEdit.Utils;

/// <summary>
///     Wordsmith's own settings — today only the language it speaks — as
///     <c>key=value</c> lines in <c>%LocalAppData%\Wordsmith\config.ini</c>.
///     Read on demand, written as soon as a value is set, missing file allowed.
/// </summary>
public static class EditorConfig {
	/// <summary>Where the settings live; tests point it elsewhere.</summary>
	public static string Path { get; set; } = System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wordsmith", "config.ini");

	/// <summary>The language Wordsmith speaks, or null when the OS decides.</summary>
	public static string? Language {
		get => Read().GetValueOrDefault("language");
		set => Write("language", value);
	}

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

	private static void Write(string key, string? value) {
		var values = Read();
		if (string.IsNullOrWhiteSpace(value)) {
			values.Remove(key);
		}
		else {
			values[key] = value.Trim();
		}
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? "");
		File.WriteAllLines(Path, values.Select(pair => $"{pair.Key}={pair.Value}"));
	}
}
