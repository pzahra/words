using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The key's parameters — name, type, description — each with a sample typed
///     for it, and the default they format into, live: what the previews will
///     show, or why they will not. Every edit to a definition lands in the
///     document as it is made; the samples are the session's (SPEC: Parameters →
///     The inputs). Close just closes, and the window closing, however it closes,
///     lets go of the key.
/// </summary>
public class TestParametersViewModel : DialogViewModel {
	private readonly WordsKey key;
	private readonly Dictionary<string, string> inputs;

	public override string Title => Words.Known["parameters.title"];
	public MainWindowViewModel Parent { get; }

	/// <summary>One row per definition, in the key's order.</summary>
	public ObservableCollection<TestParameterRow> Rows { get; } = [];

	/// <summary>The default formatted with the samples; the complaint when it will not format.</summary>
	public string Result { get; private set => ChangeProperty(ref field, value); } = "";
	public bool IsError { get; private set => ChangeProperty(ref field, value); }

	public ICommand CloseCommand { get; }
	public ICommand AddParameterCommand { get; }

	public TestParametersViewModel(MainWindowViewModel parent, WordsKey key) {
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentNullException.ThrowIfNull(key);
		this.key = key;
		Parent = parent;
		inputs = parent.Inputs.Of(key);
		CloseCommand = new DelegateCommand(Close);
		AddParameterCommand = new DelegateCommand(DoAddParameter);
		//the collection is the key's own: any edit to a row, or the row set, is a
		//document change. Watch both for the life of the dialog
		key.Parameters.CollectionChanged += OnParametersChanged;
		BuildRows();
		Refresh();
	}

	private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		BuildRows();
		Refresh();
	}

	private void BuildRows() {
		DisposeRows();
		foreach (var parameter in key.Parameters) {
			Rows.Add(new(parameter, inputs, Refresh, DoRemoveParameter));
		}
	}

	private void DisposeRows() {
		foreach (var row in Rows) {
			row.Dispose();
		}
		Rows.Clear();
	}

	//the default, references expanded, formatted the way the default preview does it
	private void Refresh() {
		try {
			Result = Parent.FormatDefaultSample(key);
			IsError = false;
		}
		catch (Exception ex) when (ex is FormatException or OverflowException) {
			Result = ex.Message;
			IsError = true;
		}
	}

	//the lowest number no definition has, so {0} and never P0 (SPEC: Parameters)
	private void DoAddParameter() {
		int i = 0;
		while (key.Parameters.Any(p => p.Key == i.ToString(CultureInfo.InvariantCulture))) {
			i++;
		}
		key.Parameters.Add(new(i.ToString(CultureInfo.InvariantCulture), WordsParameterType.Str, ""));
	}

	private void DoRemoveParameter(WordsParameter p) => key.Parameters.Remove(p);

	public override void Closed() {
		key.Parameters.CollectionChanged -= OnParametersChanged;
		DisposeRows();
	}
}
