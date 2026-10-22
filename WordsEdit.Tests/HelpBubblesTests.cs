using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Navigation;
using PatTech.Localization;
using PatTech.Localization.Authoring;
using PatTech.Localization.Wpf;
using Hyperlink = System.Windows.Documents.Hyperlink;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using WordsEdit.Views;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The help bubbles (SPEC: Help bubbles): a topic shows once its moment arises
///     and its control is on screen, not again once closed, and again when its
///     revision rises; one shows at a time; Tips off shows none; the config keeps
///     the switch and what was closed in their sections; a page's links run the
///     editor's rows, and the same link in a preview is only reported.
/// </summary>
[Collection(nameof(EditorConfig))] //swaps EditorConfig.Path: never beside another class that does
public class HelpBubblesTests {
	private static readonly Regex rxCommandLink = new(@"\]\(wordsmith:([^)\s]+)\)", RegexOptions.Compiled);

	[Fact]
	public void ATopicShows_OnceItsMomentArisesAndItsControlIsOnScreen_AndNotAgainOnceClosed() {
		var memory = new HelpMemory();
		var help = new HelpBubbles(memory);

		help.Arise(HelpTopics.Start);
		Assert.Null(help.Open); //no control yet
		help.Anchor("start", onScreen: true);
		Assert.Same(HelpTopics.Start, help.Open);
		Assert.Equal(Words.Known["help.start"], help.Page);

		help.GotIt();
		Assert.Null(help.Open);
		Assert.Equal("", help.Page);
		Assert.Equal(HelpTopics.Start.Revision, memory.Seen("start"));
		help.Arise(HelpTopics.Start);
		Assert.Null(help.Open); //closed at this revision

		//a revision above the one remembered: back again
		memory.Remember("start", HelpTopics.Start.Revision - 1);
		help.Arise(HelpTopics.Start);
		Assert.Same(HelpTopics.Start, help.Open);
	}

	[Fact]
	public void ABubbleWhoseMomentPasses_IsNotRemembered_AndOneShowsAtATime() {
		var memory = new HelpMemory();
		var help = new HelpBubbles(memory);
		help.Anchor("start", onScreen: true);
		help.Anchor("tree", onScreen: true);

		help.Arise(HelpTopics.Start);
		help.Arise(HelpTopics.Tree); //another is open: it waits for the next time it arises
		Assert.Same(HelpTopics.Start, help.Open);
		help.Pass(HelpTopics.Start);
		Assert.Null(help.Open);
		Assert.Equal(0, memory.Seen("start"));

		help.Arise(HelpTopics.Tree);
		Assert.Same(HelpTopics.Tree, help.Open);
		//its control leaving the screen passes its moment
		help.Anchor("tree", onScreen: false);
		Assert.Null(help.Open);
		Assert.Equal(0, memory.Seen("tree"));
		help.Anchor("tree", onScreen: true);
		Assert.Null(help.Open); //the moment went with it
	}

	[Fact]
	public void TipsOffShowsNone_AndShowTheTipsAgainBringsEachBack() {
		var memory = new HelpMemory();
		var help = new HelpBubbles(memory);
		help.Anchor("start", onScreen: true);
		help.Arise(HelpTopics.Start);

		help.Tips = false;
		Assert.False(memory.Tips);
		Assert.Null(help.Open);
		Assert.Equal(0, memory.Seen("start")); //put away unremembered
		help.Arise(HelpTopics.Start);
		Assert.Null(help.Open);

		help.Tips = true;
		help.Arise(HelpTopics.Start);
		help.GotIt();
		help.ShowAgain();
		Assert.Equal(0, memory.Seen("start"));
		help.Arise(HelpTopics.Start);
		Assert.Same(HelpTopics.Start, help.Open);
	}

