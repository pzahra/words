using System.Globalization;
using System.Text.RegularExpressions;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The shape of a key tree the writer walks to decide which blocks go into a
	///     file, and in what order. The editor's tree nodes implement this; tests can
	///     use any structure that answers the same three questions.
	/// </summary>
	public interface IKeyTreeNode {
		/// <summary>The full dotted key this node stands for, e.g. <c>file.group.key</c>.</summary>
		string FullLabel { get; }
		/// <summary>The nodes below this one, in write order.</summary>
		IEnumerable<IKeyTreeNode> Children { get; }
	}

	/// <summary>
	///     A freeform comment run standing on its own in the tree. The writer
	///     emits it as <c>;</c> lines at its position in the walk, so whatever
	///     block follows becomes its anchor on the next load — move keys around
	///     it, or delete them, and the comment stays where it stands.
	/// </summary>
	public interface ICommentNode : IKeyTreeNode {
		string Text { get; }
	}

	/// <summary>
	///     Decides where the writer resets the dot-relative base. A cut node is
	///     written as a full <c>[path]</c> header — bare, if it has no key of its
	///     own — and blocks after it that extend the cut come out as one
	///     <c>[.suffix]</c> header each. The writer chains regardless of strategy:
	///     a block extending the current base is written dot-relative and one that
	///     doesn't forces a full header, so a strategy can only add cuts, never
	///     break the file. Mind that a bare header becomes an empty key on reload;
	///     cut at a keyless node only when the shortened descendants are worth it.
	/// </summary>
	public interface ICutStrategy {
		/// <summary>
		///     True to make <paramref name="node"/> a new base. <paramref name="depth"/>
		///     is 0 for the file node's immediate children; look at
		///     <see cref="IKeyTreeNode.Children"/> to weigh the subtree's shape.
		/// </summary>
		bool Cuts(IKeyTreeNode node, int depth);
	}

	/// <summary>
	///     The writer's default strategy: cuts at keyless group nodes whose subtree
	///     carries at least <paramref name="minimumKeys"/> keyed blocks — the shape a
	///     hand-author writes as a bare <c>[group]</c> header followed by
	///     <c>[.child]</c> blocks. Keyed nodes never cut (their own header re-bases
	///     the chain already), and keys beyond a deeper cut don't count here: they
	///     chain off that base, not this one. A single keyed descendant isn't worth
	///     the bare header (which reloads as an empty key) — it keeps its full header.
	/// </summary>
	/// <param name="keys">Every key the writer is working from, block key to data.</param>
	/// <param name="minimumKeys">Keyed blocks a group must gather before it pays for its header.</param>
	public sealed class GroupCuts(IReadOnlyDictionary<string, WordsKey> keys, int minimumKeys = 2) : ICutStrategy {
		private readonly Dictionary<IKeyTreeNode, int> counted = [];

		/// <inheritdoc/>
		public bool Cuts(IKeyTreeNode node, int depth)
			=> !keys.ContainsKey(node.FullLabel) && ChainingKeys(node) >= minimumKeys;

		/// <summary>Keyed blocks below <paramref name="node"/> that would chain off its header.</summary>
		private int ChainingKeys(IKeyTreeNode node) {
			if (!counted.TryGetValue(node, out int count)) {
				foreach (var child in node.Children) {
					if (child is ICommentNode) {
						continue;
					}
					if (keys.ContainsKey(child.FullLabel)) {
						count++;
					}
					if (!Cuts(child, 0)) {
						count += ChainingKeys(child);
					}
				}
				counted[node] = count;
			}
			return count;
		}
	}

	public sealed class IniWriter(TextWriter writer, ICutStrategy? cutStrategy = null) : IDisposable, IAsyncDisposable {
		private sealed class ChainOnly : ICutStrategy {
			public bool Cuts(IKeyTreeNode node, int depth) => false;
		}
		/// <summary>A strategy that never cuts: only parent→child chains compress.</summary>
		public static ICutStrategy NeverCuts { get; } = new ChainOnly();

		public static void WriteFile(IKeyTreeNode fileNode, string fileName, IReadOnlyDictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages, ICutStrategy? cutStrategy = null, string preamble = "", string trailer = "", string settings = "", IReadOnlyDictionary<string, string>? languageSettings = null) {
			using var stream = new StreamWriter(fileName);
			WriteFile(fileNode, stream, allKeys, languages, cutStrategy, preamble, trailer, settings, languageSettings);
		}
		public static void WriteFile(IKeyTreeNode fileNode, TextWriter stream, IReadOnlyDictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages, ICutStrategy? cutStrategy = null, string preamble = "", string trailer = "", string settings = "", IReadOnlyDictionary<string, string>? languageSettings = null) {
			using var writer = new IniWriter(stream, cutStrategy);
			if (preamble != "") {
				writer.WriteComment(preamble);
			}
			writer.WriteLanguages(languages, settings, languageSettings);
			writer.WriteKeys(fileNode, allKeys);
			if (trailer != "") {
				writer.WriteComment(trailer);
			}
		}

		/// <summary>
		///     Writes the top-of-file language table: a <c>value-</c>/<c>comment-</c>
		///     pair per language, then the project settings references as keyless
		///     <c>param=</c> and <c>param-xx=</c> fields (recovered by
		///     <see cref="WordsParserToLocalizationProvider.Settings"/> and
		///     <see cref="WordsParserToLocalizationProvider.LanguageSettings"/> on the
		///     next load). A file with neither languages nor settings writes no header.
		/// </summary>
		/// <param name="languages">The file's own table.</param>
		/// <param name="settings">The dictionary's settings file, relative to it, or empty.</param>
		/// <param name="languageSettings">Per-language settings files, code → relative path; empty paths are skipped.</param>
		public void WriteLanguages(IReadOnlyCollection<LanguageEntry> languages, string settings = "", IReadOnlyDictionary<string, string>? languageSettings = null) {
			bool hasSettings = settings != "" || languageSettings?.Values.Any(path => path != "") is true;
			//a file that declares no languages (a bare library file) has no header —
			//unless it names settings files, which live in this same section
			if (languages.Count == 0 && !hasSettings) {
				return;
			}
			foreach (var lang in languages) {
				WritePair($"value-{lang.Code}", lang.NativeName);
				WritePair($"comment-{lang.Code}", lang.EnglishName);
			}
			if (settings != "") {
				WritePair("param", settings);
			}
			if (languageSettings is not null) {
				foreach (var (code, path) in languageSettings) {
					if (path != "") {
						WritePair($"param-{code}", path);
					}
				}
			}
			WriteLine();
		}
		public void WriteKeys(IKeyTreeNode node, IReadOnlyDictionary<string, WordsKey> allKeys) {
			WriteKeys(node, allKeys, depth: -1, cuts ?? new GroupCuts(allKeys));
		}
		private void WriteKeys(IKeyTreeNode node, IReadOnlyDictionary<string, WordsKey> allKeys, int depth, ICutStrategy cuts) {
			if (node is ICommentNode comment) {
				if (comment.Text != "") {
					WriteComment(comment.Text);
				}
				return; //comment nodes carry no key and no children
			}
			bool cut = depth >= 0 && cuts.Cuts(node, depth);
			if (allKeys.TryGetValue(node.FullLabel, out var key)) {
				WriteBlock(key, forceCut: cut);
			}
			else if (cut) {
				// a bare header: establishes the base for the descendants, and
				// becomes an empty key the next time the file is loaded — which
				// writes back as this same bare header, so no blank line here
				StartBase(node.FullLabel);
			}
			foreach (var child in node.Children) {
				WriteKeys(child, allKeys, depth + 1, cuts);
			}
		}

		private readonly ICutStrategy? cuts = cutStrategy;
		private string baseKey = "";

		public void WriteBlockHeader(string name) => writer.WriteLine("[" + name + "]");

		public void WriteComment(string text) {
			foreach (var line in text.Split('\n')) {
				writer.WriteLine(";" + line.TrimEnd('\r'));
			}
		}

		private void StartBase(string blockKey) {
			baseKey = blockKey;
			// the header drops the leading file segment; only the base keeps it
			// so the StartsWith chain check compares whole keys
			WriteBlockHeader(blockKey[(blockKey.IndexOf('.') + 1)..]);
		}

		public void WritePair(string key, string value) {
			value = Regex.Replace(value, @"\r\n?|\n\r?", "\\" + writer.NewLine);
			value = Regex.Replace(value, @"['_]", m => string.Concat(m.ValueSpan, m.ValueSpan));
			value = Regex.Replace(value, @"(.{50}(?=.{40})\S*)(?=\W+\w)", "$1_" + writer.NewLine);
			writer.Write(key);
			writer.Write('=');
			if (Regex.IsMatch(value, @"^\s")) {
				writer.WriteLine('_');
			}
			writer.WriteLine(value);
		}

		public void WriteLine() => writer.WriteLine();

		public void WriteBlock(WordsKey key) => WriteBlock(key, forceCut: false);
		private void WriteBlock(WordsKey key, bool forceCut) {
			if (!forceCut && baseKey != "" && key.BlockKey.StartsWith($"{baseKey}.", StringComparison.Ordinal)) {
				WriteBlockHeader(key.BlockKey[baseKey.Length..]);
			}
			else {
				StartBase(key.BlockKey);
			}


			if (key.Context != "") {
				WritePair("context", key.Context);
			}

			if (key.Comment != "") {
				WritePair("comment", key.Comment);
			}

			if (key.DefaultValue != "") {
				WritePair("value", key.DefaultValue);
			}

			if (key.Parameters.Count != 0) {
				foreach (WordsParameter parameter in key.Parameters) {
					WritePair(
						$"param-{parameter.Key}",
						$"{parameter.DataType.Name}:{parameter.Value}");
				}
			}

			if (key.NeedsReview) {
				WritePair("stale", "");
			}

			foreach (var (lang, entry) in key.Entries) {

				if (entry.Value != "") {
					WritePair($"value-{lang}", entry.Value);
				}

				if (entry.Stale is not null) {
					WritePair($"stale-{lang}", $"{entry.Stale?.ToString(CultureInfo.InvariantCulture)}");
				}

				if (entry.Context != "") {
					WritePair($"context-{lang}", entry.Context);
				}

				if (entry.Comment != "") {
					WritePair($"comment-{lang}", entry.Comment);
				}
			}
			if (key.DefaultValue != "") {
				WriteLine();
			}
		}

		public void Dispose() => writer.Dispose();

		public ValueTask DisposeAsync() => writer.DisposeAsync();
	}
}
