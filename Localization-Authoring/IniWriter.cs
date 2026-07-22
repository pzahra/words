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

	public sealed class IniWriter(TextWriter writer) : IDisposable, IAsyncDisposable {
		public static void WriteFile(IKeyTreeNode fileNode, string fileName, Dictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages) {
			using var stream = new StreamWriter(fileName);
			WriteFile(fileNode, stream, allKeys, languages);
		}
		public static void WriteFile(IKeyTreeNode fileNode, TextWriter stream, Dictionary<string, WordsKey> allKeys, IReadOnlyCollection<LanguageEntry> languages) {
			using var writer = new IniWriter(stream);
			if (fileNode.IsLibraryFile) {
				writer.WriteKeys(fileNode, allKeys);
				return;
			}
			writer.WriteLanguages(languages);
			writer.WriteKeys(fileNode, allKeys);
		}

		public void WriteLanguages(IReadOnlyCollection<LanguageEntry> languages) {
			foreach (var lang in languages) {
				WritePair($"value-{lang.Code}", lang.NativeName);
				WritePair($"comment-{lang.Code}", lang.EnglishName);
			}
			WriteLine();
		}
		public void WriteKeys(IKeyTreeNode node, in Dictionary<string, WordsKey> allKeys) {
			if (allKeys.TryGetValue(node.FullLabel, out var key)) {
				WriteBlock(key);
			}
			foreach (var child in node.Children) {
				WriteKeys(child, allKeys);
			}
		}

		public void WriteBlockHeader(string name) => writer.WriteLine("[" + name + "]");

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

		public void WriteBlock(WordsKey key) {
			// TODO: decide when to lean on dot-relative inheritance (`[.name]`) instead
			// of flattening: every block is written with its full path below the file
			// segment, so a hand-written `[.sub]` hierarchy does not survive a round trip.
			string blockKey = key.BlockKey[(key.BlockKey.IndexOf('.') + 1)..];
			WriteBlockHeader(blockKey);


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
