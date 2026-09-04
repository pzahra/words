using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
internal class KeyNameViewModel : DataViewModelBase {
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

		Parent.AddLocalizationKeyNode(KeyName);
		PopupDialog.Close();
	}

	private void DoRenameKey() {
		if (!Validate("")) return;

		if (KeyName == renaming!.Label) {
			PopupDialog.Close();
			return;
		}

		Parent.RenameLocalizationKeyAndNode(KeyName);
		PopupDialog.Close();
	}

	private void DoCancel() => PopupDialog.Close();

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
				SetError("Required", nameof(KeyName));
			}
			var siblings = renaming?.GetParentNode(Parent.KeyNodes)?.Children ?? Parent.KeyNodes;
			if (siblings.Any(k => k != renaming && k.Label.Equals(KeyName, StringComparison.CurrentCultureIgnoreCase))) {
				SetError("Already Exists", nameof(KeyName));
			}
			if (!rxValidName.IsMatch(KeyName)) {
				SetError("Invalid Characters");
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
