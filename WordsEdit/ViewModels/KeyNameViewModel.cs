using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
public class KeyNameViewModel : DataViewModelBase {
	public override string Title => IsRenameKey ? Words.Known["key-name.rename"] : Words.Known["key-name.add"];
	public MainWindowViewModel Parent { get; }
	public bool IsAddKey => renaming is null;
	public bool IsRenameKey => renaming is not null;
	public string KeyName { get; set => _ = ChangeProperty(ref field, value) && Validate(); }

	public ICommand CancelCommand { get; }
	public ICommand AddKeyCommand { get; }
	public ICommand RenameKeyCommand { get; }

	private readonly KeyNode? renaming;

	public KeyNameViewModel(MainWindowViewModel parent, KeyNode? rename) {
		ArgumentNullException.ThrowIfNull(parent);

		CancelCommand = new DelegateCommand(DoCancel);
		AddKeyCommand = new DelegateCommand(DoAddKey, CanProceed);
		RenameKeyCommand = new DelegateCommand(DoRenameKey);

		Parent = parent;
		if (rename is null) {
			KeyName = "";
		}
		else {
			renaming = rename;
			KeyName = rename.Label;
		}
	}

	private bool CanProceed() => !HasErrors;
	private void DoAddKey() {
		if (!Validate("")) return;

		Parent.AddNode(KeyName);
		Close();
	}

	private void DoRenameKey() {
		if (!Validate("")) return;

		if (KeyName == renaming!.Label) {
			Close();
			return;
		}

		Parent.RenameNode(KeyName);
		Close();
	}

	private void DoCancel() => Close();

	private static readonly Regex rxValidName = new(@"^\w[\w-]*$");
	protected override bool Validate([CallerMemberName] string? propertyName = null) {
		var all = string.IsNullOrEmpty(propertyName);
		if (all) {
			ClearAllErrors();
		}
		else {
			ClearErrors(propertyName!);
		}

		if (all || propertyName is nameof(KeyName)) {
			if (string.IsNullOrWhiteSpace(KeyName)) {
				SetError(Words.Known["key-name.required"], nameof(KeyName));
			}
			//a rename competes with its siblings; a new node with the children of the
			//node it goes under
			IEnumerable<KeyNode> siblings = renaming is not null
				? renaming.Parent?.Children ?? Parent.Tree.KeyNodes
				: Parent.Tree.SelectedKeyNode?.Children ?? Parent.Tree.KeyNodes;
			if (siblings.Any(k => k != renaming && k.Label.Equals(KeyName, StringComparison.CurrentCultureIgnoreCase))) {
				SetError(Words.Known["key-name.exists"], nameof(KeyName));
			}
			if (!rxValidName.IsMatch(KeyName)) {
				SetError(Words.Known["key-name.invalid"]);
			}
		}

		if (all) {
			return !HasErrors;
		}
		else {
			return IsValid(propertyName!);
		}
	}
}
