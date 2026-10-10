using PatTech.Localization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

/// <summary>
///     The main window: the document (<see cref="Session"/>), the tree that
///     presents it (<see cref="Tree"/>), what each button does, and whether there
///     is anything to save. Commands read the selection off the tree, ask the
///     session, and let the tree follow; processing lives in the session and
///     <see cref="WordsOperations"/>.
/// </summary>
public class MainWindowViewModel : ViewModelSaveBase {
	public WordsSession Session { get; } = new();
	public TreeViewModel Tree { get; }
	public KeyDrag KeyDrag { get; }
	/// <summary>The formats the editor imports from and exports to (SPEC: Import and export).</summary>
	public WordsFormats Formats { get; }

	//Wordsmith's own language (SPEC: Wordsmith's own words): the file's menu, and
	//the entry the current language reads in. Picking another is a request the
	//app answers with a restart, so the menu keeps showing the language in use
	public IReadOnlyList<KeyValuePair<string, string>> UiLanguages => EditorWords.Languages;
	public string? UiLanguage {
		get => EditorWords.MenuCode(EditorWords.Current);
		set {
			if (value is not null && value != UiLanguage) {
				UiLanguageRequested?.Invoke(value);
			}
			AffectProperty(nameof(UiLanguage));
		}
	}
	/// <summary>The user picked another language for Wordsmith; it takes effect on the restart the app arranges.</summary>
	public event Action<string>? UiLanguageRequested;

	//Previews: rendered the way a host app would show the selected key, kept
	//current while shown, each with what went wrong along the way. The default
	//pane goes by the file's own settings, the translation pane by the selected
	//language's layered over them
	public bool ShowDefaultPreview { get; set => _ = ChangeProperty(ref field, value) && RenderPreviews() && Commands.Refresh(); }
	public bool ShowLocalizationPreview { get; set => _ = ChangeProperty(ref field, value) && RenderPreviews() && Commands.Refresh(); }
	public PreviewPane DefaultPreview { get; } = new();
	public PreviewPane TranslationPreview { get; } = new();

	//the light beside the language (SPEC: the translation pane): Windows has no
	//dictionary for it, so the speller checks its boxes against nothing, silently
	public bool SpellCheckerMissing => !spellCheckers(Tree.SelectedLanguage.Code);
	[Localized]
	public string SpellCheckerNote => Words.Known.Format("main.no-spell-checker", Tree.SelectedLanguage.DisplayName);
	/// <summary>Where the runtime's gripes go: heard by whichever render is under way, dropped otherwise.</summary>
	public static GripeCollector Gripes { get; } = new();

	static MainWindowViewModel() {
		//the one process-wide logger; the shared markdown parsers forward here too
		Words.Logger = Gripes;
	}

	//Commands
	public ICommand LoadFileCommand { get; }
	public ICommand ImportCommand { get; }
	public ICommand ExportCommand { get; }
	public ICommand ResetCommand { get; }
	public ICommand SaveCommand { get; }
	public ICommand MergeFilesCommand { get; }
	public ICommand ManageLanguagesCommand { get; }
	public ICommand SettingsCommand { get; }
	public ICommand ShowGripesCommand { get; }
	public ICommand ShowFileGripesCommand { get; }
	public ICommand ClearFiltersCommand { get; }
	public ICommand TestParametersCommand { get; }
	public ICommand RemoveNodeCommand { get; }
	public ICommand RenameNodeCommand { get; }
	public ICommand AddNodeCommand { get; }
	public ICommand AddKeyCommand { get; }
	public ICommand AddOrganizerCommand { get; }
	public ICommand RemoveKeyCommand { get; }
	public ICommand StaleAllLanguagesCommand { get; }
	public ICommand ToggleStaleLanguageCommand { get; }
	public ICommand ToggleNeedsReviewCommand { get; }
	public ICommand ToggleConstantCommand { get; }
	public ICommand UndoCommand { get; }
	public ICommand RedoCommand { get; }
	public ICommand ExitCommand { get; }
	/// <summary>The command table (SPEC: Menu and toolbars): the menu, the toolbars and the context menu draw from it.</summary>
	public CommandTable Commands { get; }
	/// <summary>The user asked to leave: the window closes, asking about unsaved changes on the way.</summary>
	public event Action? ExitRequested;

