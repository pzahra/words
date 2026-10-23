namespace PatTech.Text.Ini;

/// <summary>
///     Where lines new to the document go (SPEC.md, <i>Where an edit lands</i>): the line they go
///     before, and whether a blank line goes before them or after them.
/// </summary>
public sealed record IniPlacement(int Line, bool BlankBefore = false, bool BlankAfter = false);

/// <summary>A document as it reads at one moment: its text, its elements and its sections' pairs.</summary>
public sealed class IniSnapshot {
	private readonly IniContent content;

	internal IniSnapshot(string text, IReadOnlyList<IniElement> elements, IniContent content) {
		Text = text;
		Elements = elements;
		this.content = content;
	}

	/// <summary>Its text, every line with its break.</summary>
	public string Text { get; }
	/// <summary>Its elements, in order; lines it does not recognise are none.</summary>
	public IReadOnlyList<IniElement> Elements { get; }
	/// <summary>Its sections, in the order they first stand.</summary>
	public IReadOnlyList<string> Sections => content.Sections;

	/// <summary><paramref name="section"/>'s keys, in the order they are first declared.</summary>
	public IReadOnlyList<string> Keys(string section) => content.Keys(section);

	/// <summary>The value of <paramref name="key"/> in <paramref name="section"/>; null for none.</summary>
	public string? Get(string section, string key) => content.Get(section, key);
}

/// <summary>
///     An edit as the check sees it (SPEC.md, <i>Checked before it lands</i>): the document before
///     it and after it, and every section's pairs as the edit means to leave them.
/// </summary>
public sealed class IniChange {
	private readonly IniContent expected;

	internal IniChange(IniSnapshot before, IniSnapshot after, IniContent expected) {
		Before = before;
		After = after;
		this.expected = expected;
	}

	/// <summary>The document before the edit.</summary>
	public IniSnapshot Before { get; }
	/// <summary>The document as the edit leaves it.</summary>
	public IniSnapshot After { get; }
	/// <summary>The sections the edit means to leave, in their order.</summary>
	public IReadOnlyList<string> ExpectedSections => expected.Sections;

	/// <summary>The keys the edit means <paramref name="section"/> to hold.</summary>
	public IReadOnlyList<string> ExpectedKeys(string section) => expected.Keys(section);

	/// <summary>The value the edit means <paramref name="key"/> in <paramref name="section"/> to hold; null for none.</summary>
	public string? Expected(string section, string key) => expected.Get(section, key);
}
