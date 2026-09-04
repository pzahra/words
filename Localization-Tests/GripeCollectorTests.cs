using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>The last test swaps the process logger, hence the shared collection.</summary>
[Collection("Words globals")]
public class GripeCollectorTests {
	[Fact]
	public void Listen_HearsOnlyWithinItsScope_AndNests() {
		var collector = new GripeCollector();
		collector.Warn("nobody listening");

		List<string> outer = [], inner = [];
		using (collector.Listen(outer)) {
			collector.Warn("one");
			using (collector.Listen(inner)) {
				collector.Error(new InvalidOperationException("boom"), "IMG:RES:x");
			}
			collector.Warn("two");
		}
		collector.Warn("nobody again");

		Assert.Equal(["one", "two"], outer);
		Assert.Equal(["IMG:RES:x: boom"], inner);
	}

	[Fact]
	public void Listen_IsPerThread() {
		var collector = new GripeCollector();
		List<string> mine = [], theirs = [];
		using var started = new ManualResetEventSlim();
		using var proceed = new ManualResetEventSlim();
		var other = new Thread(() => {
			using (collector.Listen(theirs)) {
				started.Set();
				proceed.Wait();
				collector.Warn("theirs");
			}
		});
		other.Start();
		started.Wait();
		using (collector.Listen(mine)) {
			collector.Warn("mine");
			proceed.Set();
			other.Join();
		}

		Assert.Equal(["mine"], mine);
		Assert.Equal(["theirs"], theirs);
	}

	[Fact]
	public void AsWordsLogger_CollectsWhatRenderingComplainsAbout() {
		//the Words globals collection: this test swaps the process logger
		var collector = new GripeCollector();
		ITakeException previous = Words.Logger;
		Words.Logger = collector;
		try {
			var provider = WordsProvider.Empty();
			List<string> gripes = [];
			using (collector.Listen(gripes)) {
				Assert.Equal("#nowhere#", Words.RenderKey(provider, "nowhere"));
			}
			Assert.Equal(["WORDS:KEY:`nowhere`"], gripes);
		}
		finally {
			Words.Logger = previous;
		}
	}
}
