using System.Runtime.ExceptionServices;
using System.Windows;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Covers the ready-made converter resources shipped as Converters.xaml: the
/// dictionary must load from the pack URI and every instance must construct.
/// (The Avalonia twin mirrors the same dictionary; it has no test host here.)
/// </summary>
public class ConverterResourceTests {

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap.</summary>
	private static T RunSta<T>(Func<T> func) {
		T result = default!;
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try {
				result = func();
			}
			catch (Exception e) {
				error = ExceptionDispatchInfo.Capture(e);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
		return result;
	}

	[Fact]
	public void ConvertersDictionary_LoadsWithEveryInstance() {
		RunSta<object?>(() => {
			// pack: URIs need the Application infrastructure spun up once
			_ = Application.Current ?? new Application();

			var dictionary = new ResourceDictionary {
				Source = new Uri("pack://application:,,,/PatTech.Localization.WPF;component/Converters.xaml"),
			};

			Assert.IsType<MarkdownConverter>(dictionary["WordsMarkdown"]);
			Assert.IsType<WordsConverter>(dictionary["WordsFormat"]);
			Assert.IsType<EnumDescriptionConverter>(dictionary["WordsEnumDescription"]);
			var joined = Assert.IsType<FlagsDescriptionConverter>(dictionary["WordsFlagsDescription"]);
			Assert.False(joined.AsArray);
			var list = Assert.IsType<FlagsDescriptionConverter>(dictionary["WordsFlagsDescriptionList"]);
			Assert.True(list.AsArray);
			Assert.IsType<ArrayMultiConverter>(dictionary["WordsParamsArray"]);
			return null;
		});
	}

	[Fact]
	public void EveryConverter_ConvertBack_ThrowsNotSupported() {
		// one-way converters, one exception type across the board
		var culture = System.Globalization.CultureInfo.InvariantCulture;
		Assert.Throws<NotSupportedException>(() => new MarkdownConverter().ConvertBack(null, typeof(object), null, culture));
		Assert.Throws<NotSupportedException>(() => new WordsConverter().ConvertBack(null!, typeof(object), null!, culture));
		Assert.Throws<NotSupportedException>(() => new EnumDescriptionConverter().ConvertBack(null, typeof(object), null, culture));
		Assert.Throws<NotSupportedException>(() => new FlagsDescriptionConverter().ConvertBack(null, typeof(object), null, culture));
		Assert.Throws<NotSupportedException>(() => new ArrayMultiConverter().ConvertBack(null, [typeof(object)], null, culture));
	}
}
