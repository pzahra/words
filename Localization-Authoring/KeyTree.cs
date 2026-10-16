namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A structural tree node: what <see cref="KeyTree.Build(IEnumerable{WordsKey}, IReadOnlyDictionary{string, string}, string)"/>
	///     produces and what the writer walks. An editor wraps these with its UI
	///     state; they carry none.
	/// </summary>
	public class KeyTreeNode : IKeyTreeNode {
		/// <summary>The last segment as shown: without its <c>$</c> marker; empty for a file; <c>;</c> for a comment.</summary>
		public string Label { get; }
		/// <inheritdoc/>
		public string FullLabel { get; }
		/// <summary>True for the root: the file node, whose <see cref="FullLabel"/> is empty.</summary>
		public bool IsFile { get; init; }
		/// <inheritdoc/>
		public WordsFile? File { get; init; }
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
		/// <summary>
		///     The tree of <paramref name="file"/> as it holds its keys right after a load,
		///     each node labelled with its path in the file, the root carrying the file.
		/// </summary>
		public static KeyTreeNode Build(WordsFile file) => Build(file.Keys.Values, file.BlockComments, file.Trailer, file);

		/// <param name="keysInOrder">The file's keys in document order; the order children take.</param>
		/// <param name="blockComments">Comment runs by the name of the block they sat above.</param>
		/// <param name="trailer">The comment run after the last block, or empty.</param>
		public static KeyTreeNode Build(IEnumerable<WordsKey> keysInOrder, IReadOnlyDictionary<string, string> blockComments, string trailer = "")
			=> Build(keysInOrder, blockComments, trailer, null);

		//the tree, its root carrying the file it is of, where it is one's
		private static KeyTreeNode Build(IEnumerable<WordsKey> keysInOrder, IReadOnlyDictionary<string, string> blockComments, string trailer, WordsFile? of) {
			var root = new KeyTreeNode("", "") { IsFile = true, File = of };
			var nodes = new Dictionary<string, KeyTreeNode> { [""] = root };
			foreach (string path in keysInOrder.Select(key => key.BlockKey).Concat(blockComments.Keys)) {
				Ensure(path);
			}
			if (trailer != "") {
				root.Children.Add(new CommentTreeNode(";trailer", trailer));
			}
			return root;

			KeyTreeNode Ensure(string path) {
				if (nodes.TryGetValue(path, out var node)) {
					return node;
				}
				KeyTreeNode parent = Ensure(path.LastIndexOf('.') is >= 0 and var dot ? path[..dot] : "");
				node = new KeyTreeNode(KeyTreeNode.LabelOf(path), path);
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

		//a name under a parent's path; under the root, the name alone
		internal static string Join(string parent, string name) => parent == "" ? name : name == "" ? parent : $"{parent}.{name}";
	}
}
