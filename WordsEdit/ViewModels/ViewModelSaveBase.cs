using PatTech.Localization.Authoring;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace WordsEdit.ViewModels {
	public abstract class ViewModelSaveBase : ViewModelBase {
		public string TitleMarked => IsDirty ? Title + " *" : Title;
		public string Title { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); } = "";
		public bool IsDirty { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); }

		public abstract void Save();

		protected bool ChangeProperty<T>(
			[NotNullIfNotNull(nameof(newValue))] ref T field,
			T newValue,
			bool dirty = false,
			[CallerMemberName] string propertyName = ""
		) {
			if (ChangeProperty(ref field, newValue, propertyName)) {
				IsDirty |= dirty;
				return true;
			}
			return false;
		}
	}
}
