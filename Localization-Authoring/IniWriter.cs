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
		/// <summary>Library files skip the language header; their languages belong to the main file.</summary>
		bool IsLibraryFile { get; }
		/// <summary>The nodes below this one, in write order.</summary>
		IEnumerable<IKeyTreeNode> Children { get; }
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

	public sealed class IniWriter(TextWriter writer, ICutStrategy? cutStrategy = null) : IDisposable, IAsyncDisposable {
		// TODO: the default strategy never cuts, so only parent→child chains
		// compress ([enum] then [.two]); replace with one that inspects the
		// descendants and cuts at keyless group nodes with enough keyed children
		// to pay for the bare header.
		private sealed class ChainOnly : ICutStrategy {
			public bool Cuts(IKeyTreeNode node, int depth) => false;
		}
		private static readonly ICutStrategy chainOnly = new ChainOnly();

		public static void WriteFile(IKeyTreeNode fileNode, string fileName, Dictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages, ICutStrategy? cutStrategy = null, string preamble = "", string trailer = "") {
			using var stream = new StreamWriter(fileName);
			WriteFile(fileNode, stream, allKeys, languages, cutStrategy, preamble, trailer);
		}
		public static void WriteFile(IKeyTreeNode fileNode, TextWriter stream, Dictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages, ICutStrategy? cutStrategy = null, string preamble = "", string trailer = "") {
			using var writer = new IniWriter(stream, cutStrategy);
			if (preamble != "") {
				writer.WriteComment(preamble);
			}
			if (!fileNode.IsLibraryFile) {
				writer.WriteLanguages(languages);
			}
			writer.WriteKeys(fileNode, allKeys);
			if (trailer != "") {
				writer.WriteComment(trailer);
			}
		}

		public void WriteLanguages(IReadOnlyCollection<LanguageEntry> languages) {
			foreach (var lang in languages) {
				WritePair($"value-{lang.Code}", lang.NativeName);
				WritePair($"comment-{lang.Code}", lang.EnglishName);
			}
			WriteLine();
		}
		public void WriteKeys(IKeyTreeNode node, in Dictionary<string, WordsKey> allKeys) {
			WriteKeys(node, allKeys, depth: -1);
		}
		private void WriteKeys(IKeyTreeNode node, Dictionary<string, WordsKey> allKeys, int depth) {
			bool cut = depth >= 0 && cuts.Cuts(node, depth);
			if (allKeys.TryGetValue(node.FullLabel, out var key)) {
				WriteBlock(key, forceCut: cut);
			}
			else if (cut) {
				// a bare header: establishes the base for the descendants, and
				// becomes an empty key the next time the file is loaded
				StartBase(node.FullLabel);
				WriteLine();
			}
			foreach (var child in node.Children) {
				WriteKeys(child, allKeys, depth + 1);
			}
		}

		private readonly ICutStrategy cuts = cutStrategy ?? chainOnly;
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
			if (key.Banner != "") {
				WriteComment(key.Banner);
			}
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
