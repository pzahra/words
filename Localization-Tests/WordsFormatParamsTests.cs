using System.Globalization;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// <see cref="Words.FormatParams"/> is the one rule the XAML inlines and converters
/// share for filling a template from whatever they were handed, and
/// <see cref="Words.ConvertValue"/> is the converters' whole body — both hoisted to
/// Core so they are proven once. Explicit words and cultures; no globals.
/// </summary>
public class WordsFormatParamsTests {

	private static readonly IWords Templates = WordsBuilder.Create()
		.Load(new StringReader("value-en=English\n\n[plain]\nvalue=Hello {0}\n\n[two]\nvalue={0} and {1}\n\n[named]\nvalue={Name} since {When:d}\n"))
		.ToWords("en");

	private static readonly CultureInfo De = new("de-DE");

	[Fact]
	public void FormatParams_Null_IsNoArguments_TemplateAsIs() {
		Assert.Equal("Hello {0}", Templates.FormatParams("plain", null));
	}

	[Fact]
	public void FormatParams_ObjectArray_FillsPositionalPlaceholders_InTheCulture() {
		Assert.Equal("1,5 and 2", Templates.FormatParams("two", new object[] { 1.5, 2 }, De));
	}

	[Fact]
	public void FormatParams_TypedArray_IsPositionalToo() {
		Assert.Equal("1,5 and 2,5", Templates.FormatParams("two", new[] { 1.5, 2.5 }, De));
	}

	[Fact]
	public void FormatParams_AnyOtherObject_FillsNamedPlaceholders_InTheCulture() {
		var value = new { Name = "Ada", When = new DateTime(2026, 9, 11) };

		Assert.Equal("Ada since 11.09.2026", Templates.FormatParams("named", value, De));
	}

	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	[Fact]
	public void ConvertValue_StringParameter_FormatsTheValueIn() {
		// a scalar fills {0} by itself: the named-object path's slot 0 is the object
		Assert.Equal("Hello Ada", Templates.ConvertValue("Ada", "plain", De));
	}

	[Fact]
	public void ConvertValue_NullValue_FillsWithNothing_NotTheTemplate() {
		// a bound value that isn't there yet shows blanks, not markup
		Assert.Equal("Hello ", Templates.ConvertValue(null, "plain", De));
	}

	[Fact]
	public void ConvertValue_MissingParameter_WarnsAndHashesTheValue() {
		var logger = new CaptureLogger();

		Assert.Equal("#a value that runs on#", Templates.ConvertValue("a value that runs on and on", null, De, logger));
		Assert.Contains(logger.Messages, m => m.Contains("ConverterParameter not specified"));
	}

	[Fact]
	public void ConvertValue_NonStringParameter_WarnsAndHashesTheParameter() {
		var logger = new CaptureLogger();

		Assert.Equal("#42#", Templates.ConvertValue("x", 42, De, logger));
		Assert.Contains(logger.Messages, m => m.Contains("expecting string") && m.Contains("42"));
	}
}
