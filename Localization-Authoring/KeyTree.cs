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
		/// <summary>True for the root: the file node, whose <see cref="FullLabel"/> is the file's label.</summary>
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
	///     the file. Node identities for comments are synthetic
	///     (<c>block.;comment</c>, <c>file.;trailer</c>): the writer ignores them.
	/// </summary>
	public static class KeyTree {
		/// <summary>The tree of <paramref name="file"/> as the session holds it right after a load.</summary>
		public static KeyTreeNode Build(WordsSession session, WordsFile file)
			=> Build(file.Label, session.KeysOf(file), file.BlockComments, file.Trailer);

		/// <param name="fileLabel">The root's label; every key is expected to start with <c>fileLabel.</c>.</param>
		/// <param name="keysInOrder">The file's keys in document order; the order children take.</param>
		/// <param name="blockComments">Comment runs by the (prefixed) block key they sat above.</param>
		/// <param name="trailer">The comment run after the last block, or empty.</param>
		public static KeyTreeNode Build(string fileLabel, IEnumerable<WordsKey> keysInOrder, IReadOnlyDictionary<string, string> blockComments, string trailer = "") {
			var file = new KeyTreeNode(fileLabel, fileLabel) { IsFile = true };
			var nodes = new Dictionary<string, KeyTreeNode> { [fileLabel] = file };
			string prefix = fileLabel + ".";
			foreach (string path in keysInOrder.Select(key => key.BlockKey).Concat(blockComments.Keys)) {
				if (path.StartsWith(prefix, StringComparison.Ordinal)) {
					Ensure(path);
				}
			}
			if (trailer != "") {
				file.Children.Add(new CommentTreeNode($"{fileLabel}.;trailer", trailer));
			}
			return file;

			KeyTreeNode Ensure(string path) {
				if (nodes.TryGetValue(path, out var node)) {
					return node;
				}
				KeyTreeNode parent = Ensure(path[..path.LastIndexOf('.')]);
				node = new KeyTreeNode(KeyTreeNode.LabelOf(path), path);
				if (blockComments.TryGetValue(path, out var text)) {
					//the run that sat above this block stands in front of it; from
					//here on, position is the anchor
					parent.Children.Add(new CommentTreeNode($"{path}.;comment", text));
				}
				parent.Children.Add(node);
				nodes[path] = node;
				return node;
			}
		}

		/// <summary>
		///     A structural copy of <paramref name="tree"/> under a new file label —
		///     the shape a merge or split writes its output in. Comments come along.
		/// </summary>
		public static KeyTreeNode Relabel(IKeyTreeNode tree, string newLabel) {
			string oldPrefix = tree.FullLabel + ".";
			return Copy(tree, newLabel, isFile: true);

			KeyTreeNode Copy(IKeyTreeNode node, string fullLabel, bool isFile) {
				KeyTreeNode copy = node is ICommentNode comment
					? new CommentTreeNode(fullLabel, comment.Text)
					: new KeyTreeNode(isFile ? fullLabel : KeyTreeNode.LabelOf(fullLabel), fullLabel) { IsFile = isFile };
				foreach (IKeyTreeNode child in node.Children) {
					string childLabel = child.FullLabel.StartsWith(oldPrefix, StringComparison.Ordinal)
						? newLabel + child.FullLabel[tree.FullLabel.Length..]
						: child.FullLabel;
					copy.Children.Add(Copy(child, childLabel, isFile: false));
				}
				return copy;
			}
		}
	}
}
