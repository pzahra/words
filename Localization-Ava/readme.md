# Words for Avalonia

Use the Words extension to put Words in the AXAML.

## Include Words

``` csharp
public override void Initialize() {
	Words.Known = Words.Builder()
		// Use as many of these as you need.
		.LoadResource("avares://My-Project/Assets/words.ini")
		// Select the language to use.
		.ToWords("en");
	AvaloniaXamlLoader.Load(this);
}
```

## Handle Hyperlinks

Markdown links render underlined and blue in the traditional manner, carry
pointer-placed tooltips from their `"title"`, and route every click through
one global handler — custom schemes make handy in-app commands:

``` csharp
public override void OnFrameworkInitializationCompleted() {
	Hyperlink.RegisterGlobalNavigateHandler(uri => {
		if (uri.Scheme is "appcmd") {
			// Handle application command hyperlinks.
		}
		else {
			// Handle URL hyperlinks.
			Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
		}
	});

	// ...
```

## Use Words in AXAML

One namespace gives you everything:

``` xml
	xmlns:l="pattech.words"
	Title="{l:Words main.title}">

	<TextBlock>
      <l:WordsInline Key="main.sample-markdown"/>
    </TextBlock>
```

- `{l:Words key}` — a markup extension that resolves to the localized string.
- `<l:WordsInline Key="key"/>` — an inline that renders the value, markdown
  and all, inside a `TextBlock` or other flow content.

`WordsInline` also fills format placeholders from its `Params` property: bind
an array for positional `{0}` tags, or any other object for `{Name}` tags read
off its public fields and properties. The inlines re-render whenever `Key` or
`Params` changes.

``` xml
	<TextBlock>
      <l:WordsInline Key="main.unread" Params="{Binding UnreadParams}"/>
    </TextBlock>
```

## Put pictures in your Words

Markdown images work in any rendered value, with the URI scheme deciding where
the picture comes from:

``` ini
[main.save-hint]
value=Press ![save icon](staticres:SaveIconGeometry?height=16&foreground=DarkGreen) to save.
```

Out of the box the parser speaks `avares:` (embedded assets), `assets:` (files
under the application's `Assets` folder — and only that folder; `../` escapes
are clamped, no matter how creatively encoded), and `staticres:` (application
resource by `x:Key`). Query options `width`, `height`, `background`, and
`foreground` apply whatever the scheme; the query carries display options, not
asset identity, so resolvers always receive the URI with it already split off.
Raster images render at their natural size unless `width` or `height` says
otherwise; geometry, having no natural size, defaults to the font height.
Anything that fails to resolve degrades to its alt text as `[🖼️!alt]`, because
a missing icon should never eat your sentence.

Teach it new schemes by registering an `IImageSchemeResolver` on the shared
parser at startup — say, Material Design icons:

``` csharp
class PackIconResolver : IImageSchemeResolver {
	public Control? Resolve(Uri source, ImageOptions options)
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
- `MarkdownConverter` — turns any bound markdown string into rendered inlines
  (or a whole `TextBlock`, when the target wants a control).
- `FlagsDescriptionConverter` — turns a `[Words]`-decorated enum (flags
  included) into its display text.

## See it all at once

The [Sample-Ava](../Sample-Ava) project is the full tour: formatting, entities
and emoji, tooltipped and in-app hyperlinks, every image scheme, live format
parameters, a markdown playground, and a language dropdown that relaunches the
app in the selected language.
