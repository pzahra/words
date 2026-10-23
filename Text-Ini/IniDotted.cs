namespace PatTech.Text.Ini;

/// <summary>
///     How the dotted dialect's chains are cut (SPEC.md, <i>Cutting</i>): asked, for a group an
///     edit touched, whether it should head a chain of its own, a bare base its sections chain off.
/// </summary>
public interface ICutStrategy {
	/// <summary>
	///     Whether <paramref name="group"/>, a name no header has, should head its own chain:
	///     <paramref name="sections"/> are the names of every section holding keys, a bare base
	///     being structure the strategy decides, compared as <paramref name="names"/> compares them.
	/// </summary>
	bool Cuts(string group, IReadOnlyCollection<string> sections, StringComparer names);
}

/// <summary>
///     The cut a hand-author makes, and the one <c>words.ini</c>'s writer makes: a group no
///     section names heads its own chain once it gathers <c>minimumKeys</c> sections that would
///     chain off it. A section beyond a deeper group that heads its own counts there, not here,
///     and a group one section names never cuts, its own header basing its chain already.
/// </summary>
public sealed class GroupCuts : ICutStrategy {
	private readonly int minimumKeys;

	/// <param name="minimumKeys">Sections a group must gather before it pays for its header; at least 1.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumKeys"/> is less than 1.</exception>
	public GroupCuts(int minimumKeys = 2) {
		ArgumentOutOfRangeException.ThrowIfLessThan(minimumKeys, 1);
		this.minimumKeys = minimumKeys;
	}

	/// <inheritdoc/>
	public bool Cuts(string group, IReadOnlyCollection<string> sections, StringComparer names) {
		ArgumentNullException.ThrowIfNull(group);
		ArgumentNullException.ThrowIfNull(sections);
		ArgumentNullException.ThrowIfNull(names);
		return new Count(sections, names, minimumKeys).Cuts(group);
	}

	//one question's counts over one set of names
	private sealed class Count {
		private readonly HashSet<string> sections;
		private readonly Dictionary<string, List<string>> children;
		private readonly Dictionary<string, int> chaining;
		private readonly int minimumKeys;

		public Count(IReadOnlyCollection<string> names, StringComparer comparer, int minimumKeys) {
			this.minimumKeys = minimumKeys;
			sections = new(names, comparer);
			children = new(comparer);
			chaining = new(comparer);
			//every group a name passes through, with the groups and sections right under it
			var seen = new HashSet<string>(comparer);
			foreach (string name in names) {
				int dot = name.IndexOf('.');
				string parent = "";
				while (true) {
					string child = dot < 0 ? name : name[..dot];
					if (seen.Add(child)) {
						if (!children.TryGetValue(parent, out List<string>? under)) {
							children[parent] = under = [];
						}
						under.Add(child);
					}
					if (dot < 0) {
						break;
					}
					parent = child;
					dot = name.IndexOf('.', dot + 1);
				}
			}
		}

		public bool Cuts(string group) => !sections.Contains(group) && Chaining(group) >= minimumKeys;

		//the sections under group that would chain off its header: each child that is one, and
		//those under a child that heads no chain of its own
		private int Chaining(string group) {
			if (!chaining.TryGetValue(group, out int count)) {
				foreach (string child in children.GetValueOrDefault(group) ?? []) {
					if (sections.Contains(child)) {
						count++;
					}
					if (!Cuts(child)) {
						count += Chaining(child);
					}
				}
				chaining[group] = count;
			}
			return count;
		}
	}
}

/// <summary>
///     The dotted dialect (SPEC.md, <i>Its dialect</i>, <i>Cutting</i>): section names in dotted
///     segments, a <c>[.child]</c> header chaining off the last full one (<c>[group.a]</c> then
///     <c>[.b]</c> is <c>group.a.b</c>), and a cut strategy that decides how a group an edit
///     touched is cut. A section new to the document goes beside its nearest sibling: into the
///     chain whose base it extends, else after the chain holding the section sharing the most
///     leading segments with it, else after the last block. A header that bases a chain stays,
///     bare, when its section goes, and the check passes a bare base either way.
/// </summary>
public class IniDottedDialect : IniDialect {
	/// <summary>Creates a dotted dialect over <paramref name="rules"/>, cut by <paramref name="cuts"/>, <see cref="GroupCuts"/> when none is given.</summary>
	protected internal IniDottedDialect(IniRules rules, ICutStrategy? cuts) : base(rules) => Cuts = cuts ?? new GroupCuts();

	/// <summary>How its chains are cut.</summary>
	public ICutStrategy Cuts { get; }

	internal override (string Name, bool Chained) Resolve(string written, ref string chainBase) {
		if (written.StartsWith('.')) {
			return (chainBase + written, true);
		}
		chainBase = written;
		return (written, false);
	}

