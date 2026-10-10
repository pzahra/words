using System.Collections.Specialized;
using System.Windows.Input;
using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The command table (SPEC: Menu and toolbars): the menu is the inventory,
///     the toolbars and the tree's context menu draw from it, every row reads
///     without a key leaking, a key is bound once, a toggle mirrors the state
///     it stands for wherever that state changes, and a choice mirrors its
///     owner's options and pick.
/// </summary>
public class CommandTableTests {
	private const string Ini = "value-en=English\nvalue-de=Deutsch\ncomment-de=German\n\n[k]\nvalue=x\nvalue-de=y\n";

	private static MainWindowViewModel NewVm() => new(new FakeDialogs());

	private static MainWindowViewModel LoadedVm() {
		var vm = NewVm();
		vm.LoadFile(new StringReader(Ini), "Example");
		return vm;
	}

	private static ToggleItem ToggleOf(MainWindowViewModel vm, string key) => vm.Commands.Rows.OfType<ToggleItem>().Single(toggle => toggle.Caption == Words.Known[key]);
	private static ChoiceItem ChoiceOf(MainWindowViewModel vm, string key) => vm.Commands.Choices.Single(choice => choice.Caption == Words.Known[key]);

	private static IEnumerable<MenuRow> Tools(CommandTable table)
		=> table.NodeTools.Concat(table.KeyTools).Concat(table.FilterTools).Concat(table.NameTools)
			.Concat(table.DefaultTools).Concat(table.TranslationTools).Concat(table.NavigationTools).Concat<MenuRow>(table.LanguageTools);

