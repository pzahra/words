using System.ComponentModel;
using System.IO;
using PatTech.Localization;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The toolbars (SPEC: The toolbar editor): seven, by name, each row named by
///     its caption key and on one toolbar at most, the menu's own on none; a line
///     in the config sets a toolbar, breaks and all; a hand-edited line loses what
///     the editor does not know or may not put there, its pinned entries go back
///     where they belong, and it is written back so; Copy Key Name copies what a
///     program passes.
/// </summary>
[Collection(nameof(EditorConfig))] //swaps EditorConfig.Path: never beside another class that does
public class ToolbarTests {
	private const string Ini = "value-en=English\n\n[k]\nvalue=x\n\n; a note\n[.child]\nvalue=y\n";

	private static CommandTable Table(IToolbarMemory memory) => new MainWindowViewModel(new FakeDialogs(), toolbars: memory).Commands;

	private static IEnumerable<string> Placed(CommandTable table) => table.Toolbars.SelectMany(toolbar => toolbar.Entries).Select(Toolbar.Spell);

	[Fact]
	public void EveryRowIsNamedByItsCaptionKey_ThePinnedEntriesToo_AndTheLinksAreTheCommandRows() {
		CommandTable table = Table(new ToolbarMemory());
		List<MenuRow> rows = [.. table.Menu.SelectMany(group => group.Items).Where(row => row is not MenuBreak)];
		Assert.All(rows, row => Assert.Same(row, table.Named[row.Name]));
		Assert.All(rows.OfType<CommandItem>(), row => Assert.Equal(Words.Known["menu." + row.Name], row.Caption));
		Assert.All(rows.OfType<ChoiceItem>(), row => Assert.Equal(Words.Known["menu." + row.Name], row.Caption));
		List<MenuRow> pinned = [.. table.Toolbars.SelectMany(toolbar => toolbar.Pinned)];
		Assert.Equal(["search-box", "filters", "node-name", "translation-language"], pinned.Select(pin => pin.Name));
		Assert.All(pinned, pin => Assert.Same(pin, table.Named[pin.Name]));
		//the menu's rows and the three the window draws, nothing else
		Assert.Equal(rows.Count + 3, table.Named.Count);
		Assert.Equal(rows.OfType<CommandItem>().ToHashSet(), table.Links.Values.ToHashSet());
		Assert.All(CommandTable.MenuOnly, name => Assert.Contains(name, table.Named.Keys));
	}

	[Fact]
	public void TheToolbarsShipAsBefore_AndKeepNoLine() {
		var memory = new ToolbarMemory();
		CommandTable table = Table(memory);
		Assert.Equal(["search", "name", "default", "languages", "translation", "node", "key"], table.Toolbars.Select(toolbar => toolbar.Name));
		Assert.Equal("back,forward,search-box,filters", table.SearchTools.Line);
		Assert.Equal("node-name,rename,copy-key", table.NameTools.Line);
		Assert.Equal("default-form,toggle-constant,toggle-review,default-preview", table.DefaultTools.Line);
		Assert.Equal("languages,translation-language", table.LanguageTools.Line);
		Assert.Equal("translation-form,toggle-stale,translation-preview", table.TranslationTools.Line);
		Assert.Equal("add-node,remove,add-comment", table.NodeTools.Line);
		Assert.Equal("add-key,remove-key,stale-all", table.KeyTools.Line);
		Assert.All(table.Toolbars, toolbar => Assert.Null(memory.Line(toolbar.Name)));
		Assert.DoesNotContain(Placed(table), name => CommandTable.MenuOnly.Contains(name));
		//the filter popup is the View menu's filters, not a toolbar
		Assert.Equal(["stale-view", "review-view", "missing-view", "mismatch-view", "clear-filters"], table.FilterTools.Select(Toolbar.Spell));
		Assert.Same(table.FilterTools, Assert.IsType<FiltersEntry>(table.Named["filters"]).Filters);
	}

