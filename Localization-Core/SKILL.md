---
name: pattech-words
description: Work with the PatTech Words localization library — the words.ini format, Words.Known lookups, the markdown dialect, WPF/Avalonia XAML integration, image schemes, and format parameters. Use when adding or changing user-facing strings, localizing text, editing words.ini files (through the `words` command line when it is installed), or rendering Words in XAML/AXAML/console.
---

# Words (PatTech.Localization)

Words keeps every user-facing string in a `words.ini` file, keyed and
per-language. Values may carry a markdown dialect, rendered by the library's
markdown-aware consumers (see below).

**The golden rule: never hardcode a user-facing string.** Add it to the
project's `words.ini` and reference it by key. Parameters marked
`[Localized]` warn (PTL001) when handed a raw string — that warning means
"move this text into words.ini", not "suppress me".

**Name the file `*words.ini` and hand it to the analyzer**, so a key the
code names is checked against the keys the file declares (PTL002):

```xml
<ItemGroup>
  <AdditionalFiles Include="Assets\app-words.ini" />
</ItemGroup>
```

The analyzer reads only files whose name ends in `words.ini`. With one, a
misspelt key (`Words.Known["menu.flie"]`, `Format("menu.flie", …)`, a
`[WordsKey]` parameter of your own) warns at build time instead of rendering
`#menu.flie#` at run time. A key with a plural form in it
(`"files#other"`) warns too: code names the key, and the count picks the
form.

## The words.ini format

```ini
; comment lines start with `;`
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
stale=marks the value as needing re-translation (gripes to the builder's logger)

; `[.name]` is dot-relative: nests under the last full header → group.key.sub
[.sub]
value=really `group.key.sub`
; ^ a key's name is segments of any script's letters and digits (Unicode's
;   identifier characters), _ and - joined by dots (menu.file-open), in NFC,
;   and a $constant is one segment. A block named otherwise (a space, #, =,
;   ;, an empty segment, a constant's child) is skipped with a warning,
;   WP:NAME: `#` marks plural forms. WordsParser.IsKeyName checks one.

[file]
value=file
value#other=files
value-mt=fajl
value-mt#few=fajls
; ^ plural forms: the CLDR category after the language. The plain value is the
;   `one` form; add the others the language uses (English: other; Maltese: two,
;   few, many, other; PluralRules.Categories("xx") lists them). A missing form
;   reads `other`, then the language's own plain value, never the default's
;   forms; an optional one reads its stand-in first (PluralRules.Optional("xx"):
;   Maltese two reads few, many reads other, other reads the plain value). `#`
;   is reserved for forms.

