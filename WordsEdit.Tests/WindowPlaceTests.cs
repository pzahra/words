using System.IO;
using System.Windows;
using WordsEdit.Utils;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The main window opens as it last closed: its normal size and whether it was
///     maximized, kept in the editor's config beside the language, cut to fit the screen.
/// </summary>
[Collection(nameof(EditorConfig))] //swaps EditorConfig.Path: never beside another class that does
public class WindowPlaceTests {
	[Fact]
	public void TheConfigRemembersTheWindowBesideTheLanguage() {
		string configured = EditorConfig.Path;
		EditorConfig.Path = Path.Combine(Path.GetTempPath(), $"wordsmith-{Guid.NewGuid():N}", "config.ini");
		try {
			Assert.Null(EditorConfig.Window); //no file yet
			EditorConfig.Language = "it";

			EditorConfig.Window = new WindowPlace(1234.4, 777.6, Maximized: true);
			Assert.Equal(new WindowPlace(1234, 778, Maximized: true), EditorConfig.Window); //whole pixels
			Assert.Equal("it", EditorConfig.Language);
			Assert.Contains("window-state=maximized", File.ReadAllLines(EditorConfig.Path));

			EditorConfig.Window = new WindowPlace(900, 500, Maximized: false);
			Assert.Equal(new WindowPlace(900, 500, Maximized: false), EditorConfig.Window);

			//a hand-edited size that is not one is no size at all
			File.WriteAllLines(EditorConfig.Path, ["language=it", "window-width=wide", "window-height=500"]);
			Assert.Null(EditorConfig.Window);
			Assert.Equal("it", EditorConfig.Language);

			EditorConfig.Window = null;
			Assert.Equal(["language=it"], File.ReadAllLines(EditorConfig.Path));
		}
		finally {
			if (File.Exists(EditorConfig.Path)) {
				Directory.Delete(Path.GetDirectoryName(EditorConfig.Path)!, recursive: true);
			}
			EditorConfig.Path = configured;
		}
	}

	[Fact]
	public void APlaceIsCutToFitBetweenTheWindowsMinimumAndTheRoom() {
		var place = new WindowPlace(3000, 300, Maximized: true);

		Assert.Equal(new WindowPlace(1920, 450, Maximized: true), place.Fit(800, 450, 1920, 1040));
		Assert.Equal(new WindowPlace(1000, 600, Maximized: false), new WindowPlace(1000, 600, false).Fit(800, 450, 1920, 1040));
	}

	[Fact]
	public void AWindowTakesItsPlaceAndGivesItBack() {
		NavigationTests.RunSta(() => {
			var window = new Window { MinWidth = 400, MinHeight = 300 };
			new WindowPlace(500, 350, Maximized: true).ApplyTo(window);

			Assert.Equal(500, window.Width);
			Assert.Equal(350, window.Height);
			Assert.Equal(WindowState.Maximized, window.WindowState);
			Assert.Equal(new WindowPlace(500, 350, Maximized: true), WindowPlace.Of(window));
		});
	}
}