	[Fact]
	public void ALineSetsItsToolbar_BreaksAndAll_AndARowsFirstPlaceWins() {
		var memory = new ToolbarMemory();
		memory.Keep("search", "back,-,search-box,filters,forward,stale-view");
		memory.Keep("name", "node-name,rename,copy-key,-,add-key");
		//rename ships on name, which comes first: it stays there
		memory.Keep("key", "add-key,remove-key,stale-all,rename");
		CommandTable table = Table(memory);

		Assert.IsType<MenuBreak>(table.SearchTools.Entries[1]);
		Assert.Equal("back,-,search-box,filters,forward,stale-view", table.SearchTools.Line);
		Assert.Equal("node-name,rename,copy-key,-,add-key", table.NameTools.Line);
		Assert.Equal("remove-key,stale-all", table.KeyTools.Line);
		Assert.Equal("back,-,search-box,filters,forward,stale-view", memory.Line("search"));
		Assert.Equal("remove-key,stale-all", memory.Line("key")); //written back without what it lost
		//a filter as a button besides, and the popup as it was
		Assert.Equal(5, table.FilterTools.Count);
		//each row once
		List<string> placed = [.. Placed(table).Where(name => name != Toolbar.Break)];
		Assert.Equal(placed.Distinct(), placed);
	}

	[Fact]
	public void AHandEditedLine_LosesWhatItMayNotHold_AndIsWrittenBackWithout() {
		var memory = new ToolbarMemory();
		//unknown, the menu's own, twice, and pinned to a toolbar before or after it; spaces and case are forgiven
		memory.Keep("node", " Add-Node , nonsense, exit, ui-language, remove, add-node, search-box, translation-language, add-comment ");
		memory.Keep("default", "default-form,toggle-constant,tips-again,toggle-review,reset,default-preview");
		CommandTable table = Table(memory);

		Assert.Equal("add-node,remove,add-comment", table.NodeTools.Line);
		Assert.Equal("default-form,toggle-constant,toggle-review,default-preview", table.DefaultTools.Line);
		//both came out as they ship: their lines go
		Assert.Null(memory.Line("node"));
		Assert.Null(memory.Line("default"));
		Assert.Equal("back,forward,search-box,filters", table.SearchTools.Line);
		Assert.Equal("languages,translation-language", table.LanguageTools.Line);
	}

	[Fact]
	public void APinnedEntryLeftOutOrPutElsewhere_GoesBackAtItsPlaceAsShipped() {
		var memory = new ToolbarMemory();
		//the box and the filters left out: back at their places, the rest after them
		memory.Keep("search", "back,forward,add-node,remove");
		//the language put on the toolbar before its own, and its own left empty
		memory.Keep("name", "node-name,translation-language,rename,copy-key");
		memory.Keep("languages", "");
		//the name left out, its place past the line's end: at the end
		memory.Keep("translation", "translation-form,toggle-stale,translation-preview,node-name");
		CommandTable table = Table(memory);

		Assert.Equal("back,forward,search-box,filters,add-node,remove", table.SearchTools.Line);
		Assert.Equal("node-name,rename,copy-key", table.NameTools.Line);
		Assert.Equal("translation-language", table.LanguageTools.Line);
		Assert.Equal("translation-form,toggle-stale,translation-preview", table.TranslationTools.Line);
		Assert.Equal("add-comment", table.NodeTools.Line);
		Assert.Equal("back,forward,search-box,filters,add-node,remove", memory.Line("search"));
		Assert.Null(memory.Line("name"));
		Assert.Equal("translation-language", memory.Line("languages"));
		Assert.Null(memory.Line("translation"));
		Assert.Equal("add-comment", memory.Line("node"));
	}

