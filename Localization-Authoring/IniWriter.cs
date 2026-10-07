using System.Globalization;
using System.Text;
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
	///     carries at least <c>minimumKeys</c> keyed blocks — the shape a
	///     hand-author writes as a bare <c>[group]</c> header followed by
	///     <c>[.child]</c> blocks. Keyed nodes never cut (their own header re-bases
	///     the chain already), and keys beyond a deeper cut don't count here: they
	///     chain off that base, not this one. A single keyed descendant isn't worth
	///     the bare header (which reloads as an empty key) — it keeps its full header.
	/// </summary>
	/// <remarks>
	///     Built for one write of one tree: it snapshots the key set and memoizes
	///     subtree counts as it goes, so reuse it across a mutated tree at your peril.
	///     The writer makes a fresh one per file, which is the intended lifetime.
	/// </remarks>
	public sealed class GroupCuts : ICutStrategy {
		private readonly HashSet<string> keys;
		private readonly int minimumKeys;
		private readonly Dictionary<IKeyTreeNode, int> counted = [];

		/// <param name="keys">Every key the writer is working from, block key to data.</param>
		/// <param name="minimumKeys">Keyed blocks a group must gather before it pays for its header; at least 1.</param>
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumKeys"/> is less than 1.</exception>
		public GroupCuts(IReadOnlyDictionary<string, WordsKey> keys, int minimumKeys = 2) {
			ArgumentNullException.ThrowIfNull(keys);
			ArgumentOutOfRangeException.ThrowIfLessThan(minimumKeys, 1);
			// snapshot the key set: the strategy decides cuts for a single write, so a
			// later change to the caller's dictionary must not desync those decisions
			this.keys = [.. keys.Keys];
			this.minimumKeys = minimumKeys;
		}

		/// <inheritdoc/>
		public bool Cuts(IKeyTreeNode node, int depth)
			=> !keys.Contains(node.FullLabel) && ChainingKeys(node) >= minimumKeys;

		/// <summary>Keyed blocks below <paramref name="node"/> that would chain off its header.</summary>
		private int ChainingKeys(IKeyTreeNode node) {
			if (!counted.TryGetValue(node, out int count)) {
				foreach (var child in node.Children) {
					if (child is ICommentNode) {
						continue;
					}
					if (keys.Contains(child.FullLabel)) {
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

		//a StreamWriter's own default: no BOM, and text it can't encode throws
		internal static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

		/// <summary>
		///     Writes a file atomically, with <paramref name="newLine"/> for its line
		///     breaks, or the system's, in <paramref name="encoding"/>, or UTF-8
		///     without a BOM.
		/// </summary>
		public static void WriteFile(IKeyTreeNode fileNode, string fileName, IReadOnlyDictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages, ICutStrategy? cutStrategy = null, string preamble = "", string trailer = "", string settings = "", IReadOnlyDictionary<string, string>? languageSettings = null, string? newLine = null, string? defaultLanguage = null, Encoding? encoding = null)
			=> WriteAtomic(fileName, stream => {
				stream.NewLine = newLine ?? stream.NewLine;
				WriteFile(fileNode, stream, allKeys, languages, cutStrategy, preamble, trailer, settings, languageSettings, defaultLanguage);
			}, encoding);

		/// <summary>
		///     Runs <paramref name="write"/> against a temp sibling of
		///     <paramref name="fileName"/>, then atomically replaces the destination
		///     — so a failure partway leaves the original file untouched.
		/// </summary>
		/// <param name="encoding">
		///     The file's encoding, BOM and all; UTF-8 without a BOM when
		///     <see langword="null"/>. Text it can't encode, such as a lone
		///     surrogate, throws <see cref="EncoderFallbackException"/> and leaves
		///     the original alone.
		/// </param>
		public static void WriteAtomic(string fileName, Action<TextWriter> write, Encoding? encoding = null) {
			string full = Path.GetFullPath(fileName);
			string temp = Path.Combine(Path.GetDirectoryName(full) ?? ".", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
			try {
				using (var stream = new StreamWriter(temp, append: false, encoding ?? Utf8)) {
					write(stream);
				}
				File.Move(temp, full, overwrite: true);
			}
			catch {
				try { File.Delete(temp); } catch { /* the write's own error is the one worth raising */ }
				throw;
			}
		}
		public static void WriteFile(IKeyTreeNode fileNode, TextWriter stream, IReadOnlyDictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages, ICutStrategy? cutStrategy = null, string preamble = "", string trailer = "", string settings = "", IReadOnlyDictionary<string, string>? languageSettings = null, string? defaultLanguage = null) {
			using var writer = new IniWriter(stream, cutStrategy);
			if (preamble != "") {
				writer.WriteComment(preamble);
			}
			writer.WriteLanguages(languages, settings, languageSettings, defaultLanguage);
			writer.WriteKeys(fileNode, allKeys);
			if (trailer != "") {
				writer.WriteComment(trailer);
			}
		}

		/// <summary>
		///     Writes the top-of-file language table: the default's language as a
		///     keyless <c>value=!xx</c>, a <c>value-</c>/<c>comment-</c> pair per
		///     language, then the project settings references as keyless
		///     <c>param=</c> and <c>param-xx=</c> fields (recovered by
		///     <see cref="WordsParserToLocalizationProvider.Settings"/> and
		///     <see cref="WordsParserToLocalizationProvider.LanguageSettings"/> on the
		///     next load). A file with none of these writes no header.
		/// </summary>
		/// <param name="languages">The file's own table.</param>
		/// <param name="settings">The dictionary's settings file, relative to it, or empty.</param>
		/// <param name="languageSettings">Per-language settings files, code → relative path; empty paths are skipped.</param>
		/// <param name="defaultLanguage">The language the default is written in, or <see langword="null"/>.</param>
		public void WriteLanguages(IReadOnlyCollection<LanguageEntry> languages, string settings = "", IReadOnlyDictionary<string, string>? languageSettings = null, string? defaultLanguage = null) {
			bool hasSettings = settings != "" || languageSettings?.Values.Any(path => path != "") is true;
			//a file that declares no languages (a bare library file) has no header —
			//unless it names settings files or the default's language, which live in this same section
			if (languages.Count == 0 && !hasSettings && defaultLanguage is null) {
				return;
			}
			if (defaultLanguage is not null) {
				WritePair("value", "!" + defaultLanguage);
			}
			foreach (var lang in languages) {
				WritePair($"value-{lang.Code}", lang.NativeName);
				if (lang.EnglishName != "") {
					WritePair($"comment-{lang.Code}", lang.EnglishName);
				}
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
			value = value.Replace("\\", "\\\\");
			value = Regex.Replace(value, @"\r\n|\r|\n", "\\" + writer.NewLine);
			value = Regex.Replace(value, @"['_]", m => string.Concat(m.ValueSpan, m.ValueSpan));
			value = Fold(value, writer.NewLine);
			writer.Write(key);
			writer.Write('=');
			if (Regex.IsMatch(value, @"^\s")) {
				writer.WriteLine('_');
			}
			writer.WriteLine(value);
		}

		private static readonly Regex rxWord = new(@"\w+"), rxSpace = new(@"\s+");

		/// <summary>
		///     Folds an escaped value's long lines with a <c>_</c> continuation:
		///     from a point with 120 or more characters left on its line, at the
		///     last break the run of non-space reaching its 80th character offers —
		///     before non-word characters that a word follows, never after a
		///     <c>\</c> or <c>'</c> (half an escape) or between a surrogate pair —
		///     then on from the fold, or from the next character when there's none.
		/// </summary>
		/// <remarks>
		///     The regex <c>(.{80}(?=.{40})\S*)(?&lt;![\\'\uD800-\uDBFF])(?=\W+\w)</c>
		///     replaced with <c>$1_</c> and a line break, in one forward scan: the
		///     regex backtracks through the rest of a value with no break to fold
		///     at, so a long URL or base64 run took seconds.
		/// </remarks>
		internal static string Fold(string value, string newLine) {
			int n = value.Length;
			if (n < 120) {
				return value;
			}
			// the regex engine's own \w and \s, so the classes match it exactly
			var word = new bool[n];
			foreach (var match in rxWord.EnumerateMatches(value)) {
				word.AsSpan(match.Index, match.Length).Fill(true);
			}
			var space = new bool[n];
			foreach (var match in rxSpace.EnumerateMatches(value)) {
				space.AsSpan(match.Index, match.Length).Fill(true);
			}
			// right to left: the next line break, word character and space at or after each index
			var nextBreak = new int[n + 1];
			var nextWord = new int[n + 1];
			var runEnd = new int[n + 1];
			nextBreak[n] = nextWord[n] = runEnd[n] = n;
			for (int i = n - 1; i >= 0; i--) {
				nextBreak[i] = value[i] == '\n' ? i : nextBreak[i + 1];
				nextWord[i] = word[i] ? i : nextWord[i + 1];
				runEnd[i] = space[i] ? i : runEnd[i + 1];
			}
			// left to right: the last fold point at or before each index
			var lastPoint = new int[n + 1];
			lastPoint[0] = -1;
			for (int i = 1; i <= n; i++) {
				bool point = i < n && !word[i] && nextWord[i] < n
					&& value[i - 1] is not ('\\' or '\'') && !char.IsHighSurrogate(value[i - 1]);
				lastPoint[i] = point ? i : lastPoint[i - 1];
			}
			StringBuilder? folded = null;
			int written = 0;
			for (int from = 0; from + 120 <= n;) {
				int fold = nextBreak[from] >= from + 120 ? lastPoint[runEnd[from + 80]] : -1;
				if (fold < from + 80) {
					from++;
					continue;
				}
				(folded ??= new(n + 16)).Append(value, written, fold - written).Append('_').Append(newLine);
				written = from = fold;
			}
			return folded is null ? value : folded.Append(value, written, n - written).ToString();
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
			WriteForms("value", key.Forms);

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
				WriteForms($"value-{lang}", entry.Forms);

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

		//each plural form after its plain value, value-xx#few, in CLDR's order
		private void WriteForms(string field, Dictionary<string, string> forms) {
			foreach (var (form, text) in forms.Written()) {
				WritePair($"{field}#{form}", text);
			}
		}

		public void Dispose() => writer.Dispose();

		public ValueTask DisposeAsync() => writer.DisposeAsync();
	}
}
