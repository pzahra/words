# Words for WPF

Use the Words extension to put Words in the XAML.

## Include Words

Load your Words once, before any window shows up:

``` csharp
public partial class App : Application {
	public App() {
		Words.Known = WordsBuilder.Create()
			// LoadResource reads straight out of your pack resources.
			.LoadResource("pack://application:,,,/My-Project;Component/Assets/words.ini")
			// Select the language to use.
			.ToWords("en");
	}
}
```

## Use Words in XAML

One namespace gives you everything:

``` xml
<Window xmlns:l="pattech.words"
        Title="{l:Words main.title}">

	<TextBlock>
		<l:WordsInline Key="main.sample-markdown"/>
	</TextBlock>
</Window>
```

- `{l:Words key}` — a markup extension that resolves to the localized string.
- `<l:WordsInline Key="key"/>` — an inline that renders the value, markdown
  and all, inside a `TextBlock`.

`WordsInline` also fills format placeholders from its `Params` property: bind
an array for positional `{0}` tags, or any other object for `{Name}` tags read
off its public fields and properties. The inlines re-render whenever `Key` or
`Params` changes.

## Make hyperlinks go somewhere

WPF hyperlinks raise `RequestNavigate` and then do nothing. Register the
application-wide handler once at startup and every link the markdown renders
routes through it — custom schemes make in-app commands:

``` csharp
Hyperlink.RegisterGlobalNavigateHandler(uri => {
	if (uri.Scheme is "appcmd") {
		// Handle application command hyperlinks.
	}
	else {
		// Handle URL hyperlinks.
		Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
	}
});
```

There is one global handler: registering again replaces it, and disposing the
returned subscription unregisters it.

## Put pictures in your Words

Markdown images work in any rendered value, with the URI scheme deciding where
the picture comes from:

``` ini
[main.save-hint]
value=Press ![save icon](staticres:SaveIconGeometry?height=16&foreground=DarkGreen) to save.
```

Out of the box the parser speaks `staticres:` (application resource by
`x:Key`), `pack:` (WPF pack URIs), `resx:` (a `Resources` class in your loaded
assemblies), and `assets:` (files under the application's `Assets` folder —
and only that folder; `../` escapes are clamped, no matter how creatively
encoded). Query options `width`, `height`, `background`, and `foreground`
apply whatever the scheme; the query carries display options, not asset
identity, so resolvers always receive the URI with it already split off.
Raster images render at their natural size unless `width` or `height` says
otherwise; geometry, having no natural size, defaults to the font height.
Anything that fails to resolve degrades to its alt text as `[🖼️!alt]`, because
a missing icon should never eat your sentence.

Teach it new schemes by registering an `IImageSchemeResolver` on the shared
parser at startup — say, Material Design icons:

``` csharp
class PackIconResolver : IImageSchemeResolver {
	public FrameworkElement? Resolve(Uri source, ImageOptions options)
		=> Enum.TryParse<PackIconKind>(source.AbsolutePath.TrimStart('/'), out var kind)
			? new PackIcon { Kind = kind, Foreground = options.Foreground ?? Brushes.Black }
			: null;
}

// at startup:
MarkdownParser.Default.ImageSchemes["md"] = new PackIconResolver();
// and now `![save](md:ContentSave)` gives you Words with icons in them.
```

## Convert Words

For values that only exist at runtime, there are converters:

- `WordsConverter` — formats a bound value into the Words template named by
  `ConverterParameter`.
- `MarkdownConverter` — turns a markdown string into WPF inlines.
- `EnumDescriptionConverter` — turns a `[Words]`-decorated enum value into its
  display text; the ConverterParameter picks the `Describe` format (tooltip,
  description, unit…).
- `FlagsDescriptionConverter` — the same for `[Flags]` combinations, as a list
  of descriptions or one delimited string (`AsArray="False"`).
- `ArrayMultiConverter` — gathers a `MultiBinding` into the array that
  `WordsInline.Params` wants.

None of them need configuring, so the package ships them pre-instantiated in
`Converters.xaml` — merge it once:

``` xml
<Application.Resources>
	<ResourceDictionary>
		<ResourceDictionary.MergedDictionaries>
			<ResourceDictionary Source="pack://application:,,,/PatTech.Localization.WPF;component/Converters.xaml"/>
		</ResourceDictionary.MergedDictionaries>
	</ResourceDictionary>
</Application.Resources>
```

and every view can say `{StaticResource WordsMarkdown}`, `WordsFormat`,
`WordsEnumDescription`, `WordsFlagsDescription` (joined text),
`WordsFlagsDescriptionList` (one description per flag), or `WordsParamsArray`.
