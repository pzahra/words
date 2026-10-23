namespace PatTech.Text.Ini;

/// <summary>
///     What a document says, as ini reads it: its sections in the order they first stand, each
///     its keys in the order they are first declared, the last of a key declared twice
///     winning. The section <c>""</c> is there while it has a field, or a <c>[]</c> header.
///     The check compares two of these.
/// </summary>
internal sealed class IniContent {
	private readonly StringComparer names;
	private readonly List<Section> order = [];
	private readonly Dictionary<string, Section> sections;

	internal IniContent(StringComparer names) {
		this.names = names;
		sections = new(names);
	}

	internal static IniContent Of(IEnumerable<IniElement> elements, StringComparer names) {
		var content = new IniContent(names);
		foreach (IniElement element in elements) {
			switch (element) {
				case IniHeader header:
					content.Ensure(header.Name);
					break;
				case IniField field:
					content.Ensure(field.Section).Set(field.Key, field.Value);
					break;
			}
		}
		return content;
	}

	internal IniContent Clone() {
		var clone = new IniContent(names);
		foreach (Section section in order) {
			Section copy = clone.Ensure(section.Name);
			foreach (string key in section.Keys) {
				copy.Set(key, section.Values[key]);
			}
		}
		return clone;
	}

	internal IReadOnlyList<string> Sections => [.. order.Select(section => section.Name)];

	internal bool Has(string section) => sections.ContainsKey(section);

	internal IReadOnlyList<string> Keys(string section) => sections.TryGetValue(section, out Section? found) ? [.. found.Keys] : [];

	internal string? Get(string section, string key)
		=> sections.TryGetValue(section, out Section? found) && found.Values.TryGetValue(key, out string? value) ? value : null;

	internal void Set(string section, string key, string value) => Ensure(section).Set(key, value);

	//a key gone; whether its section goes with it is the document's to say
	internal void Remove(string section, string key) {
		if (sections.TryGetValue(section, out Section? found)) {
			found.Remove(key);
		}
	}

	internal void Replace(string section, IEnumerable<KeyValuePair<string, string>> pairs) {
		Section found = Ensure(section);
		found.Clear();
		foreach ((string key, string value) in pairs) {
			found.Set(key, value);
		}
	}

	internal void RemoveSection(string section) {
		if (sections.Remove(section, out Section? found)) {
			order.Remove(found);
		}
	}

	internal void Rename(string section, string name) {
		if (sections.Remove(section, out Section? found)) {
			found.Name = name;
			sections[name] = found;
		}
	}

	//every section and pair that reads otherwise in actual
	internal List<IniDifference> Against(IniContent actual) {
		var differences = new List<IniDifference>();
		foreach (Section section in order) {
			if (!actual.sections.TryGetValue(section.Name, out Section? other)) {
				differences.Add(new(section.Name, null, section.Name, null));
				continue;
			}
			foreach (string key in section.Keys.Union(other.Keys, names)) {
				string? expected = section.Values.GetValueOrDefault(key);
				string? read = other.Values.GetValueOrDefault(key);
				if (expected != read) {
					differences.Add(new(section.Name, key, expected, read));
				}
			}
		}
		foreach (Section section in actual.order.Where(section => !sections.ContainsKey(section.Name))) {
			differences.Add(new(section.Name, null, null, section.Name));
		}
		return differences;
	}

	private Section Ensure(string name) {
		if (!sections.TryGetValue(name, out Section? section)) {
			section = new Section(name, names);
			sections[name] = section;
			order.Add(section);
		}
		return section;
	}

	private sealed class Section(string name, StringComparer names) {
		public string Name { get; set; } = name;
		public List<string> Keys { get; } = [];
		public Dictionary<string, string> Values { get; } = new(names);

		public void Set(string key, string value) {
			if (!Values.ContainsKey(key)) {
				Keys.Add(key);
			}
			Values[key] = value;
		}

		public bool Remove(string key) {
			if (!Values.Remove(key)) {
				return false;
			}
			Keys.RemoveAt(Keys.FindIndex(known => names.Equals(known, key)));
			return true;
		}

		public void Clear() {
			Keys.Clear();
			Values.Clear();
		}
	}
}
