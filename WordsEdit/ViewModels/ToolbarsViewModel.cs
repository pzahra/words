using System.Collections.ObjectModel;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     One entry as the toolbar editor's board shows it (SPEC: The toolbar editor, The dialog):
///     its icon, or its caption where it has none, its caption as its tooltip; a pin when it
///     moves along its own row alone; and, where its place reads wrong, the pane it is about.
/// </summary>
public sealed class ToolbarChip : ViewModelBase {
	public MenuRow Entry { get; }
	[Localized]
	public string Caption { get; }
	public PackIconKind Icon { get; }
	public bool HasIcon { get; }
	public bool IsPinned { get; }
	public bool IsBreak => Entry is MenuBreak;
	/// <summary>A chip without an icon shows its caption; a break shows a line.</summary>
	public bool ShowsCaption => !HasIcon && !IsBreak;
	/// <summary>The pane it is about, where its place is odd; null where it is at home.</summary>
	public string? Odd {
		get;
		internal set {
			if (ChangeProperty(ref field, value)) {
				AffectProperty(nameof(IsOdd));
			}
		}
	}
	public bool IsOdd => Odd is not null;

	public ToolbarChip(MenuRow entry, bool pinned) {
		Entry = entry;
		IsPinned = pinned;
		Caption = entry switch {
			CommandItem row => row.Caption,
			ChoiceItem row => row.Caption,
			WindowEntry row => row.Caption,
			_ => Words.Known["toolbars.break"],
		};
		PackIconKind? icon = entry switch {
			CommandItem row => row.Icon,
			ChoiceItem row => row.Icon,
			WindowEntry row => row.Icon,
			_ => null,
		};
		Icon = icon ?? default;
		HasIcon = icon is not null;
	}
}

/// <summary>A toolbar's row on the board: a chip for each of its entries, in its order.</summary>
public sealed class ToolbarRow(Toolbar toolbar, bool marked, ToolbarDrag drag) {
	public Toolbar Toolbar { get; } = toolbar;
	/// <summary>The toolbar whose context menu opened the dialog.</summary>
	public bool IsMarked { get; } = marked;
	public ToolbarDrag Drag { get; } = drag;
	public ObservableCollection<ToolbarChip> Chips { get; } = [.. Of(toolbar)];

	/// <summary>Puts <paramref name="entries"/> on the toolbar, and the chips with them, unless they are there already.</summary>
	public void Put(IReadOnlyList<MenuRow> entries) {
		if (Toolbar.Entries.SequenceEqual(entries)) {
			return;
		}
		Toolbar.Entries.Clear();
		foreach (MenuRow entry in entries) {
			Toolbar.Entries.Add(entry);
		}
		Chips.Clear();
		foreach (ToolbarChip chip in Of(Toolbar)) {
			Chips.Add(chip);
		}
	}

	private static IEnumerable<ToolbarChip> Of(Toolbar toolbar) => toolbar.Entries.Select(entry => new ToolbarChip(entry, toolbar.Pinned.Contains(entry)));
}

/// <summary>
///     The toolbar editor (SPEC: The toolbar editor): a board of the toolbars, a row of chips
///     each, and a bucket of the menu's rows on none. A drop moves an entry, and the toolbar
///     behind shows it at once; OK keeps the toolbars, Cancel or the window's own close puts
///     back what was, and Reset puts every toolbar back as it ships.
/// </summary>
public sealed class ToolbarsViewModel : DialogViewModel {
	public override string Title => Words.Known["toolbars.title"];
	public IReadOnlyList<ToolbarRow> Rows { get; }
	/// <summary>A break to drag out, then every entry on no toolbar, in menu order (<see cref="CommandTable.Placeable"/>); never the menu's own.</summary>
	public ObservableCollection<ToolbarChip> Bucket { get; } = [];
	/// <summary>A line for each entry in an odd place.</summary>
	public IReadOnlyList<string> OddPlaces { get; private set => ChangeProperty(ref field, value); } = [];
	public ToolbarDrag Drag { get; }

	public ICommand OkayCommand { get; }
	public ICommand CancelCommand { get; }
	public ICommand ResetCommand { get; }

	private readonly MainWindowViewModel parent;
	private readonly IReadOnlyList<IReadOnlyList<MenuRow>> before;
	private bool kept;

