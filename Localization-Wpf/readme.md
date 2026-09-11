# Words for WPF

Use the Words extension to put Words in the XAML.

## Include Words

Load your Words once, before any window shows up:

``` csharp
public partial class App : Application {
	public App() {
		WordsBuilder.Create()
			// LoadResource reads straight out of your pack resources.
			.LoadResource("pack://application:,,,/My-Project;Component/Assets/words.ini")
			// Select the language; Digest installs it as Words.Known. The flag also
			// points WPF's binding culture at it — see "Match the binding culture".
			.Digest("en", includeFrameworkElements: true);
	}
}
```

## Use Words in XAML

One namespace gives you everything (`pattech.words`, the older name, still works):

``` xml
<Window xmlns:l="https://github.com/pzahra/words"
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

### Changing language means restarting

`{l:Words}` resolves once, when the XAML loads, and `WordsInline` re-renders
only when its `Key` or `Params` change. Neither watches `Words.Known`, and
that is deliberate, not a gap to fill: a live swap would also have to catch
every `LazyWords`, every string a view model composed and kept, every title
already set, and it would only hold up in an app that is strict MVVM all the
way down. Do not hot-swap the dictionary in a running UI. Save the choice and
relaunch the process, with `--lang=xx` on the command line as the samples do
or from a settings file as Wordsmith does, and let the new process load in the
new language.

### Match the binding culture to the language

Words' own `WordsInline`, `WordsConverter` and `Words.Format` format numbers and
dates with the thread's `CurrentCulture`, and `Digest` sets that for you. Plain
WPF bindings — a `StringFormat`, someone else's converter — are the exception:
they take their culture from `FrameworkElement.Language`, which defaults to
`en-US` no matter what language you picked. The WPF `Digest` overload takes one
extra flag to repoint it:

``` csharp
wb.Digest(lang, out var languages, includeFrameworkElements: true);
```

It is the core `Digest` — build, install as `Words.Known`, sync the thread
cultures — plus the process-global `FrameworkElement.Language` step you would
otherwise have to spell out as an `OverrideMetadata` call. It is one-shot: the
first call sets it, later calls leave it. Leave the flag off (or call the core
`Digest`) if you want English words but system number and date formats — set
`CurrentCulture` yourself and the framework default stays put.

## Make hyperlinks go somewhere

WPF hyperlinks raise `RequestNavigate` and then do nothing. Register the
application-wide handler once at startup and every link the markdown renders
routes through it — custom schemes make in-app commands:

``` csharp
Hyperlink.RegisterGlobalNavigateHandler(uri => {
	if (uri.Scheme is "appcmd") {
		// Handle application command hyperlinks.
	}
	else if (uri.Scheme is "http" or "https" or "mailto") {
		// Only hand the shell schemes you trust to open externally: a rendered
		// value is display text, so never shell-open an arbitrary scheme (file:
		// and friends would run local things). An unlisted scheme is ignored.
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
assemblies), and `assets:` (files under the application's `Assets` folder. It's
a convenience, not a security boundary: the path is lexically clamped to that
folder — `../` and rooted paths resolve to nothing — and the scheme only ever
loads images, so a symlink someone planted inside `Assets` is out of scope).
Query options `width`, `height`, `background`, and `foreground`
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
