using System.Globalization;

namespace Sample_Shared;

/// <summary>
///     What a sample remembers between runs (SPEC: The config): <c>key=value</c> lines in
///     <c>%LocalAppData%\Words\&lt;sample&gt;\config.ini</c> — the language, the theme, the
///     topic last open, and the revision of each topic last visited. Read once, written
///     as each value changes; keys it does not know are kept, and a file it cannot write
///     is let be.
/// </summary>
public sealed class SampleConfig {
	private readonly Dictionary<string, string> values;

	/// <summary>Where the settings live.</summary>
	public string Path { get; }

	/// <summary>The settings at <paramref name="path"/>; a missing file is a first run.</summary>
	public SampleConfig(string path) {
		Path = path;
		values = Read(path);
	}

	/// <summary>The settings of the sample named <paramref name="sample"/>, under the user's local app data.</summary>
	public static SampleConfig For(string sample) => new(System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Words", sample, "config.ini"));

	/// <summary>The language last picked, or null.</summary>
	public string? Language {
		get => values.GetValueOrDefault("language");
		set => Set("language", value);
	}

	/// <summary>The theme last picked, <c>dark</c> or <c>light</c>, or null.</summary>
	public string? Theme {
		get => values.GetValueOrDefault("theme");
		set => Set("theme", value);
	}

	/// <summary>The id of the topic last open, or null.</summary>
	public string? Topic {
		get => values.GetValueOrDefault("topic");
		set => Set("topic", value);
	}

	/// <summary>The revision of <paramref name="topic"/> last visited; 0 when never.</summary>
	public int Seen(string topic)
		=> int.TryParse(values.GetValueOrDefault($"seen.{topic}"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int revision) ? revision : 0;

	/// <summary>Remembers that <paramref name="topic"/> was visited at <paramref name="revision"/>.</summary>
	public void MarkSeen(string topic, int revision)
		=> Set($"seen.{topic}", revision.ToString(CultureInfo.InvariantCulture));

	private static Dictionary<string, string> Read(string path) {
		var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try {
			foreach (string line in File.ReadAllLines(path)) {
				int equals = line.IndexOf('=');
				if (equals > 0 && !line.StartsWith(';')) {
					read[line[..equals].Trim()] = line[(equals + 1)..].Trim();
				}
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			//missing or unreadable: a first run
		}
		return read;
	}

	private void Set(string key, string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			values.Remove(key);
		}
		else {
			values[key] = value.Trim();
		}
		try {
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? "");
			File.WriteAllLines(Path, values.Select(pair => $"{pair.Key}={pair.Value}"));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			//a sample that cannot remember still runs
		}
	}
}
