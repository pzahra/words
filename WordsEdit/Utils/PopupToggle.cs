using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WordsEdit.Utils;

/// <summary>
///     A toggle that opens a popup closing on a click anywhere else: the filters
///     and each pane's plural form. The popup closes on a mouse-down outside it,
///     before that mouse-down reaches what is under it; when that is the toggle,
///     the click would reopen the popup, so the toggle swallows the one click
///     that closed it. Set <see cref="PopupProperty"/> on the toggle.
/// </summary>
public static class PopupToggle {
	public static readonly DependencyProperty PopupProperty = DependencyProperty.RegisterAttached(
		"Popup", typeof(Popup), typeof(PopupToggle), new PropertyMetadata(null, PopupChanged));

	//set while the popup's last close came from a mouse-down on its own toggle
	private static readonly DependencyProperty ClosedUnderProperty = DependencyProperty.RegisterAttached(
		"ClosedUnder", typeof(bool), typeof(PopupToggle), new PropertyMetadata(false));
	//the handler the popup's Closed carries for this toggle, so a change of popup lets it go
	private static readonly DependencyProperty ClosedHandlerProperty = DependencyProperty.RegisterAttached(
		"ClosedHandler", typeof(EventHandler), typeof(PopupToggle), new PropertyMetadata(null));

	public static Popup? GetPopup(ToggleButton toggle) => (Popup?)toggle.GetValue(PopupProperty);
	public static void SetPopup(ToggleButton toggle, Popup? value) => toggle.SetValue(PopupProperty, value);

	private static void PopupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not ToggleButton toggle) {
			return;
		}
		if (e.OldValue is Popup old && toggle.GetValue(ClosedHandlerProperty) is EventHandler handler) {
			old.Closed -= handler;
			toggle.PreviewMouseLeftButtonDown -= SwallowReopen;
		}
		if (e.NewValue is Popup popup) {
			EventHandler closed = (_, _) => toggle.SetValue(ClosedUnderProperty, new Rect(toggle.RenderSize).Contains(Mouse.GetPosition(toggle)));
			toggle.SetValue(ClosedHandlerProperty, closed);
			popup.Closed += closed;
			toggle.PreviewMouseLeftButtonDown += SwallowReopen;
		}
	}

	private static void SwallowReopen(object sender, MouseButtonEventArgs e) {
		if (sender is ToggleButton toggle && (bool)toggle.GetValue(ClosedUnderProperty)) {
			toggle.SetValue(ClosedUnderProperty, false);
			e.Handled = true;
		}
	}
}
