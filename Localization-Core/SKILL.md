---
name: pattech-words
description: Work with the PatTech Words localization library — the words.ini format, Words.Known lookups, the markdown dialect, WPF/Avalonia XAML integration, image schemes, and format parameters. Use when adding or changing user-facing strings, localizing text, editing words.ini files, or rendering Words in XAML/AXAML/console.
---

# Words (PatTech.Localization)

Words keeps every user-facing string in a `words.ini` file, keyed and
per-language, and renders a markdown dialect wherever the string lands.

**The golden rule: never hardcode a user-facing string.** Add it to the
project's `words.ini` and reference it by key. Parameters marked
`[Localized]` warn (PTL001) when handed a raw string — that warning means
"move this text into words.ini", not "suppress me".

## The words.ini format

```ini
; comment lines start with `;`, `|` or `$`
value-en=English
value-it=Italiano
; ^ top-of-file labels double as the language menu (WordsBuilder.GetLanguages).
;   A `!Name` label declares the language WITHOUT listing it in the menu:
;   subordinate assemblies ship their own words.ini and may support more
;   languages than the host app offers, so they use `!` labels — present on
;   purpose, not an error, and never "fix" one by removing the `!`. Only the
;   app's own file gives plain labels to the languages it actually offers.

[group.key]
value=Default fallback text
value-en=Language-family text
value-en-GB=Region text
comment=notes for translators
context=notes from the programmer
stale=marks the value as needing re-translation (logs a warning)

; `[.name]` is dot-relative: nests under the last full header → group.key.sub
[.sub]
value=really `group.key.sub`

[$unit]
value=m2·K/W
; constants are referenced as {$unit}; regular keys as {>group.key}

[multi]
value=a trailing backslash continues on the next line keeping the newline \
like this; a trailing underscore continues _
on the same line. Repeating `value=` also appends.
```

Only `value` fields become lookup entries; the key is the block name.
Language resolution per key: exact (`en-GB`) → family (`en`) → default.

## Load once, look up anywhere

```csharp
Words.Known = WordsBuilder.Create()        // or Words.Builder()
    .Load("path/to/words.ini")             // stack as many as needed; later wins
    .ToWords("en");                        // also sets thread cultures

string title = Words.Known["main.title"];  // unknown keys render as #key#
var menu = builder.GetLanguages();         // code/label pairs for a language menu
```

Formatting: `Words.Known.Format("key", args)` works like `string.Format`;
`Words.Known.FormatByName("key", obj)` fills `{PropertyName}` /
`{PropertyName:format}` tags from `obj`'s public fields and properties.
`LazyWords` defers a lookup for statics that initialize before loading.
`[Words("key")]` on enum members plus `Enum.Describe` provides `key`,
`key.tooltip`, `key.sub`, `key.desc`, `key.unit` variants.

## The markdown dialect (rendered in every value)

- `*italic*`, `**bold**`, `***both***`, `^superscript^`, `~subscript~`
- Links: `[label](url "tooltip")` — label may be styled markdown — and `<url>`
  autolinks. Rendered underlined and blue; tooltips follow the pointer.
- Images: `![alt](scheme:path?width=W&height=H&background=B&foreground=F)`.
  The query carries display options only — it is parsed off before the scheme
  resolver sees the URI. Raster images render at natural size unless sized;
  geometry defaults to the font height. Unresolvable images degrade to
  `[🖼️!alt]` — they never throw.
- HTML entities (`&copy;`, `&#8482;`, `&#x41;`) and `:emoji:` shortcodes.

## Avalonia (`PatTech.Localization.Avalonia`)

```xml
xmlns:l="pattech.words"
Title="{l:Words main.title}">          <!-- plain string, resolved once -->
<TextBlock>
  <l:WordsInline Key="main.body" Params="{Binding Args}"/>  <!-- markdown -->
</TextBlock>
```

- `WordsInline.Params`: an array fills `{0}` positional tags; any other object
  fills `{Name}` tags by property. Re-renders when `Key` or `Params` changes.
- Load with `.LoadResource("avares://Proj/Assets/words.ini")`.
- Hyperlink clicks route through one global handler; custom schemes make
  in-app commands:
  `Hyperlink.RegisterGlobalNavigateHandler(uri => { ... })`.
- Image schemes: `avares:` (embedded assets), `assets:` (files under the app's
  `Assets` folder only — escapes are clamped), `staticres:` (resource by
  `x:Key`; `IImage`, `Geometry`, or any `Control`).
- Converters: `MarkdownConverter` (bound string → inlines/TextBlock),
  `WordsConverter` (bound value → template named by `ConverterParameter`),
  `EnumDescriptionConverter` (enum → display text; parameter picks the
  Describe format), `FlagsDescriptionConverter` (flags → list or joined text
  with `AsArray="False"`), `ArrayMultiConverter` (MultiBinding → the array
  `WordsInline.Params` wants). All ship pre-instantiated: merge
  `avares://PatTech.Localization.Avalonia/Converters.axaml` (WPF:
  `pack://application:,,,/PatTech.Localization.WPF;component/Converters.xaml`)
  into App resources once, then use `{StaticResource WordsMarkdown}`,
  `WordsFormat`, `WordsEnumDescription`, `WordsFlagsDescription` (joined),
  `WordsFlagsDescriptionList`, `WordsParamsArray`.
- Teach new schemes: `MarkdownParser.Default.ImageSchemes["md"] = resolver;`
  where resolver implements `IImageSchemeResolver`.

## WPF (`PatTech.Localization.WPF`)

Same shapes as Avalonia (`{l:Words}`, `WordsInline`, converters). Load with
`.LoadResource("pack://application:,,,/Proj;Component/Assets/words.ini")`.
Image schemes: `staticres:`, `pack:`, `resx:`, `assets:`.

## Console (`PatTech.Localization.Core`)

```csharp
Console.WriteWordsLine("main.greeting", userName);  // .NET 10 extension
var parser = new ConsoleMarkdownParser(useAnsi: !Console.IsOutputRedirected);
Console.WriteLine(parser.ToInline(Words.Known["main.title"]));  // .NET 8
```

ANSI styling, OSC 8 clickable links; plain text with `text (url)` links when
redirected.

## Gotchas

- `{l:Words}` resolves when the XAML loads — a language change needs a reload
  (the sample relaunches itself with `--lang=xx` on the command line).
- Keys missing from the dictionary render as `#key#` on screen by design;
  grep for `#` leakage rather than letting it ship.
- `Words.Known` is process-wide; assign it once at startup before any UI.
- When adding a language, give it a top-of-file `value-xx=Label` line so it
  appears in `GetLanguages()`.
