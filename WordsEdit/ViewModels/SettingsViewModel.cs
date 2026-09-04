using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>One <c>[images]</c> row: a scheme, the folder its paths are looked up under, and how a URI becomes a path.</summary>
public class ImageRuleRow : ViewModelBase {
	public string Scheme { get; set => ChangeProperty(ref field, value); } = "";
	/// <summary>Relative to the settings file.</summary>
	public string Folder { get; set => ChangeProperty(ref field, value); } = "";
	/// <summary><c>/pattern/options/replacement</c>, or empty for the scheme's built-in shape.</summary>
	public string Decode { get; set => ChangeProperty(ref field, value); } = "";
}

/// <summary>One <c>[hyperlinks]</c> row: a scheme, what a click does, and how the URI is rewritten first.</summary>
public class LinkRuleRow : ViewModelBase {
	/// <summary>The mode choices as written in the file; the empty one keeps the scheme's default.</summary>
	public static IReadOnlyList<string> Modes { get; } = ["", "popup", "shellexec"];

	public string Scheme { get; set => ChangeProperty(ref field, value); } = "";
	public string Mode { get; set => ChangeProperty(ref field, value); } = "";
	public string Decode { get; set => ChangeProperty(ref field, value); } = "";
}

/// <summary>A settings file named by a <c>param</c> slot, as the dialog lists them.</summary>
public sealed class SettingsTarget(string label, string path) {
	/// <summary>Whose file this is and what it is called.</summary>
	public string Label { get; } = label;
	/// <summary>The absolute path.</summary>
	public string Path { get; } = path;
	public override string ToString() => Label;
}

/// <summary>One language of the file and the settings file it names, if any.</summary>
public class LanguageSettingRow(LanguageEntry language, string path, Action changed) : ViewModelBase {
	public string Code => language.Code;
	public string Name => language.EnglishName;
	/// <summary>Relative to the dictionary, or empty for none.</summary>
	public string Path { get; set => _ = ChangeProperty(ref field, value) && Run(changed); } = path;

	private static bool Run(Action action) {
		action();
		return true;
	}
}

/// <summary>
///     The two tables of one settings file, editable. Rows are kept as written;
///     <see cref="Errors"/> says what <see cref="ProjectSettings"/> makes of them
///     as they stand, so a bad rule is seen before it is saved.
/// </summary>
public class SettingsDocument : ViewModelBase {
	public string Path { get; }
	public ObservableCollection<ImageRuleRow> Images { get; } = [];
	public ObservableCollection<LinkRuleRow> Links { get; } = [];
	public IReadOnlyList<string> Errors { get; private set => ChangeProperty(ref field, value); } = [];
	public bool HasErrors => Errors.Count > 0;
	/// <summary>True once a row was added, removed or edited.</summary>
	public bool IsEdited { get; private set; }

	public ICommand AddImageCommand { get; }
	public ICommand RemoveImageCommand { get; }
	public ICommand AddLinkCommand { get; }
	public ICommand RemoveLinkCommand { get; }

	public SettingsDocument(string path) {
		Path = path;
		AddImageCommand = new DelegateCommand(() => Images.Add(new ImageRuleRow()));
		RemoveImageCommand = new DelegateCommand<ImageRuleRow>(row => Images.Remove(row), static row => row is not null);
		AddLinkCommand = new DelegateCommand(() => Links.Add(new LinkRuleRow()));
		RemoveLinkCommand = new DelegateCommand<LinkRuleRow>(row => Links.Remove(row), static row => row is not null);
		//a file that is not there yet is simply empty; anything else it says is shown
		ProjectSettings loaded = File.Exists(path) ? ProjectSettings.Load(path) : ProjectSettings.Empty;
		foreach (ImageRule rule in loaded.Images) {
			Images.Add(new ImageRuleRow { Scheme = rule.Scheme, Folder = rule.Folder, Decode = rule.Decode ?? "" });
		}
		foreach (LinkRule rule in loaded.Links) {
			Links.Add(new LinkRuleRow {
				Scheme = rule.Scheme,
				Mode = rule.Mode switch { LinkMode.Popup => "popup", LinkMode.ShellExec => "shellexec", _ => "" },
				Decode = rule.Decode ?? "",
			});
		}
		Errors = loaded.Errors;
		Images.CollectionChanged += RowsChanged;
		Links.CollectionChanged += RowsChanged;
		foreach (ImageRuleRow row in Images) {
			row.PropertyChanged += RowEdited;
		}
		foreach (LinkRuleRow row in Links) {
			row.PropertyChanged += RowEdited;
		}
	}

