using PatTech.Localization.Authoring.Codecs;
using PatTech.Utils;

namespace WordsEdit.ViewModels;

/// <summary>A language a new table can give a column, ticked or not.</summary>
public sealed class SheetLanguageTick(SheetColumnsViewModel parent, string code, string name, bool ticked) : ViewModelBase {
	public string Code => code;
	public string Name => name;
	public bool IsTicked { get; set => _ = ChangeProperty(ref field, value) && parent.Changed(); } = ticked;
}

/// <summary>A thing a sheet can carry, ticked or not, named as the loss line names it.</summary>
public sealed class SheetFeatureTick(SheetColumnsViewModel parent, WordsFeatures feature, bool ticked) : ViewModelBase {
	public WordsFeatures Feature => feature;
	public string Name => feature.Describe("G");
	public bool IsTicked { get; set => _ = ChangeProperty(ref field, value) && parent.Changed(); } = ticked;
	/// <summary>A language's context, comment and stale mark hang on a language ticked, and grey without one.</summary>
	public bool IsEnabled => (feature & SheetColumnsViewModel.PerLanguage) == 0 || parent.LanguageChannelsOpen;

	internal void Reenable() => AffectProperty(nameof(IsEnabled));
}

/// <summary>
///     The Export dialog's columns for a sheet (SPEC: Spreadsheets, Export): the
///     target's section of the <c>schema.ini</c> beside it, kept as it is, or
///     changed by one tick; a new table's ticks otherwise, each set when the file
///     uses it. The options it makes are what the plan, the loss line and the
///     write go by, and what Export writes as the section when it is new or
///     changed.
/// </summary>
public sealed class SheetColumnsViewModel : ViewModelBase {
	/// <summary>What a sheet carries a column of for each language ticked.</summary>
	internal const WordsFeatures PerLanguage = WordsFeatures.EntryContext | WordsFeatures.EntryComment | WordsFeatures.Stale;
	private static readonly WordsFeatures[] Carried = [
		WordsFeatures.Context, WordsFeatures.Comment, WordsFeatures.EntryContext, WordsFeatures.EntryComment,
		WordsFeatures.Stale, WordsFeatures.NeedsReview, WordsFeatures.PluralForms];

	private readonly ExportViewModel parent;
	private readonly CsvCodec codec;
	private readonly ExportSource source;
	//the target's section, when it has one, and what the export goes by with it: the
	//section, or the columns the file uses where it names none (CsvCodec.Effective)
	private readonly FormatOptions? section;
	private readonly FormatOptions? effective;

	/// <summary>The file it was laid out for.</summary>
	public KeyNode Node { get; }
	/// <summary>The sheet the export writes.</summary>
	public string Sheet { get; }
	/// <summary>The target's section as <c>schema.ini</c> has it; <see langword="null"/> for a new table.</summary>
	internal FormatOptions? Section => section;
	/// <summary>No section yet: a new table, its columns ticked.</summary>
	public bool IsNew => section is null;
	/// <summary>A table that exists can have its columns changed.</summary>
	public bool CanChange => !IsNew;
	/// <summary>Change a table that exists: its columns open, set from its section.</summary>
	public bool Change { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(ShowTicks)) && Changed(); }
	public bool ShowTicks => IsNew || Change;
	/// <summary>Export writes the section: a new table's, or a changed one's.</summary>
	public bool WritesSection => ShowTicks;

	/// <summary>Each language in the file's table.</summary>
	public IReadOnlyList<SheetLanguageTick> Languages { get; }
	/// <summary>Each thing a sheet can carry besides values.</summary>
	public IReadOnlyList<SheetFeatureTick> Features { get; }
	public bool LanguageChannelsOpen => Languages.Any(language => language.IsTicked);

	/// <summary>The section the export goes by: the table's as it is, or one made from the ticks, the rest of a changed table's kept.</summary>
	public FormatOptions Options => ShowTicks ? codec.Options(source, Layout(), section) : effective!;
	/// <summary>The target's columns, by name, in their order.</summary>
	public string Columns
		=> Words.Known.Format("export.sheet-columns", string.Join(", ", Options
			.Select(pair => (Number: SchemaIni.ColumnNumber(pair.Key), pair.Value))
			.Where(column => column.Number is not null)
			.OrderBy(column => column.Number)
			.Select(column => CsvCodec.ReadColumn(column.Value).Name)));

	public SheetColumnsViewModel(ExportViewModel parent, CsvCodec codec, ExportSource source, KeyNode node, string sheet) {
		this.parent = parent;
		this.codec = codec;
		this.source = source;
		Node = node;
		Sheet = sheet;
		section = SchemaIni.Read(sheet);
		effective = section is null ? null : codec.Effective(source, section);
		CsvLayout layout = effective is null ? CsvLayout.For(source) : CsvLayout.From(effective);
		Languages = [.. source.File.Languages.Select(code => new SheetLanguageTick(this, code,
			parent.Parent.Session.Languages.Find(code)?.DisplayName ?? code, layout.Languages.Contains(code)))];
		Features = [.. Carried.Select(feature => new SheetFeatureTick(this, feature, Ticked(layout, feature)))];
	}

	private static bool Ticked(CsvLayout layout, WordsFeatures feature) => feature switch {
		WordsFeatures.Context => layout.Context,
		WordsFeatures.Comment => layout.Comment,
		WordsFeatures.EntryContext => layout.EntryContext,
		WordsFeatures.EntryComment => layout.EntryComment,
		WordsFeatures.Stale => layout.Stale,
		WordsFeatures.NeedsReview => layout.Review,
		_ => layout.Forms,
	};

	private CsvLayout Layout() {
		bool Has(WordsFeatures feature) => Features.Any(tick => tick.Feature == feature && tick.IsTicked);
		var layout = new CsvLayout {
			Context = Has(WordsFeatures.Context),
			Comment = Has(WordsFeatures.Comment),
			EntryContext = Has(WordsFeatures.EntryContext),
			EntryComment = Has(WordsFeatures.EntryComment),
			Stale = Has(WordsFeatures.Stale),
			Review = Has(WordsFeatures.NeedsReview),
			Forms = Has(WordsFeatures.PluralForms),
		};
		layout.Languages.AddRange(Languages.Where(language => language.IsTicked).Select(language => language.Code));
		return layout;
	}

	//a tick changed: the greyed ones follow the languages, and the export replans
	internal bool Changed() {
		AffectProperty(nameof(LanguageChannelsOpen));
		foreach (SheetFeatureTick feature in Features) {
			feature.Reenable();
		}
		AffectProperty(nameof(Columns));
		parent.SheetChanged();
		return true;
	}
}
