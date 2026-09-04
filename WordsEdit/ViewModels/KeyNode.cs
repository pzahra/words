using PatTech.Localization.Authoring;
using System.Collections.ObjectModel;

namespace WordsEdit.ViewModels;

/// <summary>
///     A node's children: whatever is added here gets its <see cref="KeyNode.Parent"/>
///     set to the owner, and whatever leaves loses it — so the tree is always
///     walkable both ways without a search.
/// </summary>
public sealed class KeyNodeCollection(KeyNode? owner) : ObservableCollection<KeyNode> {
	protected override void InsertItem(int index, KeyNode item) {
		item.Parent = owner;
		base.InsertItem(index, item);
	}

	protected override void SetItem(int index, KeyNode item) {
		this[index].Parent = null;
		item.Parent = owner;
		base.SetItem(index, item);
	}

	protected override void RemoveItem(int index) {
		this[index].Parent = null;
		base.RemoveItem(index);
	}

	protected override void ClearItems() {
		foreach (KeyNode node in this) {
			node.Parent = null;
		}
		base.ClearItems();
	}
}

/// <summary>
///     A tree row: the structural node (<see cref="IKeyTreeNode"/>, which the
///     writer walks) plus the UI state the tree binds — selection, expansion,
///     visibility and the badges, which are computed from the document by the
///     main view model and never edited here.
/// </summary>
public class KeyNode : ViewModelBase, IKeyTreeNode {
	public string Label { get; set => ChangeProperty(ref field, value); }
	public string FullLabel {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				Path = value[(value.IndexOf('.') + 1)..];
			}
		}
	}

	/// <summary>The key without its file prefix, for the header.</summary>
	public string Path { get; set => ChangeProperty(ref field, value); }
	public bool IsConstant { get; set => ChangeProperty(ref field, value); }
	public bool IsStale { get; set => ChangeProperty(ref field, value); }
	public bool IsOverwritten { get; set => ChangeProperty(ref field, value); }
	public bool CanBeConstant { get; set => ChangeProperty(ref field, value); }
	public bool NeedsReview { get; set => ChangeProperty(ref field, value); }

	public KeyNodeCollection Children { get; }
	IEnumerable<IKeyTreeNode> IKeyTreeNode.Children => Children;
	/// <summary>The node this one sits under; null at the root (a file) or when detached.</summary>
	public KeyNode? Parent { get; internal set => ChangeProperty(ref field, value); }
	/// <summary>The file node this one belongs to (itself, for a file).</summary>
	public KeyNode Root => Parent?.Root ?? this;

	public bool IsExpanded { get; set => ChangeProperty(ref field, value); }
	//visible by default: the tree renders Visibility from this, and a fresh
	//load or a newly added node must show without waiting for a filter pass
	public bool IsVisible { get; set => ChangeProperty(ref field, value); } = true;
	public bool IsSelected { get; set => ChangeProperty(ref field, value); }
	public bool IsFile { get; set => ChangeProperty(ref field, value); }
	public bool IsBaseFile { get; set => ChangeProperty(ref field, value); }
	public bool IsLibraryFile { get; set => ChangeProperty(ref field, value); }
	public bool EmptyValue { get; set => ChangeProperty(ref field, value); }

	public KeyNode(string label, string fullLabel) {
		Children = new KeyNodeCollection(this);
		Label = label;
		FullLabel = fullLabel;
		Path = fullLabel[(fullLabel.IndexOf('.') + 1)..];
	}

	public KeyNode(KeyNode original) : this(original.Label, original.FullLabel) {
		IsConstant = original.IsConstant;
		IsStale = original.IsStale;
		IsOverwritten = original.IsOverwritten;
		CanBeConstant = original.CanBeConstant;
		IsVisible = original.IsVisible;
		IsFile = original.IsFile;
		IsBaseFile = original.IsBaseFile;
		IsLibraryFile = original.IsLibraryFile;
		EmptyValue = original.EmptyValue;
		foreach (KeyNode child in original.Children) {
			Children.Add(child.Clone());
		}
	}

	/// <summary>A deep copy keeping each node's kind (comments stay comments).</summary>
	public virtual KeyNode Clone() => new KeyNode(this);

	/// <summary>The presentation of a structural tree: one row per node, comments as comment rows.</summary>
	public static KeyNode From(KeyTreeNode node) {
		KeyNode result = node is CommentTreeNode comment
			? new CommentNode(comment.FullLabel, comment.Text)
			: new KeyNode(node.Label, node.FullLabel) { IsFile = node.IsFile };
		foreach (KeyTreeNode child in node.Children) {
			result.Children.Add(From(child));
		}
		return result;
	}

	/// <summary>
	///     Re-roots this node at <paramref name="fullLabel"/> and every descendant
	///     below it, each keeping its own last segment — marker and all. The
	///     document is renamed separately; this is the tree following.
	/// </summary>
	public void Relabel(string fullLabel) {
		FullLabel = fullLabel;
		foreach (KeyNode child in Children) {
			child.Relabel($"{fullLabel}.{WordsOperations.LastSegment(child.FullLabel)}");
		}
	}

	/// <summary>Every node below this one, depth first.</summary>
	public IEnumerable<KeyNode> Descendants() {
		foreach (KeyNode child in Children) {
			yield return child;
			foreach (KeyNode grandchild in child.Descendants()) {
				yield return grandchild;
			}
		}
	}

	/// <summary>This node, then <see cref="Descendants"/>.</summary>
	public IEnumerable<KeyNode> SelfAndDescendants() {
		yield return this;
		foreach (KeyNode node in Descendants()) {
			yield return node;
		}
	}

	/// <summary>True when <paramref name="node"/> is this node or sits anywhere below it.</summary>
	public bool Contains(KeyNode node) {
		for (KeyNode? current = node; current is not null; current = current.Parent) {
			if (current == this) {
				return true;
			}
		}
		return false;
	}
}