	//the empty editor's moment at startup, the first file's once one loads, and the Help menu
	[Fact]
	public void TheEditorsMoments_AndTheHelpMenu() {
		var memory = new HelpMemory();
		var vm = new MainWindowViewModel(new FakeDialogs(), help: memory);
		vm.Help.Anchor("start", onScreen: true);
		Assert.Same(HelpTopics.Start, vm.Help.Open);

		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\n"), "Example");
		Assert.Null(vm.Help.Open); //the empty editor's moment passed
		vm.Help.Anchor("tree", onScreen: true);
		Assert.Same(HelpTopics.Tree, vm.Help.Open);
		vm.Help.GotItCommand.Execute(null);
		Assert.Equal(HelpTopics.Tree.Revision, memory.Seen("tree"));

		MenuGroup menu = vm.Commands.Menu[^1];
		Assert.Equal(Words.Known["menu.help"], menu.Caption);
		var tips = Assert.IsType<ToggleItem>(menu.Items[0]);
		Assert.True(tips.IsChecked);
		tips.IsChecked = false;
		Assert.False(memory.Tips);
		tips.IsChecked = true;
		var again = Assert.IsType<CommandItem>(menu.Items[1]);
		again.Command.Execute(null);
		Assert.Equal(0, memory.Seen("tree"));
	}

	private const string Moments = @"
value-en=English
value-fr=Français

[plain]
value=Hello
value-fr=Bonjour

[stale]
value=Hi
value-fr=Salut
stale-fr=2026-01-01

[hand]
value=Look
stale=check the link

[counted]
value#one=one file
value#other=many files

[param]
value={0} items
";

	//each of the selection's moments arises as it comes true and passes as the selection moves
	//to a key it is not about; a preview turned on, the sheet's dialog and the toolbar editor have theirs
	[Fact]
	public void TheSelectionsMoments_ThePreviews_AndTheDialogs() {
		var memory = new HelpMemory();
		memory.Remember("start", HelpTopics.Start.Revision);
		memory.Remember("tree", HelpTopics.Tree.Revision);
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs, help: memory);
		foreach (HelpTopic topic in HelpTopics.All) {
			vm.Help.Anchor(topic.Id, onScreen: true);
		}
		vm.LoadFile(new StringReader(Moments), "Example");
		WordsFile file = Assert.Single(vm.Session.Files);
		void Select(string key) => vm.Tree.Select(vm.Tree.NodeAt(file, key));
		void Closes(HelpTopic topic) {
			Assert.Same(topic, vm.Help.Open);
			vm.Help.GotIt();
		}

		vm.Tree.SelectedLanguage = vm.Tree.FileLanguages.First(language => language.Code == "fr");
		Select("plain");
		Closes(HelpTopics.Translation);

		Select("stale");
		Assert.Same(HelpTopics.Stale, vm.Help.Open);
		Select("plain"); //a key it is not about: it goes, unremembered
		Assert.Null(vm.Help.Open);
		Assert.Equal(0, memory.Seen("stale"));
		Select("stale");
		Closes(HelpTopics.Stale);

		Select("hand");
		Closes(HelpTopics.Review);
		Select("counted");
		Closes(HelpTopics.Forms);
		Select("param");
		Closes(HelpTopics.Parameters);
		vm.ShowDefaultPreview = true;
		Closes(HelpTopics.Preview);
		Assert.All(HelpTopics.All.Where(topic => topic != HelpTopics.Sheet && topic != HelpTopics.Toolbars), topic => Assert.Equal(topic.Revision, memory.Seen(topic.Id)));

		//the toolbar editor's, on the bucket while it is open, gone unremembered with it
		dialogs.OnShow = dialog => {
			Assert.IsType<ToolbarsViewModel>(dialog);
			Assert.Same(HelpTopics.Toolbars, vm.Help.Open);
		};
		vm.CustomizeToolbarsCommand.Execute(null);
		Assert.Null(vm.Help.Open);
		Assert.Equal(0, memory.Seen("toolbars"));

