using Avalonia.Controls;
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
}
