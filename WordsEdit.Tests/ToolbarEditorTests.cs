using PatTech.Localization;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The toolbar editor (SPEC: The toolbar editor, The dialog): a row of chips for each
///     toolbar and a bucket of the rows on none; a chip moves along a row, to another, into
///     the bucket and out, and the toolbar behind follows at once; a break comes out of the
///     bucket and goes back gone; a pinned chip keeps to its row; an odd place warns; OK
///     keeps the lines, Cancel puts back, Reset ships.
/// </summary>
public class ToolbarEditorTests {
	private static ToolbarRow Row(ToolbarsViewModel editor, string toolbar) => editor.Rows.Single(row => row.Toolbar.Name == toolbar);

	private static ToolbarChip Chip(ToolbarsViewModel editor, string name)
		=> editor.Rows.SelectMany(row => row.Chips).Concat(editor.Bucket).First(chip => chip.Entry.Name == name);

	//each row's chips are its toolbar's entries, one for one
	private static void InStep(ToolbarsViewModel editor)
		=> Assert.All(editor.Rows, row => Assert.Equal(row.Toolbar.Entries, row.Chips.Select(chip => chip.Entry)));

	[Fact]
	public void TheBoard_ARowForEachToolbar_TheBucketWhatIsOnNone_AndTheRowThatAskedMarked() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		var editor = new ToolbarsViewModel(vm, vm.Commands.KeyTools);
		Assert.Equal(["search", "name", "default", "languages", "translation", "node", "key"], editor.Rows.Select(row => row.Toolbar.Name));
		InStep(editor);
		Assert.Equal(["key"], editor.Rows.Where(row => row.IsMarked).Select(row => row.Toolbar.Name));
		Assert.Equal(["search-box", "node-name", "translation-language"],
			editor.Rows.SelectMany(row => row.Chips).Where(chip => chip.IsPinned).Select(chip => chip.Entry.Name));
		Assert.All(editor.Rows, row => FakeDialogs.Rendered(row.Toolbar.Caption + row.Toolbar.Place));

		//a chip shows its icon, or its caption where it has none
		ToolbarChip back = Chip(editor, "back");
		Assert.True(back.HasIcon);
		Assert.False(back.ShowsCaption);
		Assert.Equal(Words.Known["menu.back"], back.Caption);
		ToolbarChip box = Chip(editor, "search-box");
		Assert.True(box.ShowsCaption);
		Assert.Equal(Words.Known["toolbars.search-box"], box.Caption);
		Assert.True(Chip(editor, "filters").HasIcon);

		//the bucket: a break, then the menu's rows on no toolbar in menu order, none of the menu's own
		Assert.True(editor.Bucket[0].IsBreak);
		List<string> bucket = [.. editor.Bucket.Skip(1).Select(chip => chip.Entry.Name)];
		List<string> menu = [.. vm.Commands.Menu.SelectMany(group => group.Items).Where(row => row is not MenuBreak).Select(row => row.Name)];
		Assert.Equal(menu.Where(bucket.Contains), bucket);
		Assert.Contains("undo", bucket);
		Assert.Contains("toolbars", bucket);
		Assert.DoesNotContain(bucket, CommandTable.MenuOnly.Contains);
		Assert.DoesNotContain(bucket, name => editor.Rows.Any(row => row.Chips.Any(chip => chip.Entry.Name == name)));
		Assert.All(editor.Bucket, chip => FakeDialogs.Rendered(chip.Caption));

