using System.IO;
using System.Text;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>What machine translation remembers between runs: each language's brief, the keys a request asks for, the stale mark.</summary>
public interface ITranslationMemory {
	/// <summary>A value kept under <paramref name="name"/>; null when none is.</summary>
	string? Value(string name);
	/// <summary>Keeps <paramref name="value"/> under <paramref name="name"/>; null forgets it.</summary>
	void Keep(string name, string? value);
}

/// <summary>Machine translation's memory for one run alone: what a test, or an editor built without the config, keeps.</summary>
public sealed class TranslationMemory : ITranslationMemory {
	private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

	public string? Value(string name) => values.GetValueOrDefault(name);

	public void Keep(string name, string? value) {
		if (value is null) {
			values.Remove(name);
		}
		else {
			values[name] = value;
		}
	}
}

/// <summary>Machine translation's memory in the editor's config, under <c>[translation]</c>.</summary>
public sealed class ConfigTranslationMemory : ITranslationMemory {
	public string? Value(string name) => EditorConfig.Translation(name);
	public void Keep(string name, string? value) => EditorConfig.KeepTranslation(name, value);
}

/// <summary>
///     One slot an answer filled, as the review grid shows it: the key, the form, the
///     default's words for it, the text proposed, which the person may edit, and
///     whether it is written. Its notes say why its key was flagged, or why its hand
///     goes up.
/// </summary>
public sealed class TranslationRow(TranslationViewModel parent, int item, string key, string slot, string @default, string text, bool ticked) : ViewModelBase {
	/// <summary>The answer's item the row is of.</summary>
	internal int Item => item;
	public string Key => key;
	/// <summary>The slot as the document names it: <c>value</c>, <c>value#few</c>.</summary>
	public string Slot => slot;
	/// <summary>The plural form, empty for the plain value.</summary>
	public string Form => slot == TranslationRequest.Plain ? "" : slot[(TranslationRequest.Plain.Length + 1)..];
	public string Default => @default;
	public string Text { get; set => _ = ChangeProperty(ref field, value) && parent.Edited(); } = text;
	public bool Ticked { get; set => _ = ChangeProperty(ref field, value) && parent.TickedChanged(); } = ticked;
	/// <summary>Why the key was flagged, or why its hand goes up; empty when neither.</summary>
	public string Notes { get; internal set => ChangeProperty(ref field, value); } = "";
	/// <summary>The check flags the key: a person ticks it, or leaves it.</summary>
	public bool Flagged { get; internal set => ChangeProperty(ref field, value); }
}

/// <summary>
///     Tools &gt; Machine Translation (SPEC: Machine translation, In Wordsmith): the
///     selected file's keys a language needs, as a request a translator fills, and
///     the answer it gives back, checked and shown a row per slot for review. By hand,
///     the request is copied or saved and the answer pasted or opened. Apply writes the
///     ticked rows into the file as one undo entry, each key's entry marked stale with
///     the mark, and goes back for the next request, which the keys just written have
///     left: no longer missing, and stale with the mark, which the selection passes over.
/// </summary>
public sealed class TranslationViewModel : DialogViewModel {
	/// <summary>The stale mark a run leaves when none is kept.</summary>
	public const string DefaultMark = "machine translated";
	/// <summary>The keys a request asks for when no number is kept.</summary>
	public const int DefaultBatch = 50;

	private readonly MainWindowViewModel parent;
	private readonly ITranslationMemory memory;
	private readonly WordsFile file;
	//the node the selection may keep to; none when the file's own is selected
	private readonly KeyNode? under;
	//the answer under review, and what the check made of it as it came
	private TranslationAnswer? answer;
	private string code = "";

	public override string Title => Words.Known["machine.title"];
	public string Blurb => Words.Known.Format("machine.blurb", file.Name);

