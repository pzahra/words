using System.Windows;
using System.Windows.Controls;

namespace WordsEdit.Utils;

/// <summary>
///     Lays its children out in a row, each at the width it wants but the ones marked
///     <see cref="FillProperty"/>, which share the room the rest leave (SPEC: The
///     toolbar editor, Pinned entries): the search box beside Back and Forward, the
///     node's name beside Rename, wherever the user put them along the toolbar.
/// </summary>
public sealed class FillPanel : Panel {
	public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
		"Fill", typeof(bool), typeof(FillPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

	public static bool GetFill(DependencyObject element) => (bool)element.GetValue(FillProperty);
	public static void SetFill(DependencyObject element, bool value) => element.SetValue(FillProperty, value);

	//the others first, at the width they want; then the fillers, with what is left between them
	protected override Size MeasureOverride(Size available) {
		double width = 0, height = 0;
		foreach (UIElement child in InternalChildren) {
			if (!GetFill(child)) {
				child.Measure(new Size(double.PositiveInfinity, available.Height));
				width += child.DesiredSize.Width;
				height = Math.Max(height, child.DesiredSize.Height);
			}
		}
		double share = Share(available.Width, width);
		foreach (UIElement child in InternalChildren) {
			if (GetFill(child)) {
				child.Measure(new Size(share, available.Height));
				width += child.DesiredSize.Width;
				height = Math.Max(height, child.DesiredSize.Height);
			}
		}
		return new Size(width, height);
	}

	protected override Size ArrangeOverride(Size final) {
		double others = InternalChildren.Cast<UIElement>().Where(child => !GetFill(child)).Sum(child => child.DesiredSize.Width);
		double share = Share(final.Width, others);
		double x = 0;
		foreach (UIElement child in InternalChildren) {
			double width = GetFill(child) ? share : child.DesiredSize.Width;
			child.Arrange(new Rect(x, 0, width, final.Height));
			x += width;
		}
		return final;
	}

	//each filler's part of the room the others leave; all it wants while the room has no end
	private double Share(double room, double others) {
		int fillers = InternalChildren.Cast<UIElement>().Count(GetFill);
		return fillers == 0 ? 0 : double.IsInfinity(room) ? double.PositiveInfinity : Math.Max(0, room - others) / fillers;
	}
}
