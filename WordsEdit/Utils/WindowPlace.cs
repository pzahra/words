using System.Windows;

namespace WordsEdit.Utils;

/// <summary>
///     A window's size in its normal state, and whether it was maximized over it:
///     what <see cref="EditorConfig.Window"/> remembers between runs.
/// </summary>
public readonly record struct WindowPlace(double Width, double Height, bool Maximized) {
	/// <summary>The window's place now; a minimized window is remembered at its normal size.</summary>
	public static WindowPlace Of(Window window) {
		Size size = window.RestoreBounds is { IsEmpty: false } bounds ? bounds.Size : new Size(window.Width, window.Height);
		return new WindowPlace(size.Width, size.Height, window.WindowState == WindowState.Maximized);
	}

	/// <summary>Gives a window not yet shown this place, cut to fit the screen's room.</summary>
	public void ApplyTo(Window window) {
		Rect room = SystemParameters.WorkArea;
		WindowPlace fit = Fit(window.MinWidth, window.MinHeight, room.Width, room.Height);
		window.Width = fit.Width;
		window.Height = fit.Height;
		if (fit.Maximized) {
			window.WindowState = WindowState.Maximized;
		}
	}

	/// <summary>The place cut to fit: no smaller than the window allows, no larger than the room.</summary>
	public WindowPlace Fit(double minWidth, double minHeight, double roomWidth, double roomHeight)
		=> this with {
			Width = Math.Max(minWidth, Math.Min(Width, roomWidth)),
			Height = Math.Max(minHeight, Math.Min(Height, roomHeight)),
		};
}
