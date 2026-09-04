using GongSolutions.Wpf.DragDrop;
using System.Windows;

namespace WordsEdit.ViewModels;

/// <summary>
///     Reordering the language table by drag in the Language Manager. The new
///     order goes to the session, which passes it to every file's table.
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
		if (dropInfo.Data is LanguageEntry && dropInfo.TargetItem is LanguageEntry) {
			dropInfo.DropTargetAdorner = typeof(DropTargetInsertionAdorner);
		}
	}

	public void Drop(IDropInfo dropInfo) {
		if (Vm is null) {
			throw new InvalidOperationException("No view model");
		}
		if (dropInfo.Data is not LanguageEntry dragged || dropInfo.TargetItem is not LanguageEntry target || dragged.Code == target.Code) {
			return;
		}
		int draggedIndex = Vm.KnownLanguages.IndexOf(dragged);
		int targetIndex = Vm.KnownLanguages.IndexOf(target);
		if (targetIndex == draggedIndex) {
			return;
		}
		//the new order reaches every file's table, so the writer sees it
		Vm.Parent.Session.Languages.Reorder(draggedIndex, targetIndex);
		Vm.Parent.IsDirty = true;
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
