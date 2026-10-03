using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using PatTech.Localization.Avalonia;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The Avalonia look of rendered markdown a theme may restyle — the twin of the WPF
/// <see cref="ThemeResourceTests"/>, in the headless test host.
/// </summary>
public class AvaThemeResourceTests {

	private static T Attach<T>(string markdown, out Window window, ResourceDictionary? resources = null, ThemeVariant? theme = null) where T : Inline {
		window = new Window();
		if (resources is not null) window.Resources = resources;
		if (theme is not null) window.RequestedThemeVariant = theme;
		var textBlock = new TextBlock();
		window.Content = textBlock;
		window.Show();
		var inline = new MarkdownParser().ToInline(markdown);
		textBlock.Inlines!.Add(inline);
		return Assert.IsType<T>(inline);
	}

	private static ResourceDictionary Themed(string key, object light, object dark) {
		var themed = new ResourceDictionary();
		themed.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { [key] = light };
		themed.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { [key] = dark };
		return themed;
	}

	private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

	[AvaloniaFact]
	public void Link_WithNoResources_IsBlue() {
		var link = Assert.IsType<Hyperlink>(new MarkdownParser().ToInline("[docs](https://example.test/)"));

		Assert.Equal(Colors.Blue, ColorOf(link.Foreground));
	}

	[AvaloniaFact]
	public void Link_FollowsAThemeVariantChange() {
		var themed = Themed("WordsLinkBrush", new ImmutableSolidColorBrush(Colors.Gold), new ImmutableSolidColorBrush(Colors.Silver));
		var link = Attach<Hyperlink>("[docs](https://example.test/)", out var window, themed, ThemeVariant.Light);
		Assert.Equal(Colors.Gold, ColorOf(link.Foreground));

		window.RequestedThemeVariant = ThemeVariant.Dark; // the sample's dark-theme switch

		Assert.Equal(Colors.Silver, ColorOf(link.Foreground));
	}

	[AvaloniaFact]
	public void Link_ThePackageDefault_ReadsInBothVariants() {
		var resources = new ResourceDictionary();
		resources.MergedDictionaries.Add(Assert.IsAssignableFrom<ResourceDictionary>(
			AvaloniaXamlLoader.Load(new Uri("avares://PatTech.Localization.Avalonia/Converters.axaml"))));
		var link = Attach<Hyperlink>("[docs](https://example.test/)", out var window, resources, ThemeVariant.Light);
		Assert.Equal(Colors.Blue, ColorOf(link.Foreground));

		window.RequestedThemeVariant = ThemeVariant.Dark;

		Assert.Equal(Color.Parse("#6CB4FF"), ColorOf(link.Foreground));
	}

	[AvaloniaFact]
	public void CodeSpan_WithNoResources_IsMonospaceOnATint() {
		var span = Assert.IsType<Span>(new MarkdownParser().ToInline("a `*b*` c"));
		var runs = span.Inlines.Cast<Run>().ToList();

		Assert.Equal(["a ", "*b*", " c"], runs.Select(run => run.Text));
		Assert.Contains("Consolas", runs[1].FontFamily.ToString());
		Assert.Equal(Color.FromArgb(0x22, 0x80, 0x80, 0x80), ColorOf(runs[1].Background));
		Assert.False(runs[0].IsSet(TextElement.FontFamilyProperty));
	}

	[AvaloniaFact]
	public void CodeSpan_TakesTheResourcesWhereItLands_AndFollowsThem() {
		var font = new FontFamily("Cascadia Mono");
		var local = new ResourceDictionary {
			["WordsCodeFont"] = font,
			["WordsCodeBackground"] = new ImmutableSolidColorBrush(Colors.Gold),
		};
		var run = Attach<Run>("`x`", out _, local);

		Assert.Same(font, run.FontFamily);
		Assert.Equal(Colors.Gold, ColorOf(run.Background));

		local["WordsCodeBackground"] = Colors.Silver; // a colour this time
		local.Remove("WordsCodeFont");

		Assert.Equal(Colors.Silver, ColorOf(run.Background));
		Assert.Contains("Consolas", run.FontFamily.ToString());
	}

	[AvaloniaFact]
	public void CodeSpan_FollowsAThemeVariantChange() {
		var themed = Themed("WordsCodeBackground", new ImmutableSolidColorBrush(Colors.Gold), new ImmutableSolidColorBrush(Colors.Silver));
		var run = Attach<Run>("`x`", out var window, themed, ThemeVariant.Light);
		Assert.Equal(Colors.Gold, ColorOf(run.Background));

		window.RequestedThemeVariant = ThemeVariant.Dark;

		Assert.Equal(Colors.Silver, ColorOf(run.Background));
	}

	[AvaloniaFact]
	public void CodeSpan_InALink_TakesTheLinkColour() {
		var link = Attach<Hyperlink>("[`key`](https://example.test/)", out _, new ResourceDictionary {
			["WordsLinkBrush"] = new ImmutableSolidColorBrush(Colors.Gold),
		});

		var run = Assert.IsType<Run>(Assert.Single(link.Inlines));
		Assert.Equal("key", run.Text);
		Assert.Equal(Colors.Gold, ColorOf(run.Foreground));
		Assert.Contains("Consolas", run.FontFamily.ToString());
	}
}
