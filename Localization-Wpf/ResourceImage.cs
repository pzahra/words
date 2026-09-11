using System;
using System.Windows;
using System.Windows.Controls;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     The visual a <c>staticres:</c> or <c>dynres:</c> image resolves to: a host that
	///     looks its resource up from where it lands in the tree — the way
	///     <c>{StaticResource}</c> and <c>{DynamicResource}</c> do, through the
	///     framework's own resource reference — and renders the value through
	///     <see cref="ResourceVisualConverter"/> once it arrives. Static keeps the first
	///     value it sees; dynamic follows every change, a theme swap included.
	/// </summary>
	/// <remarks>
	///     A resource that exists but is not an image type throws, as a wrong-typed
	///     resource would anywhere else in WPF. A key that resolves to nothing renders
	///     the alt text (and gripes <c>IMG:RES</c> once it is loaded and still empty), so
	///     a typo in a <c>words.ini</c> never eats the sentence.
	/// </remarks>
	public class ResourceImage : Decorator {
		/// <summary>The raw resource value as the framework resolves it; the child is rebuilt from it.</summary>
		public static readonly DependencyProperty ResourceValueProperty = DependencyProperty.Register(
			nameof(ResourceValue), typeof(object), typeof(ResourceImage),
			new PropertyMetadata(null, (d, e) => ((ResourceImage)d).OnResourceValueChanged(e.NewValue)));

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
			// the framework's own reference: evaluated from this element's place in
			// the tree and re-evaluated when the tree or the dictionaries change
			SetResourceReference(ResourceValueProperty, key);
			Loaded += OnLoaded;
		}

		/// <inheritdoc cref="ResourceValueProperty"/>
		public object? ResourceValue {
			get => GetValue(ResourceValueProperty);
			set => SetValue(ResourceValueProperty, value);
		}

		private void OnLoaded(object sender, RoutedEventArgs e) {
			Loaded -= OnLoaded;
			// in the tree, and still nothing: the key is wrong, so say so — once
			if (ResourceValue is null) {
				ITakeException.Global.Warn($"IMG:RES:{(isDynamic ? "dynres" : "staticres")}:{key}");
			}
		}

		private void OnResourceValueChanged(object? value) {
			if (value is not null && !isDynamic) {
				// static: pin the first value found — a local value replaces the
				// resource reference, so later changes to the dictionary go unheard
				SetValue(ResourceValueProperty, value);
			}
			Child = value is null ? Placeholder() : Build(value);
		}

		private FrameworkElement Build(object value) {
			FrameworkElement visual;
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
}
