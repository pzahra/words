using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
public class TestParametersViewModel : DialogViewModel {
	public override string Title => "Test Parameters";
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
		//the collection is the key's own: any edit to a row, or the row set, is a
		//document change. Watch both for the life of the dialog
		Parameters.CollectionChanged += OnParametersChanged;
		foreach (var parameter in Parameters) {
			parameter.PropertyChanged += OnParameterEdited;
		}
	}

	private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		if (e.OldItems is not null) {
			foreach (WordsParameter parameter in e.OldItems) {
				parameter.PropertyChanged -= OnParameterEdited;
			}
		}
		if (e.NewItems is not null) {
			foreach (WordsParameter parameter in e.NewItems) {
				parameter.PropertyChanged += OnParameterEdited;
			}
		}
		Parent.IsDirty = true;
	}

	private void OnParameterEdited(object? sender, PropertyChangedEventArgs e) => Parent.IsDirty = true;

	private void DoAddParameter() {
		//the first free P<n>
		int i = 0;
		while (Parameters.Any(p => p.Key == $"P{i}")) {
			i++;
		}
		Parameters.Add(new($"P{i}", WordsParameterType.String, ""));
	}

	private bool CanRemoveParameter(WordsParameter p) => p is not null;
	private void DoRemoveParameter(WordsParameter p) => Parameters.Remove(p);

	private void DoCancel() {
		Parameters.CollectionChanged -= OnParametersChanged;
		foreach (var parameter in Parameters) {
			parameter.PropertyChanged -= OnParameterEdited;
		}
		Close();
	}
}
