using System.Text;
using PatTech.Text.Ini;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     Save as a patch (the editor's SPEC: Save as a patch): a file's document, as it was read,
	///     edited through the words dialect until it reads as the text the writer writes for the
	///     file's model, so every line the edits do not reach stays as it was. A key renamed is its
	///     section renamed; a key gone, its section removed; a key whose fields differ, those
	///     fields set or removed; a new key, its section added where the writer would put it; the
	///     language table, its fields set, or replaced where it holds other fields; keys out of the
	///     writer's order, moved; and each comment run that reads otherwise, set, added or removed.
	///     Then the document must read through Core as the canonical text does.
	/// </summary>
	internal static class WordsPatch {
		/// <summary>
		///     Edits <paramref name="document"/> toward <paramref name="canonical"/>: true when it
		///     then reads as that does, in every key's fields and parameters, the language table,
		///     the default's language, the settings references, every comment run, the parser's
		///     gripes and the tree a load builds; false when an edit is refused or it reads
		///     otherwise, the document left part edited for the caller to replace whole.
		/// </summary>
		/// <param name="document">The file as it was read, which takes the edits.</param>
		/// <param name="canonical">The text the writer writes for the file's model.</param>
		/// <param name="renames">Each key renamed since the document was read, from its name then to its name now.</param>
		public static bool Apply(IniDocument document, string canonical, IReadOnlyList<(string From, string To)> renames) {
			var wanted = new Wanted(canonical);
			try {
				//placed one by one, checked once
				document.Batch(() => {
					Rename(document, renames);
					Keys(document, wanted);
					Table(document, wanted);
					Order(document, wanted);
					Comments(document, wanted);
				});
			}
			catch (Exception refused) when (refused is IniException or ArgumentException) {
				return false;
			}
			return Reads(document, wanted);
		}

		//the canonical text as Core and the driver read it, never encoded: a character no
		//encoding writes reaches the file's own, which refuses it
		private sealed class Wanted(string text) {
			public WordsReading Reading { get; } = WordsReading.Of(text);
			public IniDocument Document { get; } = Read(text);
			public Dictionary<string, Dictionary<string, string>> Keys => keys ??= WordsDialect.Flatten(Reading.Words.WordKeys);
			private Dictionary<string, Dictionary<string, string>>? keys;

			private static IniDocument Read(string text) {
				IniDocument document = IniDocument.New(WordsDialect.Instance);
				document.Replace(text, new UTF8Encoding(false));
				return document;
			}
		}

		//each renamed key's section renamed while the name it had stands and the one it has is
		//free, parents first; one onto a name another rename frees waits for it. A section
		//whose name a renamed key takes, and whose own key is no rename's, is a key gone, and
		//goes first; renames that wait on each other, a swap, go one through a name of none
		private static void Rename(IniDocument document, IReadOnlyList<(string From, string To)> renames) {
			List<(string From, string To)> waiting = [.. renames
				.Where(rename => rename.From != rename.To && document.HasSection(rename.From))
				.OrderBy(rename => rename.From.Count(c => c == '.'))];
			foreach (var (_, to) in waiting.Where(rename => document.HasSection(rename.To) && !waiting.Any(other => other.From == rename.To)).ToList()) {
				document.RemoveSection(to);
			}
			while (waiting.Count > 0) {
				bool renamed = false;
				foreach (var rename in waiting.ToList()) {
					if (!document.HasSection(rename.To)) {
						document.RenameSection(rename.From, rename.To);
						waiting.Remove(rename);
						renamed = true;
					}
				}
				if (!renamed) {
					var (from, to) = waiting[0];
					string free = Free(document, from);
					document.RenameSection(from, free);
					waiting[0] = (free, to);
				}
			}
		}

		//a name no section has, beside one that does
		private static string Free(IniDocument document, string name) {
			for (int count = 1; ; count++) {
				string free = $"{name}-renaming-{count}";
				if (!document.HasSection(free)) {
					return free;
				}
			}
		}

		//keys gone removed; keys whose fields read otherwise set field by field; new keys added
		//whole, in the canonical order, so each finds the siblings before it
		private static void Keys(IniDocument document, Wanted wanted) {
			var have = WordsDialect.Flatten(WordsReading.Of(document.Text).Words.WordKeys);
			foreach (string gone in have.Keys.Where(name => !wanted.Keys.ContainsKey(name))) {
				document.RemoveSection(gone);
			}
			foreach (string name in wanted.Reading.Order) {
				if (!wanted.Keys.TryGetValue(name, out var fields)) {
					continue;
				}
				if (!have.TryGetValue(name, out var had)) {
					document.ReplaceSection(name, Pairs(wanted.Document, name));
				}
				else if (!Same(had, fields)) {
					IReadOnlyList<string> keys = wanted.Document.Keys(name);
					foreach (string key in keys) {
						if (wanted.Document.Get(name, key) is { } value && document.Get(name, key) != value) {
							document.Set(name, key, value);
						}
					}
					foreach (string key in document.Keys(name).Where(key => !keys.Contains(key, WordsFieldNames.Instance)).ToList()) {
						document.Remove(name, key);
					}
				}
			}
		}

		//the language table, the default's language and the settings references: their fields set
		//and removed, and where the table then reads in another order, replaced
		private static void Table(IniDocument document, Wanted wanted) {
			if (SameHead(WordsReading.Of(document.Text).Words, wanted.Reading.Words)) {
				return;
			}
			List<KeyValuePair<string, string>> pairs = Pairs(wanted.Document, "");
			foreach (var (key, value) in pairs) {
				document.Set("", key, value);
			}
			foreach (string key in document.Keys("").Where(key => !pairs.Any(pair => WordsFieldNames.Instance.Equals(pair.Key, key))).ToList()) {
				document.Remove("", key);
			}
			if (!SameHead(WordsReading.Of(document.Text).Words, wanted.Reading.Words)) {
				document.ReplaceSection("", pairs);
			}
		}

		//keys out of the canonical order moved after the key before them there; the longest run of
		//keys standing in that order stays, the first key among it where nothing heads the file
		private static void Order(IniDocument document, Wanted wanted) {
			WordsReading have = WordsReading.Of(document.Text);
			if (Walk(have, comments: false).SequenceEqual(Walk(wanted.Reading, comments: false))) {
				return;
			}
			List<string> want = Keyed(wanted.Reading), stand = Keyed(have);
			if (!new HashSet<string>(stand).SetEquals(want)) {
				return;
			}
			var rank = want.Select((name, at) => (name, at)).ToDictionary(pair => pair.name, pair => pair.at);
			int[] ranks = [.. stand.Select(name => rank[name])];
			HashSet<int> kept = Rising(ranks);
			if (!kept.Contains(0) && !document.HasSection("")) {
				kept = [0, .. Rising(ranks[(Array.IndexOf(ranks, 0) + 1)..])];
			}
			for (int at = 0; at < want.Count; at++) {
				if (!kept.Contains(at)) {
					document.MoveSection(want[at], at == 0 ? "" : want[at - 1]);
				}
			}
		}

		//the values of a longest rising run through ranks, its members not necessarily adjacent
		private static HashSet<int> Rising(int[] ranks) {
			int[] tails = new int[ranks.Length], before = new int[ranks.Length];
			int length = 0;
			for (int at = 0; at < ranks.Length; at++) {
				int low = 0, high = length;
				while (low < high) {
					int middle = (low + high) / 2;
					if (ranks[tails[middle]] < ranks[at]) {
						low = middle + 1;
					}
					else {
						high = middle;
					}
				}
				before[at] = low > 0 ? tails[low - 1] : -1;
				tails[low] = at;
				length = Math.Max(length, low + 1);
			}
			HashSet<int> run = [];
			for (int at = length > 0 ? tails[length - 1] : -1; at >= 0; at = before[at]) {
				run.Add(ranks[at]);
			}
			return run;
		}

		//each comment run that reads otherwise: the blocks Core reads into it, the first set to it
		//and the rest removed, or, with none, it added above its block's first header, the table's
		//first field for the preamble, or at the end for the trailer. A run is named by its block,
		//"" for the preamble and null for the trailer: Core names no block ""
		private static void Comments(IniDocument document, Wanted wanted) {
			WordsParserToLocalizationProvider have = WordsReading.Of(document.Text).Words, want = wanted.Reading.Words;
			foreach (string? run in (string?[])["", .. have.BlockComments.Keys.Union(want.BlockComments.Keys), null]) {
				string now = RunOf(want, run);
				if (RunOf(have, run) == now) {
					continue;
				}
				List<string> lines = now.Length == 0 ? [] : [.. now.Split('\n').Select(line => ";" + line)];
				List<IniComment> blocks = Blocks(document, run);
				if (blocks.Count > 0) {
					for (int at = blocks.Count - 1; at > 0; at--) {
						document.RemoveComment(blocks[at]);
					}
					document.SetComment(blocks[0], lines);
				}
				else if (lines.Count > 0) {
					IniElement? above = run switch {
						null => null,
						"" => document.Elements.OfType<IniField>().FirstOrDefault(field => field.Section.Length == 0),
						_ => document.Elements.OfType<IniHeader>().FirstOrDefault(header => header.Name == run),
					};
					if (above is not null || run is null) {
						document.AddComment(above, lines);
					}
				}
			}
		}

		private static string RunOf(WordsParserToLocalizationProvider words, string? run) => run switch {
			null => words.Trailer,
			"" => words.Preamble,
			_ => words.BlockComments.GetValueOrDefault(run, ""),
		};

		//the comment blocks Core reads into a run, as Core reads them: those before a field, the
		//preamble's above the first header and that field's block's below it; those before a
		//header, its block's; those after the last of either, the trailer's
		private static List<IniComment> Blocks(IniDocument document, string? run) {
			List<IniComment> pending = [], found = [];
			bool headed = false;
			foreach (IniElement element in document.Elements) {
				string? taker = element switch {
					IniHeader header => header.Name,
					IniField when !headed => "",
					IniField field when field.Section.Length > 0 => field.Section,
					_ => null,
				};
				if (element is IniComment block) {
					pending.Add(block);
				}
				else if (taker is not null) {
					if (taker == run) {
						found.AddRange(pending);
					}
					pending.Clear();
				}
				headed |= element is IniHeader;
			}
			return run is null ? pending : found;
		}

		//whether the document reads through Core as the canonical text does
		private static bool Reads(IniDocument document, Wanted wanted) {
			WordsReading got = WordsReading.Of(document.Text), want = wanted.Reading;
			var keys = WordsDialect.Flatten(got.Words.WordKeys);
			return keys.Count == wanted.Keys.Count
				&& keys.All(pair => wanted.Keys.TryGetValue(pair.Key, out var fields) && Same(pair.Value, fields)
					&& got.Words.WordKeys[pair.Key].Parameters.Select(parameter => parameter.Key).SequenceEqual(want.Words.WordKeys[pair.Key].Parameters.Select(parameter => parameter.Key)))
				&& SameHead(got.Words, want.Words)
				&& got.Words.Preamble == want.Words.Preamble
				&& got.Words.Trailer == want.Words.Trailer
				&& got.Words.BlockComments.Count == want.Words.BlockComments.Count
				&& got.Words.BlockComments.All(pair => want.Words.BlockComments.GetValueOrDefault(pair.Key) == pair.Value)
				&& got.Words.Errors.Order(StringComparer.Ordinal).SequenceEqual(want.Words.Errors.Order(StringComparer.Ordinal))
				&& Walk(got, comments: true).SequenceEqual(Walk(want, comments: true));
		}

		//the language table, the default's language and the settings references alike
		private static bool SameHead(WordsParserToLocalizationProvider have, WordsParserToLocalizationProvider want)
			=> WordsDialect.SameTable(have, want)
				&& have.DefaultLanguage == want.DefaultLanguage
				&& have.Settings == want.Settings
				&& have.LanguageSettings.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(want.LanguageSettings.OrderBy(pair => pair.Key, StringComparer.Ordinal));

		private static bool Same(Dictionary<string, string> have, Dictionary<string, string> want)
			=> have.Count == want.Count && have.All(pair => want.TryGetValue(pair.Key, out string? text) && text == pair.Value);

		//a section's fields as the driver reads them, in order
		private static List<KeyValuePair<string, string>> Pairs(IniDocument document, string section)
			=> [.. document.Keys(section).Select(key => new KeyValuePair<string, string>(key, document.Get(section, key)!))];

		//the keys a reading holds, bare groups left out, in the order their blocks first stand
		private static List<string> Keyed(WordsReading reading)
			=> [.. reading.Order.Where(name => reading.Words.WordKeys.TryGetValue(name, out WordsKey? key) && !key.IsEmpty())];

		//the tree a load builds from a reading, node by node in the order the writer walks it: each
		//node's name, and with comments, each comment node's text
		private static List<string> Walk(WordsReading reading, bool comments) {
			KeyTreeNode root = KeyTree.Build(Keyed(reading).Select(name => reading.Words.WordKeys[name]),
				comments ? reading.Words.BlockComments : new Dictionary<string, string>(), comments ? reading.Words.Trailer : "");
			List<string> walk = [];
			Visit(root);
			return walk;

			void Visit(KeyTreeNode node) {
				foreach (KeyTreeNode child in node.Children) {
					walk.Add(child is CommentTreeNode comment ? $"{comment.FullLabel}\n{comment.Text}" : child.FullLabel);
					Visit(child);
				}
			}
		}
	}
}
