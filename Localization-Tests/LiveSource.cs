using System.ComponentModel;

namespace PatTech.Localization.Tests;

/// <summary>
/// A view model for the bound shapes of <c>{l:Words}</c>: a key to look up, and a count
/// to format. Top-level and public, so compiled AXAML can name it as its data type.
/// </summary>
public sealed class LiveSource : INotifyPropertyChanged {
	private string key = "k";
	private double count = 5;

	public string Key {
		get => key;
		set { key = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Key))); }
	}

	public double Count {
		get => count;
		set { count = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count))); }
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}
