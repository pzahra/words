using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>A row of the command table: a command, a toggle, a choice, or the break between two runs.</summary>
public abstract class MenuRow : ViewModelBase;

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

/// <summary>
///     A pick among options — the translation language, Wordsmith's own — as a
///     submenu of ticked rows in a menu and a combo box on a toolbar. The
///     options and the pick live on the owner: the row mirrors the options as
///     <see cref="Choice"/> rows, re-reads them (<see cref="Refresh"/>) when the
///     owner changed, and sends a pick back through the owner.
/// </summary>
public sealed class ChoiceItem : MenuRow {
	private readonly Func<IEnumerable<object>> options;
	private readonly Func<object, string> label;
	private readonly Func<object?> current;
	private readonly Action<object> pick;

	[Localized]
	public string Caption { get; }
	public PackIconKind Icon { get; }
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
			if (!Equals(value.Value, current())) {
				pick(value.Value);
			}
			Refresh(); //a pick the owner declined (Wordsmith's language asks first) stays put
		}
	}

	private ChoiceItem([Localized] string caption, PackIconKind icon, Func<IEnumerable<object>> options, Func<object, string> label, Func<object?> current, Action<object> pick) {
		Caption = caption;
		Icon = icon;
		this.options = options;
		this.label = label;
		this.current = current;
		this.pick = pick;
		Refresh();
	}

	/// <summary>A choice among <typeparamref name="T"/>s, read and written as the owner holds them.</summary>
	public static ChoiceItem Of<T>([Localized] string caption, PackIconKind icon, Func<IEnumerable<T>> options, Func<T, string> label, Func<T?> current, Action<T> pick) where T : notnull
		=> new(caption, icon, () => options().Cast<object>(), value => label((T)value), () => current(), value => pick((T)value));

	internal string LabelOf(object value) => label(value);
	internal bool IsCurrent(object value) => Equals(value, current());

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

	public void Refresh() {
		AffectProperty(nameof(IsChecked));
		AffectProperty(nameof(Label));
	}
}

/// <summary>A top menu: its caption, the access key marked with an underscore, and its rows.</summary>
public sealed class MenuGroup([Localized] string caption, IReadOnlyList<MenuRow> items) {
	[Localized]
	public string Caption { get; } = caption;
	public IReadOnlyList<MenuRow> Items { get; } = items;
}

/// <summary>
///     The command table (SPEC: Menu and toolbars). The menu is the inventory —
///     every command the editor has, grouped the usual way; the toolbars carry
///     only what is convenient and the tree's context menu is the Edit menu
///     again, all drawn from the same rows. The words are looked up here, by
///     literal key, so the editor's file names every one of them.
/// </summary>
public sealed class CommandTable {
	public IReadOnlyList<MenuGroup> Menu { get; }
	/// <summary>The Edit menu's rows: the tree's context menu.</summary>
	public IReadOnlyList<MenuRow> EditRows { get; }
	/// <summary>The node operations, under the tree.</summary>
	public IReadOnlyList<CommandItem> NodeTools { get; }
	/// <summary>The key operations, under the baseline pane.</summary>
	public IReadOnlyList<CommandItem> KeyTools { get; }
	/// <summary>The filters: a vertical toolbar in the popup beside the search box.</summary>
	public IReadOnlyList<CommandItem> FilterTools { get; }
	/// <summary>Beside the selected node's name: Rename.</summary>
	public IReadOnlyList<CommandItem> NameTools { get; }
	/// <summary>The baseline pane's header: the test, the key's flags, its preview.</summary>
	public IReadOnlyList<CommandItem> DefaultTools { get; }
	/// <summary>The translation pane's header: the test, the stale flag, its preview.</summary>
	public IReadOnlyList<CommandItem> TranslationTools { get; }
	/// <summary>Above the translation pane: the languages, and the one selected as a combo box.</summary>
	public IReadOnlyList<MenuRow> LanguageTools { get; }
	/// <summary>By the search: Back and Forward, which move through the document as it does.</summary>
	public IReadOnlyList<CommandItem> NavigationTools { get; }

	/// <summary>Every command row, in menu order.</summary>
	public IEnumerable<CommandItem> Rows => Menu.SelectMany(group => group.Items).OfType<CommandItem>();
	/// <summary>Every choice, in menu order.</summary>
	public IEnumerable<ChoiceItem> Choices => Menu.SelectMany(group => group.Items).OfType<ChoiceItem>();
	/// <summary>The rows whose key the window binds; a routed command (Find) carries its own.</summary>
	public IEnumerable<CommandItem> Shortcuts => Rows.Where(row => row.Gesture is not null && row.Command is not RoutedCommand);
	/// <summary>The rows a mouse button runs too, which the window binds with its keys.</summary>
	public IEnumerable<CommandItem> MouseShortcuts => Rows.Where(row => row.Button is not null);

