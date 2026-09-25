namespace PatTech.Localization.Authoring {
	/// <summary>
	///     What the Words model holds that a format may not. A format declares the
	///     ones it keeps (<see cref="WordsFormatInfo.Features"/>), a document reports
	///     the ones it uses (<see cref="ExportSource.Used"/>), and the difference is
	///     the loss an export announces before a byte lands. Values — the default
	///     and each language's — every format keeps, so they are not listed.
	/// </summary>
	[Flags]
	public enum WordsFeatures {
		None = 0,
		/// <summary>A key's <c>context=</c>: the programmer's note to the translator.</summary>
		Context = 1 << 0,
		/// <summary>A key's <c>comment=</c>: the translator-facing channel.</summary>
		Comment = 1 << 1,
		/// <summary>A language entry's <c>context-xx=</c>.</summary>
		EntryContext = 1 << 2,
		/// <summary>A language entry's <c>comment-xx=</c>.</summary>
		EntryComment = 1 << 3,
		/// <summary>Format parameters, <c>param-x=Type:sample</c>.</summary>
		Parameters = 1 << 4,
		/// <summary>Per-language stale marks, <c>stale-xx=</c>.</summary>
		Stale = 1 << 5,
		/// <summary>The languageless review flag, <c>stale=</c>.</summary>
		NeedsReview = 1 << 6,
		/// <summary><c>[$constants]</c>: keys that read the same in every language.</summary>
		Constants = 1 << 7,
		/// <summary>Freeform <c>;</c> runs: the preamble, the trailer and the comments between blocks.</summary>
		FreeComments = 1 << 8,
		/// <summary>The settings-file references, <c>param=</c> and <c>param-xx=</c>.</summary>
		Settings = 1 << 9,
		/// <summary>Everything: what the native format keeps.</summary>
		All = Context | Comment | EntryContext | EntryComment | Parameters | Stale | NeedsReview | Constants | FreeComments | Settings,
	}

	/// <summary>A format's descriptor: how the registry and the editor's dropdowns know it.</summary>
	/// <param name="Id">Short, lowercase, key-caps — <c>ini</c>, <c>resx</c>; it names the format's words, <c>format.&lt;id&gt;.*</c>.</param>
	/// <param name="Extensions">The extensions it claims, dot included, the primary first.</param>
	/// <param name="Features">What it preserves.</param>
	public sealed record WordsFormatInfo(string Id, IReadOnlyList<string> Extensions, WordsFeatures Features) {
		/// <summary>The key of the display name, <c>format.&lt;id&gt;.name</c> — a lookup key, never words.</summary>
		public string NameKey => $"format.{Id}.name";

		/// <summary>True when <paramref name="path"/> carries one of the claimed extensions.</summary>
		public bool Claims(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	///     The settings a format takes for one read or write, by name: the bag a
	///     format that cannot be read until it is configured (a spreadsheet, say)
	///     fills from its options dialog. Most formats take none, and read
	///     <see langword="null"/> as none.
	/// </summary>
	public sealed class FormatOptions : Dictionary<string, string> { }

	/// <summary>
	///     A dictionary format the editor can trade with. The descriptor says what it
	///     is and <see cref="Init"/> brings its words; its direction is which of
	///     <see cref="IWordsImporter"/> and <see cref="IWordsExporter"/> it implements
	///     — both, for a codec.
	/// </summary>
	public interface IWordsFormat {
		/// <summary>The descriptor.</summary>
		WordsFormatInfo Info { get; }

		/// <summary>
		///     The format's own words: a <c>words.ini</c> fragment keyed
		///     <c>format.&lt;id&gt;.*</c>, declaring its languages with <c>!</c> labels
		///     so the host's menu ignores them; <see langword="null"/> when it has
		///     none. The host loads every format's before its own, so its own win.
		/// </summary>
		TextReader? Init();
	}

	/// <summary>A format that reads: foreign files in, the document surface out.</summary>
	public interface IWordsImporter : IWordsFormat {
		/// <summary>
		///     The set of files a pick implies, the primary first: pick
		///     <c>Strings.fr.resx</c> and resx gathers <c>Strings.resx</c> and every
		///     <c>Strings.*.resx</c> beside it; ini, carrying every language in one
		///     file, answers the pick alone.
		/// </summary>
		IReadOnlyList<string> Discover(string path);

		/// <summary>
		///     Reads a set — what <see cref="Discover"/> returned, or what the user
		///     kept of it — into the document surface. Bad content never throws: what
		///     was dropped or guessed goes to <see cref="ILoadedWords.Errors"/>. I/O
		///     failures propagate.
		/// </summary>
		ILoadedWords Read(IReadOnlyList<string> paths, FormatOptions? options = null);
	}

	/// <summary>A format that writes: the document in, a set of files out.</summary>
	public interface IWordsExporter : IWordsFormat {
		/// <summary>
		///     The files <paramref name="source"/> becomes when written at
		///     <paramref name="target"/>, one unit each — so the caller can show the
		///     list and its overwrites before anything is written.
		/// </summary>
		IReadOnlyList<ExportUnit> Plan(ExportSource source, string target, FormatOptions? options = null);

		/// <summary>
		///     Writes one planned unit; what it dropped or guessed goes to
		///     <paramref name="gripes"/>. The caller makes it atomic
		///     (<see cref="IniWriter.WriteAtomic"/>).
		/// </summary>
		void Write(ExportSource source, ExportUnit unit, TextWriter writer, ICollection<string> gripes, FormatOptions? options = null);
	}

	/// <summary>Helpers over the format interfaces.</summary>
	public static class WordsFormatExtensions {
		/// <summary>What exporting <paramref name="source"/> through <paramref name="format"/> drops: the features it uses that the format does not keep.</summary>
		public static WordsFeatures Loses(this IWordsFormat format, ExportSource source) => source.Used() & ~format.Info.Features;
	}

	/// <summary>One file an export writes: where, and which languages it carries.</summary>
	/// <param name="Path">The file to write.</param>
	/// <param name="Languages">The codes whose entries go in, in order; empty for the defaults alone. A format carrying every language in one file lists them all.</param>
	public sealed record ExportUnit(string Path, IReadOnlyList<string> Languages);

	/// <summary>
	///     What an exporter is handed: one loaded file, the tree that orders it —
	///     the node <see cref="WordsSession.Save(WordsFile, IKeyTreeNode)"/> takes,
	///     and refused on the same terms — and the session they belong to.
	/// </summary>
	public sealed class ExportSource {
		/// <summary>The session the file is loaded in.</summary>
		public WordsSession Session { get; }
		/// <summary>The file being exported.</summary>
		public WordsFile File { get; }
		/// <summary>The file's node: its walk is the key order, its comment nodes the freeform comments.</summary>
		public IKeyTreeNode Tree { get; }

		/// <exception cref="InvalidOperationException">The tree is not the file's, or does not cover exactly its keys.</exception>
		public ExportSource(WordsSession session, WordsFile file, IKeyTreeNode tree) {
			session.EnsureTreeCovers(file, tree);
			Session = session;
			File = file;
			Tree = tree;
		}

		/// <summary>The file's own language table, with the session's labels.</summary>
		public IReadOnlyList<LanguageEntry> Languages => Session.Languages.For(File);

		/// <summary>The file's keys in the order the tree walks them; groups and comments skipped.</summary>
		public IEnumerable<WordsKey> Keys() => Walk(Tree);

		private IEnumerable<WordsKey> Walk(IKeyTreeNode node) {
			if (node is ICommentNode) {
				yield break;
			}
			if (Session.Keys.TryGetValue(node.FullLabel, out var key)) {
				yield return key;
			}
			foreach (IKeyTreeNode child in node.Children) {
				foreach (WordsKey below in Walk(child)) {
					yield return below;
				}
			}
		}

		/// <summary>What this document uses of the model: the document's half of the loss.</summary>
		public WordsFeatures Used() {
			WordsFeatures used = WordsFeatures.None;
			if (File.Settings != "" || File.LanguageSettings.Values.Any(path => path != "")) {
				used |= WordsFeatures.Settings;
			}
			if (File.Preamble != "" || HasComment(Tree)) {
				used |= WordsFeatures.FreeComments;
			}
			foreach (WordsKey key in Keys()) {
				if (key.Context != "") {
					used |= WordsFeatures.Context;
				}
				if (key.Comment != "") {
					used |= WordsFeatures.Comment;
				}
				if (key.Parameters.Count != 0) {
					used |= WordsFeatures.Parameters;
				}
				if (key.NeedsReview) {
					used |= WordsFeatures.NeedsReview;
				}
				if (key.IsConstant) {
					used |= WordsFeatures.Constants;
				}
				foreach (WordsEntry entry in key.Entries.Values) {
					if (entry.Context != "") {
						used |= WordsFeatures.EntryContext;
					}
					if (entry.Comment != "") {
						used |= WordsFeatures.EntryComment;
					}
					if (entry.Stale is not null) {
						used |= WordsFeatures.Stale;
					}
				}
			}
			return used;
		}

		private static bool HasComment(IKeyTreeNode node)
			=> node is ICommentNode comment ? comment.Text != "" : node.Children.Any(HasComment);
	}
}
