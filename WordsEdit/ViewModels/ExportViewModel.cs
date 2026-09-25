using System.IO;
using System.Windows.Input;
using PatTech.Utils;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>One exporter as the dropdown lists it: its name in its own words, and the file-dialog filter for its extensions.</summary>
public sealed class ExportFormatRow(IWordsExporter format) {
	public IWordsExporter Format => format;
	public string Name => Words.Known[format.Info.NameKey];
	public string Patterns => string.Join(";", format.Info.Extensions.Select(extension => "*" + extension));
	public string Label => $"{Name} ({Patterns})";
	[Localized]
	public string Filter => MainWindowViewModel.FormatFilter(format);
}

/// <summary>One file the export will write, as the plan lists it.</summary>
public sealed class ExportUnitRow(ExportUnit unit) {
	public ExportUnit Unit => unit;
	public string Path => unit.Path;
	/// <summary>Already on disk: writing it is an overwrite, said before it happens.</summary>
	public bool Exists => File.Exists(unit.Path);
}

/// <summary>
///     Export (SPEC: Import and export): one loaded file, a format and a target,
///     and before a byte lands, the plan — the files the format will write, the
///     ones it overwrites — and the loss, what this file uses that the format has
///     no slot for. Export writes each planned file atomically and shows what
///     the format dropped or guessed. Save is untouched: the words.ini stays the
///     document.
/// </summary>
public class ExportViewModel : DialogViewModel {
	public override string Title => Words.Known["export.title"];
	public MainWindowViewModel Parent { get; }

	/// <summary>Every loaded file, in tree order.</summary>
	public IReadOnlyList<KeyNode> Files { get; }
	public KeyNode? FileNode { get; set => _ = ChangeProperty(ref field, value) && Replan(); }
	/// <summary>Every format that writes.</summary>
	public IReadOnlyList<ExportFormatRow> Formats { get; }
	public ExportFormatRow? Format { get; set => _ = ChangeProperty(ref field, value) && Replan(); }
	/// <summary>Where the format writes: the one file, or the neutral one of a set.</summary>
	public string Target { get; set => _ = ChangeProperty(ref field, value) && Replan(); } = "";

	/// <summary>The files the export will write, for the file, format and target as they stand.</summary>
	public IReadOnlyList<ExportUnitRow> Plan { get; private set => ChangeProperty(ref field, value); } = [];
	/// <summary>What the file uses that the format drops, in words; empty until there is a plan.</summary>
	public string Loss { get; private set => ChangeProperty(ref field, value); } = "";
	public bool CanExport => Plan.Count > 0;

	public ICommand BrowseCommand { get; }
	public ICommand ExportCommand { get; }
	public ICommand CancelCommand { get; }

	public ExportViewModel(MainWindowViewModel parent) {
		ArgumentNullException.ThrowIfNull(parent);
		Parent = parent;
		BrowseCommand = new DelegateCommand(DoBrowse, () => Format is not null);
		ExportCommand = new DelegateCommand(DoExport, () => CanExport);
		CancelCommand = new DelegateCommand(Close);
		Files = [.. parent.Tree.KeyNodes];
		Formats = [.. parent.Formats.Exporters.Select(format => new ExportFormatRow(format))];
		//the selection's file, and the first format: what the user most likely means
		FileNode = parent.Tree.SelectedKeyNode?.Root ?? Files.FirstOrDefault();
		Format = Formats.FirstOrDefault();
	}

	private ExportSource Source(KeyNode node)
		=> new(Parent.Session, Parent.Tree.FileOf(node), node);

	private bool Replan() {
		if (FileNode is null || Format is null || Target.Trim() == "") {
			Plan = [];
			Loss = "";
		}
		else {
			ExportSource source = Source(FileNode);
			Plan = [.. Format.Format.Plan(source, Target).Select(unit => new ExportUnitRow(unit))];
			Loss = Describe(Format.Format.Loses(source));
		}
		AffectProperty(nameof(CanExport));
		return true;
	}

	/// <summary>
	///     The loss in words: each feature the format has no slot for, named, or
	///     that nothing is lost. A flag names its words by key (<c>[Words]</c> on
	///     <see cref="WordsFeatures"/>): the seam's fragment carries the name, and
	///     the editor's file, stacked over it, adds the <c>.sub</c> phrasing the
	///     loss line lists.
	/// </summary>
	public static string Describe(WordsFeatures loss) {
		if (loss == WordsFeatures.None) {
			return Words.Known["export.loses-nothing"];
		}
		IEnumerable<string> names = Enum.GetValues<WordsFeatures>()
			.Where(flag => flag is not (WordsFeatures.None or WordsFeatures.All) && (loss & flag) != 0)
			.Select(flag => flag.Describe("S"));
		return Words.Known.Format("export.loses", string.Join(", ", names));
	}

	private void DoBrowse() {
		if (Format is not null && Parent.Dialogs.TrySaveFile(Words.Known["file.export-title"], Format.Filter, out string? target)) {
			Target = target;
		}
	}

	private void DoExport() {
		if (!CanExport || FileNode is null || Format is null) {
			return;
		}
		int overwrites = Plan.Count(row => row.Exists);
		if (overwrites > 0 && !Parent.Dialogs.Confirm(Words.Known.Format("ask.export-overwrite", overwrites))) {
			return;
		}
		ExportSource source = Source(FileNode);
		List<string> gripes = [];
		foreach (ExportUnitRow row in Plan) {
			try {
				//each file atomic: a failure partway leaves what was there
				IniWriter.WriteAtomic(row.Path, writer => Format.Format.Write(source, row.Unit, writer, gripes));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Parent.Dialogs.Tell(Words.Known.Format("file.write-failed", row.Path, ex.Message));
				Replan(); //the files written so far now exist
				return;
			}
		}
		if (gripes.Count > 0) {
			Parent.Dialogs.Show(new GripesViewModel(Words.Known.Format("gripes.export", Format.Name), gripes));
		}
		Close();
	}
}
