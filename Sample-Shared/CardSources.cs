using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Sample_Shared;

/// <summary>
///     One file's part of a card's How (SPEC: How each card is made): the file's name, and
///     the lines cut from it, as written.
/// </summary>
public sealed record CardSource(string File, string Text) {
	/// <summary>Reads a whole file, its line breaks made <c>\n</c>.</summary>
	public static CardSource Read(string file, Stream stream) {
		using var reader = new StreamReader(stream);
		return new(file, reader.ReadToEnd().Replace("\r\n", "\n"));
	}

	/// <summary>Reads every resource of <paramref name="assembly"/> whose name starts with <paramref name="prefix"/>, named for what follows it.</summary>
	public static IReadOnlyList<CardSource> ReadAll(Assembly assembly, string prefix)
		=> [.. assembly.GetManifestResourceNames()
			.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
			.Order(StringComparer.Ordinal)
			.Select(name => Read(name[prefix.Length..], assembly.GetManifestResourceStream(name)!))];
}

/// <summary>
///     What each card's How shows, read from the real files so it cannot drift from what runs:
///     the card's markup, cut from whichever page holds it between the marker comments named
///     for the card, and the blocks of each words file that its keys look up.
/// </summary>
/// <param name="pages">The page markup files, whole.</param>
/// <param name="words">The words files, whole: <c>sample.ini</c>, then the sample's <c>framework.ini</c>.</param>
public sealed partial class CardSources(IReadOnlyList<CardSource> pages, IReadOnlyList<CardSource> words) {
	private readonly Lazy<IReadOnlyList<IniFile>> parsed = new(() => [.. words.Select(IniFile.Parse)]);

	/// <summary>
	///     The card <paramref name="key"/>'s How: its markup, then the blocks of each words file
	///     for the key and the keys beneath it, for each of <paramref name="moreKeys"/> and the
	///     keys beneath those, and for whatever those blocks reference with <c>{>key}</c> or
	///     <c>{$constant}</c>. A file with none of them is left out.
	/// </summary>
	/// <param name="key">The card's key.</param>
	/// <param name="moreKeys">Comma-separated keys the card looks up in code, such as a <c>[Words]</c> enum's.</param>
	public IReadOnlyList<CardSource> How(string key, string? moreKeys) {
		List<CardSource> how = [];
		foreach (var page in pages) {
			if (Markup(page.Text, key) is { } markup) {
				how.Add(new(page.File, markup));
				break;
			}
		}
		string[] prefixes = [key, .. moreKeys?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []];
		var wanted = Wanted(prefixes);
		foreach (var file in parsed.Value) {
			//the card's own words first, then what they reach, each in file order
			var shown = file.Blocks.Where(block => wanted.Contains(block.Key)).OrderBy(block => IsUnder(block.Key, prefixes) ? 0 : 1);
			if (file.Cut([.. shown]) is { } cut) {
				how.Add(new(file.Name, cut));
			}
		}
		return how;
	}

	private static bool IsUnder(string key, string[] prefixes)
		=> prefixes.Any(prefix => key == prefix || key.StartsWith(prefix + ".", StringComparison.Ordinal));

	/// <summary>The lines between <c>&lt;!-- card: key --&gt;</c> and the next <c>&lt;!-- /card --&gt;</c>, their common indent taken off.</summary>
	public static string? Markup(string page, string key) {
		var lines = page.Split('\n');
		int start = Array.FindIndex(lines, line => line.Trim() == $"<!-- card: {key} -->");
		int end = start < 0 ? -1 : Array.FindIndex(lines, start + 1, line => line.Trim() == "<!-- /card -->");
		if (end < 0) {
			return null;
		}
		var body = lines[(start + 1)..end];
		int indent = body.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
		return string.Join('\n', body.Select(line => line.Length > indent ? line[indent..] : line.TrimStart()));
	}

	//every key to show: those under a prefix, and what they reference, followed to the end
	private HashSet<string> Wanted(string[] prefixes) {
		var blocks = parsed.Value.SelectMany(file => file.Blocks.Select(block => (File: file, Block: block))).ToList();
		var wanted = new HashSet<string>(blocks.Where(each => IsUnder(each.Block.Key, prefixes)).Select(each => each.Block.Key));
		var queue = new Queue<string>(wanted);
		while (queue.TryDequeue(out var key)) {
			foreach (var (file, block) in blocks.Where(each => each.Block.Key == key)) {
				foreach (var referenced in file.References(block)) {
					if (wanted.Add(referenced)) {
						queue.Enqueue(referenced);
					}
				}
			}
		}
		return wanted;
	}