	[Fact]
	public void AnEmptiedToolbarStaysEmpty_ItsRowsOnNone() {
		var memory = new ToolbarMemory();
		memory.Keep("node", "");
		CommandTable table = Table(memory);
		Assert.True(table.NodeTools.IsEmpty);
		Assert.Equal("", memory.Line("node"));
		Assert.DoesNotContain("add-node", Placed(table));

		//it shows again once a row is put back on it
		List<string?> changed = [];
		table.NodeTools.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
		table.NodeTools.Entries.Add(table.Named["add-node"]);
		Assert.False(table.NodeTools.IsEmpty);
		Assert.Contains(nameof(Toolbar.IsEmpty), changed);
	}

	[Fact]
	public void TheConfigKeepsALineAToolbar_AnEmptyOneToo_AndAHandEditedOneIsWrittenBackCorrected() {
		string configured = EditorConfig.Path;
		EditorConfig.Path = Path.Combine(Path.GetTempPath(), $"wordsmith-{Guid.NewGuid():N}", "config.ini");
		try {
			var memory = new ConfigToolbarMemory();
			Assert.Null(memory.Line("node")); //no file: as shipped
			EditorConfig.Language = "it";
			memory.Keep("node", "");
			memory.Keep("search", "back,-,forward,search-box,filters");
			Assert.Equal(["[editor]", "language=it", "", "[toolbars]", "node=", "search=back,-,forward,search-box,filters"], File.ReadAllLines(EditorConfig.Path));
			Assert.Equal("", memory.Line("node"));
			memory.Keep("node", null);
			memory.Keep("search", null);
			Assert.Null(memory.Line("search"));

			//the editor reads the lines, and writes back what it corrected
			File.WriteAllLines(EditorConfig.Path, ["[editor]", "language=it", "", "[toolbars]", "node=add-node,exit,remove", "search=forward,back", "key=add-key,remove-key,stale-all"]);
			CommandTable table = Table(memory);
			Assert.Equal("add-node,remove", table.NodeTools.Line);
			Assert.Equal("forward,back,search-box,filters", table.SearchTools.Line);
			Assert.Equal(["[editor]", "language=it", "", "[toolbars]", "node=add-node,remove", "search=forward,back,search-box,filters"], File.ReadAllLines(EditorConfig.Path));
		}
		finally {
			if (File.Exists(EditorConfig.Path)) {
				Directory.Delete(Path.GetDirectoryName(EditorConfig.Path)!, recursive: true);
			}
			EditorConfig.Path = configured;
		}
	}

	[Fact]
	public void CopyKeyName_CopiesWhatAProgramPasses_AndIsGreyedOnAFileAndAComment() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader(Ini), "Example");
		KeyNode file = vm.Tree.KeyNodes[0];
		KeyNode k = file.Children.Single(node => node.Label == "k");
		KeyNode child = k.Children.Single(node => node is not OrganizerNode);
		OrganizerNode note = k.Children.OfType<OrganizerNode>().Single();

		foreach (KeyNode? greyed in new[] { null, file, note }) {
			vm.Tree.SelectedKeyNode = greyed;
			Assert.False(vm.CopyKeyCommand.CanExecute(null));
		}
		vm.Tree.SelectedKeyNode = child;
		Assert.True(vm.CopyKeyCommand.CanExecute(null));
		vm.CopyKeyCommand.Execute(null);
		vm.Tree.SelectedKeyNode = k;
		vm.CopyKeyCommand.Execute(null);
		Assert.Equal(["k.child", "k"], dialogs.Copied);

		//after Rename in the Edit menu, and beside it on the name toolbar; no key, Ctrl+C being the boxes'
		var copyKey = Assert.IsType<CommandItem>(vm.Commands.Named["copy-key"]);
		List<MenuRow> edit = [.. vm.Commands.EditRows];
		Assert.Equal(edit.IndexOf(vm.Commands.Named["rename"]) + 1, edit.IndexOf(copyKey));
		Assert.Equal(vm.Commands.NameTools.Entries.IndexOf(vm.Commands.Named["rename"]) + 1, vm.Commands.NameTools.Entries.IndexOf(copyKey));
		Assert.Null(copyKey.Gesture);
	}
}
