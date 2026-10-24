using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>A row of the command table: a command, a toggle, a choice, or the break between two runs; or an entry pinned to a toolbar.</summary>
public abstract class MenuRow : ViewModelBase {
	/// <summary>The help bubble that points at the row on a toolbar (SPEC: Help bubbles), if any.</summary>
	public string? Topic { get; init; }
	/// <summary>
	///     What a toolbar's line and a help page's <c>wordsmith:</c> link call it: the tail of
	///     its caption's key, <c>menu.stale-all</c> as <c>stale-all</c>, set by the table
	///     (SPEC: The toolbar editor, Rows by name). A break has none.
	/// </summary>
	public string Name { get; internal set; } = "";
	/// <summary>The pane the row is about, if one: over the other pane's toolbars its place is odd (SPEC: The toolbar editor).</summary>
	public Side? About { get; internal set; }
	/// <summary>Takes the room its toolbar's other entries leave: the search box, the node's name.</summary>
	public virtual bool Fills => false;
}

/// <summary>The line between two runs of a menu.</summary>
public sealed class MenuBreak : MenuRow;

/// <summary>
///     One command as every surface presents it (SPEC: Menu and toolbars): the
///     menu, the toolbars and the tree's context menu render from rows of these,
///     so a command is defined once — what it does, what it is called, its icon
///     and its key — and its gesture binds once, from <see cref="CommandTable.Shortcuts"/>.
/// </summary>
public class CommandItem : MenuRow {
	public ICommand Command { get; }
	[Localized]
	public string Caption { get; }
	public PackIconKind Icon { get; }
	public KeyGesture? Gesture { get; }
	/// <summary>A mouse button that runs it too: the back and forward buttons, for Back and Forward.</summary>
	public MouseButton? Button { get; }
	/// <summary>The gesture as a menu shows it; empty without one.</summary>
	public string GestureText => Gesture?.GetDisplayStringForCulture(CultureInfo.CurrentUICulture) ?? "";
	/// <summary>A toolbar's tooltip: the caption, and the key when there is one.</summary>
	public string Tip => Gesture is null ? Caption : $"{Caption} ({GestureText})";

	public CommandItem([Localized] string caption, PackIconKind icon, ICommand command, KeyGesture? gesture = null, MouseButton? button = null) {
		Caption = caption;
		Icon = icon;
		Command = command;
		Gesture = gesture;
		Button = button;
	}
}

/// <summary>
///     A command with a state behind it — a filter, a preview, a flag on the
///     selected key — checkable in a menu, a toggle on a toolbar. The command
///     flips the state and the row reads it back, so a tick never drifts from
///     what it stands for; the owner tells the row (<see cref="Refresh"/>) when
///     the state, or whether it applies, changed elsewhere.
/// </summary>
public sealed class ToggleItem : CommandItem {
	private readonly Func<bool> state;

	public bool IsChecked {
		get => state();
		set {
			//a surface pushed the opposite of what it read: flip, if the command applies
			if (value != state() && Command.CanExecute(null)) {
				Command.Execute(null);
			}
			Refresh(); //and re-read either way: a refused flip snaps back
		}
	}
	/// <summary>Whether the toggle applies now; a checkable menu item has no command to grey it.</summary>
	public bool IsEnabled => Command.CanExecute(null);

	public ToggleItem([Localized] string caption, PackIconKind icon, Func<bool> state, ICommand toggle, KeyGesture? gesture = null)
		: base(caption, icon, toggle, gesture) {
		this.state = state;
	}

	/// <summary>The state, or the selection it applies to, changed under the row: tell whoever shows it.</summary>
	public void Refresh() {
		AffectProperty(nameof(IsChecked));
		AffectProperty(nameof(IsEnabled));
	}
}

