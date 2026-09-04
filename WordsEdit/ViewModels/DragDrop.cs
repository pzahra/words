using GongSolutions.Wpf.DragDrop;
using System.Collections.ObjectModel;
using System.Windows;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels {

	public class KeyDragDropHandler : IDragSource, IDropTarget {
		public MainWindowViewModel? MainWindow { get; set; } = null;
		public bool CanStartDrag(IDragInfo dragInfo) {
			//standalone comments move freely; the pinned preamble does not
			return dragInfo?.SourceItem is KeyNode node
				&& (node is not OrganizerNode || node is CommentNode);
		}

		public void DragCancelled() { }

		public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo) {
		}

		public void DragOver(IDropInfo dropInfo) {
			if (MainWindow is null) {
				throw new InvalidOperationException("No Main Window");
			}
			dropInfo.Effects = DragDropEffects.Move;
			bool draggedNodeCanBeChildOfTargetNode = true;
			if ((dropInfo.Data is KeyNode draggedKeyNode && dropInfo.TargetItem is KeyNode targetKeyNode)) {
				if (draggedKeyNode is OrganizerNode and not CommentNode) {
					dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
					return;
				}
				if (targetKeyNode is OrganizerNode && dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter)) {
					//comments take no children; only before/after makes sense
					dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
					return;
				}
				if (draggedKeyNode.IsFile) {
					draggedNodeCanBeChildOfTargetNode = false;
					if (!targetKeyNode.IsFile) {
						dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
						return;
					}
				}
				if (draggedKeyNode.IsConstant) {
					if (!targetKeyNode.IsFile) {
						draggedNodeCanBeChildOfTargetNode = false;
					}
					if (targetKeyNode.GetParentNode(MainWindow.KeyNodes) is { } targetParentNode && !targetParentNode.IsFile) {
						dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
						return;
					}
				}
				if (targetKeyNode.IsConstant) {
					draggedNodeCanBeChildOfTargetNode = false;
				}
				if (TargetIsDraggedItemOrDescendentOfDraggedItem(draggedKeyNode, targetKeyNode)) {
					dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
					if (draggedKeyNode == targetKeyNode) {
						dropInfo.DropTargetAdorner = null;
					}
				}
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter) && draggedNodeCanBeChildOfTargetNode) {
					dropInfo.DropTargetAdorner = typeof(DropTargetHighlightAdorner);
				}
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem)) {
					dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
				}
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem)) {
					dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
				}
			}
		}

		public void Drop(IDropInfo dropInfo) {
			if (MainWindow is null) {
				throw new InvalidOperationException("No Main Window");
			}
			if (dropInfo.Data is KeyNode draggedKeyNode && dropInfo.TargetItem is KeyNode targetKeyNode) {
				if (draggedKeyNode is OrganizerNode and not CommentNode) {
					return;
				}
				if (targetKeyNode is OrganizerNode && dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter)) {
					return;
				}
				KeyNode? parentNode;
				int index;
				ObservableCollection<KeyNode> localizationKeyNodes = MainWindow.KeyNodes;
				parentNode = draggedKeyNode.GetParentNode(localizationKeyNodes);
				bool draggedNodeCanBeChildOfTargetNode = true;
				if (parentNode is null) {
					index = localizationKeyNodes.IndexOf(draggedKeyNode);
					localizationKeyNodes.Remove(draggedKeyNode);
				}
				else {
					index = parentNode.Children.IndexOf(draggedKeyNode);
					parentNode.Children.Remove(draggedKeyNode);
					KeyNode? grandParentNode = parentNode.GetParentNode(MainWindow.KeyNodes);
					if (grandParentNode is not null && grandParentNode.IsFile && parentNode.Children.Count == 0) {
						parentNode.CanBeConstant = true;
					}
					else {
						parentNode.CanBeConstant = false;
					}
				}
				if (TargetIsDraggedItemOrDescendentOfDraggedItem(draggedKeyNode, targetKeyNode)) {
					//can't drop a node into itself: put the dragged node back
					AddDraggedNodeToNewParent(parentNode, draggedKeyNode, index);
					return;
				}
				if (draggedKeyNode.IsFile) {
					if (targetKeyNode.IsFile) {
						if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem)) {
							index = localizationKeyNodes.IndexOf(targetKeyNode);
						}
						else {
							index = localizationKeyNodes.IndexOf(targetKeyNode) + 1;
						}
					}
					AddDraggedNodeToNewParent(parentNode, draggedKeyNode, index);
					return;
				}
				if (draggedKeyNode.IsConstant) {
					if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter) && !targetKeyNode.IsFile) {
						draggedNodeCanBeChildOfTargetNode = false;
					}
					else if (targetKeyNode.GetParentNode(localizationKeyNodes) is { } targetParentNode && !targetParentNode.IsFile) {
						AddDraggedNodeToNewParent(parentNode, draggedKeyNode, index);
						return;
					}
				}
				if (targetKeyNode.IsConstant) {
					draggedNodeCanBeChildOfTargetNode = false;
				}
				KeyNode? newParentNode = null;
				if ((dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter)
						|| targetKeyNode.IsFile) && draggedNodeCanBeChildOfTargetNode) {
					newParentNode = targetKeyNode;
					index = targetKeyNode.Children.Count;
				}
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem)
						&& targetKeyNode.GetParentNode(localizationKeyNodes) is { } before) {
					index = before.Children.IndexOf(targetKeyNode);
					newParentNode = before;
				}
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem)
						&& targetKeyNode.GetParentNode(localizationKeyNodes) is { } after) {
					index = after.Children.IndexOf(targetKeyNode) + 1;
					newParentNode = after;
				}
				if (newParentNode is null) {
					//nowhere valid to land; a non-file node never sits at the root,
					//where Save would not find it. Put it back where it came from
					AddDraggedNodeToNewParent(parentNode, draggedKeyNode, index);
					return;
				}
				AddDraggedNodeToNewParent(newParentNode, draggedKeyNode, index);
				string newBlockKey = draggedKeyNode.IsConstant
					? $"{newParentNode.FullLabel}.${draggedKeyNode.Label}"
					: $"{newParentNode.FullLabel}.{draggedKeyNode.Label}";
				MainWindow.MoveKey(draggedKeyNode.FullLabel, newBlockKey);
				draggedKeyNode.FullLabel = newBlockKey;
				MainWindow.UpdateChildFullLabels(draggedKeyNode.Children, draggedKeyNode.FullLabel);
				MainWindow.IsDirty = true;
			}
		}

		public void AddDraggedNodeToNewParent(KeyNode? newParentNode, KeyNode draggedKeyNode, int index) {
			if (MainWindow is null) {
				throw new InvalidOperationException("No Main Window");
			}
			//organizers all share the ';' label; deduplication is for keys only
			bool deduplicate = draggedKeyNode is not OrganizerNode;
			if (newParentNode is null) {
				if (deduplicate && MainWindow.KeyNodes.Any(keyNode => keyNode.Label == draggedKeyNode.Label)) {
					int indexToRemove = MainWindow.KeyNodes.FindIndex(keyNode => keyNode.Label == draggedKeyNode.Label);
					MainWindow.KeyNodes.RemoveAt(indexToRemove);
					if (index >= indexToRemove) {
						index--;
					}
				}
				MainWindow.KeyNodes.Insert(index, draggedKeyNode);
			}
			else {
				if (deduplicate && newParentNode.Children.Any(keyNode => keyNode.Label == draggedKeyNode.Label)) {
					int indexToRemove = newParentNode.Children.FindIndex(keyNode => keyNode.Label == draggedKeyNode.Label);
					newParentNode.Children.RemoveAt(indexToRemove);
					if (index >= indexToRemove) {
						index--;
					}
				}
				newParentNode.Children.Insert(index, draggedKeyNode);
				if (newParentNode.IsFile && draggedKeyNode.Children.Count == 0) {
					draggedKeyNode.CanBeConstant = true;
				}
				else {
					draggedKeyNode.CanBeConstant = false;
				}
				newParentNode.CanBeConstant = false;
			}
		}

		private static bool TargetIsDraggedItemOrDescendentOfDraggedItem(KeyNode draggedItem, KeyNode targetItem) {
			if (draggedItem == targetItem) {
				return true;
			}

			foreach (KeyNode childItem in draggedItem.Children) {
				if (TargetIsDraggedItemOrDescendentOfDraggedItem(childItem, targetItem)) {
					return true;
				}
			}

			return false;
		}
		public void Dropped(IDropInfo dropInfo) {
			// Handle dropped event
		}

		public void StartDrag(IDragInfo dragInfo) {
			if (dragInfo?.SourceItem is KeyNode sourceItem) {
				dragInfo.Data = sourceItem;
				dragInfo.Effects = DragDropEffects.Move;
			}
		}

		public bool TryCatchOccurredException(Exception exception) {
			return false;
		}
	}

	public class LanguageDragDropHandler : IDragSource, IDropTarget {
		public LanguageManagerViewModel? LanguageManager { get; set; } = null;
		public bool CanStartDrag(IDragInfo dragInfo) {
			return dragInfo?.SourceItem != null;
		}

		public void DragCancelled() {

		}

		public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo) {
		}

		public void DragOver(IDropInfo dropInfo) {
			if (LanguageManager is null) {
				throw new InvalidOperationException("No Main Window");
			}
			dropInfo.Effects = DragDropEffects.Move;
			if ((dropInfo.Data is LanguageEntry draggedItem && dropInfo.TargetItem is LanguageEntry targetItem)) {
				dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
			}
		}

		public void Drop(IDropInfo dropInfo) {
			if (LanguageManager is null) {
				throw new InvalidOperationException("No LanguageManager");
			}
			if (dropInfo.Data is LanguageEntry draggedLanguage && dropInfo.TargetItem is LanguageEntry targetLanguage) {
				if (draggedLanguage.Code == targetLanguage.Code) {
					return;
				}
				int draggedIndex = LanguageManager.KnownLanguages.IndexOf(draggedLanguage);
				int targetIndex = LanguageManager.KnownLanguages.IndexOf(targetLanguage);
				if (targetIndex != draggedIndex) {
					LanguageManager.KnownLanguages.Move(draggedIndex, targetIndex);
				}
			}
			LanguageManager.Parent.IsDirty = true;
		}

		public void Dropped(IDropInfo dropInfo) { }

		public void StartDrag(IDragInfo dragInfo) {
			if (dragInfo?.SourceItem is LanguageEntry sourceItem) {
				dragInfo.Data = sourceItem;
				dragInfo.Effects = DragDropEffects.Move;
			}
		}

		public bool TryCatchOccurredException(Exception exception) {
			return false;
		}
	}
}