	public CommandTable(MainWindowViewModel vm) {
		TreeViewModel tree = vm.Tree;
		//File: in, out, and away
		var load = new CommandItem(Words.Known["menu.load"], PackIconKind.FolderUpload, vm.LoadFileCommand, Ctrl(Key.O));
		var import = new CommandItem(Words.Known["menu.import"], PackIconKind.FileImport, vm.ImportCommand, Ctrl(Key.I));
		var merge = new CommandItem(Words.Known["menu.merge"], PackIconKind.Merge, vm.MergeFilesCommand);
		var save = new CommandItem(Words.Known["menu.save"], PackIconKind.ContentSave, vm.SaveCommand, Ctrl(Key.S));
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
		var remove = new CommandItem(Words.Known["menu.remove"], PackIconKind.Delete, vm.RemoveNodeCommand, new KeyGesture(Key.Delete));
		var toggleReview = new ToggleItem(Words.Known["menu.toggle-review"], PackIconKind.HandFrontLeft, () => tree.SelectedKeyNode?.NeedsReview ?? false, vm.ToggleNeedsReviewCommand);
		var toggleConstant = new ToggleItem(Words.Known["menu.toggle-constant"], PackIconKind.TranslateOff, () => tree.SelectedKeyNode?.IsConstant ?? false, vm.ToggleConstantCommand);
		var toggleStale = new ToggleItem(Words.Known["menu.toggle-stale"], PackIconKind.ClockAlertOutline, () => tree.SelectedKeyNode?.IsStale ?? false, vm.ToggleStaleLanguageCommand);
		var staleAll = new CommandItem(Words.Known["menu.stale-all"], PackIconKind.ClockAlert, vm.StaleAllLanguagesCommand, new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
		var removeKey = new CommandItem(Words.Known["menu.remove-key"], PackIconKind.KeyRemove, vm.RemoveKeyCommand);
		//View: the filters and the previews flip a property of their own; Back and Forward step the tree's history,
		//on the mouse's buttons too; Find carries Ctrl+F of its own; the languages are choices
		var staleView = new ToggleItem(Words.Known["menu.stale-view"], PackIconKind.ClockAlert, () => tree.IsStaleFilter, new DelegateCommand(() => tree.IsStaleFilter = !tree.IsStaleFilter));
		var reviewView = new ToggleItem(Words.Known["menu.review-view"], PackIconKind.HandFrontLeft, () => tree.NeedsReviewFilter, new DelegateCommand(() => tree.NeedsReviewFilter = !tree.NeedsReviewFilter));
		var missingView = new ToggleItem(Words.Known["menu.missing-view"], PackIconKind.TextBoxRemoveOutline, () => tree.MissingFilter, new DelegateCommand(() => tree.MissingFilter = !tree.MissingFilter));
		var clearFilters = new CommandItem(Words.Known["menu.clear-filters"], PackIconKind.FilterRemoveOutline, vm.ClearFiltersCommand);
		var defaultPreview = new ToggleItem(Words.Known["menu.default-preview"], PackIconKind.Eye, () => vm.ShowDefaultPreview, new DelegateCommand(() => vm.ShowDefaultPreview = !vm.ShowDefaultPreview));
		var translationPreview = new ToggleItem(Words.Known["menu.translation-preview"], PackIconKind.EyeOutline, () => vm.ShowLocalizationPreview, new DelegateCommand(() => vm.ShowLocalizationPreview = !vm.ShowLocalizationPreview));
		var back = new CommandItem(Words.Known["menu.back"], PackIconKind.ArrowLeft, tree.BackCommand, new KeyGesture(Key.Left, ModifierKeys.Alt), MouseButton.XButton1);
		var forward = new CommandItem(Words.Known["menu.forward"], PackIconKind.ArrowRight, tree.ForwardCommand, new KeyGesture(Key.Right, ModifierKeys.Alt), MouseButton.XButton2);
		var find = new CommandItem(Words.Known["menu.find"], PackIconKind.Magnify, ApplicationCommands.Find, Ctrl(Key.F));
		ChoiceItem translationLanguage = ChoiceItem.Of(Words.Known["menu.translation-language"], PackIconKind.Earth,
			() => tree.FileLanguages, language => language.NativeName, () => tree.SelectedLanguage, language => tree.SelectedLanguage = language);
		ChoiceItem uiLanguage = ChoiceItem.Of(Words.Known["menu.ui-language"], PackIconKind.Web,
			() => vm.UiLanguages, pair => pair.Value, () => vm.UiLanguages.FirstOrDefault(pair => pair.Key == vm.UiLanguage), pair => vm.UiLanguage = pair.Key);
		//Tools
		var languages = new CommandItem(Words.Known["menu.languages"], PackIconKind.Translate, vm.ManageLanguagesCommand);
		var settings = new CommandItem(Words.Known["menu.settings"], PackIconKind.Cog, vm.SettingsCommand);
		var parameters = new CommandItem(Words.Known["menu.parameters"], PackIconKind.CodeBraces, vm.TestParametersCommand);

		MenuGroup edit = new(Words.Known["menu.edit"], [undo, redo, new MenuBreak(), addNode, addKey, addComment, rename, remove, new MenuBreak(), toggleReview, toggleConstant, toggleStale, staleAll, removeKey]);
		Menu = [
			new MenuGroup(Words.Known["menu.file"], [load, import, merge, new MenuBreak(), save, export, new MenuBreak(), reset, exit]),
			edit,
			new MenuGroup(Words.Known["menu.view"], [staleView, reviewView, missingView, clearFilters, new MenuBreak(), defaultPreview, translationPreview, new MenuBreak(), back, forward, find, new MenuBreak(), translationLanguage, uiLanguage]),
			new MenuGroup(Words.Known["menu.tools"], [languages, settings, parameters]),
		];
		EditRows = edit.Items;
		NodeTools = [addNode, remove, addComment];
		KeyTools = [addKey, removeKey, staleAll];
		FilterTools = [staleView, reviewView, missingView, clearFilters];
		NameTools = [rename];
		DefaultTools = [parameters, toggleConstant, toggleReview, defaultPreview];
		TranslationTools = [parameters, toggleStale, translationPreview];
		LanguageTools = [languages, translationLanguage];
		NavigationTools = [back, forward];
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
}