/// <summary>What a <see cref="ChoiceItem"/>'s options say beyond their names, and how the row shows on a toolbar.</summary>
/// <param name="CanPick">Whether an option can be picked; every one when absent.</param>
/// <param name="Filled">Whether an option has words: a plural form, marked with a dot.</param>
/// <param name="Missing">Whether an option wants words, in bold as the tree shows a gap.</param>
/// <param name="Greyed">Whether an option reads greyed while it can still be picked.</param>
/// <param name="Enabled">Whether the choice applies now; always when absent.</param>
/// <param name="Badge">What the toolbar button wears; nothing when absent or null.</param>
/// <param name="Popup">A popup button of ticked rows on a toolbar, rather than a combo box.</param>
public sealed record ChoiceLook(Func<object, bool>? CanPick = null, Func<object, bool>? Filled = null, Func<object, bool>? Missing = null,
	Func<object, bool>? Greyed = null, Func<bool>? Enabled = null, Func<string?>? Badge = null, bool Popup = false);

/// <summary>
///     A pick among options — the translation language, Wordsmith's own, a
///     pane's plural form — as a submenu of ticked rows in a menu and a combo
///     box or a popup button on a toolbar. The options and the pick live on the
///     owner: the row mirrors the options as <see cref="Choice"/> rows, re-reads
///     them (<see cref="Refresh"/>) when the owner changed, and sends a pick
///     back through the owner.
/// </summary>
public sealed class ChoiceItem : MenuRow {
	private readonly Func<IEnumerable<object>> options;
	private readonly Func<object, string> label;
	private readonly Func<object?> current;
	private readonly Action<object> pick;
	private readonly ChoiceLook look;

	[Localized]
	public string Caption { get; }
	public PackIconKind Icon { get; }
	/// <summary>Whether the choice applies now: a greyed selector still shows its pick.</summary>
	public bool IsEnabled => look.Enabled?.Invoke() ?? true;
	/// <summary>The toolbar button's badge: the plural form showing, if not the plain value.</summary>
	public string? Badge => look.Badge?.Invoke();
	/// <summary>A popup button on a toolbar, rather than a combo box.</summary>
	public bool IsPopup => look.Popup;
	/// <summary>The options as rows, in the owner's order; turned over when the owner's change.</summary>
	public ObservableCollection<Choice> Options { get; } = [];
	/// <summary>
	///     The option in use. A combo box pushes null while its items turn over —
	///     from inside <see cref="Refresh"/>, which hands the selection back when
	///     it is done — so null picks nothing and refreshes nothing.
	/// </summary>
	public Choice? Selected {
		get {
			object? value = current();
			return Options.FirstOrDefault(option => Equals(option.Value, value));
		}
		set {
			if (value is null) {
				return;
			}
			if (!Equals(value.Value, current()) && IsEnabled && CanPick(value.Value)) {
				pick(value.Value);
			}
			Refresh(); //a pick the owner declined (Wordsmith's language asks first) stays put
		}
	}

	private ChoiceItem([Localized] string caption, PackIconKind icon, Func<IEnumerable<object>> options, Func<object, string> label, Func<object?> current, Action<object> pick, ChoiceLook? look) {
		Caption = caption;
		Icon = icon;
		this.options = options;
		this.label = label;
		this.current = current;
		this.pick = pick;
		this.look = look ?? new();
		Refresh();
	}

	/// <summary>A choice among <typeparamref name="T"/>s, read and written as the owner holds them.</summary>
	public static ChoiceItem Of<T>([Localized] string caption, PackIconKind icon, Func<IEnumerable<T>> options, Func<T, string> label, Func<T?> current, Action<T> pick, ChoiceLook? look = null, string? topic = null) where T : notnull
		=> new(caption, icon, () => options().Cast<object>(), value => label((T)value), () => current(), value => pick((T)value), look) { Topic = topic };

	internal string LabelOf(object value) => label(value);
	internal bool IsCurrent(object value) => Equals(value, current());
	internal bool CanPick(object value) => look.CanPick?.Invoke(value) ?? true;
	internal bool IsFilled(object value) => look.Filled?.Invoke(value) ?? false;
	internal bool IsMissing(object value) => look.Missing?.Invoke(value) ?? false;
	internal bool IsGreyed(object value) => look.Greyed?.Invoke(value) ?? false;

