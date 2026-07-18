using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Tip = Avalonia.Controls.ToolTip;

namespace PatTech.Localization.Avalonia;

/// <summary>
/// Represents a span of text that acts as a clickable hyperlink within a TextBlock.
/// </summary>
/// <remarks>The Hyperlink class enables interactive navigation by associating a URI with a span of text. When the
/// hyperlink is activated, the Navigation event is raised with the associated URI. Hyperlinks are enabled for a
/// TextBlock when the EnableHyperlinks attached property is set to <see langword="true"/>. This class is typically used
/// to provide clickable links in rich text scenarios.</remarks>
public class Hyperlink : Span {
	/// <summary>
	///     Attached property that turns on hyperlink hit-testing for a <see cref="TextBlock"/>:
	///     pointer handlers give links a hand cursor, a pointer-placed tooltip, and click-to-navigate.
	///     A <see cref="Hyperlink"/> sets this on its host automatically when it enters the
	///     logical tree, so setting it by hand is rarely necessary.
	/// </summary>
	public static readonly AttachedProperty<bool> EnableHyperlinksProperty =
		AvaloniaProperty.RegisterAttached<TextBlock, bool>(
			"EnableHyperlinks",
			typeof(Hyperlink));

	/// <summary>Reads <see cref="EnableHyperlinksProperty"/> from a <see cref="TextBlock"/>.</summary>
	public static bool GetEnableHyperlinks(TextBlock tb) => tb.GetValue(EnableHyperlinksProperty);
	/// <summary>Writes <see cref="EnableHyperlinksProperty"/> on a <see cref="TextBlock"/>.</summary>
	public static void SetEnableHyperlinks(TextBlock tb, bool value) => tb.SetValue(EnableHyperlinksProperty, value);

	static Hyperlink() {
		EnableHyperlinksProperty.Changed.AddClassHandler<TextBlock>(OnEnableHyperlinksChanged);
		ForegroundProperty.OverrideDefaultValue<Hyperlink>(Brushes.Blue);
		TextDecorationsProperty.OverrideDefaultValue<Hyperlink>(global::Avalonia.Media.TextDecorations.Underline);
	}

	/// <summary>
	///     Registers the application-wide handler for hyperlink activation: from then on, every
	///     hyperlink clicked in any <see cref="TextBlock"/> passes its <see cref="Uri"/> to
	///     <paramref name="handler"/>. Typically called once at startup to route <c>http:</c>
	///     links to the shell and custom schemes (e.g. <c>appcmd:</c>) to application commands.
	/// </summary>
	/// <remarks>
	///     There is one global handler: calling this again replaces the previous registration,
	///     so a click is never handled twice. Dispose the returned subscription to unregister
	///     without replacing.
	/// </remarks>
	/// <param name="handler">Receives the activated hyperlink's URI.</param>
	/// <returns>A subscription that removes the handler when disposed.</returns>
	public static IDisposable RegisterGlobalNavigateHandler(Action<Uri> handler) {
		globalNavigateSubscription?.Dispose();
		globalNavigateSubscription = NavigateEvent.AddClassHandler<TextBlock>((_, e) => handler(e.Uri));
		return globalNavigateSubscription;
	}

	private static IDisposable? globalNavigateSubscription;

	/// <summary>
	///     Raised on the host <see cref="TextBlock"/> when a hyperlink with a non-null
	///     <see cref="Uri"/> is clicked. Direct routing; no bubbling beyond the TextBlock.
	/// </summary>
	public static readonly RoutedEvent<NavigateEventArgs> NavigateEvent
		= RoutedEvent.Register<TextBlock, NavigateEventArgs>("Navigate", RoutingStrategies.Direct);

	/// <summary>Identifies the <see cref="Uri"/> styled property.</summary>
	public static readonly StyledProperty<Uri?> UriProperty =
			AvaloniaProperty.Register<Hyperlink, Uri?>(nameof(Uri));

	/// <summary>Identifies the <see cref="ToolTip"/> styled property.</summary>
	public static readonly StyledProperty<object?> ToolTipProperty =
		AvaloniaProperty.Register<Hyperlink, object?>(nameof(ToolTip));

