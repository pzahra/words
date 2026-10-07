using System.Windows;

namespace WordsEdit.Utils;

/// <summary>
///     A window's size in its normal state, and whether it was maximized over it:
///     what <see cref="EditorConfig.Window"/> remembers between runs.
/// </summary>
public readonly record struct WindowPlace(double Width, double Height, bool Maximized) {
	/// <summary>
	///     The window's place now. A minimized window is remembered at its normal
	///     size, and maximized when it was maximized as it went to the taskbar —
	///     where it would come back to (<paramref name="wasMaximized"/>; see
	///     <see cref="Track"/>).
	/// </summary>
	public static WindowPlace Of(Window window, bool wasMaximized = false) {
		Size size = window.RestoreBounds is { IsEmpty: false } bounds ? bounds.Size : new Size(window.Width, window.Height);
		bool maximized = window.WindowState switch {
			WindowState.Maximized => true,
			WindowState.Minimized => wasMaximized,
			_ => false,
		};
		return new WindowPlace(size.Width, size.Height, maximized);
	}

	/// <summary>
	///     Follows <paramref name="window"/>'s state from now on, so its place can
	///     be read at any moment — minimized from maximized too.
	/// </summary>
	public static Func<WindowPlace> Track(Window window) {
		bool wasMaximized = window.WindowState == WindowState.Maximized;
		window.StateChanged += (_, _) => {
			if (window.WindowState != WindowState.Minimized) {
				wasMaximized = window.WindowState == WindowState.Maximized;
			}
		};
		return () => Of(window, wasMaximized);
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
