using Avalonia.Headless.XUnit;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The Avalonia <see cref="WordsInline"/> twin of <see cref="WordsInlineTests"/>:
/// a rebuild must be transactional, so a formatting failure leaves the previous
/// content standing rather than blanking the inline. Swaps the Words.Known
/// global, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class AvaWordsInlineTests {

	[AvaloniaFact]
	public void FailedRebuild_LeavesPreviousContentStanding() {
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
		}
		finally {
			Words.Known = original;
		}
	}

	[AvaloniaFact]
	public void EmptyKey_ClearsContent() {
		var original = Words.Known;
		try {
			Words.Known = WordsBuilder.Create()
				.Load(new StringReader("value-en=English\n\n[greet]\nvalue=Hello\n"))
				.ToWords("en");

			var w = new WordsInline { Key = "greet" };
			Assert.NotEmpty(w.Inlines);
			w.Key = "";
			Assert.Empty(w.Inlines);
		}
		finally {
			Words.Known = original;
		}
	}
}
