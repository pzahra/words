using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Navigation;
using WordsEdit.ViewModels;
using WordsEdit.Views;

namespace WordsEdit.Utils;

/// <summary>The side of its control a help bubble opens on; its tail points back across it.</summary>
public enum BubbleSide { Right, Left, Below, Above }

/// <summary>
///     Where the help bubbles show (SPEC: Help bubbles, When one shows): the control a
///     topic points at carries it (<c>uc:Bubble.Topic="tree"</c>), and tells
///     <see cref="Bubbles"/> when it comes on screen and leaves it. When a topic
///     opens, its bubble opens beside its control, on whichever side the screen has
///     room, never over it; it follows the window as it moves or resizes, and hides
///     while the window is not the active one. A bubble's links are the editor's own
///     (<see cref="HandleLinks"/>).
/// </summary>
public static class Bubble {
	public static readonly DependencyProperty TopicProperty = DependencyProperty.RegisterAttached(
		"Topic", typeof(string), typeof(Bubble), new PropertyMetadata(null, TopicChanged));

	/// <summary>Inside a bubble: inherited by the page's links, so the link handler knows where a click came from.</summary>
	public static readonly DependencyProperty InBubbleProperty = DependencyProperty.RegisterAttached(
		"InBubble", typeof(bool), typeof(Bubble), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

	/// <summary>The side a control's bubble opens on when it has room there: below a menu, say, rather than over the bar beside it.</summary>
	public static readonly DependencyProperty PreferProperty = DependencyProperty.RegisterAttached(
		"Prefer", typeof(BubbleSide?), typeof(Bubble), new PropertyMetadata(null));

	public static BubbleSide? GetPrefer(DependencyObject element) => (BubbleSide?)element.GetValue(PreferProperty);
	public static void SetPrefer(DependencyObject element, BubbleSide? value) => element.SetValue(PreferProperty, value);

	public static string? GetTopic(DependencyObject element) => (string?)element.GetValue(TopicProperty);
	public static void SetTopic(DependencyObject element, string? value) => element.SetValue(TopicProperty, value);
	public static bool GetInBubble(DependencyObject element) => (bool)element.GetValue(InBubbleProperty);
	public static void SetInBubble(DependencyObject element, bool value) => element.SetValue(InBubbleProperty, value);

	//the room kept between a bubble's tail and its control
	private const double Gap = 2;

	//each topic's controls on screen, the latest last: both previews carry theirs, and the
	//bubble points at the one that came last
	private static readonly Dictionary<string, List<FrameworkElement>> anchors = [];
	private static HelpBubbles? bubbles;
	private static Popup? popup;
	private static HelpBubbleView? view;
	//the window the open bubble's control is in, followed while it moves
	private static Window? window;
	private static Action<Uri>? follow;

	/// <summary>The bubbles the controls report to and the popup shows: the editor's, set once at startup.</summary>
	public static HelpBubbles? Bubbles {
		get => bubbles;
		set {
			if (bubbles is not null) {
				bubbles.PropertyChanged -= OnBubblesChanged;
			}
			bubbles = value;
			if (bubbles is not null) {
				bubbles.PropertyChanged += OnBubblesChanged;
				foreach (string id in anchors.Keys) {
					bubbles.Anchor(id, onScreen: true);
				}
			}
			Place();
		}
	}

	/// <summary>
	///     Routes a click on a bubble's link to <paramref name="handler"/>, and every
	///     other link on to whoever handles it after. Called before the library's
	///     navigate handler is registered, so this class handler runs first; called
	///     again, it replaces the handler.
	/// </summary>
	public static void HandleLinks(Action<Uri> handler) {
		if (follow is null) {
			EventManager.RegisterClassHandler(typeof(Hyperlink), Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));
		}
		follow = handler;
	}

