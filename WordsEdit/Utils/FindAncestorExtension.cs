using System.Windows.Data;
using System.Windows.Markup;

namespace WordsEdit.Utils {
	[MarkupExtensionReturnType(typeof(RelativeSource))]
	public class FindAncestorExtension(Type ancestorType, int ancestorLevel)
		: RelativeSource(RelativeSourceMode.FindAncestor, ancestorType, ancestorLevel) {
		public FindAncestorExtension(Type ancestorType) : this(ancestorType, 1) { }
	}
}