	/// <summary>
	///     One block of a words file: its full key, the base a <c>[.name]</c> header nests under,
	///     and its lines — from the comments right above its header to its last field.
	/// </summary>
	private sealed record IniBlock(string Key, string Base, bool IsRelative, int Start, int Header, int End);

	/// <summary>A words file, split into its lines and the blocks they make.</summary>
	private sealed partial record IniFile(string Name, string[] Lines, IReadOnlyList<IniBlock> Blocks) {
		public static IniFile Parse(CardSource file) {
			var lines = file.Text.Split('\n');
			List<(int Line, string Key, string Base, bool Relative)> headers = [];
			string baseKey = "";
			bool continued = false;
			for (int i = 0; i < lines.Length; i++) {
				//what Words' parser does: a continued value takes the next line whatever it looks like
				if (continued) {
					continued = ContinuedPattern().IsMatch(lines[i]);
				}
				else if (HeaderPattern().Match(lines[i]) is { Success: true } header) {
					string name = header.Groups["name"].Value;
					bool relative = name.StartsWith('.');
					string full = relative ? baseKey + name : baseKey = name;
					headers.Add((i, full, relative ? baseKey : full, relative));
				}
				else if (FieldPattern().Match(lines[i]) is { Success: true } field) {
					continued = ContinuedPattern().IsMatch(field.Groups["text"].Value);
				}
			}
			List<IniBlock> blocks = [];
			for (int k = 0; k < headers.Count; k++) {
				var (line, key, @base, relative) = headers[k];
				//the comments right above a header are the block's own
				int start = line;
				while (start > 0 && IsComment(lines[start - 1]) && (k == 0 || start - 1 > headers[k - 1].Line)) {
					start--;
				}
				//and the blank lines and comments at its end belong to what follows
				int end = (k + 1 < headers.Count ? headers[k + 1].Line : lines.Length) - 1;
				while (end > line && (lines[end].Trim().Length == 0 || IsComment(lines[end]))) {
					end--;
				}
				blocks.Add(new(key, @base, relative, start, line, end));
			}
			return new(file.File, lines, blocks);
		}

		/// <summary>The keys a block's values reference: <c>{>key}</c>, <c>{>.sub}</c> beneath the block's key, and <c>{$constant}</c>; <c>{{</c> escapes.</summary>
		public IEnumerable<string> References(IniBlock block) {
			for (int i = block.Header + 1; i <= block.End; i++) {
				if (IsComment(Lines[i])) {
					continue;
				}
				foreach (Match reference in ReferencePattern().Matches(Lines[i])) {
					string name = reference.Groups["key"].Value;
					yield return reference.Groups["kind"].Value == "$" ? "$" + name
						: name.StartsWith('.') ? block.Key + name
						: name;
				}
			}
		}

		//the blocks as written, in the order given; blocks not next to each other in the file
		//are a blank line apart, and a [.name] header shown away from its own base is written
		//out in full so it still means it
		public string? Cut(IReadOnlyList<IniBlock> blocks) {
			if (blocks.Count == 0) {
				return null;
			}
			var text = new StringBuilder();
			string shownBase = "";
			int last = -1;
			foreach (var block in blocks) {
				if (last >= 0) {
					bool adjacent = block.Start > last && Lines[(last + 1)..block.Start].All(line => line.Trim().Length == 0);
					for (int i = adjacent ? last + 1 : block.Start - 1; i < block.Start; i++) {
						text.Append('\n');
					}
				}
				for (int i = block.Start; i <= block.End; i++) {
					string line = Lines[i];
					if (i == block.Header) {
						if (!block.IsRelative) {
							shownBase = block.Key;
						}
						else if (shownBase != block.Base) {
							line = $"[{block.Key}]";
							shownBase = block.Key;
						}
					}
					text.Append(line).Append('\n');
				}
				last = block.End;
			}
			return text.ToString().TrimEnd('\n');
		}

		private static bool IsComment(string line) => line.TrimStart().StartsWith(';');

		[GeneratedRegex(@"^\[(?<name>[^]]+)\]")]
		private static partial Regex HeaderPattern();
		[GeneratedRegex(@"^\w+(-\w+(?:-\w+)?)?\s*[:=]\s*(?<text>.*)")]
		private static partial Regex FieldPattern();
		[GeneratedRegex(@"^([\\_].|[^\\_])*[\\_]$")]
		private static partial Regex ContinuedPattern();
		[GeneratedRegex(@"(?<!\{)\{(?<kind>[>$])(?<key>[^{}]+)\}")]
		private static partial Regex ReferencePattern();
	}
}
