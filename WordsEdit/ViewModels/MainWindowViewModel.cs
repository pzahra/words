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
	//current while shown; a sample that will not format keeps the raw text and
	//says why
	public bool ShowDefaultPreview { get; set => _ = ChangeProperty(ref field, value) && RenderPreviews(); }
	public bool ShowLocalizationPreview { get; set => _ = ChangeProperty(ref field, value) && RenderPreviews(); }
	public string RenderedDefault { get; private set => ChangeProperty(ref field, value); } = "";
	public string? DefaultPreviewError { get; private set => ChangeProperty(ref field, value); }
	public string RenderedTranslation { get; private set => ChangeProperty(ref field, value); } = "";
	public string? TranslationPreviewError { get; private set => ChangeProperty(ref field, value); }
	/// <summary>Scheme → absolute folder for the selected key's file: what the preview resolves images through.</summary>
	public IReadOnlyDictionary<string, string> PreviewImageFolders { get; private set => ChangeProperty(ref field, value); } = NoFolders;
	private static readonly IReadOnlyDictionary<string, string> NoFolders = new Dictionary<string, string>();
	private WordsFile? previewFile;

	//Commands
	public ICommand LoadFileCommand { get; }
	public ICommand ResetCommand { get; }
	public ICommand SaveCommand { get; }
	public ICommand MergeFilesCommand { get; }
	public ICommand ManageLanguagesCommand { get; }
	public ICommand ManageImageSchemesCommand { get; }
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
		ManageImageSchemesCommand = new DelegateCommand(DoManageImageSchemes, CanManageImageSchemes);
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

	//Merge
	private void DoMergeFiles() {
		Dialogs.Show(new MergeControlViewModel(this));
	}

	//Languages
	private void DoManageLanguages() {
		Dialogs.Show(new LanguageManagerViewModel(this));
	}

	//image-scheme mappings are per-file; the dialog edits the file the selection
	//sits in, so a node must be selected to know which file that is
	private bool CanManageImageSchemes() => Tree.SelectedFile is not null;
	private void DoManageImageSchemes() {
		if (Tree.SelectedFile is not { } file) {
			return;
		}
		Dialogs.Show(new ImageSchemesViewModel(this, file));
		//the mappings may have changed under the preview
		previewFile = null;
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
		WordsFile? file = Tree.SelectedFile;
		if (file != previewFile) {
			previewFile = file;
			PreviewImageFolders = file?.ImageSchemeFolders() ?? NoFolders;
		}
		if (Tree.SelectedKey is not { } key) {
			(RenderedDefault, DefaultPreviewError) = ("", null);
			(RenderedTranslation, TranslationPreviewError) = ("", null);
			return true;
		}
		if (ShowDefaultPreview) {
			(RenderedDefault, DefaultPreviewError) = Render(key, null);
		}
		if (ShowLocalizationPreview && Tree.SelectedEntry is not null) {
			(RenderedTranslation, TranslationPreviewError) = Render(key, Tree.SelectedLanguage.Code);
		}
		return true;
	}

	//every loaded file in tree order resolves {>references} and {$constants}, like a
	//host app stacking dictionaries; the samples then go through the same formatting
	//the host applies, in the language's culture where there is one
	private (string text, string? error) Render(WordsKey key, string? languageCode) {
		IWordsProvider provider = Session.Provider(Tree.FileLabels, languageCode);
		string text = Words.RenderKey(provider, key.BlockKey);
		if (key.Parameters.Count == 0) {
			return (text, null);
		}
		try {
			return (WordsOperations.FormatSample(key, text, WordsOperations.CultureFor(languageCode)), null);
		}
		catch (Exception ex) when (ex is FormatException or OverflowException) {
			return (text, ex.Message);
		}
	}

	/// <summary>
	///     A hyperlink in a preview was clicked. Web and mail links open in the
	///     shell once the user agrees; anything else is the host app's business and
	///     is only reported.
	/// </summary>
	public void FollowLink(Uri uri) {
		if (uri.Scheme is "http" or "https" or "mailto") {
			if (Dialogs.Confirm($"Do you want to follow the link?\n\nDestination: {uri.AbsoluteUri}")) {
				Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
			}
		}
		else {
			Dialogs.Tell($"Internal link detected. Destination: {uri.OriginalString}");
		}
	}
}
