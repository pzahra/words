using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PatTech.Localization {
	/// <summary>
	/// Live language switching (SPEC: Live language switching). Opt-in: a builder kept
	/// alive as <see cref="Live"/> re-flattens on <see cref="SwitchLanguage"/>, and
	/// everything that registered through <see cref="Watch"/> is refreshed when
	/// <see cref="Known"/> is assigned. Off, the hooks return at once and the dictionary
	/// is the snapshot it always was.
	/// </summary>
	public static partial class Words {
		private static WordsBuilder? live;
		private static readonly object watchLock = new();
		//the registry: keyed weakly by the watcher, so an entry lives exactly as long as
		//whatever else holds its watcher and no longer, with nothing to sweep
		private static readonly ConditionalWeakTable<IKnowWords, Registration> watchers = new();

		/// <summary>Where a watcher registered: the context to post its refresh to, and the thread, to refresh inline on.</summary>
		private sealed class Registration(SynchronizationContext? context, int threadId) {
			public SynchronizationContext? Context { get; } = context;
			public int ThreadId { get; } = threadId;
		}

		/// <summary>
		/// The builder kept alive for live language switching, or <see langword="null"/>
		/// — the default — while the dictionary is a snapshot. <see cref="WordsBuilder.Live"/>
		/// sets it, before <see cref="WordsBuilder.Digest(string)"/>; while it is set,
		/// <see cref="Watch"/> registers and every assignment of <see cref="Known"/>
		/// refreshes what registered. Setting it back to <see langword="null"/> turns live
		/// mode off.
		/// </summary>
		public static WordsBuilder? Live {
			get => Volatile.Read(ref live);
			set => Volatile.Write(ref live, value);
		}

		/// <summary>
		/// Re-flattens the retained sources for <paramref name="languageCode"/> and installs
		/// the result as <see cref="Known"/> — the same flatten startup ran, without touching
		/// disk — which refreshes every watcher. Callable from any thread.
		/// </summary>
		/// <param name="languageCode">The language to switch to, e.g. <c>"de"</c>.</param>
		/// <returns>The installed dictionary.</returns>
		/// <exception cref="InvalidOperationException">Live mode is off: nothing was kept to re-flatten.</exception>
		public static IWords SwitchLanguage(string languageCode) {
			var builder = Live ?? throw new InvalidOperationException("Live switching is off: chain WordsBuilder.Live() before Digest.");
			return builder.Digest(languageCode);
		}

		/// <summary>
		/// Registers <paramref name="watcher"/> to be refreshed when <see cref="Known"/> is
		/// assigned. The hold is weak, so the watcher lives exactly as long as whatever
		/// else holds it; its <see cref="IKnowWords.Refresh"/> runs on this thread, or is
		/// posted to the synchronization context current now when the swap comes from
		/// another. Calling again is cheap and re-homes the watcher to the calling thread.
		/// Off, returns at once, so the call site needs no condition of its own.
		/// </summary>
		/// <param name="watcher">The holder of rendered Words to refresh.</param>
		public static void Watch(IKnowWords watcher) {
			ArgumentNullException.ThrowIfNull(watcher);
			if (Live is null) {
				return;
			}
			int here = Environment.CurrentManagedThreadId;
			if (watchers.TryGetValue(watcher, out var registered) && registered.ThreadId == here) {
				return;
			}
			lock (watchLock) {
				watchers.AddOrUpdate(watcher, new Registration(SynchronizationContext.Current, here));
			}
		}

		//Known was assigned in live mode: refresh a snapshot of the registry (a refresh
		//may register new watchers), each watcher on the thread it registered on
		private static void RefreshWatchers() {
			KeyValuePair<IKnowWords, Registration>[] snapshot;
			lock (watchLock) {
				snapshot = [.. watchers];
			}
			int here = Environment.CurrentManagedThreadId;
			foreach (var group in snapshot.GroupBy(entry => entry.Value.ThreadId == here ? null : entry.Value.Context)) {
				IKnowWords[] batch = [.. group.Select(entry => entry.Key)];
				if (group.Key is null) {
					RefreshBatch(batch);
				}
				else {
					//the switch came from another thread: that thread's cultures moved, this
					//one's follow when the refresh lands on it
					group.Key.Post(_ => {
						Known.SetCulture();
						RefreshBatch(batch);
					}, null);
				}
			}
		}

		private static void RefreshBatch(IKnowWords[] batch) {
			foreach (var watcher in batch) {
				try {
					watcher.Refresh();
				}
				catch (Exception e) {
					//one bad listener never aborts the relocalization
					Logger.Error(e, $"WORDS:REFRESH:`{watcher.GetType().Name}`");
				}
			}
		}
	}
}
