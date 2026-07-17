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
	public static readonly AttachedProperty<bool> EnableHyperlinksProperty =
		AvaloniaProperty.RegisterAttached<TextBlock, bool>(
			"EnableHyperlinks",
			typeof(Hyperlink));

	public static bool GetEnableHyperlinks(TextBlock tb) => tb.GetValue(EnableHyperlinksProperty);
	public static void SetEnableHyperlinks(TextBlock tb, bool value) => tb.SetValue(EnableHyperlinksProperty, value);

	static Hyperlink() {
		EnableHyperlinksProperty.Changed.AddClassHandler<TextBlock>(OnEnableHyperlinksChanged);
		ForegroundProperty.OverrideDefaultValue<Hyperlink>(Brushes.Blue);
		TextDecorationsProperty.OverrideDefaultValue<Hyperlink>(global::Avalonia.Media.TextDecorations.Underline);
	}

	public static void RegisterGlobalNavigateHandler(Action<Uri> handler)
		=> NavigateEvent.AddClassHandler<TextBlock>((_, e) => handler(e.Uri));

	public static readonly RoutedEvent<NavigateEventArgs> NavigateEvent
		= RoutedEvent.Register<TextBlock, NavigateEventArgs>("Navigate", RoutingStrategies.Direct);

	public static readonly StyledProperty<Uri?> UriProperty =
			AvaloniaProperty.Register<Hyperlink, Uri?>(nameof(Uri));

	public static readonly StyledProperty<object?> ToolTipProperty =
		AvaloniaProperty.Register<Hyperlink, object?>(nameof(ToolTip));

	public Uri? Uri {
		get => GetValue(UriProperty);
		set => SetValue(UriProperty, value);
	}

	public object? ToolTip {
		get => GetValue(ToolTipProperty);
		set => SetValue(ToolTipProperty, value);
	}

	private TextBlock? owner;

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

public class NavigateEventArgs(RoutedEvent? routedEvent, object? source, Uri uri) : RoutedEventArgs(routedEvent, source) {
	public Uri Uri { get; } = uri;
}