using GongSolutions.Wpf.DragDrop;
using System.Windows;

namespace WordsEdit.ViewModels;

/// <summary>
///     The toolbar editor's drags (SPEC: The toolbar editor, The dialog): a chip along its
///     row, onto another, into the bucket or out of it. The view model says where a chip
///     may go, and moves it there.
/// </summary>
public class ToolbarDrag : IDragSource, IDropTarget {
	public ToolbarsViewModel? Vm { get; set; }

	public bool CanStartDrag(IDragInfo dragInfo) => dragInfo?.SourceItem is ToolbarChip;

	public void StartDrag(IDragInfo dragInfo) {
		if (dragInfo?.SourceItem is ToolbarChip chip) {
			dragInfo.Data = chip;
			dragInfo.Effects = DragDropEffects.Move;
		}
	}

	public void DragCancelled() {
	}

	public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo) {
	}

	//a place the chip may not go takes no drop; a row shows where the chip goes in, the bucket only that it takes it
	public void DragOver(IDropInfo dropInfo) {
		ToolbarsViewModel vm = Vm ?? throw new InvalidOperationException("No view model");
		if (dropInfo.Data is ToolbarChip chip && vm.TryPlace(dropInfo.TargetCollection, out ToolbarRow? to) && vm.Accepts(chip, to)) {
			dropInfo.Effects = DragDropEffects.Move;
			dropInfo.DropTargetAdorner = to is null ? typeof(DropTargetHighlightAdorner) : typeof(DropTargetInsertionAdorner);
		}
		else {
			dropInfo.Effects = DragDropEffects.None;
		}
	}

	public void Drop(IDropInfo dropInfo) {
		ToolbarsViewModel vm = Vm ?? throw new InvalidOperationException("No view model");
		if (dropInfo.Data is ToolbarChip chip && vm.TryPlace(dropInfo.TargetCollection, out ToolbarRow? to)) {
			vm.Move(chip, to, dropInfo.InsertIndex);
		}
	}

	public void Dropped(IDropInfo dropInfo) {
	}

	public bool TryCatchOccurredException(Exception exception) => false;
}
