using System.IO;
using System.Text;
using System.Windows.Input;
using PatTech.Localization.Authoring.Codecs;
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
///     document. A sheet shows its columns (<see cref="Sheet"/>), and the loss
///     line follows them.
/// </summary>
public class ExportViewModel : DialogViewModel {
	public override string Title => Words.Known["export.title"];
	public MainWindowViewModel Parent { get; }

	/// <summary>Every loaded file, in tree order.</summary>
	public IReadOnlyList<KeyNode> Files { get; }
	public KeyNode? FileNode { get; set => _ = ChangeProperty(ref field, value) && Replan(); }
	/// <summary>Every format that writes.</summary>
	public IReadOnlyList<ExportFormatRow> Formats { get; }
	/// <summary>The format picked. A target another format claims takes this one's extension.</summary>
	public ExportFormatRow? Format {
		get;
		set {
			if (ChangeProperty(ref field, value) && !RenameTarget()) {
				Replan();
			}
		}
	}
	/// <summary>Where the format writes: the one file, or the neutral one of a set. A name another format claims picks that format.</summary>
	public string Target {
		get;
		set {
			if (ChangeProperty(ref field, value) && !FollowTarget()) {
				Replan();
			}
		}
	} = "";
	/// <summary>Said when no format claims the target's extension, so the format names the files its own way; empty otherwise.</summary>
	public string Unclaimed { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasUnclaimed)); } = "";
	public bool HasUnclaimed => Unclaimed != "";

	/// <summary>The files the export will write, for the file, format and target as they stand.</summary>
	public IReadOnlyList<ExportUnitRow> Plan { get; private set => ChangeProperty(ref field, value); } = [];
	/// <summary>What the file uses that the format drops, in words; empty until there is a plan.</summary>
	public string Loss { get; private set => ChangeProperty(ref field, value); } = "";
	public bool CanExport => Plan.Count > 0;
	/// <summary>A sheet's columns, when the format is the spreadsheet: the target's section, or a new table's ticks.</summary>
	public SheetColumnsViewModel? Sheet { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasSheet)); }
	public bool HasSheet => Sheet is not null;
	/// <summary>Why there is no plan, when planning failed (a <c>schema.ini</c> that can't be read); empty otherwise.</summary>
	public string PlanProblem { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasPlanProblem)); } = "";
	public bool HasPlanProblem => PlanProblem != "";

	//how Export writes a file, and a sheet's section, and puts a schema.ini back as it was
	//(its bytes, or none): seams a test fails a write through
	internal Action<string, Action<TextWriter>, Encoding?> WriteFile { get; set; } = IniWriter.WriteAtomic;
	internal Action<string, FormatOptions> WriteSection { get; set; } = CsvCodec.SaveSchema;
	internal Action<string, byte[]?> PutBackSchema { get; set; } = PutBack;

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

	//the old plan goes first, so a plan that fails leaves none to export by; the old sheet
	//is kept aside only to keep its ticks
	private bool Replan() {
		SheetColumnsViewModel? kept = Sheet;
		Plan = [];
		Loss = "";
		Unclaimed = "";
		PlanProblem = "";
		if (FileNode is not null && Format is not null && Target.Trim() != "") {
			try {
				ExportSource source = Source(FileNode);
				Sheet = Format.Format is CsvCodec codec ? SheetFor(codec, source, FileNode, kept) : null;
				FormatOptions? options = Sheet?.Options;
				Plan = [.. Format.Format.Plan(source, Target, options).Select(unit => new ExportUnitRow(unit))];
				Loss = Describe(Format.Format.Loses(source, options));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Plan = [];
				Loss = "";
				PlanProblem = Words.Known.Format("export.plan-failed", SchemaIni.For(Target.Trim()), ex.Message);
			}
		}
		if (Plan.Count == 0) {
			Sheet = null;
		}
		Unclaimed = Plan.Count > 0 && Path.GetExtension(Target.Trim()) is { Length: > 1 } extension && !Formats.Any(row => row.Format.Info.Claims(Target.Trim()))
			? Words.Known.Format("export.unclaimed", extension, Format!.Name)
			: "";
		AffectProperty(nameof(CanExport));
		return true;
	}

	//a target another format claims switches to it, unless the format picked claims it too
	//(Strings.xlf typed with CSV picked is XLIFF); true when it switched, which replanned
	private bool FollowTarget() {
		string target = Target.Trim();
		if (Format is { } picked && picked.Format.Info.Claims(target) || Formats.FirstOrDefault(row => row.Format.Info.Claims(target)) is not { } claimed) {
			return false;
		}
		Format = claimed;
		return true;
	}

	//the other way: a format picked after a target another format claims renames it
	//(CSV picked over Strings.xlf makes it Strings.csv); true when it did, which replanned
	private bool RenameTarget() {
		string target = Target.Trim();
		if (Format is not { } picked || picked.Format.Info.Claims(target) || !Formats.Any(row => row.Format.Info.Claims(target))) {
			return false;
		}
		Target = Path.ChangeExtension(target, picked.Format.Info.Extensions[0]);
		return true;
	}

	//the sheet's columns, laid out afresh when the file or the sheet changes, and kept,
	//ticks and all, while neither does
	private SheetColumnsViewModel SheetFor(CsvCodec codec, ExportSource source, KeyNode node, SheetColumnsViewModel? kept) {
		string sheet = codec.Plan(source, Target).Single().Path;
		return kept is not null && kept.Node == node && WordsSession.SamePath(kept.Sheet, sheet)
			? kept
			: new SheetColumnsViewModel(this, codec, source, node, sheet);
	}

	/// <summary>A tick changed what the sheet's columns are: the plan and the loss follow.</summary>
	internal void SheetChanged() => Replan();

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

	//every format's type, the one picked first so the dialog names its files; picking
	//another type names that format's, and the target then picks the format
	private void DoBrowse() {
		if (Format is not { } picked) {
			return;
		}
		if (Parent.Dialogs.TrySaveFile(Words.Known["file.export-title"], BrowseFilter(picked), Target.Trim() is { Length: > 0 } proposed ? proposed : null, out string? target)) {
			Target = target;
		}
	}

	//the formats' filters, each in its own words, joined
	[return: Localized]
	private string BrowseFilter(ExportFormatRow picked) => string.Join("|", Formats.OrderBy(row => row != picked).Select(row => row.Filter));

	//a schema.ini as it was before Export wrote its section: its bytes, through a file beside
	//it moved over it, or no file, when Export made it
	private static void PutBack(string path, byte[]? bytes) {
		if (bytes is null) {
			File.Delete(path);
			return;
		}
		string temp = path + $".{Guid.NewGuid():N}.tmp";
		try {
			File.WriteAllBytes(temp, bytes);
			File.Move(temp, path, overwrite: true);
		}
		catch {
			try { File.Delete(temp); } catch { /* the move's own error is the one worth raising */ }
			throw;
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
		FormatOptions? options = Sheet?.Options;
		List<string> gripes = [];
		//a sheet's section goes first, so a schema.ini that refuses it is known before a byte
		//of the sheet lands: a new table's, a changed one's, or one whose cells need quoting
		//that TextDelimiter=none would not give them
		string? schema = null;
		byte[]? before = null;
		if (Sheet is { } sheet && Format.Format is CsvCodec codec && options is not null) {
			FormatOptions wanted = codec.Quoting(source, options);
			if (sheet.WritesSection || !ReferenceEquals(wanted, options)) {
				schema = SchemaIni.For(sheet.Sheet);
				try {
					before = File.Exists(schema) ? File.ReadAllBytes(schema) : null;
					WriteSection(sheet.Sheet, wanted);
					if (!ReferenceEquals(wanted, options)) {
						gripes.Add(Words.Known.Format("export.quoted", schema));
					}
					options = wanted;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
					if (sheet.WritesSection) {
						//the sheet is not written under columns its section does not say
						Parent.Dialogs.Tell(Words.Known.Format("file.write-failed", schema, ex.Message));
						return;
					}
					//the section is locked: the sheet goes by it as it is, its cells unquoted
					gripes.Add(Words.Known.Format("export.schema-locked", schema, ex.Message));
					schema = null;
				}
			}
		}
		foreach (ExportUnitRow row in Plan) {
			try {
				//each file atomic: a failure partway leaves what was there
				WriteFile(row.Path, writer => Format.Format.Write(source, row.Unit, writer, gripes, options), row.Unit.Encoding);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EncoderFallbackException) {
				Parent.Dialogs.Tell(Words.Known.Format("file.write-failed", row.Path, ex.Message));
				//the section written for a sheet that wasn't goes back as it was
				if (schema is not null) {
					try {
						PutBackSchema(schema, before);
					}
					catch (Exception again) when (again is IOException or UnauthorizedAccessException) {
						Parent.Dialogs.Tell(Words.Known.Format("export.schema-changed", schema, row.Path, again.Message));
						Sheet = null; //the section is what Export wrote: the plan reads it afresh, ticks and all
					}
				}
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
