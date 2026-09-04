using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WordsEdit.ViewModels;

namespace WordsEdit.Utils;

/// <summary>A dialog with per-property validation errors, for the views' red text. UI thread only.</summary>
public abstract class DataViewModelBase : DialogViewModel, INotifyDataErrorInfo {
	private readonly Dictionary<string, List<string>> errors = [];

	public bool HasErrors => errors.Any(pair => pair.Value.Count > 0);

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

	/// <summary>Adds an error for the property (a repeat is ignored) and raises <see cref="ErrorsChanged"/>.</summary>
	protected void SetError(string message, [CallerMemberName] string propertyName = "") {
		ArgumentNullException.ThrowIfNull(message);
		if (!errors.TryGetValue(propertyName, out var list)) {
			errors[propertyName] = list = [];
		}
		if (!list.Contains(message)) {
			list.Add(message);
		}
		RaiseErrorsChanged(propertyName);
	}

	/// <summary>The property's errors; every error when <paramref name="propertyName"/> is empty.</summary>
	public IEnumerable GetErrors(string? propertyName)
		=> string.IsNullOrEmpty(propertyName)
			? errors.Values.SelectMany(list => list)
			: errors.GetValueOrDefault(propertyName, []);

	/// <summary>Drops the property's errors, raising <see cref="ErrorsChanged"/> if it had any.</summary>
	protected void ClearErrors(string propertyName) {
		if (errors.Remove(propertyName)) {
			RaiseErrorsChanged(propertyName);
		}
	}

	/// <summary>Drops every error, raising <see cref="ErrorsChanged"/> for each property that had one.</summary>
	protected void ClearAllErrors() {
		List<string> had = [.. errors.Keys];
		errors.Clear();
		had.ForEach(RaiseErrorsChanged);
	}

	protected void RaiseErrorsChanged(string propertyName)
		=> ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));

	protected bool IsValid(string propertyName) => !errors.TryGetValue(propertyName, out var items) || items.Count == 0;

	/// <summary>Validates one property, calling <see cref="SetError"/> for each failure; true when valid.</summary>
	protected abstract bool Validate([CallerMemberName] string propertyName = "");
}
