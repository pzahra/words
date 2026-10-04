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
- `{l:Words {Binding KeyName}}` — the same over a binding: the bound value is
  the key, looked up again whenever it changes.
- `{l:Words {Binding Status, Converter={StaticResource WordsFormat}, ConverterParameter=op.status}}`
  — a binding with a converter of its own, which says how to localize. It is
  handed on as it is; live (below), it converts again on a language switch.
- `<l:WordsInline Key="key"/>` — an inline that renders the value, markdown
  and all, inside a `TextBlock`.

`WordsInline` also fills format placeholders. Bind its `Params` property when
there is an object or an array to hand to Format: an array fills positional
`{0}` tags, any other object fills `{Name}` tags read off its public fields
and properties. Or populate the content with bindings and let the XAML build
that array instead — a stack of `Binding`s, or one `MultiBinding` — evaluated
live, so the inline follows its sources. A constant among them is a `Binding`
with a `Source` and no path; a lone child sets `Params` as it is, so a bound
array or a named object works as it would set directly. Either way the inlines
re-render whenever `Key` or the arguments change.

``` xml
	<TextBlock>
		<l:WordsInline Key="main.unread">
			<Binding Path="Unread"/>
		</l:WordsInline>
	</TextBlock>
```

The children are bindings and nothing else because WPF admits a `Binding` as a
child only of a `Collection<BindingBase>`, and a `MultiBinding` child stands
alone, WPF being unable to nest one in another.

### Changing language: relaunch, or go live

Out of the box `{l:Words}` resolves once, when the XAML loads, and `WordsInline`
re-renders only when its `Key` or `Params` change; neither watches `Words.Known`.
Save the choice and relaunch the process, with `--lang=xx` on the command line
or from a settings file as Wordsmith does, and let the new process load in the
new language.

Or opt in: `.Live()` on the builder before `Digest` keeps the sources, and
`Words.SwitchLanguage("de")` re-flattens and installs the new language in place.
Live, `{l:Words}` hands a dependency property (or a style setter, or anything in
a template) a binding to a shared `LazyWords` instead of a string, so the text
follows the switch; a property that can hold no binding — a `ConverterParameter`, a `StringFormat` —
still gets the string, resolved once. A bound key is looked up again, and a
converted binding wrapped in `{l:Words}` converts again: its converter moves up
to a `MultiBinding` beside `TriggerWords`, the process's pulse, since a
`MultiBinding` never re-runs a child's own converter. `WordsInline` renders
again. Everything is held weakly and refreshed on the dispatcher it lives on.
What does not follow: strings a view model composed and kept (implement
`IKnowWords` and call `Words.Watch(this)` to re-raise them), a converted binding
not wrapped in `{l:Words}`, and the `includeFrameworkElements` binding culture
below, which is one-shot — a live app sets `Language` on its windows itself if
its bindings format numbers.

### Match the binding culture to the language

Words' own `WordsInline` and `Words.Format` format numbers and dates with the
thread's `CurrentCulture`, and `Digest` sets that for you. Bindings are another
matter — a `StringFormat`, `WordsConverter`, someone else's converter: WPF hands
them the target element's `Language`, which defaults to `en-US` no matter what
language you picked. The WPF `Digest` overload takes one extra flag to repoint it:

``` csharp
wb.Digest(lang, out var languages, includeFrameworkElements: true);
```

It is the core `Digest` — build, install as `Words.Known`, sync the thread
cultures — plus the process-global `Language` step you would otherwise spell
out as `OverrideMetadata` calls: one for controls and one for the `TextElement`
flow content a bound `Run` lives in, since a default is not inherited down the
tree. It is one-shot: the first call sets it, later calls leave it. It points at
whichever formatting culture `Digest` installed, so `.UseSystemNumbers()` before
it gives you English words with the system's number and date formats in the
bindings too. A single element or binding can still say otherwise with
`xml:lang` or `ConverterCulture`, as in any WPF app. Leave the flag off (or call
the core `Digest`) to keep WPF's own default.

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

