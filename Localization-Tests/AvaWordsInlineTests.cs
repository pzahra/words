using System.Globalization;
using Avalonia.Controls.Documents;
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

	[AvaloniaFact]
	public void PositionalArgs_FormatWithCurrentCulture_NotUiCulture() {
		var originalKnown = Words.Known;
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		try {
			Words.Known = WordsBuilder.Create()
				.Load(new StringReader("value-en=English\n\n[n]\nvalue={0}\n"))
				.ToWords("en"); // also sets the thread cultures to en

			// text language en (UI culture), but numbers follow CurrentCulture:
			// German here, to prove the positional path honors CurrentCulture
			CultureInfo.CurrentUICulture = new CultureInfo("en-US");
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");

			var w = new WordsInline { Key = "n" };
			w.Params = new object[] { 1.5 };

			var run = Assert.IsType<Run>(w.Inlines[0]);
			Assert.Equal("1,5", run.Text); // de-DE decimal comma, not en-US "1.5"
		}
		finally {
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			Words.Known = originalKnown;
		}
	}
}
