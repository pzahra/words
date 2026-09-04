using System.Windows.Input;

namespace WordsEdit.Utils;

/// <summary>A command over two delegates; WPF re-queries it whenever the input state changes.</summary>
public class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand {
	public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
	public void Execute(object? parameter) => execute();

	public event EventHandler? CanExecuteChanged {
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}
}

/// <summary>
///     The same, handed its parameter. A null parameter — a binding not yet
///     resolved — reads as <see langword="default"/>, so a predicate can say no.
/// </summary>
public class DelegateCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand {
	public bool CanExecute(object? parameter) => canExecute?.Invoke(Cast(parameter)) ?? true;
	public void Execute(object? parameter) => execute(Cast(parameter));

	private static T Cast(object? parameter) => parameter switch {
		T value => value,
		null => default!,
		_ => throw new ArgumentException($"Parameter is not of type {typeof(T)}.", nameof(parameter)),
	};

	public event EventHandler? CanExecuteChanged {
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}
}
