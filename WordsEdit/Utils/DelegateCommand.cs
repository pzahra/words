using System.Windows.Input;

namespace WordsEdit.Utils {
	public class DelegateCommand : ICommand {
		private readonly Action execute;
		private readonly Func<bool> canExecute;
		private EventHandler? canExecuteChanged;

		public DelegateCommand(Action executeMethod) : this(executeMethod, Yes) { }

		public DelegateCommand(Action executeMethod, Func<bool> canExecuteMethod) {
			execute = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
			canExecute = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
		}

		private static bool Yes() => true;

		public bool CanExecute(object? parameter) => canExecute();

		public void Execute(object? parameter) => execute();

		public event EventHandler? CanExecuteChanged {
			add {
				CommandManager.RequerySuggested += value;
				canExecuteChanged += value;
			}
			remove {
				CommandManager.RequerySuggested -= value;
				canExecuteChanged -= value;
			}
		}

		public bool CanExecute() => canExecute();
		public void Execute() => execute();
		public bool SafeExecute() {
			if (canExecute()) {
				execute();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Manually raise <see cref="CanExecuteChanged"/>.
		/// </summary>
		public void RaiseCanExecuteChanged() => canExecuteChanged?.Invoke(this, EventArgs.Empty);
	}

	public class DelegateCommand<T> : ICommand {
		private readonly Action<T> execute;
		private readonly Func<T, bool> canExecute;
		private EventHandler? canExecuteChanged;

		public DelegateCommand(Action<T> executeMethod) : this(executeMethod, Yes) { }

		public DelegateCommand(Action<T> executeMethod, Func<T, bool> canExecuteMethod) {
			execute = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
			canExecute = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
		}

		private static bool Yes(T _) => true;

		public bool CanExecute(T parameter) => canExecute(parameter);

		public bool CanExecute(object? parameter) {
			if (parameter is T p) return CanExecute(p);

			// Accept null for reference types and nullable value types by using default(T)
			if (parameter is null) {
				if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
					throw new ArgumentException($"Parameter is null but type {typeof(T)} is a non-nullable value type.", nameof(parameter));

				return CanExecute(default!);
			}

			throw new ArgumentException($"Parameter is not of type {typeof(T)}.", nameof(parameter));
		}

		public void Execute(T parameter) => execute(parameter);

		public void Execute(object? parameter) {
			if (parameter is T p) {
				Execute(p);
				return;
			}

			if (parameter is null) {
				if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
					throw new ArgumentException($"Parameter is null but type {typeof(T)} is a non-nullable value type.", nameof(parameter));

				Execute(default!);
				return;
			}

			throw new ArgumentException($"Parameter is not of type {typeof(T)}.", nameof(parameter));
		}

		public bool SafeExecute(T parameter) {
			if (canExecute(parameter)) {
				execute(parameter);
				return true;
			}
			return false;
		}

		public event EventHandler? CanExecuteChanged {
			add {
				CommandManager.RequerySuggested += value;
				canExecuteChanged += value;
			}
			remove {
				CommandManager.RequerySuggested -= value;
				canExecuteChanged -= value;
			}
		}

		/// <summary>
		/// Manually raise <see cref="CanExecuteChanged"/>.
		/// </summary>
		public void RaiseCanExecuteChanged() => canExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