		//the sheet's dialog: its bubble while it is open, gone unremembered with it
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditHelp-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string sheet = Path.Combine(folder, "Strings.csv");
			File.WriteAllText(sheet, "key,value\r\ngreeting,Hello\r\n");
			dialogs.FilesToOpen = [sheet];
			dialogs.OnShow = dialog => {
				Assert.IsType<SheetImportViewModel>(dialog);
				Assert.Same(HelpTopics.Sheet, vm.Help.Open);
				((SheetImportViewModel)dialog).CancelCommand.Execute(null);
			};
			vm.ImportCommand.Execute(null);
			Assert.Null(vm.Help.Open);
			Assert.Equal(0, memory.Seen("sheet"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void TheConfigKeepsTheTipsSwitchAndEachBubbleClosedInTheirSections() {
		string configured = EditorConfig.Path;
		EditorConfig.Path = Path.Combine(Path.GetTempPath(), $"wordsmith-{Guid.NewGuid():N}", "config.ini");
		try {
			var memory = new ConfigHelpMemory();
			Assert.True(memory.Tips); //no file: on
			Assert.Equal(0, memory.Seen("start"));
			EditorConfig.Language = "it";
			memory.Tips = false;
			memory.Remember("start", 2);
			memory.Remember("tree", 1);
			Assert.Equal(["[editor]", "language=it", "tips=off", "", "[seen]", "start=2", "tree=1"], File.ReadAllLines(EditorConfig.Path));
			Assert.False(memory.Tips);
			Assert.Equal(2, memory.Seen("start"));

			memory.Tips = true;
			memory.Forget();
			Assert.Equal(["[editor]", "language=it"], File.ReadAllLines(EditorConfig.Path));
			Assert.True(memory.Tips);
			Assert.Equal(0, memory.Seen("tree"));

			//a section after it stays, the seen ones gone from between
			File.WriteAllLines(EditorConfig.Path, ["[seen]", "start=1", "", "[window]", "width=900", "height=500"]);
			memory.Forget();
			Assert.Equal(["[window]", "width=900", "height=500"], File.ReadAllLines(EditorConfig.Path));
		}
		finally {
			if (File.Exists(EditorConfig.Path)) {
				Directory.Delete(Path.GetDirectoryName(EditorConfig.Path)!, recursive: true);
			}
			EditorConfig.Path = configured;
		}
	}

	//every page is in the words, renders without a gripe, and links only rows the table has
	[Fact]
	public void TheCatalogue_EveryPageReads_RendersClean_AndLinksOnlyRowsTheTableHas() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		Assert.Equal(HelpTopics.All.Count, HelpTopics.All.Select(topic => topic.Id).Distinct().Count());
		Assert.Equal(HelpTopics.All, HelpTopics.All.Select(topic => HelpTopics.Find(topic.Id)));
		var gripes = new List<string>();
		NavigationTests.RunSta(() => {
			var parser = new MarkdownParser(logger: new Collecting(gripes));
			foreach (HelpTopic topic in HelpTopics.All) {
				string page = FakeDialogs.Rendered(topic.Page());
				Assert.NotEqual("", page.Trim());
				Assert.True(topic.Revision > 0);
				parser.ToInline(page);
				foreach (Match link in rxCommandLink.Matches(page)) {
					Assert.Contains(link.Groups[1].Value, vm.Commands.Links.Keys);
				}
			}
		});
		Assert.Empty(gripes);
		FakeDialogs.Rendered(Words.Known["help.got-it"]);
		Assert.Contains("the {0} an app", HelpTopics.Parameters.Page()); //a brace written twice is one, read literally

		//every command row is a link, by the tail of its caption's key
		Assert.Equal(vm.Commands.Rows.Count(), vm.Commands.Links.Count);
		foreach (var (id, row) in vm.Commands.Links) {
			Assert.Equal(Words.Known[$"menu.{id}"], row.Caption);
			Assert.Contains(row, vm.Commands.Rows);
		}
	}

	[Fact]
	public void ABubblesCommandLinkRunsItsRow_AndTheSameLinkInAPreviewIsReported() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\n"), "Example");
		bool shown = vm.ShowDefaultPreview;

		vm.FollowHelpLink(new Uri("wordsmith:default-preview"));
		Assert.NotEqual(shown, vm.ShowDefaultPreview);
		vm.FollowHelpLink(new Uri("wordsmith:remove-key")); //no key selected: it does not apply, and nothing happens
		vm.FollowHelpLink(new Uri("wordsmith:no-such-row"));
		Assert.Empty(dialogs.Notices);
		Assert.Empty(dialogs.Shown);

		List<Uri> opened = [];
		vm.OpenBrowser = opened.Add;
		vm.FollowHelpLink(new Uri("https://github.com/pzahra/words"));
		Assert.Equal([new Uri("https://github.com/pzahra/words")], opened);
		Assert.Empty(dialogs.Confirmations); //no question, as a preview's link asks

		//in a preview, it is the dictionary's link: a scheme its settings do not list, reported and never run
		shown = vm.ShowDefaultPreview;
		vm.FollowLink(new Uri("wordsmith:default-preview"));
		Assert.Equal(shown, vm.ShowDefaultPreview);
		Assert.Single(dialogs.Notices);
	}