	/// <param name="marked">The toolbar whose context menu asked, marked on the board; none from the menu.</param>
	public ToolbarsViewModel(MainWindowViewModel parent, Toolbar? marked = null) {
		this.parent = parent;
		Drag = new ToolbarDrag { Vm = this };
		before = [.. parent.Commands.Toolbars.Select(toolbar => toolbar.Entries.ToList())];
		Rows = [.. parent.Commands.Toolbars.Select(toolbar => new ToolbarRow(toolbar, toolbar == marked, Drag))];
		OkayCommand = new DelegateCommand(DoOkay);
		CancelCommand = new DelegateCommand(Close);
		ResetCommand = new DelegateCommand(DoReset);
		Settle();
		parent.Help.Arise(HelpTopics.Toolbars); //its bubble opens on the bucket, once the dialog shows it
	}

	/// <summary>The row whose chips <paramref name="chips"/> are, or null for the bucket's; false for neither.</summary>
	public bool TryPlace(object? chips, out ToolbarRow? row) {
		row = Rows.FirstOrDefault(candidate => ReferenceEquals(candidate.Chips, chips));
		return row is not null || ReferenceEquals(chips, Bucket);
	}

	/// <summary>Whether <paramref name="chip"/> may go to <paramref name="to"/>'s row, or the bucket (null): a pinned one stays on its own.</summary>
	public bool Accepts(ToolbarChip chip, ToolbarRow? to) => !chip.IsPinned || to is not null && to.Chips.Contains(chip);

	/// <summary>
	///     Moves <paramref name="chip"/> to <paramref name="index"/> on <paramref name="to"/>'s row,
	///     counted as the row stands before the chip leaves it, or into the bucket (null), its
	///     toolbar going with it. A break out of the bucket is a new one, the bucket keeping its
	///     own; a break into it is gone.
	/// </summary>
	public void Move(ToolbarChip chip, ToolbarRow? to, int index) {
		if (!Accepts(chip, to)) {
			return;
		}
		ToolbarRow? from = Rows.FirstOrDefault(row => row.Chips.Contains(chip));
		if (from is not null && from == to) {
			int at = from.Chips.IndexOf(chip);
			index = Math.Clamp(index > at ? index - 1 : index, 0, from.Chips.Count - 1);
			if (index != at) {
				from.Chips.Move(at, index);
				from.Toolbar.Entries.Move(at, index);
			}
		}
		else {
			if (from is not null) {
				int at = from.Chips.IndexOf(chip);
				from.Chips.RemoveAt(at);
				from.Toolbar.Entries.RemoveAt(at);
			}
			if (to is not null) {
				ToolbarChip placed = chip.IsBreak && from is null ? new ToolbarChip(new MenuBreak(), pinned: false) : chip;
				index = Math.Clamp(index, 0, to.Chips.Count);
				to.Chips.Insert(index, placed);
				to.Toolbar.Entries.Insert(index, placed.Entry);
			}
		}
		Settle();
	}

	//the bucket refilled from what the rows leave, and each chip's place judged
	private void Settle() {
		HashSet<MenuRow> placed = [.. Rows.SelectMany(row => row.Toolbar.Entries)];
		Bucket.Clear();
		Bucket.Add(new ToolbarChip(new MenuBreak(), pinned: false));
		foreach (MenuRow entry in parent.Commands.Placeable.Where(entry => !placed.Contains(entry))) {
			Bucket.Add(new ToolbarChip(entry, pinned: false));
		}
		List<string> odd = [];
		foreach (ToolbarRow row in Rows) {
			foreach (ToolbarChip chip in row.Chips) {
				(chip.Odd, string? line) = (chip.Entry.About, row.Toolbar.Side) switch {
					(Side.Default, Side.Translation) => (Words.Known["toolbars.about-default"], Words.Known.Format("toolbars.over-translation", chip.Caption)),
					(Side.Translation, Side.Default) => (Words.Known["toolbars.about-translation"], Words.Known.Format("toolbars.over-default", chip.Caption)),
					_ => ((string?)null, (string?)null),
				};
				if (line is not null) {
					odd.Add(line);
				}
			}
		}
		OddPlaces = odd;
	}

	//every toolbar as it ships, at once in the window behind
	private void DoReset() {
		foreach (ToolbarRow row in Rows) {
			row.Put(row.Toolbar.Shipped);
		}
		Settle();
	}

	private void DoOkay() {
		parent.Commands.Keep();
		kept = true;
		Close();
	}

	//closed without OK, by Cancel, Escape or the window's X: the toolbars go back as they were
	public override void Closed() {
		if (!kept) {
			foreach ((ToolbarRow row, IReadOnlyList<MenuRow> entries) in Rows.Zip(before)) {
				row.Put(entries);
			}
		}
		parent.Help.Pass(HelpTopics.Toolbars);
	}
}
