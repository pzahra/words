using System.ComponentModel;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     One row of Test Parameters: a definition, the document's (name, type,
///     description), and the input typed for it, the session's. A renamed
///     definition takes its input along.
/// </summary>
public sealed class TestParameterRow : ViewModelBase, IDisposable {
	private readonly Dictionary<string, string> inputs;
	private readonly Action changed;
	//the name the input is filed under
	private string name;

	public WordsParameter Parameter { get; }

	/// <summary>The types the combo offers: the plain five, and the definition's own <c>enum</c>, prefix and all, where it has one.</summary>
	public IReadOnlyList<WordsParameterType> Types { get; }

	/// <summary>What the translator typed for this parameter: no part of the document.</summary>
	public string Sample {
		get => inputs.GetValueOrDefault(name, "");
		set {
			if (value == Sample) {
				return;
			}
			inputs[name] = value;
			AffectProperty(nameof(Sample));
			changed();
		}
	}

	public ICommand RemoveCommand { get; }

	/// <param name="parameter">The definition, the key's own.</param>
	/// <param name="inputs">The key's inputs (<see cref="ParameterInputs.Of"/>).</param>
	/// <param name="changed">Called on any edit to the row, the definition's or the input's.</param>
	/// <param name="remove">Takes the definition off the key.</param>
	public TestParameterRow(WordsParameter parameter, Dictionary<string, string> inputs, Action changed, Action<WordsParameter> remove) {
		Parameter = parameter;
		this.inputs = inputs;
		this.changed = changed;
		name = parameter.Key;
		Types = WordsParameterType.Plain.Contains(parameter.DataType) ? WordsParameterType.Plain : [.. WordsParameterType.Plain, parameter.DataType];
		RemoveCommand = new DelegateCommand(() => remove(Parameter));
		Parameter.PropertyChanged += OnEdited;
	}

	private void OnEdited(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(WordsParameter.Key) && Parameter.Key != name) {
			if (inputs.Remove(name, out string? typed)) {
				inputs.TryAdd(Parameter.Key, typed);
			}
			name = Parameter.Key;
			AffectProperty(nameof(Sample));
		}
		changed();
	}

	public void Dispose() => Parameter.PropertyChanged -= OnEdited;
}