[files.count]
value={0} {0#file}
; ^ `{0#file}` splices the form of `file` that argument 0's count picks, without
;   printing the count: "1 file", "2 files". Named works too: {Count#file}.

[$unit]
value=m2·K/W
; a `[$constant]` holds a NON-TRANSLATABLE string such as an SI unit or a
; formula — one value, no per-language entries. Reference it in other values
; as {$unit}; regular (translatable) keys are referenced as {>group.key}

[multi]
value=a trailing backslash continues on the next line keeping the newline \
like this; a trailing underscore continues _
on the same line. Repeating `value=` overwrites (last wins, and Wordsmith
gripes) — that is how a later `Load` overlays an earlier one, not a continuation.
```

Only `value` fields become lookup entries; the key is the block name.
A language code is `language(-Script)?(-REGION)?`: 2–3 letters, a 4-letter
script, a 2-letter or 3-digit region (`en`, `ceb`, `es-419`, `zh-Hans-CN`),
cased by kind. A field whose suffix is none (`value-english=`) is skipped with
`WP:LANG`; a key's `param-name=` is a parameter's name, not a language.
Language resolution per key drops one subtag at a time: `zh-Hant-TW` →
`zh-Hant` → `zh` → default, so Traditional never reads Simplified. A Chinese
region with no script goes through the one it writes: `zh-TW` → `zh-Hant`,
`zh-CN` → `zh-Hans`.

## Editing words.ini: the tools

Each Wordsmith release (https://github.com/pzahra/words/releases, the
`editor/` tags) carries two tools:

- **Wordsmith**, the Windows editor, for a person editing by hand.
- **`words`**, the command line, for an agent or a script on any platform
  (Windows, Linux, macOS; one self-contained file, nothing to install). It
  changes the one field it is asked to and leaves every other byte of the file
  alone; each edit is read back, and refused if anything else would change.

Check for it with `words --version`. When it is installed, make every change to
a words.ini through it rather than by hand:

```sh
words get    words.ini menu.file value-it            # a field's value, unescaped
words get    words.ini menu.file                     # the key's whole block
words set    words.ini menu.file value-it "Archivio" # sets a field, adding it or the key
words set    words.ini files value-mt#few - <<'EOF'
fajls
EOF
# ^ a value from stdin (its last line break dropped): multi-line, no quoting
words set    words.ini menu.file value-it "Archivio" --stale "machine translated"
words remove words.ini menu.file comment-it          # one field, or the whole key
words list   words.ini menu.                         # keys under a prefix, in file order
words list   words.ini --missing it                  # keys Italian still misses
```

Fields are named as in the file (`value`, `value-fr`, `value-mt#few`,
`context-fr`, `comment-fr`, `stale-fr`, `param-name`); a key is its full dotted
name, whatever `[.child]` header the file wrote it under. Exit codes: 0 done,
1 the key or field is not there, 2 a bad call, a file that is missing, can't be
read or written or does not parse, or an edit refused because it would change
more than asked; the file is left as it was.
Read stderr: a field in a language the file does not declare, or a form the
language never reads, is griped about as it is written, and so is a
translation that drops a placeholder the default uses, or uses one it does not
and no `param-` defines: fix that translation. Mark the translations
you write `--stale`, so the person reviewing in Wordsmith finds them. Setting
a default (`value`, `value#few`) to new words marks every translation that has
words stale with the time, as Wordsmith does; a mark already there stays.
Translate those again and mark them yourself.
`--missing xx` follows Wordsmith's badges: no words in `xx`, or a plural key
missing a form `xx` counts by; a language the default speaks misses nothing.

Without the tool, edit by hand and keep the format's rules: a literal `\`, `_`
or `'` is written doubled, a trailing `\` continues a value on the next line
keeping the line break and a trailing `_` without it, and a new block never
goes between a base and its `[.child]` headers, which would re-base them.

## Load once, look up anywhere

```csharp
Words.Logger = logger;                     // runtime gripes — see Logging
WordsBuilder.Create(logger)                // load-time gripes; or Words.Builder()
    .Load("path/to/words.ini")             // stack as many as needed; later wins
    .Digest("en");                         // installs Words.Known, sets thread cultures

string title = Words.Known["main.title"];  // unknown keys render as #key#
var menu = builder.GetLanguages();         // code/label pairs for a language menu
```

Formatting: `Words.Known.Format("key", args)` works like `string.Format`;
`Words.Known.FormatByName("key", obj)` fills `{PropertyName}` /
`{PropertyName:format}` tags from `obj`'s public fields and properties, or from
its values by name when `obj` is a dictionary;
`Words.Known.FormatParams("key", x)` picks by what `x` is — an array is
positional, any other object is named, `null` is the text as is (the rule the
XAML inlines and converters use).
Counts pick plural forms: a `{0#key}` selector resolves in every formatting call
(`Format`, `FormatByName`, `FormatParams`, the XAML inlines and converters), and
`Words.Known["file", n]` returns the key's own form for `n`. A missing form
falls to the key's `other` form, then its plain value, both from the language
that has the key's words; a fraction picks `other`. A selector or `{>.sub}`
resolves against the key whose text it is in, a form's text being its key's.
Never write `n == 1 ? "file" : "files"` — that is English's rule in code.
Numbers and dates in parameters format with the thread's `CurrentCulture`,
which `Digest` sets to the language; `.UseSystemNumbers()` before `Digest`
keeps the system's regional format (`Words.SystemCulture`) with the words
unchanged. `WordsConverter` formats with the culture the binding hands it.
`LazyWords` defers a lookup for statics that initialize before loading, and
live (`.Live()` before `Digest`) is the proxy that follows `Words.SwitchLanguage`.
`[Words("key")]` on enum members plus `Enum.Describe` provides `key`,
`key.tooltip`, `key.sub`, `key.desc` variants (format letters `G`, `T`, `S`,
`d`; `D` is the number, as Enum's `D`); `Describable.Slot('U', ".unit")` adds a slot of the app's own,
`Describable.Fill<TAttribute>` points an existing attribute at a slot,
`Describable.Keys<TEnum>("prefix")` or `Keys<TEnum>(member => key)` keys an
enum without attributes (one the app doesn't own included), and a letter no
slot answers reads as `G` marked `#!U#`.

## Logging — silent unless you wire it

Words gripes through two channels, and **both discard everything by default**:
the logger passed to `WordsBuilder.Create(logger)` receives load-time warnings
(parse problems, value overwrites, `stale=` marks), and the `Words.Logger`
static receives runtime ones (missing keys and references, unresolvable
images, markdown gripes). Wire both at startup or stale translations and
missing keys ship unnoticed.

Adapt whatever logging the host app already has (NLog is a good default when
it has none) with a small `ITakeException` adapter — two members:

```csharp
class WordsLog : ITakeException {
    private static readonly NLog.Logger log = NLog.LogManager.GetLogger("Words");
    public void Warn(string text) => log.Warn(text);
    public void Error(Exception exception, string message) => log.Error(exception, message);
}
```

Any output the app has works the same way — an `ILogger`, a diagnostics pane,
even `Console.Error` — the adapter is the pattern, NLog just the suggestion.

## The markdown dialect (in values)

Values may contain markdown, but **only markdown-aware consumers render it**:
`WordsInline`, `MarkdownConverter`, and `ConsoleMarkdownParser`. Plain-string
surfaces — `Words.Known[key]`, `Format`/`FormatByName`, and the `{l:Words}`
markup extension — return the text with the markup intact, so keep markdown
out of values destined for window titles, tooltips-as-text, accessibility
properties, or logs.

- `*italic*`, `**bold**`, `***both***`, `^superscript^`, `~subscript~`
- Links: `[label](url "tooltip")` — label may be styled markdown — and `<url>`
  autolinks. Rendered underlined, in the resource `WordsLinkBrush` (blue
  without it; follows theme swaps); tooltips follow the pointer.
- Images: `![alt](scheme:path?width=W&height=H&background=B&foreground=F)`.
  The query carries display options only — it is parsed off before the scheme
  resolver sees the URI. `B`/`F` are a color, or a brush resource as
  `staticres:key`/`dynres:key` (`dynres:` follows theme swaps; a missing key
  keeps the default rather than going transparent). Raster images render at natural size unless sized;
  geometry defaults to the font height. Unresolvable images — unknown scheme,
  missing asset, even a malformed URI — degrade to `[🖼️!alt]` and gripe to
  the logger; they never throw. A link whose URI won't parse renders its
  label as plain unlinked content.
- HTML entities (`&copy;`, `&#8482;`, `&#x41;`) and `:emoji:` shortcodes.
- Code spans: `` `{l:Words key}` `` shows its text as written — nothing inside
  is markup — in a monospace run on a tint (resources `WordsCodeFont`,
  `WordsCodeBackground`; dim in the console). Double backticks hold a single
  one. Line breaks inside are kept, and backticks on lines of their own (value
  lines ending `\`) fence a block. A span does not stop `{>key}` expanding at
  lookup: write `{{>key}` to show one.

## Avalonia (`PatTech.Localization.Avalonia`)

```xml
xmlns:l="https://github.com/pzahra/words"
Title="{l:Words main.title}">          <!-- plain string, resolved once -->
<TextBlock>
  <l:WordsInline Key="main.body" Params="{Binding Args}"/>  <!-- markdown -->
  <l:WordsInline Key="main.unread">      <!-- the XAML builds the {0} array -->
    <Binding Path="Unread"/>
  </l:WordsInline>
</TextBlock>
```

- `WordsInline.Params`: bind it when there is an object or an array to hand
  to Format — an array fills `{0}` positional tags; any other object fills
  `{Name}` tags by property. Or populate the content with bindings and the
  XAML builds the array: a stack of `Binding`s or one `MultiBinding`,
  evaluated live. A constant is a `Binding` with a `Source` and no path; a
  lone child sets `Params` as it is (a bound array or named object). Children
  are bindings only (WPF admits nothing else in a `Collection<BindingBase>`)
  and on WPF a `MultiBinding` child stands alone. Re-renders when `Key` or the
  arguments change.
- Load with `.LoadResource("avares://Proj/Assets/words.ini")`.
- Hyperlink clicks route through one global handler; custom schemes make
  in-app commands:
  `Hyperlink.RegisterGlobalNavigateHandler(uri => { ... })`.
- Image schemes: `avares:` (embedded assets), `assets:` (files under the app's
  `Assets` folder only — escapes are clamped), `staticres:` and `dynres:`
  (resource by `x:Key`, found from where the image lands in the tree like
  `{StaticResource}`/`{DynamicResource}`; `dynres:` follows theme swaps. The
  value may be an `IImage`, a `Geometry`, or a `DataTemplate` — the reusable
  form of a control; any other type throws).
- Converters: `MarkdownConverter` (bound string → inlines/TextBlock),
  `WordsConverter` (bound value → template named by `ConverterParameter`),
  `EnumDescriptionConverter` (enum → display text; parameter picks the
  Describe format), `FlagsDescriptionConverter` (flags → list or joined text
  with `AsArray="False"`), `ArrayMultiConverter` (MultiBinding → the array
  `WordsInline.Params` wants; a `MultiBinding` child of `WordsInline` gets it
  for free). All ship pre-instantiated: merge
  `avares://PatTech.Localization.Avalonia/Converters.axaml` (WPF:
  `pack://application:,,,/PatTech.Localization.WPF;component/Converters.xaml`)
  into App resources once, then use `{StaticResource WordsMarkdown}`,
  `WordsFormat`, `WordsEnumDescription`, `WordsFlagsDescription` (joined),
  `WordsFlagsDescriptionList`, `WordsParamsArray`, `WordsResourceVisual`
  (resource value → fresh visual).
- Teach new schemes: `MarkdownParser.Default.ImageSchemes["md"] = resolver;`
  where resolver implements `IImageSchemeResolver`.

## WPF (`PatTech.Localization.WPF`)

Same shapes as Avalonia (`{l:Words}`, `WordsInline`, converters). Load with
`.LoadResource("pack://application:,,,/Proj;Component/Assets/words.ini")` and
digest with the WPF overload, `.Digest(lang, includeFrameworkElements: true)`:
WPF hands bindings (`StringFormat`, `WordsConverter`) the element's `Language`,
`en-US` by default, and the flag repoints it — controls and flow content alike
— at the formatting culture just installed.
Image schemes: `staticres:`, `dynres:`, `pack:`, `resx:`, `assets:`.

## Console (`PatTech.Localization.Core`)

```csharp
Console.WriteWordsLine("main.greeting", userName);  // .NET 10 extension
var parser = new ConsoleMarkdownParser(useAnsi: !Console.IsOutputRedirected);
Console.WriteLine(parser.ToInline(Words.Known["main.title"]));  // .NET 8
```

ANSI styling, OSC 8 clickable links; plain text with `text (url)` links when
redirected.

## Gotchas

- Out of the box a language change is a restart: `{l:Words}` resolves when the
  XAML loads, `LazyWords` caches, and loaded XAML goes stale on a swap (Wordsmith
  saves a config file and restarts). To switch in place, chain `.Live()` before
  `Digest` and call `Words.SwitchLanguage("xx")`: `{l:Words}` then binds,
  `LazyWords` re-resolves and notifies, `WordsInline` re-renders. A localized
  binding follows only when wrapped: `{l:Words {Binding KeyName}}` for a bound
  key, `{l:Words {Binding X, Converter=…, ConverterParameter=…}}` for a converted
  one. Strings a view model kept do not follow unless it implements `IKnowWords`
  and `Words.Watch`es.
- Keys missing from the dictionary render as `#key#` on screen by design;
  grep for `#` leakage rather than letting it ship.
- `Words.Known` is process-wide; assign it once at startup before any UI.
- When adding a language, give it a top-of-file `value-xx=Label` line so it
  appears in `GetLanguages()`.
- Declare the language the defaults are written in with a keyless top-of-file
  `value=!xx` (the `!` keeps it off `GetLanguages()`). Fallbacks to the default
  are then no missing words in `xx` (and in each longer code that falls back
  to it, `en-AU` for `en`): `.Debug()` and Wordsmith stop flagging them. A
  `value-xx=` label is the language's own name for itself; a `comment-xx=`
  label, optional, is its name in the default's language: in an English
  file, `value-it=Italiano` beside `comment-it=Italian`.
