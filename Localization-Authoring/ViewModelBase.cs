using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PatTech.Localization.Authoring {
	/// <summary>
	/// Base ViewModel for use with MVVM architecture.
	/// </summary>
	public abstract class ViewModelBase : INotifyPropertyChanged {
		/// <inheritdoc/>
		public event PropertyChangedEventHandler? PropertyChanged;

		/// <summary>
		/// Change a property value and trigger a PropertyChanged event if the value has changed.
		/// </summary>
		/// <typeparam name="T">The property type.</typeparam>
		/// <param name="backer">The backing field.</param>
		/// <param name="value">The new value.</param>
		/// <param name="propertyName">The name of the property that changed.</param>
		/// <returns>True if the value changed and an event was fired.</returns>
		protected virtual bool ChangeProperty<T>(ref T backer, T value, [CallerMemberName] string propertyName = "") {
			if (EqualityComparer<T>.Default.Equals(backer, value)) return false;
			backer = value;
			return AffectProperty(propertyName);
		}

		/// <summary>
		/// Sends a PropertyChanged event on a specified property. Call when an
		/// action causes a calculated property to return a new value.
		/// </summary>
		/// <param name="propertyName">Name of a property that changed.</param>
		/// <returns>True.</returns>
		protected virtual bool AffectProperty(string propertyName) {
			PropertyChanged?.Invoke(this, new(propertyName));
			return true;
		}
	}
}
