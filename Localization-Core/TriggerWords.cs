using System.ComponentModel;
using System.Threading;

namespace PatTech.Localization {
	/// <summary>
	/// The pulse a live binding listens to (SPEC: Live language switching): one for the
	/// whole process, keyless, carrying no text. In live mode every swap of
	/// <see cref="Words.Known"/> raises <see cref="PropertyChanged"/> for <see cref="Pulse"/>,
	/// so a multi-binding that has it as a leg beside the binding it wraps converts again,
	/// though the wrapped source did not move. The framework packages' <c>{l:Words}</c>
	/// adds the leg; an app can bind to it the same way.
	/// </summary>
	public sealed class TriggerWords : INotifyPropertyChanged, IKnowWords {
		/// <summary>The one trigger; it lives as long as the process.</summary>
		public static TriggerWords Instance { get; } = new();

		private TriggerWords() { }

		/// <summary>How many swaps it has felt: the value a leg reads, different after every one.</summary>
		public int Pulse { get; private set; }

		/// <inheritdoc/>
		public event PropertyChangedEventHandler? PropertyChanged;

		/// <summary>
		/// Registers the trigger with the live registry for the calling thread and returns it;
		/// off, it is returned unregistered and never pulses. Call it on the thread whose
		/// bindings listen. The one trigger serves every thread, so a call from a thread
		/// with no synchronization context (a pool thread, a service at startup) leaves it
		/// where a UI thread registered it.
		/// </summary>
		public static TriggerWords Watch() {
			if (SynchronizationContext.Current is not null || !Words.IsWatched(Instance)) {
				Words.Watch(Instance);
			}
			return Instance;
		}

		/// <summary>The dictionary was swapped: pulse.</summary>
		public void Refresh() {
			Pulse++;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pulse)));
		}
	}
}