	private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		foreach (ViewModelBase row in e.OldItems ?? Array.Empty<ViewModelBase>()) {
			row.PropertyChanged -= RowEdited;
		}
		foreach (ViewModelBase row in e.NewItems ?? Array.Empty<ViewModelBase>()) {
			row.PropertyChanged += RowEdited;
		}
		Edited();
	}

	private void RowEdited(object? sender, PropertyChangedEventArgs e) => Edited();

	private void Edited() {
		IsEdited = true;
		Errors = ToSettings().Errors;
		AffectProperty(nameof(HasErrors));
	}

	/// <summary>The rows as settings: blank-scheme rows dropped, whitespace trimmed, a later duplicate scheme winning.</summary>
	public ProjectSettings ToSettings() {
		string directory = System.IO.Path.GetDirectoryName(Path) ?? "";
		var images = Images
			.Where(row => row.Scheme.Trim() != "")
			.Select(row => new ImageRule(row.Scheme.Trim(), row.Folder.Trim(), Blank(row.Decode), directory));
		var links = Links
			.Where(row => row.Scheme.Trim() != "")
			.Select(row => new LinkRule(row.Scheme.Trim(), row.Mode switch { "popup" => LinkMode.Popup, "shellexec" => LinkMode.ShellExec, _ => null }, Blank(row.Decode)));
		return new ProjectSettings(Path, images, links);
	}

	private static string? Blank(string text) => text.Trim() is "" ? null : text.Trim();
}

/// <summary>
///     The project settings of one dictionary (SPEC: Markdown previews): which
///     files its <c>param</c> slots name — one for the dictionary, one per
///     language it declares — and the image and hyperlink tables of whichever of
///     those is picked. OK writes the slots into the file (a document change) and
///     every table that was touched to its settings file; Cancel leaves both.
/// </summary>
public class SettingsViewModel : DialogViewModel {
	public override string Title => $"Project Settings — {File.Label}";
	public MainWindowViewModel Parent { get; }
	/// <summary>The dictionary whose settings these are.</summary>
	public WordsFile File { get; }
	public string FileLabel => File.Label;

	/// <summary>The dictionary's settings file, relative to it, or empty.</summary>
	public string SettingsFile { get; set => _ = ChangeProperty(ref field, value) && RefreshTargets(); }
	/// <summary>The file's languages, each with the settings file it names.</summary>
	public IReadOnlyList<LanguageSettingRow> Languages { get; }

	/// <summary>The settings files named above, to pick one to edit.</summary>
	public ObservableCollection<SettingsTarget> Targets { get; } = [];
	public SettingsTarget? Target { get; set => _ = ChangeProperty(ref field, value) && ShowTarget(); }
	/// <summary>The tables of <see cref="Target"/>, or none when nothing is named.</summary>
	public SettingsDocument? Document { get; private set => ChangeProperty(ref field, value); }

	public ICommand OkayCommand { get; }
	public ICommand CancelCommand { get; }

	private readonly Dictionary<string, SettingsDocument> documents = new(StringComparer.OrdinalIgnoreCase);

	public SettingsViewModel(MainWindowViewModel parent, WordsFile file) {
		Parent = parent;
		File = file;
		SettingsFile = file.Settings;
		Languages = [.. parent.Session.Languages.For(file).Select(language => new LanguageSettingRow(language, file.LanguageSettings.GetValueOrDefault(language.Code, ""), () => RefreshTargets()))];
		OkayCommand = new DelegateCommand(DoOkay);
		CancelCommand = new DelegateCommand(Close);
		RefreshTargets();
	}

	private string? Resolve(string relative)
		=> relative.Trim() == "" ? null : Path.GetFullPath(Path.Combine(File.Directory, relative.Trim()));

	//the pick list follows the paths as they are typed; the pick itself survives
	//when its path is still among them
	private bool RefreshTargets() {
		string? picked = Target?.Path;
		Targets.Clear();
		if (Resolve(SettingsFile) is { } path) {
			Targets.Add(new SettingsTarget($"{File.Label}: {SettingsFile.Trim()}", path));
		}
		foreach (LanguageSettingRow language in Languages ?? []) {
			if (Resolve(language.Path) is { } languagePath) {
				Targets.Add(new SettingsTarget($"{language.Code}: {language.Path.Trim()}", languagePath));
			}
		}
		Target = Targets.FirstOrDefault(target => string.Equals(target.Path, picked, StringComparison.OrdinalIgnoreCase)) ?? Targets.FirstOrDefault();
		return true;
	}

	private bool ShowTarget() {
		if (Target is null) {
			Document = null;
			return true;
		}
		if (!documents.TryGetValue(Target.Path, out SettingsDocument? document)) {
			document = new SettingsDocument(Target.Path);
			documents[Target.Path] = document;
		}
		Document = document;
		return true;
	}

	private void DoOkay() {
		//the slots are the dictionary's own and travel with it
		string settings = SettingsFile.Trim();
		if (File.Settings != settings) {
			File.Settings = settings;
			Parent.IsDirty = true;
		}
		foreach (LanguageSettingRow language in Languages) {
			string path = language.Path.Trim();
			if (File.LanguageSettings.GetValueOrDefault(language.Code, "") != path) {
				if (path == "") {
					File.LanguageSettings.Remove(language.Code);
				}
				else {
					File.LanguageSettings[language.Code] = path;
				}
				Parent.IsDirty = true;
			}
		}
		//the tables go to their own files, touched ones only; a file that will
		//not write is told and the rest still save
		foreach (SettingsDocument document in documents.Values.Where(document => document.IsEdited)) {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(document.Path) ?? "");
				document.ToSettings().Save(document.Path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Parent.Dialogs.Tell($"Could not write {document.Path}:\n{ex.Message}");
			}
		}
		Close();
	}
}
