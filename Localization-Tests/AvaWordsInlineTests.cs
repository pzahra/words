using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

	// The children of a WordsInline are its arguments: a stack of Bindings needs no
	// WordsInline.Params / MultiBinding / ArrayMultiConverter nest to become the
	// positional array, and it follows its sources. A constant rides as a Binding with
	// a Source and no path — the one rule both twins share, since WPF admits a Binding
	// child only in a Collection<BindingBase>.

	private sealed class Model : INotifyPropertyChanged {
		private string first = "a";
		public string First {
			get => first;
			set { first = value; PropertyChanged?.Invoke(this, new(nameof(First))); }
		}
		public string Second { get; set; } = "b";
		public event PropertyChangedEventHandler? PropertyChanged;
	}

	private static string Text(WordsInline w) => string.Concat(w.Inlines.OfType<Run>().Select(r => r.Text));

	private static WordsInline Greeting(string value, object? dataContext = null) {
		Words.Known = WordsBuilder.Create()
			.Load(new StringReader($"value-en=English\n\n[greet]\nvalue={value}\n"))
			.ToWords("en");
		return new WordsInline { Key = "greet", DataContext = dataContext };
	}

	[AvaloniaFact]
	public void ChildBindings_BecomePositionalArgs_AndFollowTheirSources() {
		var original = Words.Known;
		try {
			var model = new Model();
			var w = Greeting("Hello {0} {1}", model);
			w.Args.Add(new Binding(nameof(Model.First)));
			w.Args.Add(new Binding(nameof(Model.Second)));
			Dispatcher.UIThread.RunJobs();   // the children resolve on the next dispatcher turn
			Assert.Equal("Hello a b", Text(w));

			model.First = "x";
			Dispatcher.UIThread.RunJobs();
			Assert.Equal("Hello x b", Text(w));
		}
		finally {
			Words.Known = original;
		}
	}

	[AvaloniaFact]
	public void BareMultiBindingChild_GetsTheArrayConverter() {
		var original = Words.Known;
		try {
			var w = Greeting("Hello {0} {1}", new Model());
			var multi = new MultiBinding();
			multi.Bindings.Add(new Binding(nameof(Model.First)));
			multi.Bindings.Add(new Binding(nameof(Model.Second)));
			w.Args.Add(multi);   // no converter of its own
			Dispatcher.UIThread.RunJobs();
			Assert.Equal("Hello a b", Text(w));
		}
		finally {
			Words.Known = original;
		}
	}

	[AvaloniaFact]
	public void OneBoundObject_IsStillTheNamedObject() {
		var original = Words.Known;
		try {
			var w = Greeting("Hi {Name}");
			w.Args.Add(new Binding { Source = new { Name = "Pat" } });   // no path: the object itself, not wrapped in an array
			Dispatcher.UIThread.RunJobs();
			Assert.Equal("Hi Pat", Text(w));
		}
		finally {
			Words.Known = original;
		}
	}

	[AvaloniaFact]
	public void ConstantsAsBindingSources_BecomePositionalArgs() {
		var original = Words.Known;
		try {
			var w = Greeting("Hello {0} {1}");
			w.Args.Add(new Binding { Source = "a" });   // a constant is a Binding with a Source
			w.Args.Add(new Binding { Source = "b" });
			Dispatcher.UIThread.RunJobs();
			Assert.Equal("Hello a b", Text(w));
		}
		finally {
			Words.Known = original;
		}
	}

	[AvaloniaFact]
	public void ConstantsAndBindings_MixOnOneMultiBinding() {
		var original = Words.Known;
		try {
			var w = Greeting("Hello {0} {1}", new Model());
			w.Args.Add(new Binding { Source = "lit" });
			w.Args.Add(new Binding(nameof(Model.Second)));
			Dispatcher.UIThread.RunJobs();
			Assert.Equal("Hello lit b", Text(w));
		}
		finally {
			Words.Known = original;
		}
	}
}
