using System.Collections.ObjectModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
internal class TestParametersViewModel : ViewModelBase {
	public MainWindowViewModel Parent { get; }

	public ObservableCollection<WordsParameter> Parameters { get; }

	public IEnumerable<WordsParameterType> DataTypes { get; } = WordsParameterType.All;

	public ICommand CancelCommand { get; }
	public ICommand AddParameterCommand { get; }
	public ICommand RemoveParameterCommand { get; }

	public TestParametersViewModel(MainWindowViewModel parent, ObservableCollection<WordsParameter> parameters) {
		ArgumentNullException.ThrowIfNull(parent);
		Parameters = parameters;
		Parent = parent;
		CancelCommand = new DelegateCommand(DoCancel);
		AddParameterCommand = new DelegateCommand(DoAddParameter);
		RemoveParameterCommand = new DelegateCommand<WordsParameter>(DoRemoveParameter, CanRemoveParameter);
	}

	private void DoAddParameter() {
		for (int i = 0; i < Parameters.Count; i++) {
			string name = $"P{i}";
			if (Parameters.Any(p => p.Key == name)) continue;
			Parameters.Add(new(name, WordsParameterType.String, ""));
		}
		Parent.IsDirty = true;
	}

	private bool CanRemoveParameter(WordsParameter p) => p is not null;
	private void DoRemoveParameter(WordsParameter p) {
		Parameters.Remove(p);
		// TODO: make sure the original collection is being observed to set this flag.
		Parent.IsDirty = true;
	}

	private void DoCancel() {
		PopupDialog.Close();
	}
}
