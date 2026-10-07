using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;
using WordsEdit.Utils;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Every toolbar gives back the room the theme keeps for its overflow button,
///     until it overflows and the button returns.
/// </summary>
public class ToolBarOverflowTests {
	[Fact]
	public void AToolBarGivesBackItsOverflowRoomUntilItOverflows() {
		NavigationTests.RunSta(() => {
			var theme = new ResourceDictionary();
			theme.MergedDictionaries.Add(new BundledTheme { BaseTheme = BaseTheme.Light, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.DeepOrange });
			theme.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml", UriKind.Relative)));
			var bar = new ToolBar();
			for (int i = 0; i < 6; i++) {
				bar.Items.Add(new Button { Width = 40, Content = i });
			}
			ToolBarOverflow.SetCollapse(bar, true);
			var host = new Grid { Resources = theme, Children = { bar } };
			Lay(host, 1000);
			bar.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
			Lay(host, 1000);
			var overflow = (FrameworkElement)bar.Template.FindName("OverflowGrid", bar);
			var panel = (FrameworkElement)bar.Template.FindName("MainPanelBorder", bar);

			Assert.False(bar.HasOverflowItems);
			Assert.Equal(Visibility.Collapsed, overflow.Visibility);
			Assert.Equal(new Thickness(0), panel.Margin);

			Lay(host, 120);
			Assert.True(bar.HasOverflowItems);
			Assert.Equal(Visibility.Visible, overflow.Visibility);
			Assert.NotEqual(new Thickness(0), panel.Margin); //the theme's, back beside the button

			Lay(host, 1000);
			Assert.False(bar.HasOverflowItems);
			Assert.Equal(Visibility.Collapsed, overflow.Visibility);
		});
	}

	[Fact]
	public void TurnedOffAToolBarHasTheThemesRoomBack() {
		NavigationTests.RunSta(() => {
			var theme = new ResourceDictionary();
			theme.MergedDictionaries.Add(new BundledTheme { BaseTheme = BaseTheme.Light, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.DeepOrange });
			theme.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml", UriKind.Relative)));
			var bar = new ToolBar { Items = { new Button { Width = 40 } } };
			var host = new Grid { Resources = theme, Children = { bar } };
			Lay(host, 1000);
			var overflow = (FrameworkElement)bar.Template.FindName("OverflowGrid", bar);
			var panel = (FrameworkElement)bar.Template.FindName("MainPanelBorder", bar);
			Thickness themes = panel.Margin;
			ToolBarOverflow.SetCollapse(bar, true);
			bar.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
			Lay(host, 1000);
			Assert.Equal(Visibility.Collapsed, overflow.Visibility);

			ToolBarOverflow.SetCollapse(bar, false);
			bar.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); //no handler left to collapse it again
			Lay(host, 1000);

			Assert.NotEqual(Visibility.Collapsed, overflow.Visibility);
			Assert.Equal(themes, panel.Margin);
		});
	}

	private static void Lay(FrameworkElement host, double width) {
		host.Measure(new Size(width, 60));
		host.Arrange(new Rect(0, 0, width, 60));
		host.UpdateLayout();
	}
}
