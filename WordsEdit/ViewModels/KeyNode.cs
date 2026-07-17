using System.Collections.ObjectModel;
using System.IO;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels {
	public class KeyNode : ViewModelBase {
		private string _Label = "";
		public string Label {
			get => _Label;
			set => ChangeProperty(ref _Label, value);
		}
		private string _FullLabel = "";
		public string FullLabel {
			get => _FullLabel;
			set {
				if (ChangeProperty(ref _FullLabel, value)) {
					_Path = _FullLabel[(_FullLabel.IndexOf('.') + 1)..];
					AffectProperty(nameof(Path));
				}
			}

		}
		private string _Path = "";
		public string Path {
			get => _Path;
			set => ChangeProperty(ref _Path, value);
		}
		private bool _IsConstant;
		public bool IsConstant {
			get => _IsConstant;
			set => ChangeProperty(ref _IsConstant, value);
		}
		private bool _IsStale;
		public bool IsStale {
			get => _IsStale;
			set => ChangeProperty(ref _IsStale, value);
		}
		private bool _IsOverwritten;
		public bool IsOverwritten {
			get => _IsOverwritten;
			set => ChangeProperty(ref _IsOverwritten, value);
		}
		private bool _CanBeConstant;
		public bool CanBeConstant {
			get => _CanBeConstant;
			set => ChangeProperty(ref _CanBeConstant, value);
		}
		private bool _NeedsReview;
		public bool NeedsReview {
			get => _NeedsReview;
			set => ChangeProperty(ref _NeedsReview, value);
		}
		private ObservableCollection<KeyNode> _Children = new();
		public ObservableCollection<KeyNode> Children {
			get => _Children;
			set => ChangeProperty(ref _Children, value);
		}

		private bool _IsExpanded;
		public bool IsExpanded {
			get => _IsExpanded;
			set => ChangeProperty(ref _IsExpanded, value);
		}

		private bool _IsVisible = true;
		public bool IsVisible {
			get => _IsVisible;
			set => ChangeProperty(ref _IsVisible, value);
		}

		private bool _IsSelected;
		public bool IsSelected {
			get => _IsSelected;
			set => ChangeProperty(ref _IsSelected, value);
		}

		private bool _IsFile = false;
		public bool IsFile {
			get => _IsFile;
			set => ChangeProperty(ref _IsFile, value);
		}

		private bool _IsBaseFile = false;
		public bool IsBaseFile {
			get => _IsBaseFile;
			set => ChangeProperty(ref _IsBaseFile, value);
		}

		private bool _IsLibraryFile = false;
		public bool IsLibraryFile {
			get => _IsLibraryFile;
			set => ChangeProperty(ref _IsLibraryFile, value);
		}

		private bool _EmptyValue = false;
		public bool EmptyValue {
			get => _EmptyValue;
			set => ChangeProperty(ref _EmptyValue, value);
		}

		public KeyNode(string label, string fullLabel) {
			_Label = label;
			_FullLabel = fullLabel;
			_Path = fullLabel[(fullLabel.IndexOf('.') + 1)..];
		}
		public KeyNode(IEnumerable<KeyNode> staleChildren) {
			_Children = new(staleChildren);
		}
		public KeyNode() { }

		public KeyNode(KeyNode original) {
			_Label = original._Label;
			_FullLabel = original._FullLabel;
			_Path = original._Path;
			_IsConstant = original._IsConstant;
			_IsStale = original._IsStale;
			_IsOverwritten = original._IsOverwritten;
			_CanBeConstant = original._CanBeConstant;
			_IsExpanded = false;
			_IsVisible = original._IsVisible;
			_IsSelected = false;
			_IsFile = original._IsFile;
			_IsBaseFile = original._IsBaseFile;
			_IsLibraryFile = original._IsLibraryFile;
			_EmptyValue = original._EmptyValue;
			_Children = new ObservableCollection<KeyNode>();
			foreach (var child in original._Children) {
				KeyNode childCopy = new KeyNode(child);
				_Children.Add(childCopy);
			}
		}

		public KeyNode? GetParentNode(IEnumerable<KeyNode> keyNodes) {
			if (FullLabel is null) {
				throw new InvalidDataException("Key Node has no label.");
			}
			if (keyNodes.Any(k => k.FullLabel == FullLabel)) {
				return null;
			}
			string[] parentNodes = FullLabel.Split('.');
			KeyNode rootNode = new();
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
