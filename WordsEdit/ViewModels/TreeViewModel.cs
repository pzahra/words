using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The tree pane: one row per node of every loaded file, the selection (and
///     what the panes show for it, and where it has been), the language the
///     badges are computed for, and the filters. It presents <see cref="WordsSession"/> and never writes
///     to it — edits made through the selection are announced by
///     <see cref="Edited"/> for the owner to mark the session dirty, and typing
///     by <see cref="FieldEdited"/>, with what it replaced, for the owner's undo.
/// </summary>
public class TreeViewModel : ViewModelBase {
	private readonly WordsSession session;

	public KeyNodeCollection KeyNodes { get; } = new(null);
	public ObservableCollection<LanguageEntry> KnownLanguages => session.Languages.Known;
	/// <summary>
	///     The dropdown: what the selected key's file speaks — its language table,
	///     plus codes found on its fields (the ! placeholders) — and the language
	///     selected, so the choice always shows. The session union when nothing is
	///     selected. (SPEC: translation pane)
	/// </summary>
	public ObservableCollection<LanguageEntry> FileLanguages { get; } = [];

	/// <summary>Raised when an edit reached the document through the selection.</summary>
	public event Action? Edited;
	/// <summary>A text field of the selection was typed into: which, what it held and what it holds (SPEC: Undo → Fields).</summary>
	public event Action<FieldEdit>? FieldEdited;

	public TreeViewModel(WordsSession session) {
		this.session = session;
		//the panes before the language: setting it reads them
		DefaultForms = new FormPane(() => DefaultRulesLanguage, () => SelectedKey?.Forms, () => SelectedKey?.DefaultValue ?? "");
		TranslationForms = new FormPane(() => SelectedLanguage.Code, () => SelectedEntry?.Forms, () => SelectedEntry?.Value ?? "");
		DefaultForms.PropertyChanged += OnFormPicked;
		TranslationForms.PropertyChanged += OnFormPicked;
		SelectedLanguage = KnownLanguages[0];
		BackCommand = new DelegateCommand(() => Navigate(History.Back(Resolves)), () => History.CanGoBack);
		ForwardCommand = new DelegateCommand(() => Navigate(History.Forward(Resolves)), () => History.CanGoForward);
	}

	//Selection
	public KeyNode? SelectedKeyNode {
		get;
		set {
			if (ChangeProperty(ref field, value)) {
				OnSelectedKeyNodeChanged();
			}
		}
	}

	public WordsKey? SelectedKey {
		get;
		private set {
			WordsKey? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedKeyValueChanged;
				value?.PropertyChanged += OnSelectedKeyValueChanged;
				Remember(DocumentField.DefaultValue, DocumentField.KeyContext, DocumentField.KeyComment);
			}
		}
	}

	public WordsEntry? SelectedEntry {
		get;
		private set {
			WordsEntry? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedEntryChanged;
				value?.PropertyChanged += OnSelectedEntryChanged;
				Remember(DocumentField.EntryValue, DocumentField.EntryContext, DocumentField.EntryComment);
			}
		}
	}

	public OrganizerNode? SelectedOrganizer {
		get;
		private set {
			OrganizerNode? oldValue = field;
			if (ChangeProperty(ref field, value)) {
				oldValue?.PropertyChanged -= OnSelectedOrganizerChanged;
				value?.PropertyChanged += OnSelectedOrganizerChanged;
				Remember(DocumentField.CommentText);
			}
		}
	}

	public LanguageEntry SelectedLanguage {
		get;
		set {
			//the ComboBox pushes null while its items turn over; the session always
			//has a language, so the selection never goes without one
			if (value is null) {
				return;
			}
			if (ChangeProperty(ref field, value)) {
				RefreshBadges();
				FollowSelectedKey();
				ApplyFilters();
			}
		}
	}

	/// <summary>The file the selected node belongs to, if any.</summary>
	public WordsFile? SelectedFile => SelectedKeyNode is null ? null : session.FileOf(SelectedKeyNode.Root.FullLabel);

	/// <summary>The language the selected node's file writes its default in, if it says: the default and the developer's notes spell-check in it.</summary>
	public string? DefaultLanguage => SelectedFile?.DefaultLanguage;

	/// <summary>
	///     The default, as the translation box's hint where the default speaks the
	///     selected language (<see cref="WordsParser.DefaultSpeaks"/>): an empty entry
	///     there falls back to it, so it shows what the entry reads as.
	/// </summary>
	public string? DefaultHint
		=> SelectedKey is { } key && WordsParser.DefaultSpeaks(DefaultLanguage, SelectedLanguage.Code) ? key.DefaultValue : null;

	//the selection, its language or its file's default changed: what reads them follows
	private void RaiseDefault() {
		AffectProperty(nameof(DefaultLanguage));
		AffectProperty(nameof(DefaultHint));
		RaiseForms();
	}

	//Plural forms (SPEC: Plural forms)
	/// <summary>The baseline pane's form selector, over the default's language.</summary>
	public FormPane DefaultForms { get; }
	/// <summary>The translation pane's, over the selected language.</summary>
	public FormPane TranslationForms { get; }

	/// <summary>The language whose rules the baseline's forms follow: the default's, English while the file declares none.</summary>
	public string DefaultRulesLanguage => DefaultLanguage ?? "en";

	/// <summary>A key with a form in any language, the default included.</summary>
	public bool IsPlural => SelectedKey is { } key && MissingWords.IsPlural(key);

	/// <summary>A baseline row the badge counts as missing: bold, as the tree shows a gap.</summary>
	public bool DefaultMisses(string category) => SelectedKey is { IsConstant: false } key
		&& Missing(category, DefaultRulesLanguage, key.DefaultValue, key.Forms, formsWanted: MissingWords.IsPlural(key));
	/// <summary>A translation row the badge counts as missing, where the selected language wants words.</summary>
	public bool EntryMisses(string category) => SelectedKey is { IsConstant: false } key && SelectedEntry is { } entry
		&& Wants(SelectedFile, SelectedLanguage.Code)
		&& Missing(category, SelectedLanguage.Code, entry.Value, entry.Forms, formsWanted: MissingWords.IsPlural(key) && entry.Value.Trim() != "");

	//the plain value empty, or on a plural key a form the language requires empty
	private static bool Missing(string category, string language, string plain, IReadOnlyDictionary<string, string> forms, bool formsWanted)
		=> category == FormPane.Plain ? plain.Trim() == "" : formsWanted && MissingWords.Requires(language, category) && forms.GetValueOrDefault(category, "") == "";

	/// <summary>The baseline's selector: how a key becomes plural, so live on any key.</summary>
	public bool DefaultFormsEnabled => SelectedKey is not null && DefaultForms.AnyToPick;
	/// <summary>The translation's: live on a plural key, where the language has forms to pick.</summary>
	public bool TranslationFormsEnabled => SelectedEntry is not null && IsPlural && TranslationForms.AnyToPick;

	/// <summary>The baseline box: the default's plain value, or the form picked.</summary>
	public string DefaultText {
		get => SelectedKey is not { } key ? "" : DefaultForms.IsPlain ? key.DefaultValue : key.Forms.GetValueOrDefault(DefaultForms.Form, "");
		set {
			if (SelectedKey is not { } key) {
				return;
			}
			if (DefaultForms.IsPlain) {
				key.DefaultValue = value; //the key says it changed
			}
			else if (FormPane.Write(key.Forms, DefaultForms.Form, value)) {
				FormEdited(DocumentField.DefaultValue);
			}
		}
	}

	/// <summary>The translation box: the entry's plain value, or the form picked.</summary>
	public string EntryText {
		get => SelectedEntry is not { } entry ? "" : TranslationForms.IsPlain ? entry.Value : entry.Forms.GetValueOrDefault(TranslationForms.Form, "");
		set {
			if (SelectedEntry is not { } entry) {
				return;
			}
			if (TranslationForms.IsPlain) {
				entry.Value = value;
			}
			else if (FormPane.Write(entry.Forms, TranslationForms.Form, value)) {
				FormEdited(DocumentField.EntryValue);
			}
		}
	}

	/// <summary>The baseline pane's title, naming the form picked ("Default · other").</summary>
	[Localized]
	public string DefaultTitle => Titled(Words.Known["main.default"], DefaultForms);
	/// <summary>The translation pane's ("Translation · few").</summary>
	[Localized]
	public string TranslationTitle => Titled(Words.Known["main.translation"], TranslationForms);

	[return: Localized]
	private static string Titled([Localized] string title, FormPane pane) => pane.IsPlain ? title : Words.Known.Format("main.pane-form", title, pane.Form);

	/// <summary>An empty form's hint in the baseline box: what that count reads as until the form has words.</summary>
	public string? DefaultFormHint => DefaultForms.IsPlain || SelectedKey is not { } key ? null : Reads(null, DefaultRulesLanguage, key, DefaultForms.Form);

	/// <summary>
	///     The translation box's hint: the default where it speaks the language
	///     (<see cref="DefaultHint"/>), and on a form what that count reads as —
	///     the language's own words once it has any for the key.
	/// </summary>
	public string? EntryHint {
		get {
			if (TranslationForms.IsPlain) {
				return DefaultHint;
			}
			if (SelectedKey is not { } key || SelectedEntry is not { } entry) {
				return null;
			}
			bool own = entry.Value != "" || entry.Forms.HasWords();
			return own || WordsParser.DefaultSpeaks(DefaultLanguage, SelectedLanguage.Code)
				? Reads(SelectedLanguage.Code, SelectedLanguage.Code, key, TranslationForms.Form)
				: null;
		}
	}

	//the raw text a count in the form reads, as the runtime picks it (Words.FormKey)
	private string? Reads(string? languageCode, string rules, WordsKey key, string form) {
		IWordsProvider provider = session.Provider(FileLabels, languageCode);
		return provider.TryGetValue(Words.FormKey(provider, rules, key.BlockKey, form), out string? text) && text != "" ? text : null;
	}

	/// <summary>Picks the baseline's form.</summary>
	public void PickDefaultForm(string form) => DefaultForms.Form = form;

	/// <summary>
	///     Picks the translation's form, and moves the baseline to the form the
	///     pick's first number takes in the default's language (SPEC: The two follow).
	/// </summary>
	public void PickTranslationForm(string form) {
		TranslationForms.Form = form;
		if (FormNumbers.Sample(SelectedLanguage.Code, form) is { } count) {
			string follows = PluralRules.Select(DefaultRulesLanguage, count);
			DefaultForms.Form = DefaultForms.Row(follows) == FormRow.Unused ? FormPane.Plain : follows;
		}
	}

	/// <summary>The pane whose box shows <paramref name="field"/>'s forms, if it has any.</summary>
	public FormPane? FormsOf(DocumentField field) => field switch {
		DocumentField.DefaultValue => DefaultForms,
		DocumentField.EntryValue => TranslationForms,
		_ => null,
	};

	/// <summary>
	///     A pick the selection no longer offers goes back to the plain value: on
	///     a key that is not plural, or where the picked row is greyed and empty
	///     (SPEC: Which keys count). Run when the key or the language changes, not
	///     on an edit, so clearing a form leaves it picked.
	/// </summary>
	public void RevalidateForms() {
		if (!IsPlural || !DefaultFormsEnabled || !DefaultForms.CanPick(DefaultForms.Form)) {
			DefaultForms.Form = FormPane.Plain;
		}
		if (!TranslationFormsEnabled || !TranslationForms.CanPick(TranslationForms.Form)) {
			TranslationForms.Form = FormPane.Plain;
		}
	}

	private void OnFormPicked(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName != nameof(FormPane.Form)) {
			return;
		}
		//the box now shows another text: the next edit replaces that one
		Remember(sender == DefaultForms ? DocumentField.DefaultValue : DocumentField.EntryValue);
		RaiseForms();
		AffectProperty(sender == DefaultForms ? nameof(DefaultForms) : nameof(TranslationForms));
	}

	//a form was typed into: reported like its field, and the badges and pickers follow
	private void FormEdited(DocumentField field) {
		Report(field);
		if (SelectedKeyNode is { } node) {
			RefreshBadges(node);
		}
		RaiseForms();
		Edited?.Invoke();
	}

	//the forms, the pick or the selection changed: whatever reads them follows
	private void RaiseForms() {
		DefaultForms.Refresh();
		TranslationForms.Refresh();
		AffectProperty(nameof(IsPlural));
		AffectProperty(nameof(DefaultFormsEnabled));
		AffectProperty(nameof(TranslationFormsEnabled));
		AffectProperty(nameof(DefaultText));
		AffectProperty(nameof(EntryText));
		AffectProperty(nameof(DefaultTitle));
		AffectProperty(nameof(TranslationTitle));
		AffectProperty(nameof(DefaultFormHint));
		AffectProperty(nameof(EntryHint));
	}

	private void OnSelectedKeyNodeChanged() {
		SelectedOrganizer = SelectedKeyNode as OrganizerNode;
		FollowSelectedKey();
		RefreshFileLanguages();
		//no selection is a passing state (the tree drops one row before it takes the next) or a reset
		if (SelectedKeyNode is { } node) {
			if (!navigating) {
				History.Visit(node.FullLabel);
			}
			if (exempt is not null && exempt != node) {
				//moving on from where Back or Forward arrived: the filters have it again
				exempt = null;
				ApplyFilters();
			}
		}
	}

	//Navigation (SPEC: Navigation)
	/// <summary>Where the selection has been.</summary>
	public SelectionHistory History { get; } = new();
	public ICommand BackCommand { get; }
	public ICommand ForwardCommand { get; }
	//set while Back or Forward moves the selection: the history's own step, not a visit
	private bool navigating;
	//where Back or Forward arrived: shown through the filters for as long as it is the selection
	private KeyNode? exempt;

	private bool Resolves(string label) => NodeAt(label) is not null;

	/// <summary>The first node carrying <paramref name="label"/>: a key's node, since comments share theirs.</summary>
	public KeyNode? NodeAt(string label) => AllNodes.FirstOrDefault(node => node.FullLabel == label);

	private void Navigate(string? label) {
		if (label is not null && NodeAt(label) is { } node) {
			Arrive(node, visit: false);
		}
	}

	/// <summary>
	///     Selects <paramref name="node"/> and brings it into view, the way Back
	///     arrives: the path to it opens and it shows through the filters until
	///     the selection moves on. A move like a click, so Back returns from it.
	/// </summary>
	public void Show(KeyNode node) => Arrive(node, visit: true);

	//Back and Forward step the history rather than visit
	private void Arrive(KeyNode node, bool visit) {
		for (KeyNode? parent = node.Parent; parent is not null; parent = parent.Parent) {
			parent.IsExpanded = true;
		}
		navigating = !visit;
		try {
			exempt = node;
			Select(node);
		}
		finally {
			navigating = false;
		}
		ApplyFilters();
	}

	private void RefreshFileLanguages() {
		IEnumerable<LanguageEntry> wanted = KnownLanguages;
		if (SelectedFile is { } file) {
			HashSet<string> codes = [.. file.Languages, SelectedLanguage.Code];
			foreach (WordsKey key in session.KeysOf(file)) {
				foreach (var (code, entry) in key.Entries) {
					if (entry.Value.Trim() != "" || entry.Forms.HasWords()) {
						codes.Add(code);
					}
				}
			}
			wanted = KnownLanguages.Where(language => codes.Contains(language.Code));
		}
		if (wanted.SequenceEqual(FileLanguages)) {
			return;
		}
		FileLanguages.Clear();
		foreach (LanguageEntry language in wanted) {
			FileLanguages.Add(language);
		}
		//the ComboBox dropped its selection while the items turned over: hand it back
		AffectProperty(nameof(SelectedLanguage));
	}

	/// <summary>Re-reads the selected node's key and entry from the document.</summary>
	public void FollowSelectedKey() {
		(WordsKey? key, WordsEntry? entry) before = (SelectedKey, SelectedEntry);
		if (SelectedKeyNode is not null && session.Keys.TryGetValue(SelectedKeyNode.FullLabel, out var key)) {
			SelectedKey = key;
			SelectedEntry = key.IsConstant ? null : key.Entries[SelectedLanguage.Code];
		}
		else {
			SelectedKey = null;
			SelectedEntry = null;
		}
		if (before != (SelectedKey, SelectedEntry)) {
			RevalidateForms(); //another key or language: the picks it no longer offers go plain
		}
		//a form written behind the boxes' backs (an undo) is what the next edit replaces
		Remember(DocumentField.DefaultValue, DocumentField.EntryValue);
		RaiseDefault();
	}

	//each handler reports a text field first, so the report precedes whatever the change sets off
	private void OnSelectedKeyValueChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedKey is null || SelectedKeyNode is null) {
			return; //selection and model briefly disagree while the selection is changing
		}
		Report(e.PropertyName switch {
			//the box shows the plain value only on the plain pick; a form reports its own
			nameof(WordsKey.DefaultValue) when DefaultForms.IsPlain => DocumentField.DefaultValue,
			nameof(WordsKey.Context) => DocumentField.KeyContext,
			nameof(WordsKey.Comment) => DocumentField.KeyComment,
			_ => null,
		});
		if (e.PropertyName is nameof(SelectedKey.DefaultValue) or nameof(SelectedKey.NeedsReview)) {
			RefreshBadges(SelectedKeyNode);
		}
		if (e.PropertyName is nameof(SelectedKey.DefaultValue)) {
			AffectProperty(nameof(DefaultHint));
			AffectProperty(nameof(DefaultText));
			AffectProperty(nameof(EntryHint));
		}
		Edited?.Invoke();
	}

	private void OnSelectedOrganizerChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(OrganizerNode.Text)) {
			Report(DocumentField.CommentText);
			Edited?.Invoke();
		}
	}

	private void OnSelectedEntryChanged(object? sender, PropertyChangedEventArgs e) {
		if (SelectedEntry is null || SelectedKey is null || SelectedKeyNode is null) {
			return; //selection and model briefly disagree while the selection is changing
		}
		Report(e.PropertyName switch {
			nameof(WordsEntry.Value) when TranslationForms.IsPlain => DocumentField.EntryValue,
			nameof(WordsEntry.Context) => DocumentField.EntryContext,
			nameof(WordsEntry.Comment) => DocumentField.EntryComment,
			_ => null,
		});
		if (e.PropertyName is nameof(SelectedEntry.Value) or nameof(SelectedEntry.Stale)) {
			RefreshBadges(SelectedKeyNode);
		}
		if (e.PropertyName is nameof(SelectedEntry.Value)) {
			AffectProperty(nameof(EntryText));
			AffectProperty(nameof(EntryHint));
		}
		Edited?.Invoke();
	}

	//the selection's text fields as they last stood (SPEC: Undo → Fields): a property
	//change does not say what it replaced
	private readonly Dictionary<DocumentField, string> texts = [];

	private void Remember(params DocumentField[] fields) {
		foreach (DocumentField field in fields) {
			texts[field] = TextOf(field) ?? "";
		}
	}

	//a value as its box shows it: the plain value or the form picked
	private string? TextOf(DocumentField field) => field switch {
		DocumentField.DefaultValue => SelectedKey is null ? null : DefaultText,
		DocumentField.KeyContext => SelectedKey?.Context,
		DocumentField.KeyComment => SelectedKey?.Comment,
		DocumentField.EntryValue => SelectedEntry is null ? null : EntryText,
		DocumentField.EntryContext => SelectedEntry?.Context,
		DocumentField.EntryComment => SelectedEntry?.Comment,
		DocumentField.CommentText => SelectedOrganizer?.Text,
		_ => null,
	};

	//a field of the selection changed: reported with what it held, an entry's field in the
	//selected language, a value in the form its pane shows
	private void Report(DocumentField? field) {
		if (field is not { } changed || SelectedKeyNode is not { } node) {
			return;
		}
		string before = texts.GetValueOrDefault(changed, "");
		string after = TextOf(changed) ?? "";
		texts[changed] = after;
		string? language = changed is DocumentField.EntryValue or DocumentField.EntryContext or DocumentField.EntryComment ? SelectedLanguage.Code : null;
		string? form = FormsOf(changed) is { IsPlain: false } pane ? pane.Form : null;
		FieldEdited?.Invoke(new FieldEdit(NodeRef.Of(node), language, changed, before, after, form));
	}

	//Filters
	/// <summary>The translator's work queue: keys stale in the selected language.</summary>
	public bool IsStaleFilter { get; set => Filter(ref field, value); }
	/// <summary>The programmer's: keys a translator raised a hand on.</summary>
	public bool NeedsReviewFilter { get; set => Filter(ref field, value); }
	/// <summary>Keys wanting words in the default, or in the selected language where their file registers it.</summary>
	public bool MissingFilter { get; set => Filter(ref field, value); }
	public string SearchFilterText { get; set => Filter(ref field, value); } = "";

	//a filter that changed re-runs the pass
	private void Filter<T>(ref T field, T value, [CallerMemberName] string propertyName = "") {
		if (ChangeProperty(ref field, value, propertyName)) {
			ApplyFilters();
		}
	}
	/// <summary>True while any filter narrows the tree.</summary>
	public bool IsFiltering => IsStaleFilter || NeedsReviewFilter || MissingFilter || SearchFilterText != "";
	/// <summary>How many rows the filters hide.</summary>
	public int HiddenCount { get; private set => ChangeProperty(ref field, value); }

	public void ClearFilters() {
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		MissingFilter = false;
	}

	public IEnumerable<KeyNode> AllNodes => KeyNodes.SelectMany(root => root.SelfAndDescendants());

	public void ApplyFilters() {
		foreach (KeyNode node in AllNodes) {
			node.IsVisible = PassesVisibilityFilters(node);
		}
		foreach (KeyNode node in AllNodes) {
			if (!node.IsVisible) {
				node.IsVisible = EnsureVisibleDescendant(node);
			}
		}
		//where Back or Forward arrived shows through the filters, and the path to it
		if (exempt is not null && exempt == SelectedKeyNode) {
			for (KeyNode? node = exempt; node is not null; node = node.Parent) {
				node.IsVisible = true;
			}
		}
		HiddenCount = AllNodes.Count(node => !node.IsVisible);
		AffectProperty(nameof(IsFiltering));
		//a selection the filter hid moves up to the nearest row still showing
		if (SelectedKeyNode is { IsVisible: false } hidden) {
			KeyNode? shown = hidden.Parent;
			while (shown is { IsVisible: false }) {
				shown = shown.Parent;
			}
			Select(shown);
		}
	}

	/// <summary>Selects <paramref name="node"/> (or nothing), the way a click would.</summary>
	public void Select(KeyNode? node) {
		if (SelectedKeyNode is { } previous && previous != node) {
			previous.IsSelected = false;
		}
		node?.IsSelected = true;
		SelectedKeyNode = node;
	}

	private bool PassesVisibilityFilters(KeyNode node) {
		bool passesFilter = true;

		if (IsStaleFilter) {
			passesFilter &= node.IsStale;
		}
		if (NeedsReviewFilter) {
			passesFilter &= node.NeedsReview;
		}
		if (MissingFilter) {
			passesFilter &= node.EmptyValue;
		}
		if (!string.IsNullOrEmpty(SearchFilterText)) {
			passesFilter &= Matches(node, SearchFilterText);
		}

		return passesFilter;
	}

	//what a translator searches for: a name, the words in the default and the
	//selected language, and the notes around them; for a comment row, its text
	//alone — its label is a synthetic marker, not something anyone typed
	private bool Matches(KeyNode node, string text) {
		const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
		if (node is OrganizerNode organizer) {
			return organizer.Text.Contains(text, ignoreCase);
		}
		if (node.FullLabel.Contains(text, ignoreCase)) {
			return true;
		}
		if (!session.Keys.TryGetValue(node.FullLabel, out var key)) {
			return false;
		}
		WordsEntry? entry = key.Entries.GetValueOrDefault(SelectedLanguage.Code);
		return key.DefaultValue.Contains(text, ignoreCase)
			|| key.Forms.Values.Any(form => form.Contains(text, ignoreCase))
			|| key.Context.Contains(text, ignoreCase)
			|| key.Comment.Contains(text, ignoreCase)
			|| (entry is not null && (entry.Value.Contains(text, ignoreCase) || entry.Forms.Values.Any(form => form.Contains(text, ignoreCase))
				|| entry.Context.Contains(text, ignoreCase) || entry.Comment.Contains(text, ignoreCase)));
	}

	private static bool EnsureVisibleDescendant(KeyNode node) {
		if (node.IsVisible) return true;
		foreach (var child in node.Children) {
			if (EnsureVisibleDescendant(child)) {
				node.IsVisible = true;
				return true;
			}
		}
		return false;
	}

	//Badges: computed from the document, for the selected language, in one pass
	public void RefreshBadges() {
		foreach (KeyNode root in KeyNodes) {
			WordsFile? file = session.FileOf(root.FullLabel);
			foreach (KeyNode node in root.SelfAndDescendants()) {
				RefreshBadges(node, file);
			}
		}
		//every path that changes the language table passes here
		RefreshFileLanguages();
		RaiseDefault();
	}

	public void RefreshBadges(KeyNode node) => RefreshBadges(node, session.FileOf(node.Root.FullLabel));

	private void RefreshBadges(KeyNode node, WordsFile? file) {
		if (node is OrganizerNode) {
			return;
		}
		if (!session.Keys.TryGetValue(node.FullLabel, out var key)) {
			node.IsConstant = false;
			node.NeedsReview = false;
			node.IsStale = false;
			node.IsOverwritten = false;
			node.EmptyValue = false;
			return;
		}
		string code = SelectedLanguage.Code;
		node.IsConstant = key.IsConstant;
		node.NeedsReview = key.NeedsReview;
		node.IsStale = key.HasStaleValue(code);
		node.IsOverwritten = key.HasRegionalOverride(code);
		//a key wanting words in the default, or in the selected language where its
		//file registers that language (listed or !-hidden), reads emphasized; a file
		//that never declared the language has no gap to show, nor one whose default
		//speaks it, since its empty entries fall back to the default (SPEC: Badges)
		//a plural key also wants every form its languages count by, bar the optional ones;
		//a language with no words of its own misses its value, and the default's forms
		//stand in until it has some (SPEC: Plural forms → Badges); the command line's
		//list --missing reads the same rule
		node.EmptyValue = MissingWords.InDefault(key, file?.DefaultLanguage)
			|| (Wants(file, code) && MissingWords.InLanguage(key, code));
	}

	//a language the file registers (listed or !-hidden) wants words; one the file never
	//declared has no gap to show, nor one its default speaks, whose empty entries fall back
	private static bool Wants(WordsFile? file, string code)
		=> file is not null && MissingWords.Wants(file.Languages, file.DefaultLanguage, code);

	//only a leaf directly under a file may become a constant (SPEC: baseline pane)
	public static void UpdateCanBeConstant(KeyNode fileNode) {
		foreach (KeyNode node in fileNode.Descendants()) {
			node.CanBeConstant = node is not OrganizerNode && node.Parent == fileNode && node.Children.Count == 0;
		}
	}

	//Files
	/// <summary>Puts a loaded file in the tree; a reload takes the old node's place.</summary>
	public void Present(WordsFile file) {
		KeyNode node = KeyNode.From(KeyTree.Build(session, file));
		node.IsLibraryFile = file.IsLibrary;
		node.GripeCount = file.Errors.Count;
		if (file.Preamble != "") {
			//the preamble shows as an organizer pinned to the file's start; its text
			//is the file's own, which Save writes above the language table
			node.Children.Insert(0, new OrganizerNode($"{file.Label}.;preamble",
				() => file.Preamble,
				text => file.Preamble = text));
		}
		int existing = KeyNodes.FindIndex(root => root.FullLabel == file.Label);
		if (existing >= 0) {
			KeyNodes[existing] = node;
		}
		else {
			KeyNodes.Add(node);
		}
		if (SelectedKeyNode is not null && !KeyNodes.Contains(SelectedKeyNode.Root)) {
			SelectedKeyNode = null;
		}
		UpdateCanBeConstant(node);
		FollowLanguage();
		RefreshBadges();
		ApplyFilters();
	}

	/// <summary>Takes a file's node out of the tree, after the session let the file go.</summary>
	public void RemoveFile(KeyNode fileNode) {
		KeyNodes.Remove(fileNode);
		SelectedKeyNode = KeyNodes.FirstOrDefault();
		FollowLanguage();
		RefreshBadges();
	}

	/// <summary>The empty tree over the reset session.</summary>
	public void Clear() {
		KeyNodes.Clear();
		SelectedKeyNode = null;
		History.Clear();
		exempt = null;
		SelectedLanguage = KnownLanguages[0];
		RefreshFileLanguages();
		SearchFilterText = "";
		IsStaleFilter = false;
		NeedsReviewFilter = false;
		MissingFilter = false;
	}

	//the dropdown's entry may have been replaced or pruned: follow the code, never go without
	public void FollowLanguage()
		=> SelectedLanguage = KnownLanguages.FirstOrDefault(language => language.Code == SelectedLanguage.Code) ?? KnownLanguages[0];

	/// <summary>The file a tree node belongs to.</summary>
	public WordsFile FileOf(KeyNode node)
		=> session.FileOf(node.Root.FullLabel)
			?? throw new InvalidOperationException($"no loaded file for node {node.FullLabel}");

	/// <summary>The file node for a loaded file.</summary>
	public KeyNode NodeOf(WordsFile file)
		=> KeyNodes.FirstOrDefault(root => root.FullLabel == file.Label)
			?? throw new InvalidOperationException($"no tree for loaded file {file.Label}");

	//file nodes in tree order; later files win bare-reference lookups, like a
	//host app stacking dictionaries
	public IEnumerable<string> FileLabels => KeyNodes.Select(node => node.FullLabel);

	//Structure
	/// <summary>Adds an empty node under <paramref name="parent"/> and selects it.</summary>
	public KeyNode Add(KeyNode parent, string label) {
		KeyNode node = new(label, $"{parent.FullLabel}.{label}") {
			IsSelected = true,
		};
		parent.IsExpanded = true;
		parent.IsSelected = false;
		parent.Children.Add(node);
		UpdateCanBeConstant(parent.Root);
		SelectedKeyNode = node;
		return node;
	}

	/// <summary>
	///     Takes a node out of the tree and selects its parent. A removed key leaves
	///     any comment above it standing; on the next load the comment anchors to
	///     whatever block follows it.
	/// </summary>
	public void Remove(KeyNode node) {
		KeyNode? parent = node.Parent;
		KeyNode root = node.Root;
		parent?.Children.Remove(node);
		UpdateCanBeConstant(root);
		SelectedKeyNode = parent;
	}

	/// <summary>
	///     Selects the comment standing ahead of <paramref name="node"/>, inserting a
	///     blank one if there is none. Returns true when one was inserted.
	/// </summary>
	public bool CommentAhead(KeyNode node) {
		if (node.Parent is not { } parent) {
			return false;
		}
		int index = parent.Children.IndexOf(node);
		bool inserted = false;
		KeyNode select;
		if (index > 0 && parent.Children[index - 1] is OrganizerNode existing) {
			select = existing;
		}
		else {
			select = new CommentNode($"{node.FullLabel}.;comment");
			parent.Children.Insert(index, select);
			inserted = true;
		}
		node.IsSelected = false;
		select.IsSelected = true;
		SelectedKeyNode = select;
		return inserted;
	}
}