	//the one handler hears a URI, not where it was clicked: the editor's class handler takes a bubble's link, and passes the rest
	[Fact]
	public void ALinkInsideABubbleGoesToTheBubblesHandler_AndOneOutsideGoesOn() {
		NavigationTests.RunSta(() => {
			List<Uri> followed = [];
			Bubble.HandleLinks(followed.Add);
			var inside = new Hyperlink(new Run("in")) { NavigateUri = new Uri("wordsmith:languages") };
			var bubble = new TextBlock(inside);
			Bubble.SetInBubble(bubble, true);
			var outside = new Hyperlink(new Run("out")) { NavigateUri = new Uri("wordsmith:languages") };
			_ = new TextBlock(outside);

			var clicked = new RequestNavigateEventArgs(inside.NavigateUri, null) { RoutedEvent = Hyperlink.RequestNavigateEvent };
			inside.RaiseEvent(clicked);
			Assert.True(clicked.Handled);
			var passed = new RequestNavigateEventArgs(outside.NavigateUri, null) { RoutedEvent = Hyperlink.RequestNavigateEvent };
			outside.RaiseEvent(passed);

			Assert.Equal([new Uri("wordsmith:languages")], followed);
		});
	}

	[Fact]
	public void ABubbleOpensOnTheSideWithRoom_NeverOverItsControl() {
		var area = new Rect(0, 0, 1000, 800);
		var bubble = new Size(300, 120);
		Assert.Equal(BubbleSide.Right, Bubble.Choose(new Rect(10, 100, 200, 400), bubble, area));
		Assert.Equal(BubbleSide.Left, Bubble.Choose(new Rect(800, 100, 150, 400), bubble, area));
		Assert.Equal(BubbleSide.Below, Bubble.Choose(new Rect(100, 50, 850, 300), bubble, area));
		Assert.Equal(BubbleSide.Above, Bubble.Choose(new Rect(100, 600, 850, 190), bubble, area));
		//no side with room for it whole: the one with the most
		Assert.Equal(BubbleSide.Below, Bubble.Choose(new Rect(0, 0, 1000, 700), bubble, area));

		//a top menu prefers below, off the bar, while there is room there
		var file = new Rect(5, 30, 40, 24);
		Assert.Equal(BubbleSide.Right, Bubble.Choose(file, bubble, area));
		Assert.Equal(BubbleSide.Below, Bubble.Choose(file, bubble, area, prefer: BubbleSide.Below));
		Assert.Equal(BubbleSide.Right, Bubble.Choose(new Rect(5, 700, 40, 24), bubble, area, prefer: BubbleSide.Below));
	}

	//centred along its control, unless that runs off the screen: then pushed back inside
	[Fact]
	public void ABubbleIsCentredAlongItsControl_ButKeptOnTheScreen() {
		Assert.Equal(-100, Bubble.Along(start: 300, span: 100, length: 300, min: 0, max: 1000), 3);
		Assert.Equal(-5, Bubble.Along(start: 5, span: 40, length: 300, min: 0, max: 1000), 3); //under File: centred, it would start at -125
		Assert.Equal(-200, Bubble.Along(start: 900, span: 80, length: 300, min: 0, max: 1000), 3); //it ends at the screen's edge
	}