	[Fact]
	public void TheMenuIsTheInventory() {
		var vm = NewVm();
		//every command the view model exposes is in the menu once, except the two
		//that take what they act on from the badge they sit beside
		IEnumerable<ICommand> commands = typeof(MainWindowViewModel).GetProperties()
			.Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType)
				&& property.Name is not (nameof(MainWindowViewModel.ShowGripesCommand) or nameof(MainWindowViewModel.ShowFileGripesCommand)))
			.Select(property => (ICommand)property.GetValue(vm)!)
			//and the tree's own, Back and Forward
			.Concat(typeof(TreeViewModel).GetProperties()
				.Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
				.Select(property => (ICommand)property.GetValue(vm.Tree)!));
		List<CommandItem> rows = [.. vm.Commands.Rows];
		Assert.Contains(rows, row => row.Command == vm.Tree.BackCommand);
		foreach (ICommand command in commands) {
			Assert.Single(rows, row => ReferenceEquals(row.Command, command));
		}
		Assert.Equal(4, vm.Commands.Menu.Count);
		Assert.All(vm.Commands.Menu, group => Assert.Contains('_', FakeDialogs.Rendered(group.Caption))); //an access key each
		//the flags on the selected key are toggles: a tick in the menu, a state on the toolbar
		Assert.IsType<ToggleItem>(rows.Single(row => row.Command == vm.ToggleConstantCommand));
		Assert.IsType<ToggleItem>(rows.Single(row => row.Command == vm.ToggleStaleLanguageCommand));
		//both panes' plural forms and both languages are choices, under View
		Assert.Equal(4, vm.Commands.Menu[2].Items.OfType<ChoiceItem>().Count());
	}

	[Fact]
	public void EveryRowReadsAndAKeyIsBoundOnce() {
		var vm = LoadedVm();
		List<CommandItem> rows = [.. vm.Commands.Rows];
		foreach (CommandItem row in rows) {
			FakeDialogs.Rendered(row.Caption);
			FakeDialogs.Rendered(row.Tip);
		}
		foreach (ChoiceItem choice in vm.Commands.Choices) {
			FakeDialogs.Rendered(choice.Caption);
			Assert.NotEmpty(choice.Options);
			Assert.All(choice.Options, option => FakeDialogs.Rendered(option.Label));
		}
		List<string> gestures = [.. rows.Where(row => row.Gesture is not null).Select(row => row.GestureText)];
		Assert.NotEmpty(gestures);
		Assert.Equal(gestures.Count, gestures.Distinct().Count());
		Assert.All(rows.Where(row => row.Gesture is not null), row => Assert.EndsWith($"({row.GestureText})", row.Tip));
		//Find carries Ctrl+F of its own: in the menu, not among the keys the window binds
		Assert.Contains(rows, row => row.Command == ApplicationCommands.Find);
		Assert.DoesNotContain(vm.Commands.Shortcuts, row => row.Command is RoutedCommand);
		Assert.Contains(vm.Commands.Shortcuts, row => row.Command == vm.SaveCommand);
		//the mouse's back and forward buttons run Back and Forward, each bound once
		Assert.Equal([vm.Tree.BackCommand, vm.Tree.ForwardCommand], vm.Commands.MouseShortcuts.Select(row => row.Command));
		Assert.Equal([MouseButton.XButton1, MouseButton.XButton2], vm.Commands.MouseShortcuts.Select(row => row.Button!.Value));
	}

	[Fact]
	public void TheToolbarsAndTheContextMenuDrawFromTheMenu() {
		var vm = NewVm();
		List<MenuRow> rows = [.. vm.Commands.Menu.SelectMany(group => group.Items)];
		foreach (MenuRow tool in Tools(vm.Commands)) {
			Assert.Contains(tool, rows);
		}
		Assert.Same(vm.Commands.Menu[1].Items, vm.Commands.EditRows);
		//files in and out are the menu's alone, with their keys
		Assert.DoesNotContain(Tools(vm.Commands).OfType<CommandItem>(), tool => tool.Command == vm.LoadFileCommand);
		Assert.DoesNotContain(Tools(vm.Commands).OfType<CommandItem>(), tool => tool.Command == vm.ResetCommand);
		//the test sits in both pane headers, the same row; each header toggles its own preview
		//and leads with its own plural form, a popup
		Assert.Same(vm.Commands.DefaultTools.OfType<CommandItem>().Single(tool => tool.Command == vm.TestParametersCommand),
			vm.Commands.TranslationTools.OfType<CommandItem>().Single(tool => tool.Command == vm.TestParametersCommand));
		Assert.Contains(vm.Commands.DefaultTools.OfType<CommandItem>(), tool => tool.Caption == Words.Known["menu.default-preview"]);
		Assert.Contains(vm.Commands.TranslationTools.OfType<CommandItem>(), tool => tool.Caption == Words.Known["menu.translation-preview"]);
		Assert.Equal(Words.Known["menu.default-form"], Assert.IsType<ChoiceItem>(vm.Commands.DefaultTools[0]).Caption);
		Assert.Equal(Words.Known["menu.translation-form"], Assert.IsType<ChoiceItem>(vm.Commands.TranslationTools[0]).Caption);
		Assert.All([vm.Commands.DefaultTools[0], vm.Commands.TranslationTools[0]], tool => Assert.True(((ChoiceItem)tool).IsPopup));
		Assert.False(ChoiceOf(vm, "menu.translation-language").IsPopup);
		//the filter popup: the three views and the clear; Rename alone beside the name
		Assert.Equal(3, vm.Commands.FilterTools.OfType<ToggleItem>().Count());
		Assert.Contains(vm.Commands.FilterTools, tool => tool.Command == vm.ClearFiltersCommand);
		Assert.Equal(vm.RenameNodeCommand, Assert.Single(vm.Commands.NameTools).Command);
		//by the search: Back and Forward
		Assert.Equal([vm.Tree.BackCommand, vm.Tree.ForwardCommand], vm.Commands.NavigationTools.Select(tool => tool.Command));
		//the language strip: the manager, and the translation language as a combo box
		Assert.Contains(vm.Commands.LanguageTools, tool => tool is CommandItem { Command: var command } && command == vm.ManageLanguagesCommand);
		Assert.Contains(vm.Commands.LanguageTools, tool => tool is ChoiceItem choice && choice.Caption == Words.Known["menu.translation-language"]);
	}

	[Fact]
	public void AToggleMirrorsItsStateWhereverItChanges() {
		var vm = NewVm();
		ToggleItem stale = ToggleOf(vm, "menu.stale-view");
		List<string?> raised = [];
		stale.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
		Assert.False(stale.IsChecked);
		Assert.True(stale.IsEnabled);

		stale.IsChecked = true; //the menu's tick
		Assert.True(vm.Tree.IsStaleFilter);
		Assert.Contains(nameof(ToggleItem.IsChecked), raised);

		raised.Clear();
		stale.Command.Execute(null); //its key
		Assert.False(vm.Tree.IsStaleFilter);
		Assert.Contains(nameof(ToggleItem.IsChecked), raised);

		raised.Clear();
		vm.Tree.IsStaleFilter = true; //the popup's button: the row hears of it
		Assert.True(stale.IsChecked);
		Assert.Contains(nameof(ToggleItem.IsChecked), raised);
		vm.ClearFiltersCommand.Execute(null);
		Assert.False(stale.IsChecked);

		ToggleItem preview = ToggleOf(vm, "menu.default-preview");
		raised.Clear();
		preview.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
		vm.ShowDefaultPreview = true; //the property itself
		Assert.True(preview.IsChecked);
		Assert.Contains(nameof(ToggleItem.IsChecked), raised);
	}

	[Fact]
	public void AFlagToggleReadsTheSelectedKey() {
		var vm = LoadedVm();
		ToggleItem review = ToggleOf(vm, "menu.toggle-review");
		ToggleItem stale = ToggleOf(vm, "menu.toggle-stale");
		Assert.False(review.IsEnabled); //nothing selected: greyed, unticked
		Assert.False(review.IsChecked);

		List<string?> raised = [];
		review.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0].Children.Single();
		Assert.True(review.IsEnabled); //the selection changed under the row
		Assert.Contains(nameof(ToggleItem.IsEnabled), raised);
		Assert.False(review.IsChecked);

		review.IsChecked = true; //the toolbar's toggle
		Assert.True(vm.Tree.SelectedKey!.NeedsReview);
		Assert.True(review.IsChecked);
		review.Command.Execute(null); //the menu, or the context menu
		Assert.False(vm.Tree.SelectedKey.NeedsReview);
		Assert.False(review.IsChecked);

		Assert.True(stale.IsEnabled);
		Assert.False(stale.IsChecked);
		stale.IsChecked = true;
		Assert.NotNull(vm.Tree.SelectedEntry!.Stale);
		Assert.True(stale.IsChecked);

		vm.Tree.SelectedKeyNode = null;
		Assert.False(review.IsEnabled);
		Assert.False(stale.IsChecked);
	}

	[Fact]
	public void AFlagToggleHearsOfAFlagNoToggleChanged() {
		var vm = LoadedVm();
		ToggleItem review = ToggleOf(vm, "menu.toggle-review");
		ToggleItem stale = ToggleOf(vm, "menu.toggle-stale");
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0].Children.Single();
		List<string?> raised = [];
		review.PropertyChanged += (_, e) => raised.Add($"review.{e.PropertyName}");
		stale.PropertyChanged += (_, e) => raised.Add($"stale.{e.PropertyName}");

		vm.Tree.SelectedEntry!.Comment = "check this"; //a note typed raises the hand
		Assert.True(vm.Tree.SelectedKey!.NeedsReview);
		Assert.Contains("review.IsChecked", raised);

		raised.Clear();
		vm.StaleAllLanguagesCommand.Execute(null); //a button, not the toggle
		Assert.True(stale.IsChecked);
		Assert.Contains("stale.IsChecked", raised);
	}

	[Fact]
	public void AChoiceMirrorsItsOwner() {
		var vm = LoadedVm();
		ChoiceItem language = ChoiceOf(vm, "menu.translation-language");
		//each language by its exonym, its own name where it has none
		Assert.Equal(["English", "German"], language.Options.Select(option => option.Label));
		Assert.Same(vm.Tree.SelectedLanguage, language.Selected!.Value);

		Choice german = language.Options.Single(option => option.Label == "German");
		List<string?> raised = [];
		german.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
		german.IsChecked = true; //the submenu's tick
		Assert.Equal("de", vm.Tree.SelectedLanguage.Code);
		Assert.Same(german, language.Selected);
		Assert.Contains(nameof(Choice.IsChecked), raised);

		german.IsChecked = false; //unticking the one in use: nothing moves, the tick comes back
		Assert.Equal("de", vm.Tree.SelectedLanguage.Code);
		Assert.True(german.IsChecked);
		language.Selected = null; //the combo box while its items turn over
		Assert.Equal("de", vm.Tree.SelectedLanguage.Code);

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.Single(entry => entry.Code == "en"); //the owner moved: the rows follow
		Assert.False(german.IsChecked);
		Assert.Equal("en", ((LanguageEntry)language.Selected!.Value).Code);

		//Wordsmith's own language: a pick is a request, and the tick stays on the one in use
		ChoiceItem own = ChoiceOf(vm, "menu.ui-language");
		Assert.Equal(vm.UiLanguages.Select(pair => pair.Value), own.Options.Select(option => option.Label));
		Assert.NotNull(own.Selected);
		string? requested = null;
		vm.UiLanguageRequested += code => requested = code;
		Choice other = own.Options.First(option => !option.IsChecked);
		own.Selected = other;
		Assert.Equal(((KeyValuePair<string, string>)other.Value).Key, requested);
		Assert.False(other.IsChecked);
	}

	[Fact]
	public void AChoiceSurvivesItsOptionsTurningOver() {
		//a combo box answers a cleared list by pushing null into the selection, from
		//inside the refresh that cleared it; the refresh must not start over
		var vm = LoadedVm();
		ChoiceItem language = ChoiceOf(vm, "menu.translation-language");
		int pushes = 0;
		language.Options.CollectionChanged += (_, e) => {
			if (e.Action == NotifyCollectionChangedAction.Reset) {
				Assert.True(++pushes < 10, "the refresh and the combo box are feeding each other");
				language.Selected = null;
			}
		};
		vm.Session.Languages.Add(vm.Session.Files[0], new LanguageEntry("fr", "Français"));
		vm.Tree.RefreshBadges(); //every path that changes the table passes here: the rows turn over once
		Assert.Equal(1, pushes);
		Assert.Equal(["English", "German", "Français"], language.Options.Select(option => option.Label));
		Assert.Same(vm.Tree.SelectedLanguage, language.Selected!.Value);
	}

	[Fact]
	public void ExitIsTheWindowsToAnswer() {
		var vm = NewVm();
		int asked = 0;
		vm.ExitRequested += () => asked++;
		vm.ExitCommand.Execute(null);
		Assert.Equal(1, asked);
	}
}
