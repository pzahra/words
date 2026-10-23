using System.Globalization;
using System.IO;
using PatTech.Text.Ini;

namespace WordsEdit.Utils;

/// <summary>
///     Wordsmith's own settings — the language it speaks, the main window as it
///     last closed, the help bubbles and the toolbars — in <c>%LocalAppData%\Wordsmith\config.ini</c>,
///     in sections: <c>[editor]</c> for the language and the tips switch,
///     <c>[window]</c> for the window, <c>[seen]</c> for each bubble closed,
///     <c>[toolbars]</c> for each toolbar changed, each a
///     group of <c>key=value</c> lines, their names matched without case. Read
///     once, each value then read from memory; written as soon as a value is set,
///     every value a moment sets in one transaction of the ini driver's (SPEC: Save
///     as a patch), missing file allowed; a file that won't read or write costs the
///     settings, never the editor.
/// </summary>
public static class EditorConfig {
	private static string path = System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wordsmith", "config.ini");
	//the file as last read or written; null until it is read
	private static IniDocument? read;

	/// <summary>Where the settings live; tests point it elsewhere.</summary>
	public static string Path {
		get => path;
		set {
			path = value;
			read = null;
		}
	}

	/// <summary>Drops the settings read, so the next value read reads the file again: one changed behind Wordsmith's back.</summary>
	public static void Reload() => read = null;

	private const string EditorSection = "editor";
	private const string WindowSection = "window";
	private const string SeenSection = "seen";
	private const string ToolbarsSection = "toolbars";

	//names without case, `=`, `;` comments, UTF-8 with no byte-order mark when it makes one
	private static readonly IniDialect Dialect = IniDialect.Sections(new IniRules());

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
		set => Write(WindowSection, Place(value));
	}

	/// <summary>
	///     The main window as it closes, and the language Wordsmith restarts in when
	///     that is why it closes, null when it is not: the close's one write.
	/// </summary>
	public static void KeepClosing(WindowPlace window, string? language = null)
		=> Commit(file => {
			if (language is not null) {
				Set(file, EditorSection, ("language", language));
			}
			Set(file, WindowSection, Place(window));
		});

	//the window's values; each null for none, so the section goes
	private static (string Key, string? Value)[] Place(WindowPlace? value) => [
		("width", value?.Width.ToString("0", CultureInfo.InvariantCulture)),
		("height", value?.Height.ToString("0", CultureInfo.InvariantCulture)),
		("state", value is null ? null : value.Value.Maximized ? "maximized" : "normal"),
	];

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
	public static void ForgetSeen() => Commit(file => file.RemoveSection(SeenSection));

	/// <summary>The line <c>[toolbars]</c> gives <paramref name="toolbar"/>, its entries by name; null when it has none, the toolbar as it ships, and empty for a toolbar emptied.</summary>
	public static string? Toolbar(string toolbar) => Read(ToolbarsSection).GetValueOrDefault(toolbar);

	/// <summary>Keeps each toolbar's line under <c>[toolbars]</c>, all in one write; a null line drops it.</summary>
	public static void KeepToolbars(IEnumerable<KeyValuePair<string, string?>> lines)
		=> Write(ToolbarsSection, [.. lines.Select(pair => (pair.Key, pair.Value))]);

	private static double? Number(Dictionary<string, string> values, string key)
		=> double.TryParse(values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number > 0 ? number : null;

	//a section's values, the last line naming a key winning, from the file as last read
	//or written. Lines above the first section are in none of these, so a config from
	//before the sections reads as empty; a file the driver won't read is no settings at
	//all, until the next write reads it again
	private static Dictionary<string, string> Read(string section) {
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (read is null) {
			try {
				using IniTransaction file = IniTransaction.Open(Path, Dialect);
				read = file.Document;
			}
			catch (Exception ex) when (ex is IniException or IOException or UnauthorizedAccessException) {
				read = IniDocument.New(Dialect);
			}
		}
		try {
			foreach (string key in read.Keys(section)) {
				values[key] = read.Get(section, key)!;
			}
		}
		catch (IniException) {
			//a file that is no text
		}
		return values;
	}

	//each change sets its key in its section where it stands, or after the section's last
	//value; a null one goes, and an empty one stays, empty (a toolbar emptied: SPEC: The
	//toolbar editor)
	private static void Write(string section, params (string Key, string? Value)[] changes) => Commit(file => Set(file, section, changes));

	private static void Set(IniTransaction file, string section, params (string Key, string? Value)[] changes) {
		foreach ((string key, string? value) in changes) {
			file.Set(section, key, value?.Trim());
		}
	}

	//one moment, one transaction, on the file as it now stands, which is then the file as
	//read. A config that won't write (read-only, held, a commit refused) keeps what it had,
	//and is read again: the setting is lost, never the close it was saved on. One left
	//pending stands, and reads as written
	private static void Commit(Action<IniTransaction> edit) {
		IniDocument? written = null;
		try {
			using IniTransaction file = IniTransaction.Open(Path, Dialect);
			written = file.Document;
			edit(file);
			if (file.Document.IsNew && file.Document.IsEdited) {
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path) ?? "");
			}
			file.Commit();
		}
		catch (IniPendingException) {
		}
		catch (Exception ex) when (ex is IniException or IOException or UnauthorizedAccessException) {
			written = null;
		}
		read = written;
	}
}
