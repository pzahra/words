using GongSolutions.Wpf.DragDrop;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels {

	public class DragDropKeysViewModel : IDragSource, IDropTarget {
		public MainWindowViewModel? MainWindow { get; set; } = null;
		public bool CanStartDrag(IDragInfo dragInfo) {
			return dragInfo?.SourceItem != null;
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
					if (targetKeyNode.GetParentNode(MainWindow.LocalizationKeyNodes) is not null) {
						KeyNode? targetParentNode = targetKeyNode.GetParentNode(MainWindow.LocalizationKeyNodes)
							?? throw new InvalidDataException("targetKeyNode parent became null between checks");
						if (!targetParentNode.IsFile) {
							dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
							return;
						}
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
				KeyNode? parentNode;
				int index;
				ObservableCollection<KeyNode> localizationKeyNodes = MainWindow.LocalizationKeyNodes;
				parentNode = draggedKeyNode.GetParentNode(localizationKeyNodes);
				bool draggedNodeCanBeChildOfTargetNode = true;
				if (parentNode is null) {
					index = localizationKeyNodes.IndexOf(draggedKeyNode);
					localizationKeyNodes.Remove(draggedKeyNode);
				}
				else {
					index = parentNode.Children.IndexOf(draggedKeyNode);
					parentNode.Children.Remove(draggedKeyNode);
					KeyNode? grandParentNode = parentNode.GetParentNode(MainWindow.LocalizationKeyNodes);
					if (grandParentNode is not null && grandParentNode.IsFile && parentNode.Children.Count == 0) {
						parentNode.CanBeConstant = true;
					}
					else {
						parentNode.CanBeConstant = false;
					}
				}
				if (TargetIsDraggedItemOrDescendentOfDraggedItem(draggedKeyNode, targetKeyNode)) {
					AddDraggedNodeToNewParent(parentNode, targetKeyNode, index);
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
					else {
						if (targetKeyNode.GetParentNode(localizationKeyNodes) is not null) {
							KeyNode? targetParentNode = targetKeyNode.GetParentNode(localizationKeyNodes)
								?? throw new InvalidDataException("targetKeyNode parent became null between checks");
							if (!targetParentNode.IsFile) {
								AddDraggedNodeToNewParent(parentNode, draggedKeyNode, index);
								return;
							}
						}
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
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem)) {
					KeyNode? targetParentNode = targetKeyNode.GetParentNode(localizationKeyNodes)
						?? throw new InvalidDataException("targetKeyNode parent became null between checks");
					index = targetParentNode.Children.IndexOf(targetKeyNode);
					newParentNode = targetParentNode;
				}
				else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem)) {
					KeyNode? targetParentNode = targetKeyNode.GetParentNode(localizationKeyNodes)
						?? throw new InvalidDataException("targetKeyNode parent became null between checks");
					index = targetParentNode.Children.IndexOf(targetKeyNode) + 1;
					newParentNode = targetParentNode;
				}
				AddDraggedNodeToNewParent(newParentNode, draggedKeyNode, index);
				string newBlockKey;
				if (newParentNode is null) {
					newBlockKey = draggedKeyNode.Label;
				}
				else if (draggedKeyNode.IsConstant) {
					newBlockKey = $"{newParentNode.FullLabel}.${draggedKeyNode.Label}";
				}
				else {
					string[] newBlockKeyParts = newParentNode.FullLabel.Split('.');
					newBlockKeyParts = [.. newBlockKeyParts, draggedKeyNode.Label];
					newBlockKey = string.Join('.', newBlockKeyParts);
				}
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
			if (newParentNode is null) {
				if (MainWindow.LocalizationKeyNodes.Any(keyNode => keyNode.Label == draggedKeyNode.Label)) {
					int indexToRemove = MainWindow.LocalizationKeyNodes.FindIndex(keyNode => keyNode.Label == draggedKeyNode.Label);
					MainWindow.LocalizationKeyNodes.RemoveAt(indexToRemove);
					if (index >= indexToRemove) {
						index--;
					}
				}
				MainWindow.LocalizationKeyNodes.Insert(index, draggedKeyNode);
			}
			else {
				if (newParentNode.Children.Any(keyNode => keyNode.Label == draggedKeyNode.Label)) {
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

	public class DragDropLanguagesViewModel : IDragSource, IDropTarget {
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
			if ((dropInfo.Data is LocalizationLanguage draggedItem && dropInfo.TargetItem is LocalizationLanguage targetItem)) {
				dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
			}
		}

		public void Drop(IDropInfo dropInfo) {
			if (LanguageManager is null) {
				throw new InvalidOperationException("No LanguageManager");
			}
			if (dropInfo.Data is LocalizationLanguage draggedLanguage && dropInfo.TargetItem is LocalizationLanguage targetLanguage) {
				if (draggedLanguage.Code == targetLanguage.Code) {
					return;
				}
				int draggedIndex = LanguageManager.LocalizationLanguages.IndexOf(draggedLanguage);
				int targetIndex = LanguageManager.LocalizationLanguages.IndexOf(targetLanguage);
				if (targetIndex != draggedIndex) {
					LanguageManager.LocalizationLanguages.Move(draggedIndex, targetIndex);
				}
			}
			LanguageManager.MainWindowViewModel.IsDirty = true;
		}

		public void Dropped(IDropInfo dropInfo) { }

		public void StartDrag(IDragInfo dragInfo) {
			if (dragInfo?.SourceItem is LocalizationLanguage sourceItem) {
				dragInfo.Data = sourceItem;
				dragInfo.Effects = DragDropEffects.Move;
			}
		}

		public bool TryCatchOccurredException(Exception exception) {
			return false;
		}
	}
}
