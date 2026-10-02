using GongSolutions.Wpf.DragDrop;
using System.Windows;

namespace WordsEdit.ViewModels;

/// <summary>
///     Reordering the Language Manager's working copy by drag (SPEC: Languages).
///     The order reaches the session, and every file's table, on OK.
/// </summary>
public class LanguageDrag : IDragSource, IDropTarget {
	public LanguageManagerViewModel? Vm { get; set; }

	public bool CanStartDrag(IDragInfo dragInfo) {
		return dragInfo?.SourceItem != null;
	}

	public void DragCancelled() {
	}

	public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo) {
	}

	public void DragOver(IDropInfo dropInfo) {
		if (Vm is null) {
			throw new InvalidOperationException("No view model");
		}
		dropInfo.Effects = DragDropEffects.Move;
		if (dropInfo.Data is LanguageRow && dropInfo.TargetItem is LanguageRow) {
			dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
		}
	}

	public void Drop(IDropInfo dropInfo) {
		if (Vm is null) {
			throw new InvalidOperationException("No view model");
		}
		if (dropInfo.Data is not LanguageRow dragged || dropInfo.TargetItem is not LanguageRow target || dragged == target) {
			return;
		}
		int from = Vm.Rows.IndexOf(dragged);
		int to = Vm.Rows.IndexOf(target);
		if (from < 0 || to < 0 || from == to) {
			return;
		}
		Vm.Reorder(from, to);
	}

	public void Dropped(IDropInfo dropInfo) { }

	public void StartDrag(IDragInfo dragInfo) {
		if (dragInfo?.SourceItem is LanguageRow row) {
			dragInfo.Data = row;
			dragInfo.Effects = DragDropEffects.Move;
		}
	}

	public bool TryCatchOccurredException(Exception exception) {
		return false;
	}
}