Out of the box the parser speaks `staticres:` and `dynres:` (a resource by
`x:Key`, found from where the image lands in the tree — the window or user
control it is in, then the application — the way `{StaticResource}` and
`{DynamicResource}` are; `dynres:` stays live, so swapping the resource for a
theme change re-renders it), `pack:` (WPF pack URIs), `resx:` (a `Resources`
class in your loaded assemblies), and `assets:` (files under the application's
`Assets` folder. It's a convenience, not a security boundary: the path is
lexically clamped to that folder — `../` and rooted paths resolve to nothing —
and the scheme only ever loads images, so a symlink someone planted inside
`Assets` is out of scope). A resource renders as a fresh visual every time: an
`ImageSource` in an `Image`, a `Geometry` in a filled `Path`, a `DataTemplate`
as newly loaded content — which is how you reuse an element, since one instance
can't live under two parents. Any other resource type throws, as it would
anywhere else in WPF.
Query options `width`, `height`, `background`, and `foreground`
apply whatever the scheme; the query carries display options, not asset
identity, so resolvers always receive the URI with it already split off.
`background` and `foreground` take a color, or a brush resource spelled the way
the image schemes are — `staticres:key` or `dynres:key`, found from where the
image lands, and `dynres:` follows a theme swap (a `Color` resource is wrapped in
a brush; any other type throws; a missing key leaves the default standing — black
fill, no border — and gripes, rather than going transparent).
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
  description, unit…). A member with nothing for that format gives null, so a
  tooltip bound to it stays hidden.
- `FlagsDescriptionConverter` — the same for `[Flags]` combinations, as a list
  of descriptions or one delimited string (`AsArray="False"`). A flag with
  nothing for the format is left out.
- `ArrayMultiConverter` — gathers a `MultiBinding` into the array that
  `WordsInline.Params` wants; a `MultiBinding` child of `WordsInline` gets it
  without asking.
- `ResourceVisualConverter` — turns a resource value (`ImageSource`, `Geometry`,
  `DataTemplate`) into a fresh visual; what the `staticres:`/`dynres:` image
  schemes render through, should you want the same from a binding.

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
`WordsFlagsDescriptionList` (one description per flag), `WordsParamsArray`, or
`WordsResourceVisual`.

It also holds the look of rendered markdown: `WordsLinkBrush`, the colour of a
link, and `WordsCodeFont` and `WordsCodeBackground` for a `` `code` `` span
(brushes or colours). Define your own under those keys, in the window, the
application or the theme dictionary you swap in, and links and code spans
follow, a theme swap included. Merge nothing and define nothing, and links still
come out blue and code spans monospace on a faint grey.

## See it all at once

The [Sample-Wpf](https://github.com/pzahra/words/tree/main/Sample-Wpf) project is the full tour, a page per topic:
markdown and code spans, references and constants, tooltipped and in-app
hyperlinks, every image scheme, format parameters, `[Words]` enums, live
switching and diagnostics. Each demonstration says what it uses, and its *How
it's made* shows the exact XAML and `words.ini` behind it, cut from the real
files. A language dropdown switches the app in place, and a dark/light switch
repaints the links and shows a `dynres:` image following the theme while its
`staticres:` twin stays put. Sample-Ava is its Avalonia twin.

## The rest of the suite

- **[PatTech.Localization.Core](https://www.nuget.org/packages/PatTech.Localization.Core)** — the engine: `words.ini` files, lookups by key, languages and fallbacks, `{0}`/`{Name}` parameters, `{>key}` references, a markdown dialect.
- **[PatTech.Localization.Avalonia](https://www.nuget.org/packages/PatTech.Localization.Avalonia)** — the same as this, for Avalonia's AXAML.
- **[PatTech.Localization.Analyzer](https://www.nuget.org/packages/PatTech.Localization.Analyzer)** — the `[Localized]` attribute and rule PTL001, which flags a localized seam handed a raw string. It arrives with Core.
- **Wordsmith** — the desktop editor for `words.ini` files, published on [GitHub Releases](https://github.com/pzahra/words/releases).