	/// <summary>The owner's options, their names or its pick changed: the rows follow.</summary>
	public void Refresh() {
		List<object> values = [.. options()];
		if (!values.SequenceEqual(Options.Select(option => option.Value))) {
			Options.Clear();
			foreach (object value in values) {
				Options.Add(new Choice(this, value));
			}
		}
		foreach (Choice option in Options) {
			option.Refresh();
		}
		AffectProperty(nameof(Selected));
		AffectProperty(nameof(IsEnabled));
		AffectProperty(nameof(Badge));
	}
}

/// <summary>One option of a <see cref="ChoiceItem"/>: a checkable row whose tick means "this one".</summary>
public sealed class Choice(ChoiceItem owner, object value) : ViewModelBase {
	public object Value { get; } = value;
	public string Label => owner.LabelOf(Value);
	/// <summary>Ticking picks this one; unticking the one in use changes nothing, and the tick comes back.</summary>
	public bool IsChecked {
		get => owner.IsCurrent(Value);
		set {
			if (value) {
				owner.Selected = this;
			}
			else {
				owner.Refresh();
			}
		}
	}
	/// <summary>Whether it can be picked: an empty plural form no count reads cannot.</summary>
	public bool IsEnabled => owner.CanPick(Value);
	/// <summary>A plural form with words: a dot beside it.</summary>
	public bool HasWords => owner.IsFilled(Value);
	/// <summary>A plural form the badge counts as missing: bold, as the tree shows a gap.</summary>
	public bool IsMissing => owner.IsMissing(Value);
	/// <summary>Greyed, though it may still be picked: a form the language does not count by.</summary>
	public bool IsGreyed => owner.IsGreyed(Value);

	public void Refresh() {
		AffectProperty(nameof(IsChecked));
		AffectProperty(nameof(Label));
		AffectProperty(nameof(IsEnabled));
		AffectProperty(nameof(HasWords));
		AffectProperty(nameof(IsMissing));
		AffectProperty(nameof(IsGreyed));
	}
}

/// <summary>A top menu: its caption, the access key marked with an underscore, its rows, and the help topic that points at it, if any.</summary>
public sealed class MenuGroup([Localized] string caption, IReadOnlyList<MenuRow> items, string? topic = null) {
	[Localized]
	public string Caption { get; } = caption;
	public IReadOnlyList<MenuRow> Items { get; } = items;
	/// <summary>The help bubble that points at this menu (SPEC: Help bubbles): File's is the empty editor's.</summary>
	public string? Topic { get; } = topic;
}

/// <summary>
///     The command table (SPEC: Menu and toolbars). The menu is the inventory —
///     every command the editor has, grouped the usual way; the toolbars carry
///     what the user puts on them, shipping with what is convenient (SPEC: The
///     toolbar editor), and the tree's context menu is the Edit menu again, all
///     drawn from the same rows. The words are looked up here, by literal key,
///     so the editor's file names every one of them.
/// </summary>
public sealed class CommandTable {
	public IReadOnlyList<MenuGroup> Menu { get; }
	/// <summary>The Edit menu's rows: the tree's context menu.</summary>
	public IReadOnlyList<MenuRow> EditRows { get; }
	/// <summary>The toolbars, in the order a row's first place on one wins.</summary>
	public IReadOnlyList<Toolbar> Toolbars { get; }
	/// <summary>Over the tree: Back and Forward, the search box, the filter button.</summary>
	public Toolbar SearchTools { get; }
	/// <summary>Over the default pane: the selected node's name, Rename, Copy Key Name.</summary>
	public Toolbar NameTools { get; }
	/// <summary>The default pane's header: the form, the key's flags, its preview.</summary>
	public Toolbar DefaultTools { get; }
	/// <summary>Above the translation pane: the languages, and the one selected as a combo box.</summary>
	public Toolbar LanguageTools { get; }
	/// <summary>The translation pane's header: the form, the stale flag, its preview.</summary>
	public Toolbar TranslationTools { get; }
	/// <summary>The node operations, under the tree.</summary>
	public Toolbar NodeTools { get; }
	/// <summary>The key operations, under the default pane.</summary>
	public Toolbar KeyTools { get; }
	/// <summary>The filters, in the filter button's popup: no toolbar, so fixed.</summary>
	public IReadOnlyList<CommandItem> FilterTools { get; }
	/// <summary>Every row of the menu and every entry the window draws, by name.</summary>
	public IReadOnlyDictionary<string, MenuRow> Named { get; }
	/// <summary>Every entry a toolbar may hold but a pinned one, in menu order, the filter button before the filters it holds.</summary>
	public IReadOnlyList<MenuRow> Placeable { get; }
	/// <summary>
	///     Each command row by name: what a help page's <c>wordsmith:</c> link names
	///     (SPEC: Help bubbles, Links).
	/// </summary>
	public IReadOnlyDictionary<string, CommandItem> Links { get; }
	/// <summary>The rows none of which warrants a button: they are the menu's alone.</summary>
	public static IReadOnlySet<string> MenuOnly { get; } = new HashSet<string> { "ui-language", "exit", "reset", "tips-again" };