	//Undo (SPEC: Undo)
	/// <summary>The edits made, to take back, and the edits taken back, to put back.</summary>
	public UndoStack UndoStack { get; }
	/// <summary>What the editing boxes undo and redo from (SPEC: Undo → Text boxes): the document's history, a run's steps at a time.</summary>
	public ITextHistory TextHistory { get; }
	/// <summary>
	///     A field edit was undone or redone: the window focuses its box, so the
	///     next keystroke lands where the change did, the selection where the step
	///     left it, or the caret at the end when no box said.
	/// </summary>
	public event Action<DocumentField, Selection?>? FieldFocusRequested;
	//above zero while a command or an undo changes the document: the fields' reports are not typing
	private int quiet;
	//the dirtiness a keystroke taking its run back returns to, for the tree's Edited
	//that follows the report
	private bool? typedBack;

	//how the editor asks and tells: modal windows in the app, a fake in tests
	public IDialogs Dialogs { get; }
	//whether this system can spell-check a language: Windows's answer, or a test's
	private readonly Func<string, bool> spellCheckers;
	//the clock typing reads: when a keystroke landed, and the stale stamps it writes
	private readonly TimeProvider time;

	public MainWindowViewModel(IDialogs? dialogs = null, WordsFormats? formats = null, Func<string, bool>? spellCheckers = null, TimeProvider? time = null) {
		Dialogs = dialogs ?? new WpfDialogs();
		this.time = time ?? TimeProvider.System;
		UndoStack = new UndoStack(this.time);
		TextHistory = new BoxHistory(this);
		Formats = formats ?? WordsFormats.BuiltIn();
		this.spellCheckers = spellCheckers ?? SpellCheckers.IsInstalled;
		Tree = new TreeViewModel(Session);
		Tree.Edited += () => {
			//a keystroke that took its run back leaves the document as the run found it
			IsDirty = typedBack ?? true;
			typedBack = null;
			RenderPreviews();
		};
		Tree.FieldEdited += OnFieldEdited;
		Tree.KeyNodes.CollectionChanged += (_, e) => {
			UpdateTitle();
			//a file in or out is a boundary (SPEC: Undo); a reorder is only precedence
			if (e.Action != NotifyCollectionChangedAction.Move) {
				UndoStack.Clear();
			}
		};
		LoadFileCommand = new DelegateCommand(DoLoadFiles);
		ImportCommand = new DelegateCommand(DoImport);
		ExportCommand = new DelegateCommand(DoExport, () => Tree.KeyNodes.Count > 0);
		ResetCommand = new DelegateCommand(DoReset);
		SaveCommand = new DelegateCommand(DoSave);
		MergeFilesCommand = new DelegateCommand(DoMergeFiles);
		ManageLanguagesCommand = new DelegateCommand(DoManageLanguages, () => LanguagesFile is not null);
		SettingsCommand = new DelegateCommand(DoSettings, () => Tree.SelectedFile is not null);
		ShowGripesCommand = new DelegateCommand<PreviewPane>(
			pane => Dialogs.Show(new GripesViewModel(Words.Known["gripes.preview"], pane.Gripes)),
			static pane => pane is { GripeCount: > 0 });
		//what the parser griped about loading a file, from the badge on its node
		ShowFileGripesCommand = new DelegateCommand<KeyNode>(
			node => {
				if (Session.FileOf(node.FullLabel) is { } file) {
					Dialogs.Show(new GripesViewModel(Words.Known.Format("gripes.file", file.Label), file.Errors));
				}
			},
			static node => node is { IsFile: true, GripeCount: > 0 });
		ClearFiltersCommand = new DelegateCommand(Tree.ClearFilters, () => Tree.IsFiltering);
		//the structure edits say whether they apply, so a menu or a key can offer them all
		RemoveNodeCommand = new DelegateCommand(DoRemoveNode, () => Tree.SelectedKeyNode is not null);
		RenameNodeCommand = new DelegateCommand(DoRenameNode, () => Tree.SelectedKeyNode is { IsFile: false } and not OrganizerNode);
		AddNodeCommand = new DelegateCommand(DoAddNode, () => Tree.SelectedKeyNode is { IsConstant: false } and not OrganizerNode);
		AddKeyCommand = new DelegateCommand(DoAddKey, () => Tree.SelectedKeyNode is { IsFile: false } and not OrganizerNode && Tree.SelectedKey is null);
		AddOrganizerCommand = new DelegateCommand(DoAddOrganizer, () => Tree.SelectedKeyNode is { IsFile: false } and not OrganizerNode);
		RemoveKeyCommand = new DelegateCommand(DoRemoveKey, () => Tree.SelectedKey is not null);
		StaleAllLanguagesCommand = new DelegateCommand(DoStaleAllLanguages, () => Tree.SelectedKey is { IsConstant: false });
		ToggleStaleLanguageCommand = new DelegateCommand(() => DoToggleStaleLanguage(Tree.SelectedLanguage.Code), () => Tree.SelectedEntry is not null);
		ToggleNeedsReviewCommand = new DelegateCommand(DoToggleNeedsReview, () => Tree.SelectedKey is not null);
		ToggleConstantCommand = new DelegateCommand(DoToggleConstant, () => Tree.SelectedKey is not null && Tree.SelectedKeyNode is { CanBeConstant: true });
		TestParametersCommand = new DelegateCommand(() => DoTestParameters(Tree.SelectedKey!), () => Tree.SelectedKey is not null);
		UndoCommand = new DelegateCommand(() => Step(undoing: true), () => UndoStack.DoneCount > 0);
		RedoCommand = new DelegateCommand(() => Step(undoing: false), () => UndoStack.CanRedo);
		ExitCommand = new DelegateCommand(() => ExitRequested?.Invoke());
		//the table last: it holds the commands above
		Commands = new CommandTable(this);
		//and only now the tree reaches the table: its toggles re-read on every change
		Tree.PropertyChanged += (_, e) => {
			if (e.PropertyName is nameof(TreeViewModel.SelectedKeyNode)) {
				UndoStack.EndRun(); //typing on another node is another run
			}
			if (e.PropertyName is nameof(TreeViewModel.SelectedKey) or nameof(TreeViewModel.SelectedEntry) or nameof(TreeViewModel.SelectedLanguage)
					or nameof(TreeViewModel.DefaultForms) or nameof(TreeViewModel.TranslationForms)) {
				RenderPreviews(); //a pane picked another form: its preview renders that one
			}
			if (e.PropertyName is nameof(TreeViewModel.SelectedLanguage)) {
				AffectProperty(nameof(SpellCheckerMissing));
				AffectProperty(nameof(SpellCheckerNote));
			}
			Commands.Refresh(); //a filter or a selection changed under a toggle's row
		};

		UpdateTitle();
		KeyDrag = new KeyDrag { Vm = this };
	}