	[Fact]
	public void TheBubble_ShowsItsPage_AndItsTailPointsAtItsControl() {
		NavigationTests.RunSta(() => {
			var help = new HelpBubbles(new HelpMemory());
			help.Anchor("start", onScreen: true);
			help.Arise(HelpTopics.Start);
			var view = new HelpBubbleView { DataContext = help };
			view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); //the bindings settle

			TextBlock page = (TextBlock)view.FindName("PageText");
			Assert.Equal(help.Page, MarkdownPreview.GetText(page));
			Assert.Contains(page.Inlines, inline => Descendants(inline).OfType<Hyperlink>().Any(link => link.NavigateUri == new Uri("wordsmith:load")));
			Assert.True(Bubble.GetInBubble(Descendants(page.Inlines.First()).OfType<Hyperlink>().First()));

			var tail = (System.Windows.Shapes.Path)view.FindName("Tail");
			foreach (var (side, row, column) in new[] { (BubbleSide.Right, 1, 0), (BubbleSide.Left, 1, 2), (BubbleSide.Below, 0, 1), (BubbleSide.Above, 2, 1) }) {
				view.Side = side;
				Assert.Equal((row, column), (Grid.GetRow(tail), Grid.GetColumn(tail)));
			}
			//pushed along, the tail slides to meet its control's middle: below File, near the bubble's left
			view.Side = BubbleSide.Below;
			view.TailOffset = 25;
			Assert.Equal((HorizontalAlignment.Left, new Thickness(17, 0, 0, 0)), (tail.HorizontalAlignment, tail.Margin));
			view.Side = BubbleSide.Right;
			Assert.Equal((VerticalAlignment.Top, new Thickness(0, 17, 0, 0)), (tail.VerticalAlignment, tail.Margin));
			view.TailOffset = double.NaN;
			Assert.Equal((VerticalAlignment.Center, new Thickness(0)), (tail.VerticalAlignment, tail.Margin));
		});
	}

	//a bubble opens as its topic does, before its page's binding hears of the page: it measures the page it opens with, not the last
	[Fact]
	public void TheBubbleMeasuresThePageItOpensWith() {
		NavigationTests.RunSta(() => {
			var help = new HelpBubbles(new HelpMemory());
			help.Anchor("start", onScreen: true);
			help.Anchor("sheet", onScreen: true);
			help.Arise(HelpTopics.Start);
			var view = new HelpBubbleView { DataContext = help };
			view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); //the bindings settle
			Size start = view.Fit();
			Size? opened = null;
			help.PropertyChanged += (_, e) => {
				if (e.PropertyName == nameof(HelpBubbles.Open) && help.Open is not null) {
					opened = view.Fit();
				}
			};

			help.GotIt();
			help.Arise(HelpTopics.Sheet);
			view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
			view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			Assert.NotEqual(start, view.DesiredSize);
			Assert.Equal(view.DesiredSize, opened);
		});
	}

	//the parameters' heading scrolled away below its pane's fold is not on screen, till it scrolls into view
	[Fact]
	public void AControlScrolledAwayIsNotOnScreen_TillItScrollsIntoView() {
		NavigationTests.RunSta(() => {
			var heading = new TextBlock { Text = "Parameters", Height = 30 };
			var stack = new StackPanel();
			stack.Children.Add(new Border { Height = 400 });
			stack.Children.Add(heading);
			var scroll = new ScrollViewer { Height = 100, Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
			//a window that is never shown: no WS_VISIBLE
			using var source = new HwndSource(new HwndSourceParameters("bubble-test") { Width = 300, Height = 200, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = scroll };
			void Settle() => scroll.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
			Bubble.SetTopic(heading, "parameters");
			try {
				Settle();
				Assert.True(heading.IsLoaded && heading.IsVisible);
				Assert.False(Bubble.Anchored("parameters"));
				scroll.ScrollToEnd();
				Settle();
				Assert.True(Bubble.Anchored("parameters"));
				scroll.ScrollToHome();
				Settle();
				Assert.False(Bubble.Anchored("parameters"));
			}
			finally {
				Bubble.SetTopic(heading, null);
			}
		});
	}

	private static IEnumerable<Inline> Descendants(Inline inline)
		=> inline is Span span ? [span, .. span.Inlines.SelectMany(Descendants)] : [inline];

	private sealed class Collecting(List<string> gripes) : ITakeException {
		public void Warn(string text) => gripes.Add(text);
		public void Error(Exception exception, string message) => gripes.Add(message);
	}
}
