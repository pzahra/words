using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Documents;
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

	[Fact]
	public void PositionalArgs_FormatWithCurrentCulture_NotUiCulture() {
		RunSta<object?>(() => {
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

				var run = Assert.IsType<Run>(w.Inlines.FirstInline);
				Assert.Equal("1,5", run.Text); // de-DE decimal comma, not en-US "1.5"
				return null;
			}
			finally {
				CultureInfo.CurrentCulture = originalCulture;
				CultureInfo.CurrentUICulture = originalUiCulture;
				Words.Known = originalKnown;
			}
		});
	}

	[Fact]
	public void Digest_WpfOverload_InstallsKnownAndAppliesCulture() {
		// The WPF Digest(lang, bool) extension is the core Digest — build, install as
		// Words.Known, sync the thread cultures — plus an optional one-shot repoint of
		// FrameworkElement.Language. That override is process-global, so we only
		// assert the safely-repeatable half here and leave the flag off.
		var originalKnown = Words.Known;
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
		var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
		try {
			var words = WordsBuilder.Create()
				.Load(new StringReader("value-de=Deutsch\n\n[k]\nvalue=v\n"))
				.Digest("de", includeFrameworkElements: false); // core Digest has no bool, so this is the WPF overload

			Assert.Same(words, Words.Known);
			var expected = CultureInfo.CreateSpecificCulture("de");
			Assert.Equal(expected, CultureInfo.CurrentCulture);
			Assert.Equal(expected, CultureInfo.DefaultThreadCurrentCulture);
		}
		finally {
			Words.Known = originalKnown; // re-applies the culture Known carries, hence the restores below
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			CultureInfo.DefaultThreadCurrentCulture = originalDefault;
			CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
		}
	}

	[Fact]
	public void Digest_WpfOverload_IncludeFrameworkElements_RepointsLanguage_ForControlsAndFlowContent() {
		// The one test that flips the flag: the override is process-global and one-shot,
		// so it runs once, here, and every element created afterwards in this process
		// defaults to the culture it set. Controls (FrameworkElement) and flow content
		// (TextElement: a Run is not a FrameworkElement, and a default is not inherited)
		// must both follow, or a bound Run would still format en-US.
		RunSta<object?>(() => {
			var originalKnown = Words.Known;
			var originalCulture = CultureInfo.CurrentCulture;
			var originalUiCulture = CultureInfo.CurrentUICulture;
			var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
			var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
			try {
				WordsBuilder.Create()
					.Load(new StringReader("value-de=Deutsch\n\n[k]\nvalue=v\n"))
					.Digest("de", includeFrameworkElements: true);

				var expected = CultureInfo.CreateSpecificCulture("de");
				Assert.Equal(expected, new TextBlock().Language.GetSpecificCulture()); // a control
				Assert.Equal(expected, new Run().Language.GetSpecificCulture());       // flow content
				return null;
			}
			finally {
				Words.Known = originalKnown;
				CultureInfo.CurrentCulture = originalCulture;
				CultureInfo.CurrentUICulture = originalUiCulture;
				CultureInfo.DefaultThreadCurrentCulture = originalDefault;
				CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
			}
		});
	}
}
