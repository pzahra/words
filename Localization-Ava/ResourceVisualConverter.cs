using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PathGeometry = Avalonia.Controls.Shapes.Path;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     Turns a resolved resource into a fresh visual: an <see cref="IImage"/> into an
///     <see cref="Image"/>, a <see cref="Geometry"/> into a filled
///     <see cref="PathGeometry"/> (the <see cref="ImageOptions.Foreground"/> when given,
///     else black), an <see cref="IDataTemplate"/> into newly built content. Every call
///     builds anew, so one resource can show in many places — which is why a bare
///     control resource is not accepted: one instance cannot live under two parents.
///     Wrap it in a <c>DataTemplate</c> instead.
/// </summary>
/// <remarks>
///     This is what the <c>staticres:</c>/<c>dynres:</c> image schemes render through,
///     and it is also bindable from AXAML as <c>{StaticResource WordsResourceVisual}</c>
///     (pass <see cref="ImageOptions"/> as the converter parameter to set a foreground).
///     A wrong-typed resource throws, as it would anywhere else in Avalonia; in a
///     binding, that surfaces as a binding error, the way converter exceptions do.
/// </remarks>
public class ResourceVisualConverter : IValueConverter {
	/// <inheritdoc/>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is null ? null : ToVisual(value, parameter as ImageOptions ?? new ImageOptions());

	/// <inheritdoc/>
	/// <exception cref="NotSupportedException">Always; a visual cannot be turned back into a resource.</exception>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();

	/// <summary>
	///     The conversion itself, for callers that already hold the value.
	/// </summary>
	/// <param name="value">The resolved resource.</param>
	/// <param name="options">The foreground for geometry, and the rendering context.</param>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidCastException"><paramref name="value"/> is not an <see cref="IImage"/>, a <see cref="Geometry"/> or an <see cref="IDataTemplate"/> — a control included, which needs a template to be reusable.</exception>
	public static Control ToVisual(object value, ImageOptions options) {
		ArgumentNullException.ThrowIfNull(value);
		ArgumentNullException.ThrowIfNull(options);
		switch (value) {
			case IImage image:
				return new Image { Source = image, Stretch = Stretch.Uniform };
			case Geometry geometry:
				return new PathGeometry {
					Data = geometry,
					Fill = options.Foreground ?? (IBrush)Brushes.Black,
					Stretch = Stretch.Uniform,
				};
			case IDataTemplate template:
				// Build makes a new control every time: the reusable form of a control
				return template.Build(null)
					?? throw new InvalidCastException("the DataTemplate did not build a control");
			default:
				throw new InvalidCastException(
					$"a {value.GetType().Name} is not an image resource; expected an IImage, a Geometry or an IDataTemplate (wrap a control in a DataTemplate to reuse it)");
		}
	}
}
