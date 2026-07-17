using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;
internal class TestParametersViewModel : DataViewModelBase {
	public MainWindowViewModel MainWindowViewModel { get; }

	public ObservableCollection<LocalizationParameter> Parameters { get; }

	public IEnumerable<LocalizationParameterType> DataTypes { get; } = LocalizationParameterType.All;

	public ICommand CancelCommand { get; }
	public ICommand AddParameterCommand { get; }
	public ICommand RemoveParameterCommand { get; }

	public TestParametersViewModel(MainWindowViewModel mainWindowViewModel, ObservableCollection<LocalizationParameter> parameters) {
		ArgumentNullException.ThrowIfNull(mainWindowViewModel);
		Parameters = parameters;
		MainWindowViewModel = mainWindowViewModel;
		CancelCommand = new DelegateCommand(DoCancel);
		AddParameterCommand = new DelegateCommand(DoAddParameter);
		RemoveParameterCommand = new DelegateCommand<LocalizationParameter>(DoRemoveParameter);
	}

	private void DoAddParameter() {
		Parameters.Add(new LocalizationParameter());
		MainWindowViewModel.IsDirty = true;
	}

	private void DoRemoveParameter(LocalizationParameter parameterToRemove) {
		Parameters.Remove(parameterToRemove);
		MainWindowViewModel.IsDirty = true;
	}

	private void DoCancel() {
		PopupDialog.Close();
	}

	protected override bool Validate([AllowNull, CallerMemberName] string propertyName = null) => throw new NotImplementedException();
}
