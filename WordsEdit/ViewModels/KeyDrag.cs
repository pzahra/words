using GongSolutions.Wpf.DragDrop;
using System.Windows;

namespace WordsEdit.ViewModels;

/// <summary>
///     Drag and drop in the tree. A drop is "node X becomes child N of node Y":
///     the document is asked to move the keys first and refuses collisions, so
///     the tree only changes when the document did. Nothing is ever deleted to
///     make room.
/// </summary>
public class KeyDrag : IDragSource, IDropTarget {
	public MainWindowViewModel? Vm { get; set; }

	public bool CanStartDrag(IDragInfo dragInfo) {
		//standalone comments move freely; the pinned preamble does not
		return dragInfo?.SourceItem is KeyNode node
			&& (node is not OrganizerNode || node is CommentNode);
	}

	public void DragCancelled() { }

	public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo) {
	}

	public void DragOver(IDropInfo dropInfo) {
		if (Vm is null) {
			throw new InvalidOperationException("No view model");
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
		if (Vm is null) {
			throw new InvalidOperationException("No view model");
		}
		if (dropInfo.Data is not KeyNode dragged || dropInfo.TargetItem is not KeyNode target) {
			return;
		}
		if (dragged is OrganizerNode and not CommentNode) {
			return;
		}
		//an undo or redo during the drag may have taken either out of the tree
		if (!Vm.Tree.KeyNodes.Contains(dragged.Root) || !Vm.Tree.KeyNodes.Contains(target.Root)) {
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
			var roots = Vm.Tree.KeyNodes;
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
		//one Move on the undo stack, whether or not the drop renamed anything
		Vm.Perform(() => Land(dragged, oldParent, newParent, index));
	}

	private Move? Land(KeyNode dragged, KeyNode oldParent, KeyNode newParent, int index) {
		string newFullLabel = $"{newParent.FullLabel}.{WordsOperations.LastSegment(dragged.FullLabel)}";
		if (newParent != oldParent && dragged is not CommentNode) {
			//the document goes first and may refuse: a same-named sibling, or keys
			//already at the destination. Nothing is overwritten to make room
			if (newParent.Children.Any(sibling => sibling.Label == dragged.Label)) {
				Vm!.Dialogs.Tell(Words.Known.Format("tell.node-exists", newParent.FullLabel, dragged.Label));
				return null;
			}
			if (!Vm!.Session.TryMove(dragged.FullLabel, newParent.FullLabel, out var collisions)) {
				Vm.Dialogs.Tell(Words.Known.Format("tell.move-collides", string.Join(", ", collisions)));
				return null;
			}
		}
		Place from = Place.Of(dragged);
		int oldIndex = from.Index;
		if (newParent == oldParent && index > oldIndex) {
			index--;
		}
		if (newParent == oldParent && index == oldIndex) {
			return null; //dropped where it stood
		}
		oldParent.Children.RemoveAt(oldIndex);
		newParent.Children.Insert(index, dragged);
		if (newParent != oldParent) {
			dragged.Relabel(newFullLabel);
		}
		TreeViewModel.UpdateCanBeConstant(newParent.Root);
		if (oldParent.Root != newParent.Root) {
			TreeViewModel.UpdateCanBeConstant(oldParent.Root);
		}
		return new Move(from, Place.Of(dragged), dragged is CommentNode);
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
