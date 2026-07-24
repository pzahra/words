using System.Collections.ObjectModel;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.Views;

namespace WordsEdit.ViewModels;

/// <summary>One editable scheme→folder row in the image-scheme manager.</summary>
public class ImageSchemeMapping : ViewModelBase {
	/// <summary>The image URI scheme, without its colon (e.g. <c>md</c>, <c>icons</c>).</summary>
	public string Scheme { get; set => ChangeProperty(ref field, value); } = "";
	/// <summary>The folder the scheme's paths resolve under, relative to the ini file.</summary>
	public string Folder { get; set => ChangeProperty(ref field, value); } = "";
}

/// <summary>
///     Edits one file's image-scheme→folder mappings — the folders the markdown
///     preview looks image paths up under. The editor is not the host app, so it
///     ships no schemes of its own; a scheme shows an image only once it is mapped
///     here, and unmapped schemes fall back to the image's alt text. The mappings
///     ride along in the file's top-of-file <c>param-&lt;scheme&gt;</c> fields.
/// </summary>
public class ImageSchemesViewModel : ViewModelBase {
	public MainWindowViewModel Parent { get; }
	/// <summary>The file whose mappings are being edited (its tree label).</summary>
	public string FileLabel { get; }
	public ObservableCollection<ImageSchemeMapping> Mappings { get; } = [];

	public ICommand AddCommand { get; }
	public ICommand RemoveCommand { get; }
	public ICommand OkayCommand { get; }

	public ImageSchemesViewModel(MainWindowViewModel parent, string fileLabel) {
		Parent = parent;
		FileLabel = fileLabel;
		foreach (var (scheme, folder) in parent.ImageSchemesFor(fileLabel)) {
			Mappings.Add(new ImageSchemeMapping { Scheme = scheme, Folder = folder });
		}
		AddCommand = new DelegateCommand(DoAdd);
		RemoveCommand = new DelegateCommand<ImageSchemeMapping>(DoRemove, static row => row is not null);
		OkayCommand = new DelegateCommand(DoOkay);
	}

	private void DoAdd() => Mappings.Add(new ImageSchemeMapping());

	private void DoRemove(ImageSchemeMapping row) => Mappings.Remove(row);

	private void DoOkay() {
		//blank-scheme rows are dropped; a later row wins a duplicated scheme
		var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in Mappings) {
			var scheme = row.Scheme.Trim();
			if (scheme != "") {
				mappings[scheme] = row.Folder.Trim();
			}
		}
		Parent.SetImageSchemes(FileLabel, mappings);
		PopupDialog.Close();
	}
}