	private readonly IToolbarMemory memory;

	/// <summary>Every command row, in menu order.</summary>
	public IEnumerable<CommandItem> Rows => Menu.SelectMany(group => group.Items).OfType<CommandItem>();
	/// <summary>Every choice, in menu order.</summary>
	public IEnumerable<ChoiceItem> Choices => Menu.SelectMany(group => group.Items).OfType<ChoiceItem>();
	/// <summary>The rows whose key the window binds; a routed command (Find) carries its own.</summary>
	public IEnumerable<CommandItem> Shortcuts => Rows.Where(row => row.Gesture is not null && row.Command is not RoutedCommand);
	/// <summary>The rows a mouse button runs too, which the window binds with its keys.</summary>
	public IEnumerable<CommandItem> MouseShortcuts => Rows.Where(row => row.Button is not null);

	/// <param name="toolbars">What the toolbars remember: each one changed from how it ships.</param>
	public CommandTable(MainWindowViewModel vm, IToolbarMemory toolbars) {
		TreeViewModel tree = vm.Tree;
		//File: in, out, and away
		var create = new CommandItem(Words.Known["menu.new"], PackIconKind.FilePlus, vm.NewFileCommand, Ctrl(Key.N));
		var load = new CommandItem(Words.Known["menu.load"], PackIconKind.FolderUpload, vm.LoadFileCommand, Ctrl(Key.O));
		var import = new CommandItem(Words.Known["menu.import"], PackIconKind.FileImport, vm.ImportCommand, Ctrl(Key.I));
		var merge = new CommandItem(Words.Known["menu.merge"], PackIconKind.Merge, vm.MergeFilesCommand);
		var save = new CommandItem(Words.Known["menu.save"], PackIconKind.ContentSave, vm.SaveCommand, Ctrl(Key.S));
		//no gesture: Ctrl+Shift+S is Stale All Languages
		var saveAs = new CommandItem(Words.Known["menu.save-as"], PackIconKind.ContentSaveMove, vm.SaveAsCommand);
		var export = new CommandItem(Words.Known["menu.export"], PackIconKind.FileExport, vm.ExportCommand, Ctrl(Key.E));
		var reset = new CommandItem(Words.Known["menu.reset"], PackIconKind.Reload, vm.ResetCommand);
		var exit = new CommandItem(Words.Known["menu.exit"], PackIconKind.ExitToApp, vm.ExitCommand, new KeyGesture(Key.F4, ModifierKeys.Alt));
		//Edit: Undo and Redo, the structure, then the flags, which read off the selected node
		var undo = new CommandItem(Words.Known["menu.undo"], PackIconKind.Undo, vm.UndoCommand, Ctrl(Key.Z));
		var redo = new CommandItem(Words.Known["menu.redo"], PackIconKind.Redo, vm.RedoCommand, Ctrl(Key.Y));
		var addNode = new CommandItem(Words.Known["menu.add-node"], PackIconKind.PlusThick, vm.AddNodeCommand);
		var addKey = new CommandItem(Words.Known["menu.add-key"], PackIconKind.KeyPlus, vm.AddKeyCommand);
		var addComment = new CommandItem(Words.Known["menu.add-comment"], PackIconKind.CommentPlus, vm.AddOrganizerCommand);
		var rename = new CommandItem(Words.Known["menu.rename"], PackIconKind.RenameBox, vm.RenameNodeCommand, new KeyGesture(Key.F2));
		//no key: Ctrl+C is the boxes'
		var copyKey = new CommandItem(Words.Known["menu.copy-key"], PackIconKind.ContentCopy, vm.CopyKeyCommand);
		var remove = new CommandItem(Words.Known["menu.remove"], PackIconKind.Delete, vm.RemoveNodeCommand, new KeyGesture(Key.Delete));
		var toggleReview = new ToggleItem(Words.Known["menu.toggle-review"], PackIconKind.HandFrontLeft, () => tree.SelectedNeedsReview, vm.ToggleNeedsReviewCommand) { Topic = HelpTopics.Review.Id };
		var toggleConstant = new ToggleItem(Words.Known["menu.toggle-constant"], PackIconKind.TranslateOff, () => tree.SelectedIsConstant, vm.ToggleConstantCommand);
		var toggleStale = new ToggleItem(Words.Known["menu.toggle-stale"], PackIconKind.ClockAlertOutline, () => tree.SelectedIsStale, vm.ToggleStaleLanguageCommand) { Topic = HelpTopics.Stale.Id };
		var staleAll = new CommandItem(Words.Known["menu.stale-all"], PackIconKind.ClockAlert, vm.StaleAllLanguagesCommand, new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
		var removeKey = new CommandItem(Words.Known["menu.remove-key"], PackIconKind.KeyRemove, vm.RemoveKeyCommand);
		//View: the filters and the previews flip a property of their own; Back and Forward step the tree's history,
		//on the mouse's buttons too; Find carries Ctrl+F of its own; the languages are choices
		var staleView = new ToggleItem(Words.Known["menu.stale-view"], PackIconKind.ClockAlert, () => tree.IsStaleFilter, new DelegateCommand(() => tree.IsStaleFilter = !tree.IsStaleFilter));
		var reviewView = new ToggleItem(Words.Known["menu.review-view"], PackIconKind.HandFrontLeft, () => tree.NeedsReviewFilter, new DelegateCommand(() => tree.NeedsReviewFilter = !tree.NeedsReviewFilter));
		var missingView = new ToggleItem(Words.Known["menu.missing-view"], PackIconKind.TextBoxRemoveOutline, () => tree.MissingFilter, new DelegateCommand(() => tree.MissingFilter = !tree.MissingFilter));
		var mismatchView = new ToggleItem(Words.Known["menu.mismatch-view"], PackIconKind.CodeBraces, () => tree.MismatchFilter, new DelegateCommand(() => tree.MismatchFilter = !tree.MismatchFilter));
		var clearFilters = new CommandItem(Words.Known["menu.clear-filters"], PackIconKind.FilterRemoveOutline, vm.ClearFiltersCommand);
		var defaultPreview = new ToggleItem(Words.Known["menu.default-preview"], PackIconKind.Eye, () => vm.ShowDefaultPreview, new DelegateCommand(() => vm.ShowDefaultPreview = !vm.ShowDefaultPreview));
		var translationPreview = new ToggleItem(Words.Known["menu.translation-preview"], PackIconKind.EyeOutline, () => vm.ShowLocalizationPreview, new DelegateCommand(() => vm.ShowLocalizationPreview = !vm.ShowLocalizationPreview));
		var back = new CommandItem(Words.Known["menu.back"], PackIconKind.ArrowLeft, tree.BackCommand, new KeyGesture(Key.Left, ModifierKeys.Alt), MouseButton.XButton1);
		var forward = new CommandItem(Words.Known["menu.forward"], PackIconKind.ArrowRight, tree.ForwardCommand, new KeyGesture(Key.Right, ModifierKeys.Alt), MouseButton.XButton2);
		var find = new CommandItem(Words.Known["menu.find"], PackIconKind.Magnify, ApplicationCommands.Find, Ctrl(Key.F));
		ChoiceItem translationLanguage = ChoiceItem.Of(Words.Known["menu.translation-language"], PackIconKind.Earth,
			() => tree.FileLanguages, language => language.DisplayName, () => tree.SelectedLanguage, language => tree.SelectedLanguage = language, topic: HelpTopics.Translation.Id);
		ChoiceItem uiLanguage = ChoiceItem.Of(Words.Known["menu.ui-language"], PackIconKind.Web,
			() => vm.UiLanguages, pair => pair.Value, () => vm.UiLanguages.FirstOrDefault(pair => pair.Key == vm.UiLanguage), pair => vm.UiLanguage = pair.Key);
		//each value pane's plural form (SPEC: Plural forms): CLDR's six rows, a popup on the pane's header
		ChoiceItem defaultForm = ChoiceItem.Of(Words.Known["menu.default-form"], PackIconKind.Counter,
			() => PluralRules.Names, tree.DefaultForms.Label, () => tree.DefaultForms.Form, tree.PickDefaultForm,
			Forms(tree.DefaultForms, () => tree.DefaultFormsEnabled, tree.DefaultMisses), HelpTopics.Forms.Id);
		ChoiceItem translationForm = ChoiceItem.Of(Words.Known["menu.translation-form"], PackIconKind.Counter,
			() => PluralRules.Names, tree.TranslationForms.Label, () => tree.TranslationForms.Form, tree.PickTranslationForm,
			Forms(tree.TranslationForms, () => tree.TranslationFormsEnabled, tree.EntryMisses));
		//Tools
		var languages = new CommandItem(Words.Known["menu.languages"], PackIconKind.Translate, vm.ManageLanguagesCommand);
		var settings = new CommandItem(Words.Known["menu.settings"], PackIconKind.Cog, vm.SettingsCommand);
		var machine = new CommandItem(Words.Known["menu.machine-translation"], PackIconKind.RobotOutline, vm.MachineTranslationCommand);
		var customize = new CommandItem(Words.Known["menu.toolbars"], PackIconKind.ToolboxOutline, vm.CustomizeToolbarsCommand);
		//Help: the bubbles' switch, and their memory forgotten
		var tips = new ToggleItem(Words.Known["menu.tips"], PackIconKind.CommentQuestionOutline, () => vm.Help.Tips, new DelegateCommand(() => vm.Help.Tips = !vm.Help.Tips));
		var tipsAgain = new CommandItem(Words.Known["menu.tips-again"], PackIconKind.CommentArrowLeftOutline, new DelegateCommand(vm.Help.ShowAgain));

		MenuGroup edit = new(Words.Known["menu.edit"], [undo, redo, new MenuBreak(), addNode, addKey, addComment, rename, copyKey, remove, new MenuBreak(), toggleReview, toggleConstant, toggleStale, staleAll, removeKey]);
		Menu = [
			new MenuGroup(Words.Known["menu.file"], [create, load, import, merge, new MenuBreak(), save, saveAs, export, new MenuBreak(), reset, exit], HelpTopics.Start.Id),
			edit,
			new MenuGroup(Words.Known["menu.view"], [staleView, reviewView, missingView, mismatchView, clearFilters, new MenuBreak(), defaultPreview, translationPreview, new MenuBreak(), back, forward, find, new MenuBreak(), defaultForm, translationForm, new MenuBreak(), translationLanguage, uiLanguage]),
			new MenuGroup(Words.Known["menu.tools"], [languages, machine, settings, customize]),
			new MenuGroup(Words.Known["menu.help"], [tips, tipsAgain]),
		];
		EditRows = edit.Items;
		FilterTools = [staleView, reviewView, missingView, mismatchView, clearFilters];
		//the entries the window draws, the table naming their place: the box and the name pinned to theirs
		var searchBox = new SearchBoxEntry(tree);
		var filters = new FiltersEntry(tree, FilterTools);
		var nodeName = new NodeNameEntry(tree);
		var named = new Dictionary<string, MenuRow>(StringComparer.OrdinalIgnoreCase) {
			["new"] = create, ["load"] = load, ["import"] = import, ["merge"] = merge, ["save"] = save, ["save-as"] = saveAs,
			["export"] = export, ["reset"] = reset, ["exit"] = exit,
			["undo"] = undo, ["redo"] = redo, ["add-node"] = addNode, ["add-key"] = addKey, ["add-comment"] = addComment,
			["rename"] = rename, ["copy-key"] = copyKey, ["remove"] = remove, ["toggle-review"] = toggleReview, ["toggle-constant"] = toggleConstant,
			["toggle-stale"] = toggleStale, ["stale-all"] = staleAll, ["remove-key"] = removeKey,
			["stale-view"] = staleView, ["review-view"] = reviewView, ["missing-view"] = missingView, ["mismatch-view"] = mismatchView,
			["clear-filters"] = clearFilters, ["default-preview"] = defaultPreview, ["translation-preview"] = translationPreview,
			["back"] = back, ["forward"] = forward, ["find"] = find,
			["default-form"] = defaultForm, ["translation-form"] = translationForm,
			["translation-language"] = translationLanguage, ["ui-language"] = uiLanguage,
			["languages"] = languages, ["machine-translation"] = machine, ["settings"] = settings, ["toolbars"] = customize, ["tips"] = tips, ["tips-again"] = tipsAgain,
			["search-box"] = searchBox, ["filters"] = filters, ["node-name"] = nodeName,
		};
		foreach (var (name, row) in named) {
			row.Name = name;
		}
		Named = named;
		Links = named.Where(pair => pair.Value is CommandItem).ToDictionary(pair => pair.Key, pair => (CommandItem)pair.Value);
		//the rows about one value pane, which read wrong over the other's (SPEC: The toolbar editor, Odd places)
		defaultForm.About = defaultPreview.About = Side.Default;
		translationForm.About = translationPreview.About = toggleStale.About = Side.Translation;
		SearchTools = new Toolbar("search", Words.Known["toolbars.search"], Words.Known["toolbars.search-place"], Side.Tree,
			[back, forward, searchBox, filters], searchBox);
		NameTools = new Toolbar("name", Words.Known["toolbars.name"], Words.Known["toolbars.name-place"], Side.Default,
			[nodeName, rename, copyKey], nodeName);
		DefaultTools = new Toolbar("default", Words.Known["toolbars.default"], Words.Known["toolbars.default-place"], Side.Default,
			[defaultForm, toggleConstant, toggleReview, defaultPreview]);
		LanguageTools = new Toolbar("languages", Words.Known["toolbars.languages"], Words.Known["toolbars.languages-place"], Side.Translation,
			[languages, translationLanguage], translationLanguage);
		TranslationTools = new Toolbar("translation", Words.Known["toolbars.translation"], Words.Known["toolbars.translation-place"], Side.Translation,
			[translationForm, toggleStale, translationPreview]);
		NodeTools = new Toolbar("node", Words.Known["toolbars.node"], Words.Known["toolbars.node-place"], Side.Tree,
			[addNode, remove, addComment]);
		KeyTools = new Toolbar("key", Words.Known["toolbars.key"], Words.Known["toolbars.key-place"], Side.Default,
			[addKey, removeKey, staleAll]);
		Toolbars = [SearchTools, NameTools, DefaultTools, LanguageTools, TranslationTools, NodeTools, KeyTools];
		HashSet<MenuRow> pinned = [.. Toolbars.SelectMany(toolbar => toolbar.Pinned)];
		List<MenuRow> placeable = [.. Menu.SelectMany(group => group.Items).Where(row => row is not MenuBreak && !MenuOnly.Contains(row.Name) && !pinned.Contains(row))];
		placeable.Insert(placeable.IndexOf(staleView), filters);
		Placeable = placeable;
		memory = toolbars;
		Arrange();
	}

	/// <summary>Keeps every toolbar as it stands: a line for each one changed from how it ships, none for the rest.</summary>
	public void Keep() => Keep([.. Toolbars.Select(toolbar => (toolbar, memory.Line(toolbar.Name)))]);

	//the lines of the toolbars changed from the ones kept, written at once; a toolbar as it
	//ships keeps none
	private void Keep(IEnumerable<(Toolbar Toolbar, string? Kept)> toolbars) {
		List<KeyValuePair<string, string?>> changed = [];
		foreach ((Toolbar toolbar, string? kept) in toolbars) {
			string? line = toolbar.Line == toolbar.ShippedLine ? null : toolbar.Line;
			if (line != kept) {
				changed.Add(new(toolbar.Name, line));
			}
		}
		if (changed.Count > 0) {
			memory.Keep(changed);
		}
	}

	//each toolbar from its line, in order, a row's first place winning (SPEC: The toolbar
	//editor, Saved). What a line names that the editor does not know, or may not put there,
	//is ignored; a pinned entry left out goes back at its place as shipped. A line that
	//came out otherwise is written back so, and one that came out as shipped goes, all
	//in one write
	private void Arrange() {
		HashSet<MenuRow> placed = [];
		HashSet<MenuRow> pinned = [.. Toolbars.SelectMany(toolbar => toolbar.Pinned)];
		List<(Toolbar, string?)> read = [];
		foreach (Toolbar toolbar in Toolbars) {
			string? line = memory.Line(toolbar.Name);
			read.Add((toolbar, line));
			List<MenuRow> entries = [];
			foreach (string name in line?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? toolbar.Shipped.Select(Toolbar.Spell)) {
				if (name == Toolbar.Break) {
					entries.Add(new MenuBreak());
				}
				else if (Named.TryGetValue(name, out MenuRow? row) && !MenuOnly.Contains(row.Name) && !placed.Contains(row)
						&& !entries.Contains(row) && (!pinned.Contains(row) || toolbar.Pinned.Contains(row))) {
					entries.Add(row);
				}
			}
			foreach (MenuRow pin in toolbar.Pinned.Where(pin => !entries.Contains(pin))) {
				entries.Insert(Math.Min(toolbar.Shipped.ToList().IndexOf(pin), entries.Count), pin);
			}
			placed.UnionWith(entries.Where(entry => entry is not MenuBreak));
			toolbar.Entries.Clear();
			foreach (MenuRow entry in entries) {
				toolbar.Entries.Add(entry);
			}
		}
		Keep(read);
	}

	/// <summary>A state, a selection or a language changed somewhere other than its row: every toggle and choice re-reads. Always true, to chain.</summary>
	public bool Refresh() {
		foreach (ToggleItem toggle in Rows.OfType<ToggleItem>()) {
			toggle.Refresh();
		}
		foreach (ChoiceItem choice in Choices) {
			choice.Refresh();
		}
		return true;
	}

	private static KeyGesture Ctrl(Key key) => new(key, ModifierKeys.Control);

	//a form selector's rows: a dot with words, bold where the badge wants them, greyed where the
	//language does not count by them
	private static ChoiceLook Forms(FormPane pane, Func<bool> enabled, Func<string, bool> missing) => new(
		CanPick: category => pane.CanPick((string)category),
		Filled: category => pane.HasWords((string)category),
		Missing: category => missing((string)category),
		Greyed: category => pane.Row((string)category) == FormRow.Unused,
		Enabled: enabled,
		Badge: () => pane.Badge,
		Popup: true);
}
