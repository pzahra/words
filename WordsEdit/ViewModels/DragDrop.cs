using GongSolutions.Wpf.DragDrop;
using System.Windows;

namespace WordsEdit.ViewModels;

/// <summary>
///     Drag and drop in the tree. A drop is "node X becomes child N of node Y":
///     the document is asked to move the keys first and refuses collisions, so
///     the tree only changes when the document did. Nothing is ever deleted to
///     make room.
/// </summary>
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
		if (dropInfo.Data is not KeyNode dragged || dropInfo.TargetItem is not KeyNode target) {
			return;
		}
		if (dragged is OrganizerNode and not CommentNode) {
			dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
			return;
		}
		bool center = dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter);
		if (target is OrganizerNode && center) {
			//comments take no children; only before/after makes sense
			dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
			return;
		}
		if (dragged.IsFile && !target.IsFile) {
			dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
			return;
		}
		if (dragged.IsConstant && target.Parent is { IsFile: false }) {
			//constants sit directly under a file
			dropInfo.DropTargetAdorner = typeof(DropTargetAdorner);
			return;
		}
		if (dragged.Contains(target)) {
			dropInfo.DropTargetAdorner = dragged == target ? null : typeof(DropTargetAdorner);
		}
		else if (center && CanBeChildOf(dragged, target)) {
			dropInfo.DropTargetAdorner = typeof(DropTargetHighlightAdorner);
		}
		else if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem)
				|| dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem)) {
			dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
		}
	}

	//a constant lands only under a file, nothing lands under a constant
	private static bool CanBeChildOf(KeyNode dragged, KeyNode target)
		=> !dragged.IsFile && !target.IsConstant && (!dragged.IsConstant || target.IsFile);

	public void Drop(IDropInfo dropInfo) {
		if (MainWindow is null) {
			throw new InvalidOperationException("No Main Window");
		}
		if (dropInfo.Data is not KeyNode dragged || dropInfo.TargetItem is not KeyNode target) {
			return;
		}
		if (dragged is OrganizerNode and not CommentNode) {
			return;
		}
		bool center = dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter);
		bool after = dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem);
		if ((target is OrganizerNode && center) || dragged.Contains(target)) {
			return;
		}
		if (dragged.IsFile) {
			//files only reorder among themselves; their order is lookup precedence, not document content
			if (!target.IsFile) {
				return;
			}
			var roots = MainWindow.KeyNodes;
			int from = roots.IndexOf(dragged);
			int to = roots.IndexOf(target) + (after ? 1 : 0);
			if (to > from) {
				to--;
			}
			if (to != from) {
				roots.Move(from, to);
			}
			return;
		}
		//where does it land?
		KeyNode newParent;
		int index;
		if ((center || target.IsFile) && CanBeChildOf(dragged, target)) {
			newParent = target;
			index = target.Children.Count;
		}
		else if (target.Parent is { } beside && CanBeChildOf(dragged, beside)) {
			newParent = beside;
			index = beside.Children.IndexOf(target) + (after ? 1 : 0);
		}
		else {
			return; //nowhere valid to land; nothing has changed
		}
		if (dragged.Parent is not { } oldParent) {
			return; //a non-file node at the root is a broken invariant; leave it be
		}
		string newFullLabel = $"{newParent.FullLabel}.{WordsOperations.LastSegment(dragged.FullLabel)}";
		if (newParent != oldParent && dragged is not CommentNode) {
			//the document goes first and may refuse: a same-named sibling, or keys
			//already at the destination. Nothing is overwritten to make room
			if (newParent.Children.Any(sibling => sibling.Label == dragged.Label)) {
				MainWindow.Dialogs.Tell($"'{newParent.FullLabel}' already has a node named '{dragged.Label}'.");
				return;
			}
			if (!MainWindow.Session.TryMove(dragged.FullLabel, newParent.FullLabel, out var collisions)) {
				MainWindow.Dialogs.Tell($"Cannot move: {string.Join(", ", collisions)} already exist.");
				return;
			}
		}
		int oldIndex = oldParent.Children.IndexOf(dragged);
		oldParent.Children.RemoveAt(oldIndex);
		if (newParent == oldParent && index > oldIndex) {
			index--;
		}
		newParent.Children.Insert(index, dragged);
		if (newParent != oldParent) {
			dragged.Relabel(newFullLabel);
		}
		MainWindowViewModel.UpdateCanBeConstant(newParent.Root);
		if (oldParent.Root != newParent.Root) {
			MainWindowViewModel.UpdateCanBeConstant(oldParent.Root);
		}
		MainWindow.IsDirty = true;
	}

	public void Dropped(IDropInfo dropInfo) {
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
			throw new InvalidOperationException("No LanguageManager");
		}
		dropInfo.Effects = DragDropEffects.Move;
		if (dropInfo.Data is LanguageEntry && dropInfo.TargetItem is LanguageEntry) {
			dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
		}
	}

	public void Drop(IDropInfo dropInfo) {
		if (LanguageManager is null) {
			throw new InvalidOperationException("No LanguageManager");
		}
		if (dropInfo.Data is not LanguageEntry dragged || dropInfo.TargetItem is not LanguageEntry target || dragged.Code == target.Code) {
			return;
		}
		int draggedIndex = LanguageManager.KnownLanguages.IndexOf(dragged);
		int targetIndex = LanguageManager.KnownLanguages.IndexOf(target);
		if (targetIndex == draggedIndex) {
			return;
		}
		//the new order reaches every file's table, so the writer sees it
		LanguageManager.Parent.Session.Languages.Reorder(draggedIndex, targetIndex);
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