		//the editor is a row of the Tools menu
		Assert.Same(vm.Commands.Named["toolbars"], vm.Commands.Menu[3].Items[^1]);
	}

	[Fact]
	public void AChipMoves_AlongARow_ToAnother_IntoTheBucketAndOut_AndItsToolbarFollowsAtOnce() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		var editor = new ToolbarsViewModel(vm);

		//along its row: the index counts the row as it stands, the chip still in it
		editor.Move(Chip(editor, "back"), Row(editor, "search"), 2);
		Assert.Equal("forward,back,search-box,filters", vm.Commands.SearchTools.Line);
		editor.Move(Chip(editor, "filters"), Row(editor, "search"), 0);
		Assert.Equal("filters,forward,back,search-box", vm.Commands.SearchTools.Line);

		//out of the bucket, and off it
		editor.Move(Chip(editor, "undo"), Row(editor, "key"), 0);
		Assert.Equal("undo,add-key,remove-key,stale-all", vm.Commands.KeyTools.Line);
		Assert.DoesNotContain(editor.Bucket, chip => chip.Entry.Name == "undo");

		//into the bucket, which keeps menu order
		editor.Move(Chip(editor, "remove-key"), null, 0);
		Assert.Equal("undo,add-key,stale-all", vm.Commands.KeyTools.Line);
		List<string> menu = [.. vm.Commands.Menu.SelectMany(group => group.Items).Select(row => row.Name)];
		List<string> bucket = [.. editor.Bucket.Skip(1).Select(chip => chip.Entry.Name)];
		Assert.Contains("remove-key", bucket);
		Assert.Equal(bucket.OrderBy(menu.IndexOf), bucket);

		//to another row, at its end
		editor.Move(Chip(editor, "rename"), Row(editor, "node"), 99);
		Assert.Equal("node-name,copy-key", vm.Commands.NameTools.Line);
		Assert.Equal("add-node,remove,add-comment,rename", vm.Commands.NodeTools.Line);

		//a row emptied: its toolbar takes no room, and it still takes a chip back
		foreach (string name in new[] { "add-node", "remove", "add-comment", "rename" }) {
			editor.Move(Chip(editor, name), null, 0);
		}
		Assert.True(vm.Commands.NodeTools.IsEmpty);
		editor.Move(Chip(editor, "remove"), Row(editor, "node"), 0);
		Assert.Equal("remove", vm.Commands.NodeTools.Line);
		InStep(editor);
	}

	[Fact]
	public void TheFilterButton_GoesToAnyRow_AndIntoTheBucket_BeforeTheFiltersItHolds() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		var editor = new ToolbarsViewModel(vm);
		ToolbarChip filters = Chip(editor, "filters");
		Assert.False(filters.IsPinned);
		Assert.True(editor.Accepts(filters, Row(editor, "key")));
		Assert.True(editor.Accepts(filters, null));

		editor.Move(filters, Row(editor, "key"), 0);
		Assert.Equal("back,forward,search-box", vm.Commands.SearchTools.Line);
		Assert.Equal("filters,add-key,remove-key,stale-all", vm.Commands.KeyTools.Line);

		editor.Move(filters, null, 0);
		Assert.Equal("add-key,remove-key,stale-all", vm.Commands.KeyTools.Line);
		List<string> bucket = [.. editor.Bucket.Select(chip => chip.Entry.Name)];
		Assert.Equal(bucket.IndexOf("stale-view") - 1, bucket.IndexOf("filters"));
		InStep(editor);
	}

	[Fact]
	public void ABreakComesOutOfTheBucket_TheBucketKeepingOne_AndGoesBackGone() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		var editor = new ToolbarsViewModel(vm);

		editor.Move(editor.Bucket[0], Row(editor, "key"), 1);
		editor.Move(editor.Bucket[0], Row(editor, "key"), 3);
		Assert.Equal("add-key,-,remove-key,-,stale-all", vm.Commands.KeyTools.Line);
		Assert.NotSame(vm.Commands.KeyTools.Entries[1], vm.Commands.KeyTools.Entries[3]);
		Assert.Single(editor.Bucket, chip => chip.IsBreak);
		Assert.True(editor.Bucket[0].IsBreak);

		editor.Move(Row(editor, "key").Chips[1], null, 0);
		Assert.Equal("add-key,remove-key,-,stale-all", vm.Commands.KeyTools.Line);
		Assert.Single(editor.Bucket, chip => chip.IsBreak);
		InStep(editor);
	}

	[Fact]
	public void APinnedChip_MovesAlongItsRow_AndIsRefusedElsewhere() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		var editor = new ToolbarsViewModel(vm);
		ToolbarChip box = Chip(editor, "search-box");
		ToolbarChip language = Chip(editor, "translation-language");

		Assert.True(editor.Accepts(box, Row(editor, "search")));
		Assert.False(editor.Accepts(box, Row(editor, "name")));
		Assert.False(editor.Accepts(box, null));
		Assert.False(editor.Accepts(language, Row(editor, "translation")));
		Assert.True(editor.Accepts(Chip(editor, "back"), null));

		editor.Move(box, Row(editor, "name"), 0);
		editor.Move(box, null, 0);
		editor.Move(language, Row(editor, "translation"), 0);
		Assert.Equal("back,forward,search-box,filters", vm.Commands.SearchTools.Line);
		Assert.Equal("languages,translation-language", vm.Commands.LanguageTools.Line);

		editor.Move(box, Row(editor, "search"), 0);
		Assert.Equal("search-box,back,forward,filters", vm.Commands.SearchTools.Line);

		//where a drop lands: a row's chips, the bucket's, or neither
		Assert.True(editor.TryPlace(Row(editor, "key").Chips, out ToolbarRow? row));
		Assert.Same(Row(editor, "key"), row);
		Assert.True(editor.TryPlace(editor.Bucket, out row));
		Assert.Null(row);
		Assert.False(editor.TryPlace(new List<ToolbarChip>(), out _));
	}

	[Fact]
	public void AnOddPlaceWarns_ARowAtHomeAnywhereNever_AndTheWarningGoesWhenTheChipMovesBack() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		var editor = new ToolbarsViewModel(vm);
		Assert.Empty(editor.OddPlaces);
		Assert.DoesNotContain(editor.Rows.SelectMany(row => row.Chips), chip => chip.IsOdd);

		ToolbarChip translationPreview = Chip(editor, "translation-preview");
		editor.Move(translationPreview, Row(editor, "default"), 0);
		Assert.Equal(Words.Known["toolbars.about-translation"], translationPreview.Odd);
		editor.Move(Chip(editor, "default-preview"), Row(editor, "languages"), 0);
		Assert.Equal(Words.Known["toolbars.about-default"], Chip(editor, "default-preview").Odd);
		Assert.Equal([
			Words.Known.Format("toolbars.over-default", translationPreview.Caption),
			Words.Known.Format("toolbars.over-translation", Chip(editor, "default-preview").Caption),
		], editor.OddPlaces);
		Assert.All(editor.OddPlaces, line => FakeDialogs.Rendered(line));

		//over the tree, or a row about no pane: at home
		editor.Move(Chip(editor, "default-form"), Row(editor, "search"), 0);
		editor.Move(Chip(editor, "toggle-review"), Row(editor, "translation"), 0);
		Assert.False(Chip(editor, "default-form").IsOdd);
		Assert.False(Chip(editor, "toggle-review").IsOdd);

		editor.Move(translationPreview, Row(editor, "translation"), 0);
		Assert.False(translationPreview.IsOdd);
		Assert.Single(editor.OddPlaces);
	}

	[Fact]
	public void OkKeepsTheLines_OddPlacesAndAll() {
		var memory = new ToolbarMemory();
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs, toolbars: memory);
		dialogs.OnShow = dialog => {
			var editor = Assert.IsType<ToolbarsViewModel>(dialog);
			//the toolbar whose context menu asked is marked
			Assert.True(Row(editor, "key").IsMarked);
			editor.Move(Chip(editor, "translation-preview"), Row(editor, "key"), 0);
			editor.Move(editor.Bucket[0], Row(editor, "key"), 1);
			editor.OkayCommand.Execute(null);
		};
		vm.CustomizeToolbarsCommand.Execute(vm.Commands.KeyTools);

		Assert.Equal("translation-preview,-,add-key,remove-key,stale-all", vm.Commands.KeyTools.Line);
		Assert.Equal("translation-preview,-,add-key,remove-key,stale-all", memory.Line("key"));
		Assert.Equal("translation-form,toggle-stale", memory.Line("translation"));
		Assert.Null(memory.Line("search"));
	}

	[Fact]
	public void CancelOrTheWindowsCloseKeepsNothing_AndPutsTheToolbarsBack() {
		var memory = new ToolbarMemory();
		memory.Keep("node", "add-node,-,remove");
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs, toolbars: memory);
		MenuRow before = vm.Commands.NodeTools.Entries[1];
		foreach (bool cancel in new[] { true, false }) {
			dialogs.OnShow = dialog => {
				var editor = (ToolbarsViewModel)dialog;
				editor.Move(Chip(editor, "add-node"), null, 0);
				editor.Move(Chip(editor, "undo"), Row(editor, "search"), 0);
				Assert.Equal("undo,back,forward,search-box,filters", vm.Commands.SearchTools.Line); //shown at once
				if (cancel) {
					editor.CancelCommand.Execute(null);
				} //else Escape, or the window's X
			};
			vm.CustomizeToolbarsCommand.Execute(null);
			Assert.Equal("back,forward,search-box,filters", vm.Commands.SearchTools.Line);
			Assert.Equal("add-node,-,remove", vm.Commands.NodeTools.Line);
			Assert.Same(before, vm.Commands.NodeTools.Entries[1]);
			Assert.Equal("add-node,-,remove", memory.Line("node"));
			Assert.Null(memory.Line("search"));
		}
	}

	[Fact]
	public void ResetShipsEveryToolbar_KeptByOk_PutBackByCancel() {
		var memory = new ToolbarMemory();
		memory.Keep("node", "");
		memory.Keep("search", "search-box,filters,back,forward");
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs, toolbars: memory);
		foreach (bool okay in new[] { false, true }) {
			dialogs.OnShow = dialog => {
				var editor = (ToolbarsViewModel)dialog;
				editor.ResetCommand.Execute(null);
				Assert.All(vm.Commands.Toolbars, toolbar => Assert.Equal(toolbar.ShippedLine, toolbar.Line));
				InStep(editor);
				Assert.DoesNotContain(editor.Bucket, chip => chip.Entry.Name == "add-node");
				(okay ? editor.OkayCommand : editor.CancelCommand).Execute(null);
			};
			vm.CustomizeToolbarsCommand.Execute(null);
			if (!okay) {
				Assert.True(vm.Commands.NodeTools.IsEmpty);
				Assert.Equal("search-box,filters,back,forward", vm.Commands.SearchTools.Line);
			}
		}
		Assert.All(vm.Commands.Toolbars, toolbar => Assert.Null(memory.Line(toolbar.Name)));
		Assert.Equal("add-node,remove,add-comment", vm.Commands.NodeTools.Line);
	}
}
