using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Live language switching (SPEC: Live language switching): opt in before Digest and a
/// resolved <see cref="LazyWords"/> follows <see cref="Words.SwitchLanguage"/>; off by
/// default, a swap of <see cref="Words.Known"/> notifies nothing. The registry holds
/// weakly, posts to the context a watcher registered on, and survives a watcher that
/// throws. Swaps the Words.Known global and the thread cultures, hence the shared
/// collection.
/// </summary>
[Collection("Words globals")]
public class LiveWordsTests {
	private const string Ini =
		"value-en=English\n" +
		"value-de=Deutsch\n" +
		"\n" +
		"[k]\n" +
		"value-en=English value\n" +
		"value-de=Deutscher Wert\n";

	[Fact]
	public void SwitchLanguage_RelocalizesAResolvedHolder_AndNotifies() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var words = new LazyWords("k");
		Assert.Equal("English value", words.Value);
		var raised = new List<string?>();
		words.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

		IWords installed = Words.SwitchLanguage("de");

		Assert.Same(installed, Words.Known);
		Assert.Equal("Deutscher Wert", words.Value);
		Assert.Equal(new[] { nameof(LazyWords.Value) }, raised);
		Assert.Equal(CultureInfo.CreateSpecificCulture("de"), CultureInfo.CurrentUICulture);
	}

	[Fact]
	public void APlainAssignment_RelocalizesJustAsWell() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini).Live();
		builder.Digest("en");
		var words = new LazyWords("k");
		Assert.Equal("English value", words.Value);

		Words.Known = builder.ToWords("de");

		Assert.Equal("Deutscher Wert", words.Value);
	}

	[Fact]
	public void OffByDefault_ASwapNotifiesNothing_AndTheCacheStands() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini);
		builder.Digest("en");
		Assert.Null(Words.Live);
		var words = new LazyWords("k");
		Assert.Equal("English value", words.Value);
		var raised = 0;
		words.PropertyChanged += (_, _) => raised++;

		Words.Known = builder.ToWords("de");

		Assert.Equal(0, raised);
		Assert.Equal("English value", words.Value); //the snapshot: cached on first read
		Assert.Throws<InvalidOperationException>(() => Words.SwitchLanguage("de"));
	}

	[Fact]
	public void Live_False_LetsTheBuilderGo() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini).Live();
		Assert.Same(builder, Words.Live);
		builder.Live(false);
		Assert.Null(Words.Live);
	}

	[Fact]
	public void ALiteralNeverWatches() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
#pragma warning disable PTL001 // the point: text, not a key
		LazyWords literal = "as written";
		var assigned = new LazyWords("k") { Value = "overridden" };
#pragma warning restore PTL001
		Assert.Equal("as written", literal.Value);
		var raised = 0;
		literal.PropertyChanged += (_, _) => raised++;
		assigned.PropertyChanged += (_, _) => raised++;

		Words.SwitchLanguage("de");

		Assert.Equal(0, raised);
		Assert.Equal("as written", literal.Value);
		Assert.Equal("overridden", assigned.Value);
	}

	[Fact]
	public void TheTickle_PulsesOnEverySwap() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini).Live();
		builder.Digest("en");
		WordsTickle tickle = WordsTickle.Watch();
		var pulse = tickle.Pulse;
		var raised = new List<string?>();
		void Heard(object? sender, PropertyChangedEventArgs e) => raised.Add(e.PropertyName);
		tickle.PropertyChanged += Heard; //the tickle is the process's: let go of it after
		try {
			Words.SwitchLanguage("de");
			Words.Known = builder.ToWords("en");

			Assert.Equal(pulse + 2, tickle.Pulse);
			Assert.Equal(new[] { nameof(WordsTickle.Pulse), nameof(WordsTickle.Pulse) }, raised);
		}
		finally {
			tickle.PropertyChanged -= Heard;
		}
	}

	[Fact]
	public void TheTickle_IsStillWhenOff() {
		using var globals = new WordsGlobals();
		WordsBuilder builder = WordsBuilder.Create().LoadString(Ini);
		builder.Digest("en");
		WordsTickle tickle = WordsTickle.Watch(); //off: a no-op
		var pulse = tickle.Pulse;

		Words.Known = builder.ToWords("de");

		Assert.Equal(pulse, tickle.Pulse);
	}

	[Fact]
	public void Of_SharesOneHolderPerKey() {
		LazyWords a = LazyWords.Of("k");
		Assert.Same(a, LazyWords.Of("k"));
		Assert.NotSame(a, LazyWords.Of("other"));
		Assert.Equal("k", a.Key);
	}

	[Fact]
	public void AnUnreferencedHolder_IsCollected_AndNotRefreshed() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var (weak, raised) = Watched();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.False(weak.TryGetTarget(out _)); //nothing else held it, and the registry did not
		Words.SwitchLanguage("de");             //the husk is dropped without a word
		Assert.Empty(raised);
	}

	//built apart from the test body so the JIT keeps no reference of its own alive
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (WeakReference<LazyWords> Weak, List<string?> Raised) Watched() {
		var words = new LazyWords("k");
		var raised = new List<string?>();
		words.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
		Assert.Equal("English value", words.Value); //resolved, so registered
		return (new WeakReference<LazyWords>(words), raised);
	}

	[Fact]
	public void AWatcherThatThrows_IsReportedAndSkipped() {
		using var globals = new WordsGlobals();
		var log = new Log();
		Words.Logger = log;
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var bad = new Thrower();
		Words.Watch(bad);
		var words = new LazyWords("k");
		Assert.Equal("English value", words.Value);

		Words.SwitchLanguage("de");

		Assert.Equal(1, bad.Calls);
		Assert.Contains(log.Errors, message => message.Contains("WORDS:REFRESH"));
		Assert.Equal("Deutscher Wert", words.Value); //the one after it still refreshed
	}

	private sealed class Thrower : IKnowWords {
		public int Calls;
		public void Refresh() {
			Calls++;
			throw new InvalidOperationException("boom");
		}
	}

	private sealed class Log : ITakeException {
		public List<string> Errors { get; } = [];
		public void Warn(string text) { }
		public void Error(Exception exception, string message) => Errors.Add(message);
	}

	[Fact]
	public void ASwitchFromAnotherThread_ReachesTheWatcherOnItsOwnContext() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create().LoadString(Ini).Live().Digest("en");
		var posts = new Posts();
		SynchronizationContext? previous = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(posts);
		LazyWords words;
		try {
			words = new LazyWords("k");
			Assert.Equal("English value", words.Value); //registered under the queue
		}
		finally {
			SynchronizationContext.SetSynchronizationContext(previous);
		}
		var raised = 0;
		words.PropertyChanged += (_, _) => raised++;

		Task.Run(() => Words.SwitchLanguage("de")).Wait();

		Assert.Equal(0, raised);          //nothing ran on the switching thread
		Assert.Equal(1, posts.Drain());   //one post for this context, however many watchers
		Assert.Equal(1, raised);
		Assert.Equal("Deutscher Wert", words.Value);
	}

	/// <summary>A context that queues its posts until drained, standing in for a dispatcher.</summary>
	private sealed class Posts : SynchronizationContext {
		private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

		public override void Post(SendOrPostCallback d, object? state) {
			lock (queue) {
				queue.Enqueue((d, state));
			}
		}

		public int Drain() {
			var count = 0;
			while (true) {
				(SendOrPostCallback Callback, object? State) post;
				lock (queue) {
					if (queue.Count == 0) {
						return count;
					}
					post = queue.Dequeue();
				}
				post.Callback(post.State);
				count++;
			}
		}
	}
}
