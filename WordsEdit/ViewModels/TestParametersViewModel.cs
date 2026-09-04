using PatTech.Localization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The key's parameters — name, sample value, type — and the default they
///     format into, live: what the previews will show, or why they will not.
///     Every edit lands in the document as it is made; Close just closes.
/// </summary>
public class TestParametersViewModel : DialogViewModel {
	private readonly WordsKey key;

	public override string Title => "Test Parameters";
	public MainWindowViewModel Parent { get; }

	public ObservableCollection<WordsParameter> Parameters => key.Parameters;

	public IEnumerable<WordsParameterType> DataTypes { get; } = WordsParameterType.All;

	/// <summary>The default formatted with the samples; the complaint when it will not format.</summary>
	public string Result { get; private set => ChangeProperty(ref field, value); } = "";
	public bool IsError { get; private set => ChangeProperty(ref field, value); }

	public ICommand CloseCommand { get; }
	public ICommand AddParameterCommand { get; }
	public ICommand RemoveParameterCommand { get; }

	public TestParametersViewModel(MainWindowViewModel parent, WordsKey key) {
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentNullException.ThrowIfNull(key);
		this.key = key;
		Parent = parent;
		CloseCommand = new DelegateCommand(DoClose);
		AddParameterCommand = new DelegateCommand(DoAddParameter);
		RemoveParameterCommand = new DelegateCommand<WordsParameter>(DoRemoveParameter, CanRemoveParameter);
		//the collection is the key's own: any edit to a row, or the row set, is a
		//document change. Watch both for the life of the dialog
		Parameters.CollectionChanged += OnParametersChanged;
		foreach (var parameter in Parameters) {
			parameter.PropertyChanged += OnParameterEdited;
		}
		Refresh();
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
		Refresh();
	}

	private void OnParameterEdited(object? sender, PropertyChangedEventArgs e) {
		Parent.IsDirty = true;
		Refresh();
	}

	//the default, references expanded, formatted the way the default preview does it
	private void Refresh() {
		try {
			string text = Words.RenderKey(Parent.Session.Provider(Parent.Tree.FileLabels), key.BlockKey);
			Result = WordsOperations.FormatSample(key, text);
			IsError = false;
		}
		catch (Exception ex) when (ex is FormatException or OverflowException) {
			Result = ex.Message;
			IsError = true;
		}
	}

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

	private void DoClose() {
		Parameters.CollectionChanged -= OnParametersChanged;
		foreach (var parameter in Parameters) {
			parameter.PropertyChanged -= OnParameterEdited;
		}
		Close();
	}
}
