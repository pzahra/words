using System.Collections.ObjectModel;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     One toolbar (SPEC: The toolbar editor): its name, the entries it ships with, and
///     its entries as they stand, which the window binds. Its pinned entries belong to
///     it alone: they move along it, never off it, and never go.
/// </summary>
public sealed class Toolbar : ViewModelBase {
	/// <summary>A break's name in a line.</summary>
	public const string Break = "-";

	public string Name { get; }
	public IReadOnlyList<MenuRow> Shipped { get; }
	public IReadOnlyList<MenuRow> Pinned { get; }
	public ObservableCollection<MenuRow> Entries { get; }
	/// <summary>Nothing on it: it takes no room.</summary>
	public bool IsEmpty => Entries.Count == 0;
	/// <summary>Its entries by name, a break as <c>-</c>: its line in the config.</summary>
	public string Line => Join(Entries);
	/// <summary>Its line as it ships.</summary>
	public string ShippedLine => Join(Shipped);

	public Toolbar(string name, IReadOnlyList<MenuRow> shipped, params MenuRow[] pinned) {
		Name = name;
		Shipped = shipped;
		Pinned = pinned;
		Entries = [.. shipped];
		Entries.CollectionChanged += (_, _) => AffectProperty(nameof(IsEmpty));
	}

	/// <summary>An entry as a line names it: a row by its name, a break as <c>-</c>.</summary>
	public static string Spell(MenuRow entry) => entry is MenuBreak ? Break : entry.Name;

	private static string Join(IEnumerable<MenuRow> entries) => string.Join(",", entries.Select(Spell));
}

/// <summary>The search box over the tree, pinned to its toolbar, taking the room the toolbar's rows leave.</summary>
public sealed class SearchBoxEntry(TreeViewModel tree) : MenuRow {
	public TreeViewModel Tree { get; } = tree;
	public override bool Fills => true;
}

/// <summary>The filter button, pinned beside the search box: the View menu's filters in a popup, and the count of the rows they hide.</summary>
public sealed class FiltersEntry(TreeViewModel tree, IReadOnlyList<CommandItem> filters) : MenuRow {
	public TreeViewModel Tree { get; } = tree;
	/// <summary>The popup's rows: fixed, whatever the toolbars say, since they are what the button means.</summary>
	public IReadOnlyList<CommandItem> Filters { get; } = filters;
}

/// <summary>The selected node's name, pinned to the toolbar over the default pane, taking the room its rows leave.</summary>
public sealed class NodeNameEntry(TreeViewModel tree) : MenuRow {
	public TreeViewModel Tree { get; } = tree;
	public override bool Fills => true;
}

/// <summary>What the toolbars remember between runs: a line for each toolbar changed from how it ships.</summary>
public interface IToolbarMemory {
	/// <summary>The toolbar's line, its entries by name; null when it is as it ships.</summary>
	string? Line(string toolbar);
	/// <summary>Keeps the toolbar's line; null forgets it, so the toolbar ships.</summary>
	void Keep(string toolbar, string? line);
}

/// <summary>The toolbars' memory for one run alone: what a test, or an editor built without the config, keeps.</summary>
public sealed class ToolbarMemory : IToolbarMemory {
	private readonly Dictionary<string, string> lines = new(StringComparer.OrdinalIgnoreCase);

	public string? Line(string toolbar) => lines.GetValueOrDefault(toolbar);

	public void Keep(string toolbar, string? line) {
		if (line is null) {
			lines.Remove(toolbar);
		}
		else {
			lines[toolbar] = line;
		}
	}
}

/// <summary>The toolbars' memory in the editor's config: a line each under <c>[toolbars]</c>.</summary>
public sealed class ConfigToolbarMemory : IToolbarMemory {
	public string? Line(string toolbar) => EditorConfig.Toolbar(toolbar);
	public void Keep(string toolbar, string? line) => EditorConfig.KeepToolbar(toolbar, line);
}
