using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Sample_Wpf.ViewModels {
	public abstract class ViewModelBase : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		protected virtual bool ChangeProperty<T>(ref T backer, T value, [CallerMemberName] string propertyName = "") {
			if (EqualityComparer<T>.Default.Equals(backer, value)) return false;
			backer = value;
			return AffectProperty(propertyName);
		}
		protected virtual bool AffectProperty(string propertyName) {
			PropertyChanged?.Invoke(this, new(propertyName));
			return true;
		}
	}
}