	//the window title names the loaded files; TitleMarked stars it while dirty
	private void UpdateTitle()
		=> Title = Tree.KeyNodes.Count == 0 ? Words.Known["app.title"] : Words.Known.Format("app.title-files", string.Join(", ", Tree.FileLabels));

	//Load
	private void DoLoadFiles() {
		if (!Dialogs.TryOpenFiles(Words.Known["file.load-title"], Words.Known["file.filter"], out string[]? fileNames)) {
			return;
		}
		foreach (string fileName in fileNames) {
			LoadFile(fileName);
		}
	}

	public void LoadFile(string fileName) {
		try {
			Tree.Present(Session.Load(fileName));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Dialogs.Tell(Words.Known.Format("file.load-failed", fileName, ex.Message));
		}
	}

	public void LoadFile(TextReader reader, string fileName) => Tree.Present(Session.Load(reader, fileName));

	//Import (SPEC: Import and export): the extension picks the format, Discover
	//gathers the set the pick implies, and the set loads as a native ini
	private void DoImport() {
		if (!Dialogs.TryOpenFiles(Words.Known["file.import-title"], ImportFilter(), out string[]? fileNames)) {
			return;
		}
		//a set picked twice over (Strings.resx and Strings.fr.resx) imports once
		var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string fileName in fileNames) {
			ImportFile(fileName, imported);
		}
	}

	/// <summary>
	///     Imports the set <paramref name="fileName"/> implies, in the format its
	///     extension names, and presents it. The words.ini it becomes is where Save
	///     will write: one already on disk is asked about first. A set that is
	///     its own ini — importing ini is loading it — leaves the document clean;
	///     any other is unsaved work.
	/// </summary>
	/// <param name="fileName">The pick.</param>
	/// <param name="imported">The native paths imported so far, to skip a set already taken.</param>
	public void ImportFile(string fileName, HashSet<string>? imported = null) {
		if (Formats.ImporterFor(fileName) is not { } importer) {
			Dialogs.Tell(Words.Known.Format("tell.no-import-format", fileName));
			return;
		}
		try {
			IReadOnlyList<string> set = importer.Discover(fileName);
			if (set.Count == 0) {
				throw new FileNotFoundException(null, fileName);
			}
			string native = importer.NativePath(set);
			if (imported?.Add(Path.GetFullPath(native)) == false) {
				return;
			}
			bool isLoad = set.Any(path => SamePath(path, native));
			if (!isLoad && File.Exists(native) && !Dialogs.Confirm(Words.Known.Format("ask.import-over", native))) {
				return;
			}
			Tree.Present(Session.Import(importer, set));
			if (!isLoad) {
				MarkDirty();
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Dialogs.Tell(Words.Known.Format("file.load-failed", fileName, ex.Message));
		}
	}

	private static bool SamePath(string a, string b)
		=> string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

	//one filter entry per importer, then everything
	[return: Localized]
	private string ImportFilter()
		=> Words.Known.Format("file.import-filter", string.Join("|", Formats.Importers.Select(FormatFilter)));

	/// <summary>A file-dialog filter entry for <paramref name="format"/>: its name, in its own words, and its patterns.</summary>
	[return: Localized]
	public static string FormatFilter(IWordsFormat format) {
		string patterns = string.Join(";", format.Info.Extensions.Select(extension => "*" + extension));
		return Words.Known.Format("file.format-filter", Words.Known[format.Info.NameKey], patterns);
	}

	//Export: a dialog, since there is a plan and a loss to confirm first
	private void DoExport() {
		Dialogs.Show(new ExportViewModel(this));
	}

	//Reset
	private void DoReset() {
		if (Dialogs.Confirm(Words.Known["ask.reset"])) {
			ResetCore();
		}
	}

	public void ResetCore() {
		Session.Reset();
		Tree.Clear();
		IsDirty = false;
	}

	//Save
	private void DoSave() => Save();

	public override void Save() {
		int saved = 0;
		foreach (WordsFile file in Session.Files) {
			try {
				Session.Save(file, Tree.NodeOf(file));
				saved++;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.EncoderFallbackException) {
				Dialogs.Tell(Words.Known.Format("file.save-failed", file.Path, ex.Message));
			}
		}
		if (saved == Session.Files.Count) {
			IsDirty = false;
			UndoStack.Saved();
		}
		else if (saved > 0) {
			//some files on disk moved on and some didn't: no undo or redo comes back to that
			UndoStack.Saved(partly: true);
		}
	}

	/// <summary>
	///     The window wants to close. Clean, it may; dirty, the user chooses to
	///     save (and the close waits on every file being written), discard, or
	///     stay. True when the window may go.
	/// </summary>
	public bool TryClose() {
		if (!IsDirty) {
			return true;
		}
		switch (Dialogs.AskToSave(Words.Known["ask.save-before-close"])) {
			case CloseAnswer.Save:
				Save();
				return !IsDirty;
			case CloseAnswer.Discard:
				return true;
			default:
				return false;
		}
	}

	//Merge
	private void DoMergeFiles() {
		Dialogs.Show(new MergeControlViewModel(this));
	}

	//Languages
	/// <summary>
	///     The file the Language Manager edits (SPEC: Languages): the one the
	///     selection sits in, or the only one loaded; none while several are and
	///     nothing is selected, as a table is one file's.
	/// </summary>
	public WordsFile? LanguagesFile => Tree.SelectedFile ?? (Session.Files.Count == 1 ? Session.Files[0] : null);

	private void DoManageLanguages() {
		if (LanguagesFile is not { } file) {
			return;
		}
		Dialogs.Show(new LanguageManagerViewModel(this, file));
		Commands.Refresh(); //a language renamed in place: the choice rows re-read their labels
	}

	/// <summary>
	///     Changes the language table as one action (SPEC: Undo → Languages):
	///     <paramref name="change"/> works through the edit, which keeps each
	///     operation's inverse. A commit that merged two languages has none, and
	///     clears the stack instead.
	/// </summary>
	public void ChangeLanguages(Action<LanguagesEdit> change) => Perform(() => {
		var edit = new LanguagesEdit(Session);
		change(edit);
		if (!edit.Changed) {
			return null;
		}
		edit.Close();
		//the table changed under the tree: its badges and dropdown read it, the forms
		//count by its languages, and the previews format in them
		Tree.FollowLanguage();
		Tree.RefreshBadges();
		Tree.RevalidateForms();
		RenderPreviews();
		if (!edit.Merged) {
			return edit;
		}
		UndoStack.Clear();
		MarkDirty();
		return null;
	});

	//the settings are a dictionary's own; the dialog edits the file the selection
	//sits in, so a node must be selected to know which file that is. Its Okay is
	//one entry, the slots before and after
	private void DoSettings() {
		if (Tree.SelectedFile is not { } file) {
			return;
		}
		Perform(() => {
			var before = FileSettingsEdit.Slots.Of(file);
			Dialogs.Show(new SettingsViewModel(this, file));
			var after = FileSettingsEdit.Slots.Of(file);
			return after.Matches(before) ? null : new FileSettingsEdit(file.Label, before, after);
		});
		//the slots or the tables may have changed under the previews
		RenderPreviews();
	}

	//Structure edits
	private void DoRemoveNode() {
		if (Tree.SelectedKeyNode is not { } node) {
			return;
		}
		if (node is OrganizerNode organizer) {
			//deleting the organizer deletes the comment it presents
			Perform(() => {
				var removed = new NodeRemoved(Session, organizer);
				organizer.Text = "";
				Tree.Remove(organizer);
				return removed;
			});
			return;
		}
		if (node.IsFile) {
			if (Dialogs.Confirm(Words.Known["ask.remove-file"])) {
				RemoveFileNodeCore(node);
			}
			return;
		}
		//an empty node goes quietly; one carrying keys asks, since Delete is a keypress away
		int keys = Session.Keys.Keys.Count(label => label == node.FullLabel || label.StartsWith(node.FullLabel + ".", StringComparison.Ordinal));
		if (keys > 0 && !Dialogs.Confirm(keys == 1 ? Words.Known.Format("ask.remove-node-one", node.Label) : Words.Known.Format("ask.remove-node-many", node.Label, keys))) {
			return;
		}
		Perform(() => {
			var removed = new NodeRemoved(Session, node);
			Session.RemoveKeysUnder(node.FullLabel);
			Tree.Remove(node);
			return removed;
		});
	}

	public void RemoveFileNodeCore(KeyNode fileNode) {
		if (Session.FileOf(fileNode.FullLabel) is { } file) {
			Session.Unload(file);
		}
		Tree.RemoveFile(fileNode);
	}

	private void DoRenameNode() {
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.IsFile) {
			return;
		}
		Dialogs.Show(new KeyNameViewModel(this, Tree.SelectedKeyNode));
	}

	//a rename is a move that changes the last segment alone (SPEC: Undo → Structure)
	public void RenameNode(string newName) {
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.Parent is not { } parent) {
			return; //files keep the name of the file
		}
		KeyNode node = Tree.SelectedKeyNode;
		if (parent.Children.Any(sibling => sibling != node && sibling.Label == newName)) {
			Dialogs.Tell(Words.Known.Format("tell.node-exists", parent.FullLabel, newName));
			return;
		}
		//the marker is part of the key, not the name
		string marker = WordsOperations.LastSegment(node.FullLabel).StartsWith('$') ? "$" : "";
		string newFullLabel = $"{parent.FullLabel}.{marker}{newName}";
		Perform(() => {
			Place from = Place.Of(node);
			if (!Session.TryRename(node.FullLabel, newFullLabel, out var collisions)) {
				Dialogs.Tell(Words.Known.Format("tell.rename-collides", string.Join(", ", collisions)));
				return null;
			}
			node.Label = newName;
			node.Relabel(newFullLabel);
			return new Move(from, Place.Of(node), comment: false);
		});
	}

	private void DoAddNode() {
		if (Tree.SelectedKeyNode is null or OrganizerNode) {
			return;
		}
		Dialogs.Show(new KeyNameViewModel(this, null));
	}

	public void AddNode(string newName) {
		if (Tree.SelectedKeyNode is null or OrganizerNode) {
			return;
		}
		KeyNode parent = Tree.SelectedKeyNode;
		if (parent.Children.Any(child => child.Label == newName)) {
			Dialogs.Tell(Words.Known.Format("tell.node-exists", parent.FullLabel, newName));
			return;
		}
		Perform(() => new NodeAdded(Session, Tree.Add(parent, newName)));
	}

	private void DoAddKey() {
		//SPEC (The tree): a key can exist on any node except a file
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.IsFile) {
			return;
		}
		KeyNode node = Tree.SelectedKeyNode;
		Perform(() => {
			Session.AddKey(node.FullLabel);
			Tree.FollowSelectedKey();
			Tree.RefreshBadges(node);
			return new KeyAdded(node.FullLabel);
		});
	}

	private void DoAddOrganizer() {
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.IsFile) {
			return;
		}
		//the comment ahead is selected either way; only a new one is an edit
		KeyNode node = Tree.SelectedKeyNode;
		Perform(() => Tree.CommentAhead(node) ? new NodeAdded(Session, Tree.SelectedKeyNode!) : null);
	}

	private void DoRemoveKey() {
		if (Tree.SelectedKeyNode is not { } node) {
			return;
		}
		if (!Dialogs.Confirm(Words.Known.Format("ask.remove-key", node.Label))) {
			return;
		}
		Perform(() => {
			if (!Session.Keys.TryGetValue(node.FullLabel, out WordsKey? key)) {
				return null;
			}
			var removed = new KeyRemoved(key);
			Session.RemoveKey(node.FullLabel);
			Tree.FollowSelectedKey();
			Tree.RefreshBadges(node);
			return removed;
		});
	}

	//Flags
	private void DoStaleAllLanguages() {
		if (Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node) {
			return;
		}
		Perform(() => {
			string stamp = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
			var edit = new StaleAllEdit(key.BlockKey, key.Entries.ToDictionary(pair => pair.Key, pair => pair.Value.Stale), stamp);
			foreach (WordsEntry entry in key.Entries.Values) {
				entry.Stale = stamp;
			}
			Tree.RefreshBadges(node);
			return edit;
		});
	}

	private void DoToggleStaleLanguage(string? languageCode) {
		if (languageCode is null || Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node
				|| !key.Entries.TryGetValue(languageCode, out var entry)) {
			return;
		}
		Perform(() => {
			string? before = entry.Stale;
			entry.Stale = before is null ? DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture) : null;
			Tree.RefreshBadges(node);
			return new StaleEdit(key.BlockKey, languageCode, before, entry.Stale);
		});
	}

	private void DoToggleNeedsReview() {
		if (Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node) {
			return;
		}
		Perform(() => {
			key.NeedsReview = !key.NeedsReview;
			Tree.RefreshBadges(node);
			return new ReviewEdit(key.BlockKey, key.NeedsReview);
		});
	}

	private void DoToggleConstant() {
		if (Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node) {
			return;
		}
		bool makeConstant = !key.IsConstant;
		bool clearEntries = false;
		if (makeConstant && key.Entries.Values.Any(entry => !entry.IsEmpty())) {
			//a constant reads the same in every language: its translations go, and
			//that is the user's call to make
			if (!Dialogs.Confirm(Words.Known["ask.make-constant"])) {
				return;
			}
			clearEntries = true;
		}
		Perform(() => {
			string label = key.BlockKey;
			IReadOnlyDictionary<string, WordsEntry>? cleared = clearEntries
				? key.Entries.ToDictionary(pair => pair.Key, pair => new WordsEntry(pair.Value))
				: null;
			string? newKey = Session.SetConstant(label, makeConstant, clearEntries);
			if (newKey is null) {
				Dialogs.Tell(Words.Known.Format("tell.constant-exists", WordsOperations.SetConstantMarker(label, makeConstant)));
				return null;
			}
			node.Relabel(newKey);
			Tree.FollowSelectedKey();
			Tree.RefreshBadges(node);
			return new ConstantEdit(label, newKey, makeConstant, cleared);
		});
	}

	//the session is one entry, the parameters before and after
	private void DoTestParameters(WordsKey key) {
		Perform(() => {
			IReadOnlyList<WordsParameter> before = ParametersEdit.Copy(key);
			Dialogs.Show(new TestParametersViewModel(this, key));
			IReadOnlyList<WordsParameter> after = ParametersEdit.Copy(key);
			return ParametersEdit.Same(before, after) ? null : new ParametersEdit(key.BlockKey, before, after);
		});
		//the samples are what the previews format with
		RenderPreviews();
	}

	//Undo
	/// <summary>
	///     Runs one action on the document (SPEC: Undo → Recording):
	///     <paramref name="action"/> makes the change and returns its entry, or
	///     null when it changed nothing. While it runs the fields' reports are not
	///     typing; its entry goes on the stack, remembering whether the document
	///     was dirty before.
	/// </summary>
	public void Perform(Func<UndoEntry?> action) {
		bool wasDirty = IsDirty;
		UndoEntry? entry = Quietly(action);
		if (entry is not null) {
			UndoStack.Push(entry, wasDirty);
			MarkDirty();
		}
	}

	private T Quietly<T>(Func<T> action) {
		quiet++;
		try {
			return action();
		}
		finally {
			quiet--;
		}
	}

	//typing: a run of keystrokes in one field is one entry; a note raises the key's hand
	//as it is typed, and the default stamps the translations it leaves behind stale
	private void OnFieldEdited(FieldEdit edit) {
		typedBack = null;
		if (quiet > 0) {
			return;
		}
		bool wasDirty = IsDirty;
		if (edit.IsNote && edit.After.Trim() != "" && Tree.SelectedKey is { NeedsReview: false } key) {
			edit.RaisedReview = true;
			key.NeedsReview = true;
		}
		if (edit.Field == DocumentField.DefaultValue && Tree.SelectedKey is { } changed && changed.TranslationsToStale().ToList() is { Count: > 0 } codes) {
			string stamp = WordsKey.StaleStamp(time.GetLocalNow());
			foreach (string code in codes) {
				changed.Entries[code].Stale = stamp;
			}
			edit.Stamped(codes, stamp);
		}
		FieldEdit? open = UndoStack.NextUndo as FieldEdit;
		bool? back = UndoStack.Type(edit, wasDirty);
		//typed back to where it started: the stamps the run made go too
		if (back is not null && open is { Staled.Count: > 0 }) {
			Quietly(() => open.Apply(Session, Tree, 0));
		}
		typedBack = back;
	}

	//Undo and Redo (SPEC: Undo → Navigate first): a change out of view is gone to and
	//applied on the next call; once applied, the selection follows it and a field edit's
	//box takes the focus. An editing box steps through a typing run (stepwise), and every
	//other caller takes it whole
	private void Step(bool undoing, bool stepwise = false) {
		UndoStack.EndRun();
		if ((undoing ? UndoStack.NextUndo : UndoStack.NextToPutBack) is not { } entry) {
			return;
		}
		if (entry.Site(undoing)?.Resolve(Tree) is { } site && !InView(site, entry)) {
			Tree.Show(site);
			if (entry.Language is { } code && Session.Languages.Find(code) is { } language) {
				Tree.SelectedLanguage = language;
			}
			//last, as the key and the language have each had their say on the pick
			if (entry is FieldEdit { Field: DocumentField.EntryValue, Form: var entryForm }) {
				Tree.PickTranslationForm(entryForm ?? FormPane.Plain);
			}
			else if (entry is FieldEdit { Field: DocumentField.DefaultValue, Form: var defaultForm }) {
				Tree.PickDefaultForm(defaultForm ?? FormPane.Plain);
			}
			return;
		}
		FieldEdit? edit = entry as FieldEdit;
		int standing = edit is null ? 0 : stepwise ? edit.Standing + (undoing ? -1 : 1) : undoing ? 0 : edit.Steps;
		//undone to its last step, or put back from the entries undone; a run stepped part way stays
		bool crosses = undoing ? edit is null || standing == 0 : entry != UndoStack.NextUndo;
		NodeRef? follow;
		try {
			follow = Quietly(() => edit is null ? entry.Apply(Session, Tree, undoing) : edit.Apply(Session, Tree, standing));
		}
		catch {
			//the document may be half changed: no entry can be trusted to step from there
			UndoStack.Clear();
			MarkDirty();
			Commands.Refresh();
			throw;
		}
		if (crosses) {
			UndoStack.Take(undoing);
		}
		//the entry may have changed anything the tree reads off the document
		Tree.FollowLanguage();
		Tree.FollowSelectedKey();
		if (entry is LanguagesEdit) {
			//the same key and language, but the rules or forms a pick stood on may be gone
			Tree.RevalidateForms();
		}
		foreach (KeyNode root in Tree.KeyNodes) {
			TreeViewModel.UpdateCanBeConstant(root);
		}
		Tree.RefreshBadges();
		if (follow?.Resolve(Tree) is { } node) {
			Tree.Show(node);
		}
		else {
			Tree.ApplyFilters();
		}
		RenderPreviews();
		Commands.Refresh();
		IsDirty = edit?.DirtyAt(standing) ?? (undoing ? entry.DirtyBefore : entry.DirtyAfter);
		if (edit is not null) {
			FieldFocusRequested?.Invoke(edit.Field, edit.SelectionAt(standing, undoing));
		}
	}

	//the editing boxes' side of the history: the document's, a step at a time
	private sealed class BoxHistory(MainWindowViewModel vm) : ITextHistory {
		public bool CanUndo => vm.UndoStack.DoneCount > 0;
		public bool CanRedo => vm.UndoStack.CanRedo;
		public void Undo() => vm.Step(undoing: true, stepwise: true);
		public void Redo() => vm.Step(undoing: false, stepwise: true);
		public void Typed(string text, Selection found, Selection left) => vm.UndoStack.Typed(text, found, left);
	}

	//the node selected and, for an entry tied to a language, that language showing,
	//and for a value, the form it was typed into
	private bool InView(KeyNode site, UndoEntry entry)
		=> Tree.SelectedKeyNode == site && (entry.Language is null || entry.Language == Tree.SelectedLanguage.Code)
			&& (entry is not FieldEdit edit || Tree.FormsOf(edit.Field) is not { } pane || pane.Form == (edit.Form ?? FormPane.Plain));

	//Previews
	private bool RenderPreviews() {
		WordsKey? key = Tree.SelectedKey;
		WordsFile? file = Tree.SelectedFile;
		if (key is null || file is null || !ShowDefaultPreview) {
			DefaultPreview.Clear();
		}
		else {
			Render(DefaultPreview, key, null, file.DefaultLanguage, Tree.DefaultForms, Session.SettingsFor(file));
		}
		if (key is null || file is null || !ShowLocalizationPreview || Tree.SelectedEntry is null) {
			TranslationPreview.Clear();
		}
		else {
			Render(TranslationPreview, key, Tree.SelectedLanguage.Code, Tree.SelectedLanguage.Code, Tree.TranslationForms, Session.SettingsFor(file, Tree.SelectedLanguage.Code));
		}
		return true;
	}

	//every loaded file in tree order resolves {>references} and {$constants}, like a
	//host app stacking dictionaries; the pane's form reads as the runtime picks it for
	//a count in that form. The samples then go through the same formatting the host
	//applies, selectors and all, in the language's culture where there is one, and the
	//default in the one it is written in. A sample that will not format keeps the raw
	//text and heads the pane's gripes; what Words complained about on the way, and
	//what is wrong with the rules, follow
	private void Render(PreviewPane pane, WordsKey key, string? languageCode, string? cultureCode, FormPane forms, ProjectSettings settings) {
		List<string> gripes = [];
		string text;
		using (Gripes.Listen(gripes)) {
			IWordsProvider provider = PaneProvider(key, languageCode, forms);
			text = Words.RenderKey(provider, key.BlockKey);
			if (key.Parameters.Count != 0) {
				CultureInfo culture = WordsOperations.CultureFor(cultureCode);
				try {
					text = WordsOperations.FormatSample(new CulturedWords(provider, culture) { Language = forms.Language }, key, culture);
				}
				catch (Exception ex) when (ex is FormatException or OverflowException) {
					gripes.Insert(0, ex.Message);
				}
			}
		}
		pane.Show(text, settings, gripes.Concat(settings.Errors), Gripes);
	}

	//the words a pane reads: every loaded file in tree order, the pane's form read as the key
	private IWordsProvider PaneProvider(WordsKey key, string? languageCode, FormPane forms) {
		IWordsProvider provider = Session.Provider(Tree.FileLabels, languageCode);
		return forms.IsPlain ? provider : new FormAsKey(provider, key.BlockKey, Words.FormKey(provider, forms.Language, key.BlockKey, forms.Form));
	}

	/// <summary>
	///     The default as the baseline pane shows it, formatted with the key's
	///     samples the way the default preview formats it, selectors and all: Test
	///     Parameters' result. Throws <see cref="FormatException"/> or
	///     <see cref="OverflowException"/> where a sample will not format.
	/// </summary>
	internal string FormatDefaultSample(WordsKey key) {
		CultureInfo culture = WordsOperations.CultureFor(Session.FileOfKey(key.BlockKey)?.DefaultLanguage);
		return WordsOperations.FormatSample(new CulturedWords(PaneProvider(key, null, Tree.DefaultForms), culture) { Language = Tree.DefaultForms.Language }, key, culture);
	}

	//a provider whose key reads as one of its entries: the form a pane shows
	private sealed class FormAsKey(IWordsProvider inner, string key, string form) : IWordsProvider {
		public string this[string name] => inner[name == key ? form : name];
		public bool ContainsKey(string name) => inner.ContainsKey(name == key ? form : name);
		public bool TryGetValue(string name, [MaybeNullWhen(false), Localized] out string value) => inner.TryGetValue(name == key ? form : name, out value);
	}

	/// <summary>
	///     A hyperlink in a preview was clicked. The project's hyperlink rules
	///     decide (SPEC: Markdown previews): a decode rule rewrites the target
	///     first, then <c>shellexec</c> confirms and hands it to the shell while
	///     <c>popup</c> only reports it — web and mail links launch by default,
	///     anything else is the host app's business and is shown.
	/// </summary>
	public void FollowLink(Uri uri) {
		ProjectSettings settings = Tree.SelectedFile is { } file ? Session.SettingsFor(file, Tree.SelectedLanguage.Code) : ProjectSettings.Empty;
		string target = settings.Link(uri, out LinkMode mode);
		string destination = target == uri.OriginalString ? target : Words.Known.Format("tell.link-from", target, uri.OriginalString);
		if (mode == LinkMode.ShellExec) {
			if (Dialogs.Confirm(Words.Known.Format("ask.follow-link", destination))) {
				Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
			}
		}
		else {
			Dialogs.Tell(Words.Known.Format("tell.link", destination));
		}
	}
}
