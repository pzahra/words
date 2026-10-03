using System.Windows.Input;

namespace WordsEdit.Utils;

/// <summary>
///     A mouse button pressed, as a gesture: the back and forward buttons
///     (<see cref="MouseButton.XButton1"/>, <see cref="MouseButton.XButton2"/>), which
///     <see cref="MouseGesture"/>'s actions cannot name.
/// </summary>
public sealed class MouseButtonGesture(MouseButton button) : InputGesture {
	public MouseButton Button { get; } = button;

	public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
		=> inputEventArgs is MouseButtonEventArgs { ChangedButton: var changed } args
			&& changed == Button
			&& args.RoutedEvent == Mouse.MouseDownEvent;
}