	/// <summary>
	///     Where <paramref name="section"/>, new to the document, goes: after the block it follows
	///     (<see cref="Follows"/>), past a blank line after it and with one of its own before what
	///     follows, in a file spaced so; the head at the head.
	/// </summary>
	protected internal override IniPlacement PlaceSection(IniDocument document, string section, IReadOnlyList<KeyValuePair<string, string>> pairs) {
		ArgumentNullException.ThrowIfNull(document);
		if (section.Length == 0) {
			return base.PlaceSection(document, section, pairs);
		}
		(int end, IniHeader? follows) = Follows(document, section);
		int count = document.LineCount;
		if (follows is null) {
			return new IniPlacement(count, BlankBefore: count > 0 && !document.IsBlank(count - 1));
		}
		bool blank = end + 1 < count && document.IsBlank(end + 1);
		int at = blank ? end + 2 : end + 1;
		return new IniPlacement(at, BlankAfter: blank && at < count && !document.IsBlank(at));
	}

	/// <summary>
	///     The block a section new to <paramref name="document"/> follows, by its last line (its
	///     header's, or its last field's): in the chain whose base <paramref name="section"/>
	///     extends, the deepest such, the block of the header there sharing the most leading
	///     segments with it, the last of them; else the end of the chain holding the section
	///     sharing the most leading segments with it, the last of them, so the comments above the
	///     next block stay with it; else the last chain's. No header at all, the last line and none.
	/// </summary>
	protected (int End, IniHeader? Header) Follows(IniDocument document, string section) {
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(section);
		IReadOnlyList<IniElement> all = document.Elements;
		List<IniHeader> headers = [.. all.OfType<IniHeader>()];
		if (headers.Count == 0) {
			return (document.LineCount - 1, null);
		}
		//each header's chain: the index of the full header it runs from
		var bases = new int[headers.Count];
		for (int at = 0; at < headers.Count; at++) {
			bases[at] = !headers[at].IsChained || at == 0 ? at : bases[at - 1];
		}
		int chained = -1, longest = 0, most = 0;
		for (int at = 0; at < headers.Count; at++) {
			IniHeader based = headers[bases[at]];
			if (based.IsChained || !ChainsOff(based.Name, section) || based.Name.Length < longest) {
				continue;
			}
			int shared = Shared(section, headers[at].Name);
			if (based.Name.Length > longest || shared >= most) {
				(chained, longest, most) = (at, based.Name.Length, shared);
			}
		}
		if (chained >= 0) {
			return (BlockEnd(all, headers, chained, chained), headers[chained]);
		}
		int nearest = headers.Count - 1;
		most = 0;
		for (int at = 0; at < headers.Count; at++) {
			int shared = Shared(section, headers[at].Name);
			if (shared > 0 && shared >= most) {
				(nearest, most) = (at, shared);
			}
		}
		int last = nearest + 1;
		while (last < headers.Count && headers[last].IsChained) {
			last++;
		}
		return (BlockEnd(all, headers, bases[nearest], last - 1), headers[last - 1]);
	}

	/// <summary>Whether <paramref name="name"/> chains off <paramref name="chainBase"/>: under it, a dot after it.</summary>
	protected bool ChainsOff(string chainBase, string name) {
		ArgumentNullException.ThrowIfNull(chainBase);
		ArgumentNullException.ThrowIfNull(name);
		return chainBase.Length > 0 && name.Length > chainBase.Length + 1 && name[chainBase.Length] == '.' && Names.Equals(name[..chainBase.Length], chainBase);
	}

	//the check passes a bare base either way: a section with no keys that a section's name extends
	internal override List<IniDifference> Check(IniContent expected, IniContent actual, IniContent before) {
		List<IniDifference> found = base.Check(expected, actual, before);
		return [.. found.Where(difference => !(difference.Key is null && Keyless(difference.Section) && Groups(difference.Section)))];

		bool Keyless(string section) => expected.Keys(section).Count == 0 && actual.Keys(section).Count == 0;

		bool Groups(string section) => expected.Sections.Concat(actual.Sections).Concat(before.Sections).Any(name => ChainsOff(section, name));
	}

	//the last line of the blocks under headers first..last: the last header's, or a field's last
	private static int BlockEnd(IReadOnlyList<IniElement> all, List<IniHeader> headers, int first, int last) {
		int end = headers[last].Line;
		int under = -1;
		foreach (IniElement element in all) {
			if (element is IniHeader) {
				under++;
			}
			else if (element is IniField field && under >= first && under <= last) {
				end = Math.Max(end, field.Line + field.Count - 1);
			}
		}
		return end;
	}

	//the dotted segments two names start with alike
	private int Shared(string a, string b) {
		string[] left = a.Split('.'), right = b.Split('.');
		int count = 0;
		while (count < left.Length && count < right.Length && Names.Equals(left[count], right[count])) {
			count++;
		}
		return count;
	}
}
