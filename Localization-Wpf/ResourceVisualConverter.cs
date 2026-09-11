using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using PathGeometry = System.Windows.Shapes.Path;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     Turns a resolved resource into a fresh visual: an <see cref="ImageSource"/>
	///     into an <see cref="Image"/>, a <see cref="Geometry"/> into a filled
	///     <see cref="PathGeometry"/> (the <see cref="ImageOptions.Foreground"/> when given —
	///     a color or a resource brush — else black), a <see cref="DataTemplate"/> into
	///     newly loaded content. Every call
	///     builds anew, so one resource can show in many places — which is why a bare
	///     element resource is not accepted: one instance cannot live under two parents.
	///     Wrap it in a <see cref="DataTemplate"/> instead.
	/// </summary>
	/// <remarks>
	///     This is what the <c>staticres:</c>/<c>dynres:</c> image schemes render through,
	///     and it is also bindable from XAML as <c>{StaticResource WordsResourceVisual}</c>
	///     (pass <see cref="ImageOptions"/> as the converter parameter to set a
	///     foreground). A wrong-typed resource throws, as it would anywhere else in WPF;
	///     in a binding, that surfaces as a binding error, the way converter exceptions
	///     do.
	/// </remarks>
	public class ResourceVisualConverter : IValueConverter {
		/// <inheritdoc/>
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
			=> value is null ? null : ToVisual(value, parameter as ImageOptions ?? new ImageOptions());

		/// <inheritdoc/>
		/// <exception cref="NotSupportedException">Always; a visual cannot be turned back into a resource.</exception>
		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
			=> throw new NotSupportedException();

		/// <summary>
		///     The conversion itself, for callers that already hold the value.
		/// </summary>
		/// <param name="value">The resolved resource.</param>
		/// <param name="options">The foreground for geometry, and the rendering context.</param>
		/// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
		/// <exception cref="InvalidCastException"><paramref name="value"/> is not an <see cref="ImageSource"/>, a <see cref="Geometry"/> or a <see cref="DataTemplate"/> — an element included, which needs a template to be reusable.</exception>
		public static FrameworkElement ToVisual(object value, ImageOptions options) {
			ArgumentNullException.ThrowIfNull(value);
			ArgumentNullException.ThrowIfNull(options);
			switch (value) {
				case ImageSource imageSource:
					return new Image { Source = imageSource, Stretch = Stretch.Uniform };
				case Geometry geometry: {
					var path = new PathGeometry { Data = geometry, Fill = Brushes.Black, Stretch = Stretch.Uniform };
					options.Foreground?.ApplyTo(path, Shape.FillProperty);
					return path;
				}
				case DataTemplate template:
					// LoadContent builds a new tree every time: the reusable form of an element
					return template.LoadContent() as FrameworkElement
						?? throw new InvalidCastException("the DataTemplate did not load to an element");
				default:
					throw new InvalidCastException(
						$"a {value.GetType().Name} is not an image resource; expected an ImageSource, a Geometry or a DataTemplate (wrap an element in a DataTemplate to reuse it)");
			}
		}
	}
}
