using System.Runtime.ExceptionServices;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The WPF <see cref="WordsInline"/> rebuilds its content when Key/Params change.
/// It must do so transactionally: a formatting failure leaves the previous
/// content standing rather than blanking the inline. <see cref="AvaWordsInlineTests"/>
/// is the twin; both swap the Words.Known global, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class WordsInlineTests {

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap.</summary>
	private static T RunSta<T>(Func<T> func) {
		T result = default!;
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try { result = func(); }
			catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
		return result;
	}

	[Fact]
	public void FailedRebuild_LeavesPreviousContentStanding() {
		RunSta<object?>(() => {
			var original = Words.Known;
			try {
				Words.Known = WordsBuilder.Create()
					.Load(new StringReader("value-en=English\n\n[greet]\nvalue=Hello {0} {1}\n"))
					.ToWords("en");

				var w = new WordsInline { Key = "greet" };
				w.Params = new object[] { "a", "b" };   // "Hello a b"
				Assert.NotEmpty(w.Inlines);
				var before = w.Inlines.Count;

				// one argument for a two-placeholder string: string.Format throws
				try { w.Params = new object[] { "onlyone" }; }
				catch { /* expected; the point is what survives it */ }

				Assert.NotEmpty(w.Inlines);            // not blanked
				Assert.Equal(before, w.Inlines.Count); // still the previous render
				return null;
			}
			finally {
				Words.Known = original;
			}
		});
	}

	[Fact]
	public void EmptyKey_ClearsContent() {
		RunSta<object?>(() => {
			var original = Words.Known;
			try {
				Words.Known = WordsBuilder.Create()
					.Load(new StringReader("value-en=English\n\n[greet]\nvalue=Hello\n"))
					.ToWords("en");

				var w = new WordsInline { Key = "greet" };
				Assert.NotEmpty(w.Inlines);
				w.Key = "";
				Assert.Empty(w.Inlines);
				return null;
			}
			finally {
				Words.Known = original;
			}
		});
	}
}
