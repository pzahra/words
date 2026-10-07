using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WordsEdit.Utils;

/// <summary>
///     The theme keeps room for a toolbar's overflow button even while nothing
///     overflows — hidden, not collapsed — and a margin beside it.
///     <see cref="CollapseProperty"/> gives that room back until the toolbar does
///     overflow, when the button returns; the strip beside the search box needs it most.
/// </summary>
public static class ToolBarOverflow {
	public static readonly DependencyProperty CollapseProperty = DependencyProperty.RegisterAttached(
		"Collapse", typeof(bool), typeof(ToolBarOverflow), new PropertyMetadata(false, CollapseChanged));

	public static bool GetCollapse(DependencyObject element) => (bool)element.GetValue(CollapseProperty);
	public static void SetCollapse(DependencyObject element, bool value) => element.SetValue(CollapseProperty, value);

	//one handler, taken off before it goes on, so setting it again never stacks
	//another; turned off, the parts go back to the theme's
	private static void CollapseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not ToolBar bar) {
			return;
		}
		bar.Loaded -= OnLoaded;
		if ((bool)e.NewValue) {
			bar.Loaded += OnLoaded;
			if (bar.IsLoaded) {
				Collapse(bar);
			}
		}
		else {
			Restore(bar);
		}
	}

	private static void OnLoaded(object sender, RoutedEventArgs e) => Collapse((ToolBar)sender);

	private static void Restore(ToolBar bar) {
		if (bar.Template?.FindName("OverflowGrid", bar) is FrameworkElement overflow) {
			BindingOperations.ClearBinding(overflow, UIElement.VisibilityProperty);
		}
		if (bar.Template?.FindName("MainPanelBorder", bar) is FrameworkElement panel) {
			BindingOperations.ClearBinding(panel, FrameworkElement.MarginProperty);
		}
	}

	//the theme's template parts, by the names WPF's own toolbar template gives them
	private static void Collapse(ToolBar bar) {
		if (bar.Template?.FindName("OverflowGrid", bar) is FrameworkElement overflow) {
			Follow(bar, overflow, UIElement.VisibilityProperty, Visibility.Visible, Visibility.Collapsed);
		}
		if (bar.Template?.FindName("MainPanelBorder", bar) is FrameworkElement panel) {
			Follow(bar, panel, FrameworkElement.MarginProperty, panel.Margin, new Thickness(0));
		}
	}

	//the part takes the theme's value while the toolbar overflows, the other while it does not
	private static void Follow(ToolBar bar, FrameworkElement part, DependencyProperty property, object overflowing, object not) {
		if (!BindingOperations.IsDataBound(part, property)) {
			part.SetBinding(property, new Binding(nameof(ToolBar.HasOverflowItems)) { Source = bar, Converter = new Either(overflowing, not) });
		}
	}

	private sealed class Either(object yes, object no) : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? yes : no;
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
	}
}
