using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Documents;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The WPF markdown converter's target-type routing: an <see cref="Inline"/>
/// target gets the inline directly, any other target (TextBlock, object) gets it
/// wrapped in a <see cref="TextBlock"/>. The test used to be reversed.
/// </summary>
public class MarkdownConverterTests {

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
	public void Convert_InlineTarget_ReturnsTheInlineDirectly() {
		RunSta<object?>(() => {
			var result = new MarkdownConverter().Convert("**hi**", typeof(Span), null, CultureInfo.InvariantCulture);
			Assert.IsAssignableFrom<Inline>(result); // an Inline, not wrapped in a TextBlock
			return null;
		});
	}

	[Fact]
	public void Convert_TextBlockTarget_WrapsInATextBlock() {
		RunSta<object?>(() => {
			var result = new MarkdownConverter().Convert("**hi**", typeof(TextBlock), null, CultureInfo.InvariantCulture);
			Assert.IsType<TextBlock>(result);
			return null;
		});
	}

	[Fact]
	public void Convert_ObjectTarget_WrapsInATextBlock() {
		RunSta<object?>(() => {
			var result = new MarkdownConverter().Convert("hi", typeof(object), null, CultureInfo.InvariantCulture);
			Assert.IsType<TextBlock>(result);
			return null;
		});
	}

	[Fact]
	public void ConvertBack_ThrowsNotSupported() {
		Assert.Throws<NotSupportedException>(() =>
			new MarkdownConverter().ConvertBack("x", typeof(string), null, CultureInfo.InvariantCulture));
	}
}