	/// <summary>
	///     The navigation target. Clicking the link raises <see cref="NavigateEvent"/> with this
	///     URI; if it is <see langword="null"/>, clicks do nothing.
	/// </summary>
	public Uri? Uri {
		get => GetValue(UriProperty);
		set => SetValue(UriProperty, value);
	}

	/// <summary>
	///     Content shown as a tooltip at the pointer while it hovers over this link.
	///     <see langword="null"/> shows nothing.
	/// </summary>
	public object? ToolTip {
		get => GetValue(ToolTipProperty);
		set => SetValue(ToolTipProperty, value);
	}

	private TextBlock? owner;

	/// <summary>
	///     Creates a hyperlink, underlined and blue in the traditional manner.
	/// </summary>
	public Hyperlink() {
		SetCurrentValue(ForegroundProperty, Brushes.Blue);
		SetCurrentValue(TextDecorationsProperty, global::Avalonia.Media.TextDecorations.Underline);
	}

	/// <inheritdoc />
	protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e) {
		base.OnAttachedToLogicalTree(e);
		owner = this.FindLogicalAncestorOfType<TextBlock>();
		owner?.SetCurrentValue(EnableHyperlinksProperty, true);
	}

	private static void OnEnableHyperlinksChanged(TextBlock tb, AvaloniaPropertyChangedEventArgs e) {
		if ((bool)e.NewValue!) {
			tb.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
			tb.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
		}
		else {
			tb.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
			tb.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
		}
	}

	private static void OnPointerMoved(object? sender, PointerEventArgs e) {
		if (sender is not TextBlock tb)
			return;
		var link = GetHyperlinkAt(tb, e.GetPosition(tb));
		tb.Cursor = link is not null
			? new Cursor(StandardCursorType.Hand)
			: Cursor.Default;
		UpdateToolTip(tb, link);
	}

	private static void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
		if (sender is not TextBlock tb)
			return;

		if (GetHyperlinkAt(tb, e.GetPosition(tb)) is { Uri: not null } link) {
			tb.RaiseEvent(new NavigateEventArgs(NavigateEvent, tb, link.Uri));
			e.Handled = true;
		}
	}

	private static Hyperlink? GetHyperlinkAt(TextBlock tb, Point point) {
		var hit = tb.TextLayout?.HitTestPoint(point);
		if (hit == null)
			return null;

		var index = hit.Value.TextPosition;
		int ic = tb.Inlines?.Count ?? 0;
		for (int i = 0; i < ic; ++i) {
			if (GetInlineAt(tb.Inlines![i], ref index) is { } got) {
				return got as Hyperlink;
			}
		}

		return null;
	}

	private static Inline? GetInlineAt(Inline inline, ref int index) {
		if (inline is Run r) {
			index -= r.Text?.Length ?? 0;
			if (index < 1) return r;
		}
		else if (inline is Span s) {
			for (int i = 0; i < s.Inlines.Count; ++i) {
				if (GetInlineAt(s.Inlines[i], ref index) is { } got) {
					if (got is Hyperlink) return got;
					return s;
				}
			}
		}
		return null;
	}

	private static void UpdateToolTip(TextBlock tb, Hyperlink? link) {
		if (link?.ToolTip == null) {
			if (Tip.GetIsOpen(tb)) {
				Tip.SetIsOpen(tb, false);
			}
			return;
		}

		if (Equals(Tip.GetTip(tb), link.ToolTip))
			return;

		if (Tip.GetIsOpen(tb))
			Tip.SetIsOpen(tb, false);

		Tip.SetPlacement(tb, PlacementMode.Pointer);
		Tip.SetTip(tb, link.ToolTip);

		Tip.SetIsOpen(tb, true);
	}
}

/// <summary>
/// Event arguments for <see cref="Hyperlink.NavigateEvent"/>, carrying the activated link's URI.
/// </summary>
/// <param name="routedEvent">The routed event being raised.</param>
/// <param name="source">The object raising the event, typically the host <see cref="TextBlock"/>.</param>
/// <param name="uri">The activated hyperlink's URI.</param>
public class NavigateEventArgs(RoutedEvent? routedEvent, object? source, Uri uri) : RoutedEventArgs(routedEvent, source) {
	/// <summary>The activated hyperlink's URI.</summary>
	public Uri Uri { get; } = uri;
}