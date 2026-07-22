using PatTech.Localization.Authoring;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace WordsEdit.ViewModels {
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

		public string Path { get; set => ChangeProperty(ref field, value); }
		public bool IsConstant { get; set => ChangeProperty(ref field, value); }
		public bool IsStale { get; set => ChangeProperty(ref field, value); }
		public bool IsOverwritten { get; set => ChangeProperty(ref field, value); }
		public bool CanBeConstant { get; set => ChangeProperty(ref field, value); }
		public bool NeedsReview { get; set => ChangeProperty(ref field, value); }

		public ObservableCollection<KeyNode> Children { get; set => ChangeProperty(ref field, value); } = [];
		IEnumerable<IKeyTreeNode> IKeyTreeNode.Children => Children;
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
			Label = label;
			FullLabel = fullLabel;
			Path = fullLabel[(fullLabel.IndexOf('.') + 1)..];
		}
		public KeyNode() { }

		public KeyNode(KeyNode original) {
			Label = original.Label;
			FullLabel = original.FullLabel;
			Path = original.Path;
			IsConstant = original.IsConstant;
			IsStale = original.IsStale;
			IsOverwritten = original.IsOverwritten;
			CanBeConstant = original.CanBeConstant;
			IsExpanded = false;
			IsVisible = original.IsVisible;
			IsSelected = false;
			IsFile = original.IsFile;
			IsBaseFile = original.IsBaseFile;
			IsLibraryFile = original.IsLibraryFile;
			EmptyValue = original.EmptyValue;
			Children = [.. original.Children.Select(c => c.Clone())];
		}

		/// <summary>A deep copy keeping each node's kind (comments stay comments).</summary>
		public virtual KeyNode Clone() => new KeyNode(this);

		public KeyNode? GetParentNode(IEnumerable<KeyNode> keyNodes) {
			// FIXME: this is awful. use a property!
			if (FullLabel is null) {
				throw new InvalidDataException("Key Node has no label.");
			}
			if (keyNodes.Any(k => k.FullLabel == FullLabel)) {
				return null;
			}
			string[] parentNodes = FullLabel.Split('.');
			KeyNode rootNode = new(":root:",":root:");
			foreach (KeyNode node in keyNodes) {
				if (node.Label == parentNodes[0]) {
					rootNode = node;
				}
			}
			int indexOfParentInFullLabel = 1;
			while (!rootNode.Children.Any(k => k.FullLabel == FullLabel)) {
				foreach (KeyNode node in rootNode.Children) {
					if (node.Label == parentNodes[indexOfParentInFullLabel]) {
						rootNode = node;
					}
				}
				indexOfParentInFullLabel++;
				if (indexOfParentInFullLabel > parentNodes.Length) {
					throw new InvalidDataException("Error: label no longer in full label");
				}
			}
			return rootNode;
		}

		public KeyNode? DeepestVisibleKeyNodeInBranch(IEnumerable<KeyNode> keyNodes) {
			if (FullLabel is null) {
				throw new InvalidDataException("Key Node has no label.");
			}
			string[] parentNodes = FullLabel.Split('.');
			if (parentNodes.Length >= 2 && parentNodes[1][0] == '$') {
				parentNodes[1] = parentNodes[1][1..];
			}
			KeyNode? rootNode = null;
			foreach (KeyNode node in keyNodes) {
				if (node.Label == parentNodes[0] && node.IsVisible) {
					rootNode = node;
				}
			}
			if (rootNode is null) {
				return null;
			}
			int indexOfParentInFullLabel = 1;
			while (rootNode.FullLabel != FullLabel) {
				foreach (KeyNode node in rootNode.Children) {
					if (node.Label == parentNodes[indexOfParentInFullLabel]) {
						if (node.IsVisible) {
							rootNode = node;
						}
						else {
							node.IsSelected = false;
							return rootNode;
						}
					}
				}
				indexOfParentInFullLabel++;
				if (indexOfParentInFullLabel > parentNodes.Length) {
					throw new InvalidDataException("Error: label no longer in full label");
				}
			}
			return rootNode;
		}
	}
}
