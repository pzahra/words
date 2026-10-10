using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     The baseline pane's parameters (SPEC: Parameters → In the pane): the selected
///     key's definitions, each the programmer's message with the translator's input
///     under it, then the parameters its default uses that nobody defined, each faint
///     with its input, then the +. A description types and undoes as any field does;
///     a definition added, adopted, renamed, retyped or taken away is one entry. The
///     inputs are the session's (<see cref="ParameterInputs"/>), and the previews
///     format with them.
/// </summary>
public sealed class ParametersPane : ViewModelBase {
	private readonly MainWindowViewModel vm;
	private WordsKey? key;
	private string? language;

	/// <summary>The key's definitions, in its order.</summary>
	public ObservableCollection<DefinitionRow> Definitions { get; } = [];
	/// <summary>The parameters its default uses that no definition names.</summary>
	public ObservableCollection<FoundRow> Found { get; } = [];
	/// <summary>What the selected key's inputs stand for, definitions first (<see cref="ParameterUse.Slots"/>): what the previews format with.</summary>
	public IReadOnlyList<ParameterSlot> Slots { get; private set; } = [];
	/// <summary>A key that is no constant is selected: the + shows, and the thread where there is one.</summary>
	public bool IsShown { get; private set => ChangeProperty(ref field, value); }
	/// <summary>The key defines a parameter or uses one: the thread's heading shows.</summary>
	public bool HasThread { get; private set => ChangeProperty(ref field, value); }
	/// <summary>The +: the first parameter found and not defined, else the lowest number no definition has.</summary>
	public ICommand AddCommand { get; }

	public ParametersPane(MainWindowViewModel vm) {
		this.vm = vm;
		AddCommand = new DelegateCommand(DoAdd);
	}

	/// <summary>
	///     Reads the selected key's parameters again, before each render, so the thread
	///     shows what the previews format with. Rows stay while their definition and
	///     the found parameters stand, so a box being typed in keeps the focus.
	/// </summary>
	public void Follow() {
		WordsKey? selected = vm.Tree.SelectedKey is { IsConstant: false } shown ? shown : null;
		string? defaultLanguage = vm.Tree.DefaultLanguage;
		bool another = selected != key || defaultLanguage != language;
		key = selected;
		language = defaultLanguage;
		IsShown = key is not null;
		Slots = key is null ? [] : ParameterUse.Slots(key, ParameterUse.InDefault(key, vm.Session.Provider(vm.Tree.FileLabels), vm.Tree.DefaultForms.Language));
		HasThread = Slots.Count != 0;

		List<ParameterSlot> defined = [.. Slots.Where(slot => slot.Definition is not null)];
		if (another || !Definitions.Select(row => row.Parameter).SequenceEqual(defined.Select(slot => slot.Definition!))) {
			foreach (DefinitionRow row in Definitions) {
				row.Dispose();
			}
			Definitions.Clear();
			foreach (ParameterSlot slot in defined) {
				Definitions.Add(new DefinitionRow(this, slot.Definition!, language, vm.TextHistory));
			}
		}
		foreach (var (row, slot) in Definitions.Zip(defined)) {
			row.Follow(slot);
		}

		List<ParameterSlot> found = [.. Slots.Where(slot => slot.Definition is null)];
		if (another || !Found.Select(row => (row.Name, row.Type)).SequenceEqual(found.Select(slot => (slot.Name, slot.Type)))) {
			Found.Clear();
			foreach (ParameterSlot slot in found) {
				Found.Add(new FoundRow(this, slot));
			}
		}
		foreach (FoundRow row in Found) {
			row.Check();
		}
	}

	/// <summary>Each <c>enum</c> whose prefix has nothing under it, said as its input's tooltip says it: the previews' gripes.</summary>
	public IEnumerable<string> PrefixComplaints()
		=> Slots.Where(slot => slot.Type.Prefix is { } prefix && MembersUnder(prefix).Count == 0).Select(slot => NothingUnder(slot.Name, slot.Type.Prefix!));

	//what the translator typed: the session's, never the document's
	internal string InputOf(string name) => key is null ? "" : vm.Inputs.Of(key).GetValueOrDefault(name, "");

	internal void Type(string name, string input) {
		if (key is not null) {
			vm.Inputs.Of(key)[name] = input;
			vm.RenderPreviews();
		}
	}

	internal IReadOnlyList<string> MembersUnder(string prefix) => WordsOperations.MembersUnder(vm.Session.Keys, vm.Tree.FileLabels, prefix);

	internal static string NothingUnder(string name, string prefix) => Words.Known.Format("parameters.nothing-under", FoundParameters.Placeholder(name), prefix);

	//a description typed: a field edit, naming its parameter as a value names its form
	internal void Described(DefinitionRow row, string before, string after) {
		if (vm.Tree.SelectedKeyNode is { } node) {
			vm.Typed(new FieldEdit(NodeRef.Of(node), null, DocumentField.ParameterDescription, before, after, row.Parameter.Key));
		}
	}

	//one entry per change to the definitions, the key's parameters before and after; the
	//definitions widen what a translation may use, so the key's badge follows
	private void Change(Action<WordsKey> change) {
		if (key is not { } changed) {
			return;
		}
		vm.Perform(() => {
			IReadOnlyList<WordsParameter> before = ParametersEdit.Copy(changed);
			change(changed);
			IReadOnlyList<WordsParameter> after = ParametersEdit.Copy(changed);
			if (vm.Tree.SelectedKeyNode is { } node) {
				vm.Tree.RefreshBadges(node);
			}
			return ParametersEdit.Same(before, after) ? null : new ParametersEdit(changed.BlockKey, before, after);
		});
		vm.RenderPreviews();
	}

	private void DoAdd() {
		if (Found.FirstOrDefault() is { } first) {
			Adopt(first);
			return;
		}
		Change(changed => {
			int number = 0;
			while (changed.Parameters.Any(parameter => ParameterUse.SameName(parameter.Key, Number(number)))) {
				number++;
			}
			changed.Parameters.Add(new WordsParameter(Number(number), WordsParameterType.Str, ""));
		});

		static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);
	}

	//a found parameter made a definition, its type the one guessed
	internal void Adopt(FoundRow row) => Change(changed => changed.Parameters.Add(new WordsParameter(row.Name, row.Type, "")));

	internal void Remove(DefinitionRow row) => Change(changed => changed.Parameters.Remove(row.Parameter));

	internal void Retype(DefinitionRow row, WordsParameterType type) => Change(_ => row.Parameter.DataType = type);

	/// <summary>Renames a definition, its input going with it; the complaint when the name is no parameter's or another's.</summary>
	internal string? Rename(DefinitionRow row, string name) {
		if (key is null) {
			return null;
		}
		if (!ParameterUse.IsName(name) || key.Parameters.Any(parameter => parameter != row.Parameter && ParameterUse.SameName(parameter.Key, name))) {
			return Words.Known["parameters.name"];
		}
		Dictionary<string, string> typed = vm.Inputs.Of(key);
		if (typed.Remove(row.Parameter.Key, out string? input)) {
			typed.TryAdd(name, input);
		}
		Change(_ => row.Parameter.Key = name);
		return null;
	}
}
