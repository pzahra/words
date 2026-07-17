using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WordsEdit.Utils {
	public abstract class DataViewModelBase : ViewModelBase, INotifyDataErrorInfo {
		private readonly Dictionary<string, List<string>> errors = [];
		private readonly Lock errorsLock = new Lock();

		public bool HasErrors {
			get {
				lock (errorsLock) {
					return errors.Any(kv => kv.Value?.Count > 0);
				}
			}
		}

		public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

		/// <summary>
		/// Add an error message for the given property and raise <see cref="ErrorsChanged"/>.
		/// Duplicate messages for the same property are ignored.
		/// </summary>
		protected void SetError(string message, [CallerMemberName] string propertyName = "") {
			ArgumentNullException.ThrowIfNull(message);
			propertyName ??= string.Empty;

			lock (errorsLock) {
				if (!errors.TryGetValue(propertyName, out var list)) {
					list = [];
					errors[propertyName] = list;
				}
				if (!list.Contains(message)) {
					list.Add(message);
				}
			}

			RaiseErrorsChanged(propertyName);
		}

		/// <summary>
		/// Return errors for a specific property or all errors when <paramref name="propertyName"/> is null/empty.
		/// Always returns a snapshot (safe to enumerate).
		/// </summary>
		public IEnumerable GetErrors(string? propertyName) {
			lock (errorsLock) {
				if (string.IsNullOrEmpty(propertyName)) {
					return errors.Values.SelectMany(v => v);
				}

				if (errors.TryGetValue(propertyName, out var list)) {
					return list;
				}

				return Enumerable.Empty<string>();
			}
		}

		/// <summary>
		/// Remove all errors for the given property and raise <see cref="ErrorsChanged"/> if anything changed.
		/// </summary>
		protected void ClearErrors(string propertyName) {
			propertyName ??= string.Empty;

			bool removed;
			lock (errorsLock) {
				removed = errors.Remove(propertyName);
			}

			if (removed) RaiseErrorsChanged(propertyName);
		}

		/// <summary>
		/// Remove all errors and raise <see cref="ErrorsChanged"/> for each property that had errors.
		/// </summary>
		protected void ClearAllErrors() {
			List<string> keys;
			lock (errorsLock) {
				keys = [.. errors.Keys];
				errors.Clear();
			}

			foreach (var k in keys) RaiseErrorsChanged(k);
		}

		/// <summary>
		/// Raise the <see cref="ErrorsChanged"/> event for a property.
		/// </summary>
		protected void RaiseErrorsChanged(string propertyName) {
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Checks that the specified property has no errors.
		/// </summary>
		/// <param name="propertyName">The property name to check for.</param>
		/// <returns>True if the specified property has no errors.</returns>
		protected bool IsValid(string propertyName) => !errors.TryGetValue(propertyName, out var items) || items.Count == 0;

		/// <summary>
		/// Concrete view models implement this to validate a single property.
		/// Should call <see cref="SetError(string, string)"/> for any validation failures and return true when valid.
		/// </summary>
		protected abstract bool Validate([CallerMemberName] string propertyName = "");
	}
}
