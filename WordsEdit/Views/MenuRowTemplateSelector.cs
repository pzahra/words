using System.Windows;
using System.Windows.Controls;
using WordsEdit.ViewModels;

namespace WordsEdit.Views;

/// <summary>
///     Picks a menu's container template by the row's type (SPEC: Menu and
///     toolbars). The templates are keyed by name in CommandTemplates.xaml: an
///     ItemContainerTemplate and a DataTemplate for the same type would share
///     one implicit key, and the toolbars already hold the DataTemplates.
/// </summary>
public sealed class MenuRowTemplateSelector : ItemContainerTemplateSelector {
	public override DataTemplate? SelectTemplate(object item, ItemsControl parentItemsControl) {
		string? key = item switch {
			MenuGroup => "MenuGroupTemplate",
			ToggleItem => "ToggleMenuTemplate",
			CommandItem => "CommandMenuTemplate",
			ChoiceItem => "ChoiceMenuTemplate",
			Choice => "ChoiceOptionTemplate",
			MenuBreak => "MenuBreakTemplate",
			_ => null,
		};
		return key is null ? null : parentItemsControl.FindResource(key) as DataTemplate;
	}
}
