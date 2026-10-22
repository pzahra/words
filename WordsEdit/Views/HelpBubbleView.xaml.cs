using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WordsEdit.Utils;

namespace WordsEdit.Views;

/// <summary>A help bubble: its page, Got it, and the tail on the side toward its control.</summary>
public partial class HelpBubbleView : UserControl {
	//the tail's breadth along the bubble's edge
	private const double TailBreadth = 16;

	public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
		nameof(Side), typeof(BubbleSide), typeof(HelpBubbleView), new PropertyMetadata(BubbleSide.Right, (d, _) => ((HelpBubbleView)d).PlaceTail()));

	public static readonly DependencyProperty TailOffsetProperty = DependencyProperty.Register(
		nameof(TailOffset), typeof(double), typeof(HelpBubbleView), new PropertyMetadata(double.NaN, (d, _) => ((HelpBubbleView)d).PlaceTail()));

	/// <summary>The side of its control the bubble opens on: the tail sits on the bubble's other side, pointing back.</summary>
	public BubbleSide Side {
		get => (BubbleSide)GetValue(SideProperty);
		set => SetValue(SideProperty, value);
	}

	/// <summary>Where along its edge the tail's point sits, from the bubble's left or top, so it meets its control's middle; NaN centres it.</summary>
	public double TailOffset {
		get => (double)GetValue(TailOffsetProperty);
		set => SetValue(TailOffsetProperty, value);
	}

	public HelpBubbleView() {
		InitializeComponent();
		PlaceTail();
	}

	//beside a control on its right, the tail is on the bubble's left, pointing left; and so on round
	private void PlaceTail() {
		(int row, int column, string shape) = Side switch {
			BubbleSide.Right => (1, 0, "M 10,0 L 0,8 L 10,16 Z"),
			BubbleSide.Left => (1, 2, "M 0,0 L 10,8 L 0,16 Z"),
			BubbleSide.Below => (0, 1, "M 0,10 L 8,0 L 16,10 Z"),
			_ => (2, 1, "M 0,0 L 8,10 L 16,0 Z"),
		};
		Grid.SetRow(Tail, row);
		Grid.SetColumn(Tail, column);
		Tail.Data = Geometry.Parse(shape);
		bool along = column == 1; //on the top or bottom edge: it slides left and right
		bool centred = double.IsNaN(TailOffset);
		double from = Math.Max(6, TailOffset - TailBreadth / 2); //clear of the corner
		Tail.HorizontalAlignment = along && centred ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		Tail.VerticalAlignment = !along && centred ? VerticalAlignment.Center : VerticalAlignment.Top;
		Tail.Margin = centred ? new Thickness(0) : along ? new Thickness(from, 0, 0, 0) : new Thickness(0, from, 0, 0);
	}
}
