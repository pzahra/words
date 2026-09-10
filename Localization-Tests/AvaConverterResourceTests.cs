using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Covers the ready-made converter resources shipped as Converters.axaml —
/// the twin of the WPF <see cref="ConverterResourceTests"/>: the dictionary
/// must load from the avares URI and every instance must construct.
/// </summary>
public class AvaConverterResourceTests {

	[AvaloniaFact]
	public void ConvertersDictionary_LoadsWithEveryInstance() {
		var dictionary = Assert.IsAssignableFrom<ResourceDictionary>(
			AvaloniaXamlLoader.Load(new Uri("avares://PatTech.Localization.Avalonia/Converters.axaml")));

		Assert.IsType<MarkdownConverter>(dictionary["WordsMarkdown"]);
		Assert.IsType<WordsConverter>(dictionary["WordsFormat"]);
		Assert.IsType<EnumDescriptionConverter>(dictionary["WordsEnumDescription"]);
		var joined = Assert.IsType<FlagsDescriptionConverter>(dictionary["WordsFlagsDescription"]);
		Assert.False(joined.AsArray);
		var list = Assert.IsType<FlagsDescriptionConverter>(dictionary["WordsFlagsDescriptionList"]);
		Assert.True(list.AsArray);
		Assert.IsType<ArrayMultiConverter>(dictionary["WordsParamsArray"]);
	}

	[AvaloniaFact]
	public void Markdown_InlineTarget_ReturnsTheInlineDirectly() {
		var result = new MarkdownConverter().Convert("**hi**", typeof(Span), null, CultureInfo.InvariantCulture);
		Assert.IsAssignableFrom<Inline>(result); // an Inline, not wrapped in a TextBlock
	}

	[AvaloniaFact]
	public void Markdown_TextBlockTarget_WrapsInATextBlock() {
		var result = new MarkdownConverter().Convert("**hi**", typeof(TextBlock), null, CultureInfo.InvariantCulture);
		Assert.IsType<TextBlock>(result);
	}

	[Fact]
	public void Converters_ConvertBack_ThrowNotSupported() {
		// one-way converters, one exception type (Avalonia's IMultiValueConverter has no ConvertBack)
		var culture = CultureInfo.InvariantCulture;
		Assert.Throws<NotSupportedException>(() => new MarkdownConverter().ConvertBack(null, typeof(object), null, culture));
		Assert.Throws<NotSupportedException>(() => new WordsConverter().ConvertBack(null, typeof(object), null, culture));
		Assert.Throws<NotSupportedException>(() => new EnumDescriptionConverter().ConvertBack(null, typeof(object), null, culture));
		Assert.Throws<NotSupportedException>(() => new FlagsDescriptionConverter().ConvertBack(null, typeof(object), null, culture));
	}
}
