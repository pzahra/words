using PatTech.Localization;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
	public KeyDragDropHandler KeyDragDropHandler { get; }

	//Previews: rendered the way a host app would show the selected key, kept
	//current while shown, each with what went wrong along the way. The default
	//pane goes by the file's own settings, the translation pane by the selected
	//language's layered over them
	public bool ShowDefaultPreview { get; set => _ = ChangeProperty(ref field, value) && RenderPreviews(); }
	public bool ShowLocalizationPreview { get; set => _ = ChangeProperty(ref field, value) && RenderPreviews(); }
	public PreviewPane DefaultPreview { get; } = new();
	public PreviewPane TranslationPreview { get; } = new();
	/// <summary>Where the runtime's gripes go: heard by whichever render is under way, dropped otherwise.</summary>
	public static GripeCollector Gripes { get; } = new();

	static MainWindowViewModel() {
		//the one process-wide logger; the shared markdown parsers forward here too
		Words.Logger = Gripes;
	}

	//Commands
	public ICommand LoadFileCommand { get; }
	public ICommand ResetCommand { get; }
	public ICommand SaveCommand { get; }
	public ICommand MergeFilesCommand { get; }
	public ICommand ManageLanguagesCommand { get; }
	public ICommand SettingsCommand { get; }
	public ICommand ShowGripesCommand { get; }
	public ICommand TestParametersCommand { get; }
	public ICommand RemoveLocalizationKeyAndNodeCommand { get; }
	public ICommand RenameLocalizationKeyAndNodeCommand { get; }
	public ICommand AddLocalizationKeyNodeCommand { get; }
	public ICommand AddLocalizationKeyCommand { get; }
	public ICommand AddOrganizerCommand { get; }
	public ICommand RemoveLocalizationKeyCommand { get; }
	public ICommand StaleAllLanguagesCommand { get; }
	public ICommand ToggleStaleLanguageCommand { get; }
	public ICommand ToggleLocalizationKeyNeedsReviewCommand { get; }
	public ICommand ToggleLocalizationKeyIsConstantCommand { get; }

	//how the editor asks and tells: modal windows in the app, a fake in tests
	public IDialogs Dialogs { get; }

	public MainWindowViewModel(IDialogs? dialogs = null) {
		Dialogs = dialogs ?? new WpfDialogs();
		Tree = new TreeViewModel(Session);
		Tree.Edited += () => {
			IsDirty = true;
			RenderPreviews();
		};
		Tree.PropertyChanged += (_, e) => {
			if (e.PropertyName is nameof(TreeViewModel.SelectedKey) or nameof(TreeViewModel.SelectedEntry) or nameof(TreeViewModel.SelectedLanguage)) {
				RenderPreviews();
			}
		};
		LoadFileCommand = new DelegateCommand(DoLoadFiles);
		ResetCommand = new DelegateCommand(DoReset);
		SaveCommand = new DelegateCommand(DoSave);
		MergeFilesCommand = new DelegateCommand(DoMergeFiles);
		ManageLanguagesCommand = new DelegateCommand(DoManageLanguages);
		SettingsCommand = new DelegateCommand(DoSettings, () => Tree.SelectedFile is not null);
		ShowGripesCommand = new DelegateCommand<PreviewPane>(
			pane => Dialogs.Show(new GripesViewModel("Preview gripes", pane.Gripes)),
			static pane => pane is { GripeCount: > 0 });
		RemoveLocalizationKeyAndNodeCommand = new DelegateCommand(DoRemoveLocalizationKeyAndNode);
		RenameLocalizationKeyAndNodeCommand = new DelegateCommand(DoRenameNode);
		AddLocalizationKeyNodeCommand = new DelegateCommand(DoAddLocalizationKeyNode);
		AddLocalizationKeyCommand = new DelegateCommand(DoAddLocalizationKey);
		AddOrganizerCommand = new DelegateCommand(DoAddOrganizer);
		RemoveLocalizationKeyCommand = new DelegateCommand(DoRemoveLocalizationKey);
		StaleAllLanguagesCommand = new DelegateCommand(DoStaleAllLanguages);
		ToggleStaleLanguageCommand = new DelegateCommand<string>(DoToggleStaleLanguage);
		ToggleLocalizationKeyNeedsReviewCommand = new DelegateCommand(DoToggleKeyNeedsReview);
		ToggleLocalizationKeyIsConstantCommand = new DelegateCommand(DoToggleLocalizationKeyIsConstant);
		TestParametersCommand = new DelegateCommand<ObservableCollection<WordsParameter>>(DoTestParameters);

		Title = "Wordsmith Editor";
		KeyDragDropHandler = new KeyDragDropHandler() { MainWindow = this };
	}

	//Load
	private void DoLoadFiles() {
		if (!Dialogs.TryOpenFiles("Load", "INI file (*.ini)|*.ini|All files (*.*)|*.*", out string[]? fileNames)) {
			return;
		}
		foreach (string fileName in fileNames) {
			LoadFile(fileName);
		}
	}

	public void LoadFile(string fileName) {
		try {
			using var reader = File.OpenText(fileName);
			LoadFile(reader, fileName);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Dialogs.Tell($"Could not load {fileName}:\n{ex.Message}");
		}
	}

	public void LoadFile(TextReader reader, string fileName) => Tree.Present(Session.Load(reader, fileName));

	//Reset
	private void DoReset() {
		if (Dialogs.Confirm("Reset the session? All unsaved changes will be lost.")) {
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
		bool allSaved = true;
		foreach (WordsFile file in Session.Files) {
			try {
				Session.Save(file, Tree.NodeOf(file));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Dialogs.Tell($"Could not save {file.Path}:\n{ex.Message}");
				allSaved = false;
			}
		}
		if (allSaved) {
			IsDirty = false;
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
		switch (Dialogs.AskToSave("Do you want to save changes to this file before closing?")) {
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
	private void DoManageLanguages() {
		Dialogs.Show(new LanguageManagerViewModel(this));
	}

	//the settings are a dictionary's own; the dialog edits the file the selection
	//sits in, so a node must be selected to know which file that is
	private void DoSettings() {
		if (Tree.SelectedFile is not { } file) {
			return;
		}
		Dialogs.Show(new SettingsViewModel(this, file));
		//the slots or the tables may have changed under the previews
		RenderPreviews();
	}

	//Structure edits
	private void DoRemoveLocalizationKeyAndNode() {
		if (Tree.SelectedKeyNode is not { } node) {
			return;
		}
		if (node is OrganizerNode organizer) {
			//deleting the organizer deletes the comment it presents
			organizer.Text = "";
			Tree.Remove(organizer);
			IsDirty = true;
			return;
		}
		if (node.IsFile) {
			if (Dialogs.Confirm("Remove the selected file? All unsaved changes will be lost.")) {
				RemoveFileNodeCore(node);
			}
			return;
		}
		Session.RemoveKeysUnder(node.FullLabel);
		Tree.Remove(node);
		IsDirty = true;
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

	public void RenameLocalizationKeyAndNode(string newName) {
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.Parent is not { } parent) {
			return; //files keep the name of the file
		}
		KeyNode node = Tree.SelectedKeyNode;
		if (parent.Children.Any(sibling => sibling != node && sibling.Label == newName)) {
			Dialogs.Tell($"'{parent.FullLabel}' already has a node named '{newName}'.");
			return;
		}
		//the marker is part of the key, not the name
		string marker = WordsOperations.LastSegment(node.FullLabel).StartsWith('$') ? "$" : "";
		string newFullLabel = $"{parent.FullLabel}.{marker}{newName}";
		if (!Session.TryRename(node.FullLabel, newFullLabel, out var collisions)) {
			Dialogs.Tell($"Cannot rename: {string.Join(", ", collisions)} already exist.");
			return;
		}
		node.Label = newName;
		node.Relabel(newFullLabel);
		IsDirty = true;
	}

	private void DoAddLocalizationKeyNode() {
		if (Tree.SelectedKeyNode is null or OrganizerNode) {
			return;
		}
		Dialogs.Show(new KeyNameViewModel(this, null));
	}

	public void AddLocalizationKeyNode(string newName) {
		if (Tree.SelectedKeyNode is null or OrganizerNode) {
			return;
		}
		KeyNode parent = Tree.SelectedKeyNode;
		if (parent.Children.Any(child => child.Label == newName)) {
			Dialogs.Tell($"'{parent.FullLabel}' already has a node named '{newName}'.");
			return;
		}
		Tree.Add(parent, newName);
		IsDirty = true;
	}

	private void DoAddLocalizationKey() {
		//SPEC (The tree): a key can exist on any node except a file
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.IsFile) {
			return;
		}
		Session.AddKey(Tree.SelectedKeyNode.FullLabel);
		Tree.FollowSelectedKey();
		Tree.RefreshBadges(Tree.SelectedKeyNode);
		IsDirty = true;
	}

	private void DoAddOrganizer() {
		if (Tree.SelectedKeyNode is null or OrganizerNode || Tree.SelectedKeyNode.IsFile) {
			return;
		}
		if (Tree.CommentAhead(Tree.SelectedKeyNode)) {
			IsDirty = true;
		}
	}

	private void DoRemoveLocalizationKey() {
		if (Tree.SelectedKeyNode is not { } node) {
			return;
		}
		Session.RemoveKey(node.FullLabel);
		Tree.FollowSelectedKey();
		Tree.RefreshBadges(node);
		IsDirty = true;
	}

	//Flags
	private void DoStaleAllLanguages() {
		if (Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node) {
			return;
		}
		string stamp = DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture);
		foreach (WordsEntry entry in key.Entries.Values) {
			entry.Stale = stamp;
		}
		Tree.RefreshBadges(node);
		IsDirty = true;
	}

	private void DoToggleStaleLanguage(string? languageCode) {
		if (languageCode is null || Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node
				|| !key.Entries.TryGetValue(languageCode, out var entry)) {
			return;
		}
		entry.Stale = entry.Stale is null ? DateTimeOffset.Now.ToString(CultureInfo.InvariantCulture) : null;
		Tree.RefreshBadges(node);
		IsDirty = true;
	}

	private void DoToggleKeyNeedsReview() {
		if (Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node) {
			return;
		}
		key.NeedsReview = !key.NeedsReview;
		Tree.RefreshBadges(node);
		IsDirty = true;
	}

	private void DoToggleLocalizationKeyIsConstant() {
		if (Tree.SelectedKey is not { } key || Tree.SelectedKeyNode is not { } node) {
			return;
		}
		bool makeConstant = !key.IsConstant;
		bool clearEntries = false;
		if (makeConstant && key.Entries.Values.Any(entry => !entry.IsEmpty())) {
			//a constant reads the same in every language: its translations go, and
			//that is the user's call to make
			if (!Dialogs.Confirm("Make this key a constant? Its translations will be removed.")) {
				return;
			}
			clearEntries = true;
		}
		string? newKey = Session.SetConstant(key.BlockKey, makeConstant, clearEntries);
		if (newKey is null) {
			Dialogs.Tell($"A key named {WordsOperations.SetConstantMarker(key.BlockKey, makeConstant)} already exists.");
			return;
		}
		node.Relabel(newKey);
		Tree.FollowSelectedKey();
		Tree.RefreshBadges(node);
		IsDirty = true;
	}

	private void DoTestParameters(ObservableCollection<WordsParameter> parameters) {
		Dialogs.Show(new TestParametersViewModel(this, parameters));
		//the samples are what the previews format with
		RenderPreviews();
	}

	//Previews
	private bool RenderPreviews() {
		WordsKey? key = Tree.SelectedKey;
		WordsFile? file = Tree.SelectedFile;
		if (key is null || file is null || !ShowDefaultPreview) {
			DefaultPreview.Clear();
		}
		else {
			Render(DefaultPreview, key, null, Session.SettingsFor(file));
		}
		if (key is null || file is null || !ShowLocalizationPreview || Tree.SelectedEntry is null) {
			TranslationPreview.Clear();
		}
		else {
			Render(TranslationPreview, key, Tree.SelectedLanguage.Code, Session.SettingsFor(file, Tree.SelectedLanguage.Code));
		}
		return true;
	}

	//every loaded file in tree order resolves {>references} and {$constants}, like a
	//host app stacking dictionaries; the samples then go through the same formatting
	//the host applies, in the language's culture where there is one. A sample that
	//will not format keeps the raw text and heads the pane's gripes; what Words
	//complained about on the way, and what is wrong with the rules, follow
	private void Render(PreviewPane pane, WordsKey key, string? languageCode, ProjectSettings settings) {
		List<string> gripes = [];
		string text;
		using (Gripes.Listen(gripes)) {
			text = Words.RenderKey(Session.Provider(Tree.FileLabels, languageCode), key.BlockKey);
			if (key.Parameters.Count != 0) {
				try {
					text = WordsOperations.FormatSample(key, text, WordsOperations.CultureFor(languageCode));
				}
				catch (Exception ex) when (ex is FormatException or OverflowException) {
					gripes.Insert(0, ex.Message);
				}
			}
		}
		pane.Show(text, settings, gripes.Concat(settings.Errors), Gripes);
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
		string destination = target == uri.OriginalString ? target : $"{target}\n(from {uri.OriginalString})";
		if (mode == LinkMode.ShellExec) {
			if (Dialogs.Confirm($"Do you want to follow the link?\n\nDestination: {destination}")) {
				Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
			}
		}
		else {
			Dialogs.Tell($"Link destination: {destination}");
		}
	}
}