	private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e) {
		if (sender is DependencyObject link && GetInBubble(link) && follow is { } handler) {
			handler(e.Uri);
			e.Handled = true;
		}
	}

	/// <summary>
	///     The side a bubble of <paramref name="bubble"/>'s size opens on beside
	///     <paramref name="target"/>, within <paramref name="area"/>: the side
	///     <paramref name="prefer"/> names when it has room, else the right, the left,
	///     below or above, the first with room for it whole; when none has, the one
	///     with the most.
	/// </summary>
	public static BubbleSide Choose(Rect target, Size bubble, Rect area, BubbleSide? prefer = null) {
		(BubbleSide Side, double Room, double Needed)[] sides = [
			(BubbleSide.Right, area.Right - target.Right, bubble.Width),
			(BubbleSide.Left, target.Left - area.Left, bubble.Width),
			(BubbleSide.Below, area.Bottom - target.Bottom, bubble.Height),
			(BubbleSide.Above, target.Top - area.Top, bubble.Height),
		];
		sides = [.. sides.OrderBy(side => side.Side != prefer)];
		foreach (var (side, room, needed) in sides) {
			if (room >= needed + Gap) {
				return side;
			}
		}
		return sides.MaxBy(side => side.Room).Side;
	}

	private static void TopicChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not FrameworkElement element) {
			return;
		}
		if (e.OldValue is string old) {
			element.Loaded -= OnAnchorChanged;
			element.Unloaded -= OnAnchorChanged;
			element.IsVisibleChanged -= OnAnchorVisibleChanged;
			Report(element, old, onScreen: false);
		}
		if (e.NewValue is string) {
			element.Loaded += OnAnchorChanged;
			element.Unloaded += OnAnchorChanged;
			element.IsVisibleChanged += OnAnchorVisibleChanged;
			Report(element);
		}
	}

	private static void OnAnchorChanged(object sender, RoutedEventArgs e) => Report((FrameworkElement)sender);
	private static void OnAnchorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Report((FrameworkElement)sender);

	private static void Report(FrameworkElement element) {
		if (GetTopic(element) is { } id) {
			Report(element, id, element.IsLoaded && element.IsVisible);
		}
	}

	//a control on screen anchors its topic; the topic lets go once its last control leaves
	private static void Report(FrameworkElement element, string id, bool onScreen) {
		if (!anchors.TryGetValue(id, out List<FrameworkElement>? controls)) {
			anchors[id] = controls = [];
		}
		controls.Remove(element);
		if (onScreen) {
			controls.Add(element);
		}
		else if (controls.Count == 0) {
			anchors.Remove(id);
		}
		bubbles?.Anchor(id, controls.Count > 0);
		Place();
	}

	private static void OnBubblesChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is nameof(HelpBubbles.Open)) {
			Place();
		}
	}

	//the open topic's bubble beside its control, or none
	private static void Place() {
		if (bubbles?.Open is not { } topic || !anchors.TryGetValue(topic.Id, out List<FrameworkElement>? controls) || controls.Count == 0) {
			if (popup is not null) {
				popup.IsOpen = false;
			}
			Follow(null);
			return;
		}
		view ??= new HelpBubbleView();
		view.DataContext = bubbles;
		popup ??= new Popup { Child = view, AllowsTransparency = true, StaysOpen = true, Focusable = false, PopupAnimation = PopupAnimation.Fade };
		FrameworkElement target = controls[^1];
		popup.PlacementTarget = target;
		Window? owner = Window.GetWindow(target);
		Follow(owner);
		Position();
		popup.IsOpen = owner?.IsActive ?? true;
	}

	//the window the bubble follows: as it moves or resizes, and as it gains and loses the focus
	private static void Follow(Window? owner) {
		if (window == owner) {
			return;
		}
		if (window is not null) {
			window.LocationChanged -= OnWindowMoved;
			window.SizeChanged -= OnWindowMoved;
			window.Activated -= OnWindowActivated;
			window.Deactivated -= OnWindowActivated;
		}
		window = owner;
		if (window is not null) {
			window.LocationChanged += OnWindowMoved;
			window.SizeChanged += OnWindowMoved;
			window.Activated += OnWindowActivated;
			window.Deactivated += OnWindowActivated;
		}
	}

	private static void OnWindowMoved(object? sender, EventArgs e) {
		if (popup is { IsOpen: true }) {
			Position();
			//a popup moves only when its offsets change: a nudge there and back moves it with its control
			popup.HorizontalOffset += 1;
			popup.HorizontalOffset -= 1;
		}
	}

	private static void OnWindowActivated(object? sender, EventArgs e) {
		if (popup is not null) {
			popup.IsOpen = window?.IsActive == true && bubbles?.Open is not null;
		}
	}

	//the side the screen has room on, the bubble centred along its control there, its tail toward it
	private static void Position() {
		if (popup?.PlacementTarget is not FrameworkElement target || view is null || PresentationSource.FromVisual(target) is not { CompositionTarget: { } composition } source) {
			return;
		}
		view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
		Size size = view.DesiredSize;
		var fromDevice = composition.TransformFromDevice;
		Point topLeft = fromDevice.Transform(target.PointToScreen(new Point(0, 0)));
		var rect = new Rect(topLeft, new Size(target.ActualWidth, target.ActualHeight));
		Rect area = WorkArea(source, fromDevice);
		BubbleSide side = Choose(rect, size, area, GetPrefer(target));
		view.Side = side;
		//centred along the control, but kept on the screen, the tail sliding to meet the control's middle
		bool across = side is BubbleSide.Right or BubbleSide.Left;
		double along = across
			? Along(rect.Top, rect.Height, size.Height, area.Top, area.Bottom)
			: Along(rect.Left, rect.Width, size.Width, area.Left, area.Right);
		view.TailOffset = (across ? rect.Height : rect.Width) / 2 - along;
		(popup.Placement, popup.HorizontalOffset, popup.VerticalOffset) = side switch {
			BubbleSide.Right => (PlacementMode.Right, Gap, along),
			BubbleSide.Left => (PlacementMode.Left, -Gap, along),
			BubbleSide.Below => (PlacementMode.Bottom, along, Gap),
			_ => (PlacementMode.Top, along, -Gap),
		};
	}

	/// <summary>
	///     Where a bubble <paramref name="length"/> long starts along a control at
	///     <paramref name="start"/>, <paramref name="span"/> long, as an offset from the
	///     control's start: centred on it, unless that runs past
	///     <paramref name="min"/> or <paramref name="max"/>, when it is pushed back inside.
	/// </summary>
	public static double Along(double start, double span, double length, double min, double max) {
		double at = start + (span - length) / 2;
		at = Math.Max(min, Math.Min(at, max - length));
		return at - start;
	}

	//the work area of the monitor the control's window is on, in the window's units
	private static Rect WorkArea(PresentationSource source, System.Windows.Media.Matrix fromDevice) {
		if (source is HwndSource { Handle: var handle } && handle != IntPtr.Zero) {
			var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
			if (GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info)) {
				Point topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
				Point bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
				return new Rect(topLeft, bottomRight);
			}
		}
		return SystemParameters.WorkArea;
	}

	private const int MonitorDefaultToNearest = 2;

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRect {
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MonitorInfo {
		public int Size;
		public NativeRect Monitor;
		public NativeRect Work;
		public int Flags;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
