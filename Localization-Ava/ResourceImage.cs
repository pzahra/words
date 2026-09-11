using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     The visual a <c>staticres:</c> or <c>dynres:</c> image resolves to: a host that
///     looks its resource up from where it lands in the tree — the way
///     <c>{StaticResource}</c> and <c>{DynamicResource}</c> do, through the framework's
///     own resource lookup — and renders the value through
///     <see cref="ResourceVisualConverter"/> once it arrives. Static keeps the first
///     value it sees; dynamic follows every change, a theme variant swap included.
/// </summary>
/// <remarks>
///     A resource that exists but is not an image type throws, as a wrong-typed
///     resource would anywhere else in Avalonia. A key that resolves to nothing renders
///     the alt text (a static one gripes <c>IMG:RES</c> once), so a typo in a
///     <c>words.ini</c> never eats the sentence.
/// </remarks>
public class ResourceImage : Decorator {
	/// <summary>The raw resource value as the framework resolves it; the child is rebuilt from it.</summary>
	public static readonly StyledProperty<object?> ResourceValueProperty =
		AvaloniaProperty.Register<ResourceImage, object?>(nameof(ResourceValue));

	private readonly string key;
	private readonly ImageOptions options;
	private readonly bool isDynamic;

	/// <param name="key">The resource key (<c>x:Key</c>) to look up.</param>
	/// <param name="options">The image's options and rendering context.</param>
	/// <param name="isDynamic"><see langword="true"/> to follow later changes to the resource; <see langword="false"/> to keep the first value found.</param>
	/// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
	public ResourceImage(string key, ImageOptions options, bool isDynamic) {
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(options);
		this.key = key;
		this.options = options;
		this.isDynamic = isDynamic;
		Child = Placeholder();
		if (isDynamic) {
			// the framework's own live reference — what {DynamicResource} binds to:
			// evaluated from this element's place in the tree, re-evaluated when the
			// tree, the dictionaries or the theme variant change
			this.Bind(ResourceValueProperty, this.GetResourceObservable(key));
		}
	}

	/// <inheritdoc cref="ResourceValueProperty"/>
	public object? ResourceValue {
		get => GetValue(ResourceValueProperty);
		set => SetValue(ResourceValueProperty, value);
	}

	/// <inheritdoc/>
	protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e) {
		base.OnAttachedToLogicalTree(e);
		if (!isDynamic && ResourceValue is null) {
			// static: one lookup from where the image landed — what {StaticResource}
			// does — for the theme variant in effect: a resource kept in a
			// ThemeDictionaries entry is invisible to the theme-less overload
			if (this.TryFindResource(key, ActualThemeVariant, out var value) && value is not null) {
				ResourceValue = value;
			}
			else {
				// in the tree, and nothing: the key is wrong, so say so — once. (A
				// dynamic reference is left alone: its resource may legitimately
				// arrive later, and the observable delivers whenever it does.)
				ITakeException.Global.Warn($"IMG:RES:staticres:{key}");
			}
		}
	}

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
		base.OnPropertyChanged(change);
		if (change.Property == ResourceValueProperty) {
			var value = change.GetNewValue<object?>();
			Child = value is null ? Placeholder() : Build(value);
		}
	}

	private Control Build(object value) {
		Control visual;
		try {
			visual = ResourceVisualConverter.ToVisual(value, options);
		}
		catch (InvalidCastException e) {
			// same complaint, now naming the key the words.ini asked for
			throw new InvalidCastException($"Resource '{key}': {e.Message}", e);
		}
		ImageSizing.Apply(visual, options);
		return visual;
	}

	private TextBlock Placeholder() => new() { Text = MarkdownParser.AltPlaceholder(options.AltText) };
}
