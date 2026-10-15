namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A structural tree node: what <see cref="KeyTree.Build(string, IEnumerable{WordsKey}, IReadOnlyDictionary{string, string}, string)"/>
	///     produces and what the writer walks. An editor wraps these with its UI
	///     state; they carry none.
	/// </summary>
	public class KeyTreeNode : IKeyTreeNode {
		/// <summary>The last segment as shown: without its <c>$</c> marker; the whole label for a file; <c>;</c> for a comment.</summary>
		public string Label { get; }
		/// <inheritdoc/>
		public string FullLabel { get; }
		/// <summary>True for the root: the file node, whose <see cref="FullLabel"/> is empty, or the label a prefixed tree gives it.</summary>
		public bool IsFile { get; init; }
		/// <summary>The nodes below this one, in write order.</summary>
		public List<KeyTreeNode> Children { get; } = [];
		IEnumerable<IKeyTreeNode> IKeyTreeNode.Children => Children;

		public KeyTreeNode(string label, string fullLabel) {
			Label = label;
			FullLabel = fullLabel;
		}

		/// <summary>The label as shown for a dotted key's last segment: the <c>$</c> marker dropped.</summary>
		public static string LabelOf(string fullLabel) => WordsOperations.LastSegment(fullLabel).TrimStart('$');
	}

	/// <summary>A freeform comment run standing in the tree; the writer emits it where it stands.</summary>
	public sealed class CommentTreeNode : KeyTreeNode, ICommentNode {
		/// <inheritdoc/>
		public string Text { get; }

		public CommentTreeNode(string fullLabel, string text) : base(";", fullLabel) {
			Text = text;
		}
	}

	/// <summary>
	///     Builds the tree of one file from its keys: dotted keys nest, with a
	///     group node for every intermediate path in order of first appearance;
	///     constants show without their marker; a comment run that sat above a
	///     block becomes a comment node right in front of that block's node (the
	///     node is created for it if the block was empty), and the trailer closes
	///     the file. A node's full label is its key's name, its path in the file,
	///     and the root's is empty. Node identities for comments are synthetic
	///     (<c>block.;comment</c>, <c>;trailer</c>): the writer ignores them.
	/// </summary>
	public static class KeyTree {
		/// <summary>The tree of <paramref name="file"/> as it holds its keys right after a load, each node labelled with its path in the file.</summary>
		public static KeyTreeNode Build(WordsFile file) => Build("", file.Keys.Values, file.BlockComments, file.Trailer);

		/// <summary>
		///     The tree of <paramref name="file"/>, each node's label prefixed with the
		///     file's, <c>Label.view.key</c>, as the editor names nodes until it names
		///     them by file and path (the editor's SPEC: Keys without the file's name).
		/// </summary>
		public static KeyTreeNode Build(WordsSession session, WordsFile file) => Build(file.Label, file.Keys.Values, file.BlockComments, file.Trailer);

		/// <param name="label">The root's label, which prefixes every node's; empty for a tree labelled by path alone.</param>
		/// <param name="keysInOrder">The file's keys in document order; the order children take.</param>
		/// <param name="blockComments">Comment runs by the name of the block they sat above.</param>
		/// <param name="trailer">The comment run after the last block, or empty.</param>
		public static KeyTreeNode Build(string label, IEnumerable<WordsKey> keysInOrder, IReadOnlyDictionary<string, string> blockComments, string trailer = "") {
			var file = new KeyTreeNode(label, label) { IsFile = true };
			var nodes = new Dictionary<string, KeyTreeNode> { [""] = file };
			foreach (string path in keysInOrder.Select(key => key.BlockKey).Concat(blockComments.Keys)) {
				Ensure(path);
			}
			if (trailer != "") {
				file.Children.Add(new CommentTreeNode(Join(label, ";trailer"), trailer));
			}
			return file;

			KeyTreeNode Ensure(string path) {
				if (nodes.TryGetValue(path, out var node)) {
					return node;
				}
				KeyTreeNode parent = Ensure(path.LastIndexOf('.') is >= 0 and var dot ? path[..dot] : "");
				node = new KeyTreeNode(KeyTreeNode.LabelOf(path), Join(label, path));
				if (blockComments.TryGetValue(path, out var text)) {
					//the run that sat above this block stands in front of it; from
					//here on, position is the anchor
					parent.Children.Add(new CommentTreeNode($"{node.FullLabel}.;comment", text));
				}
				parent.Children.Add(node);
				nodes[path] = node;
				return node;
			}
		}

		/// <summary>
		///     A structural copy of <paramref name="tree"/> under a new root label,
		///     each node's path in the file kept: empty labels each node by its path
		///     alone, the shape the writer and the exporters read. Comments come along.
		/// </summary>
		public static KeyTreeNode Relabel(IKeyTreeNode tree, string newLabel) {
			return Copy(tree, newLabel, isFile: true);

			KeyTreeNode Copy(IKeyTreeNode node, string fullLabel, bool isFile) {
				KeyTreeNode copy = node is ICommentNode comment
					? new CommentTreeNode(fullLabel, comment.Text)
					: new KeyTreeNode(isFile ? fullLabel : KeyTreeNode.LabelOf(fullLabel), fullLabel) { IsFile = isFile };
				foreach (IKeyTreeNode child in node.Children) {
					copy.Children.Add(Copy(child, Join(newLabel, PathOf(tree, child)), isFile: false));
				}
				return copy;
			}
		}

		/// <summary>
		///     The path in its file of a node of <paramref name="tree"/>: its full
		///     label, the root's label and its dot taken off where the root has one.
		/// </summary>
		public static string PathOf(IKeyTreeNode tree, IKeyTreeNode node)
			=> tree.FullLabel != "" && node.FullLabel.StartsWith(tree.FullLabel + ".", StringComparison.Ordinal) ? node.FullLabel[(tree.FullLabel.Length + 1)..] : node.FullLabel;

		//a path under a label; empty adds nothing
		internal static string Join(string label, string path) => label == "" ? path : path == "" ? label : $"{label}.{path}";
	}
}