	/// <summary>The languages the file wants words in: each one it declares, but its default's.</summary>
	public IReadOnlyList<LanguageEntry> Targets { get; }
	public LanguageEntry? Target {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				Brief = value is null ? "" : memory.Value(BriefName(value.Code)) ?? "";
				Refresh();
			}
		}
	}
	public bool HasTargets => Targets.Count > 0;
	public bool Missing { get; set => _ = ChangeProperty(ref field, value) && Refresh(); } = true;
	public bool Stale { get; set => _ = ChangeProperty(ref field, value) && Refresh(); }
	/// <summary>Whether a node below the file is selected, which the selection may keep to.</summary>
	public bool CanKeepUnder => under is not null;
	public string? UnderCaption => under?.FullLabel;
	public bool Under { get; set => _ = ChangeProperty(ref field, value) && Refresh(); }
	/// <summary>The user's own words to the translator, for the tone and the terms; kept for the language.</summary>
	public string Brief {
		get;
		set {
			if (ChangeProperty(ref field, value) && Target is not null) {
				memory.Keep(BriefName(Target.Code), value.Trim() == "" ? null : value.Trim());
			}
		}
	} = "";
	/// <summary>How many keys a request asks for, as typed.</summary>
	public string Batch {
		get;
		set {
			if (ChangeProperty(ref field, value) && Size() is { } size) {
				memory.Keep("batch", size == DefaultBatch ? null : size.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
			Refresh();
		}
	}
	/// <summary>The stale mark each entry written takes, and the Stale selection passes over.</summary>
	public string Mark {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				memory.Keep("mark", value.Trim() is "" or DefaultMark ? null : value.Trim());
				Refresh();
			}
		}
	}

	/// <summary>How many keys the selection holds, and how many a request asks for.</summary>
	public string Summary { get; private set => ChangeProperty(ref field, value); } = "";
	/// <summary>Whether there is a request to make.</summary>
	public bool CanAsk { get; private set => ChangeProperty(ref field, value); }
	/// <summary>What the last Apply wrote; empty before one.</summary>
	public string Applied { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasApplied)); } = "";
	public bool HasApplied => Applied != "";

	/// <summary>The answer, pasted or opened.</summary>
	public string Answer {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				Problem = "";
			}
		}
	} = "";
	/// <summary>Why the answer cannot be reviewed; empty when it can.</summary>
	public string Problem { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasProblem)); } = "";
	public bool HasProblem => Problem != "";
	/// <summary>The answer is no JSON: the character in it, from 0, where the reader stopped; the view highlights the rest of its line.</summary>
	public event Action<int>? AnswerFaulted;

	/// <summary>The grid shows: the answer read, under review.</summary>
	public bool IsReviewing { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(IsAsking)); }
	public bool IsAsking => !IsReviewing;
	/// <summary>A row per slot the answer filled, in the answer's order.</summary>
	public IReadOnlyList<TranslationRow> Rows { get; private set => ChangeProperty(ref field, value); } = [];
	/// <summary>The keys the answer does not write whose translation it leaves at odds with their default; nothing is skipped for them.</summary>
	public string Broken { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasBroken)); } = "";
	public bool HasBroken => Broken != "";
	/// <summary>Who answered, by its own account; empty when the answer did not say.</summary>
	public string Translator { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(HasTranslator)); } = "";
	public bool HasTranslator => Translator != "";
	/// <summary>Apply's caption, the ticked rows counted.</summary>
	public string ApplyCaption => Words.Known.Format("machine.apply", Rows.Count(row => row.Ticked));

	public ICommand CopyCommand { get; }
	public ICommand SaveCommand { get; }
	public ICommand OpenCommand { get; }
	public ICommand ReviewCommand { get; }
	public ICommand BackCommand { get; }
	public ICommand ApplyCommand { get; }
	public ICommand CloseCommand { get; }

	public TranslationViewModel(MainWindowViewModel parent, ITranslationMemory memory) {
		ArgumentNullException.ThrowIfNull(parent);
		this.parent = parent;
		this.memory = memory;
		KeyNode node = parent.Tree.SelectedKeyNode ?? throw new InvalidOperationException("machine translation works on the selected node's file");
		file = parent.Tree.FileOf(node);
		//a comment selected keeps to the node it stands under
		KeyNode at = node is OrganizerNode && node.Parent is { } parentNode ? parentNode : node;
		under = at.Parent is null ? null : at;
		Targets = [.. parent.Tree.FileLanguages.Where(language => MissingWords.Wants(file.Languages, file.DefaultLanguage, language.Code))];
		Batch = memory.Value("batch") ?? DefaultBatch.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Mark = memory.Value("mark") ?? DefaultMark;
		CopyCommand = new DelegateCommand(() => parent.Dialogs.Copy(Request()), () => CanAsk);
		SaveCommand = new DelegateCommand(DoSave, () => CanAsk);
		OpenCommand = new DelegateCommand(DoOpen);
		ReviewCommand = new DelegateCommand(DoReview, () => Target is not null && Answer.Trim() != "");
		BackCommand = new DelegateCommand(() => IsReviewing = false);
		ApplyCommand = new DelegateCommand(DoApply, () => Rows.Any(row => row.Ticked));
		CloseCommand = new DelegateCommand(Close);
		Target = Targets.FirstOrDefault(language => language.Code == parent.Tree.SelectedLanguage.Code) ?? Targets.FirstOrDefault();
	}

	private static string BriefName(string code) => $"brief-{code}";

	//the keys a request may ask for, a positive whole number; null for anything else
	private int? Size() => int.TryParse(Batch, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int size) && size > 0 ? size : null;

	private string MarkWritten => Mark.Trim() == "" ? DefaultMark : Mark.Trim();

	//the keys the choices select, in the tree's order: missing the target's words, or stale
	//in it but for the mark a run leaves, so the next request moves on; and only those with
	//something to ask, as the command line's request selects them
	internal List<WordsKey> Selected() {
		if (Target is not { Code: var target } || !Missing && !Stale) {
			return [];
		}
		bool wants = MissingWords.Wants(file.Languages, file.DefaultLanguage, target);
		return [.. (Under && under is not null ? under : parent.Tree.NodeOf(file)).SelfAndDescendants()
			.Where(node => node is not OrganizerNode)
			.Select(node => file.Keys.GetValueOrDefault(node.FullLabel))
			.OfType<WordsKey>()
			.Where(key => Missing && wants && MissingWords.InLanguage(key, target)
				|| Stale && TranslationRequest.Stale(key, target) && !key.Entries[target].Stale!.StartsWith(MarkWritten, StringComparison.Ordinal))
			.Where(key => TranslationRequest.HasWords(key) && TranslationRequest.Asked(key, target).Count != 0)];
	}

	private bool Refresh() {
		int selected = Selected().Count;
		CanAsk = selected > 0 && Size() is not null;
		Summary = Target is null ? Words.Known["machine.no-language"]
			: Size() is not { } size ? Words.Known["machine.bad-batch"]
			: selected == 0 ? Words.Known.Format("machine.nothing", Target.DisplayName)
			: Words.Known.Format("machine.selected", selected, Math.Min(selected, size));
		return true;
	}

	/// <summary>The request for the selection's first keys, as many as a batch holds.</summary>
	public string Request() {
		LanguageEntry target = Target ?? throw new InvalidOperationException("no language to ask for");
		string source = file.DefaultLanguage ?? "en";
		return TranslationRequest.Write(file.Path is { } path ? Path.GetFileName(path) : file.Name,
			new(source, Name(source)), new(target.Code, Name(target.Code)), Selected().Take(Size() ?? DefaultBatch), Brief.Trim());
	}

	//a language as the file's table names it; none for a code it labels no name
	private string? Name(string language)
		=> file.Labels.TryGetValue(language, out LanguageEntry? label) && !label.NativeName.StartsWith('!') ? label.DisplayName : null;

	private void DoSave() {
		if (parent.Dialogs.TrySaveFile(Words.Known["machine.save-title"], Words.Known["machine.filter"], $"{file.Name}.{Target?.Code}.json", out string? path)) {
			try {
				File.WriteAllText(path, Request(), new UTF8Encoding(false));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				parent.Dialogs.Tell(Words.Known.Format("machine.save-failed", path, ex.Message));
			}
		}
	}

	private void DoOpen() {
		if (parent.Dialogs.TryOpenFiles(Words.Known["machine.open-title"], Words.Known["machine.filter"], out string[]? paths) && paths.Length > 0) {
			try {
				Answer = File.ReadAllText(paths[0]);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Problem = Words.Known.Format("machine.open-failed", paths[0], ex.Message);
			}
		}
	}

	//the answer read and checked whole against the file as it is, a row for each slot it filled;
	//a flagged key's rows start unticked, saying why
	private void DoReview() {
		if (Target is not { } target) {
			return;
		}
		TranslationAnswer read;
		try {
			read = TranslationAnswer.Read(Answer);
		}
		catch (InvalidDataException ex) {
			//no JSON: the box has no lines to count, so the fault is shown where it stands
			if (TranslationAnswer.FaultOf(ex) is var (at, reason)) {
				Problem = Words.Known.Format("machine.no-json", reason);
				AnswerFaulted?.Invoke(at);
			}
			else {
				Problem = Words.Known.Format("machine.no-answer", ex.Message);
			}
			return;
		}
		string language = LanguageCode.TryParse(read.Target, out var parsed) ? parsed.ToString() : read.Target;
		if (language != target.Code) {
			Problem = Words.Known.Format("machine.other-language", read.Target, target.DisplayName);
			return;
		}
		answer = read;
		code = language;
		TranslationBatch batch = read.Check(file.Keys, code, file.DefaultLanguage);
		List<TranslationRow> rows = [];
		for (int i = 0; i < read.Items.Count; i++) {
			TranslationItem item = read.Items[i];
			var held = file.Keys.TryGetValue(item.Key, out WordsKey? key) ? TranslationRequest.Held(key) : item.Default ?? new Dictionary<string, string>();
			foreach (var (slot, text) in item.Translation) {
				if (text is { } filled && filled.Trim() != "") {
					string shown = held.GetValueOrDefault(slot) ?? held.GetValueOrDefault(TranslationRequest.Plain) ?? "";
					rows.Add(new TranslationRow(this, i, item.Key, slot, shown, filled, ticked: batch.Verdicts[i].Flags.Count == 0));
				}
			}
		}
		if (rows.Count == 0) {
			Problem = Words.Known["machine.fills-nothing"];
			return;
		}
		Rows = rows;
		Translator = read.Translator is { } name ? Words.Known.Format("machine.answered-by", name) : "";
		Annotate(batch);
		AffectProperty(nameof(ApplyCaption));
		IsReviewing = true;
	}

	//the answer as the rows stand: each filled slot's text as edited; with ticked alone, the
	//unticked ones left null, as Apply writes it
	private TranslationAnswer Edited(bool tickedAlone) {
		TranslationAnswer read = answer ?? throw new InvalidOperationException("no answer under review");
		return read with {
			Items = [.. read.Items.Select((item, i) => {
				var slots = new Dictionary<string, string?>(item.Translation);
				foreach (TranslationRow row in Rows.Where(row => row.Item == i)) {
					slots[row.Slot] = !tickedAlone || row.Ticked ? row.Text : null;
				}
				return item with { Translation = slots };
			})],
		};
	}

	//each row's notes from the check: its key's flags, or the links whose target changed
	private void Annotate(TranslationBatch batch) {
		foreach (TranslationRow row in Rows) {
			TranslationVerdict verdict = batch.Verdicts[row.Item];
			row.Flagged = verdict.Flags.Count != 0;
			row.Notes = row.Flagged ? Words.Known.Format("machine.flagged", string.Join("; ", verdict.Flags))
				: verdict.Raised.Count != 0 ? Words.Known.Format("machine.raised", string.Join("; ", verdict.Raised))
				: "";
		}
		Broken = string.Join(Environment.NewLine, batch.Broken);
	}

	//a row edited is checked again with the rest, its notes and its key's with it; its tick is the person's
	internal bool Edited() {
		if (answer is not null) {
			Annotate(Edited(tickedAlone: false).Check(file.Keys, code, file.DefaultLanguage));
		}
		return true;
	}

	internal bool TickedChanged() => AffectProperty(nameof(ApplyCaption));

	//the ticked rows written as edited, as one entry, each key's entry marked; a key whose
	//link target the words written change raises its hand. Then back for the next request
	private void DoApply() {
		List<TranslationRow> ticked = [.. Rows.Where(row => row.Ticked)];
		if (ticked.Count == 0) {
			return;
		}
		TranslationAnswer written = Edited(tickedAlone: true);
		TranslationBatch batch = written.Check(file.Keys, code, file.DefaultLanguage);
		HashSet<string> raised = [.. written.Items.Where((item, i) => batch.Verdicts[i].Raised.Count != 0).Select(item => item.Key)];
		int count = parent.WriteTranslations(file, code, [.. ticked.Select(row => (row.Key, row.Form, row.Text))], written.MarkOf(MarkWritten), raised);
		Applied = Words.Known.Format("machine.applied", count);
		answer = null;
		Rows = [];
		Broken = "";
		Translator = "";
		Answer = "";
		IsReviewing = false;
		Refresh();
	}
}
