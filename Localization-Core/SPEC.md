# Words runtime — what the library guarantees

The runtime is the library an app actually ships: `Localization-Core` and the
framework packages on top of it (`Localization-Wpf`, `Localization-Ava`).
Wordsmith, the editor, has its own spec ([../WordsEdit/SPEC.md](../WordsEdit/SPEC.md)),
and so does `words`, the command line ([../WordsCli/SPEC.md](../WordsCli/SPEC.md));
this one fixes the runtime's behavior. Everything up to *Planned upgrades* is
what the library does today; the last part is what it does not do yet.

## The dictionary is a snapshot

`Words.Known` is the process-wide dictionary, built once at startup:
`WordsBuilder.Create().Load(…).Digest(code)` flattens every loaded source into
one language (exact code → each shorter code → default), installs it, and
applies its cultures to the threads. From then on it is a *snapshot*. A value handed out is a plain
string the caller owns; `{l:Words key}` resolves once when the XAML is parsed
([the extension](../Localization-Wpf/WordsExtension.cs) returns the string and is
discarded); `LazyWords` caches its lookup on first read. Replacing `Words.Known`
changes what *later* lookups return — it does not reach back into anything
already rendered.

Changing the display language therefore means building a new dictionary and
restarting — the model Wordsmith itself follows (its own spec, *Wordsmith's own
words*: picking a language "restarts the editor with the same files open") —
unless the app opts in to *Live language switching* below. The builder's
un-flattened sources are released once `Digest` has run; nothing keeps them,
unless `Live()` asked.

**Why.** The common app ships one language per process, or relaunches to switch.
Holding every language's source in memory and wiring every rendered string for
live replacement is a cost that app should not pay by default. Live switching is
the opt-in below.

## Live language switching

Switch the display language in a running process and have every string on screen
follow — for the app that wants it, and only for that app. The whole feature is
opt-in, chosen before `Digest`, and invisible when it is not: off, the runtime
behaves exactly as *The dictionary is a snapshot* describes, byte for byte.

**Turning it on.** `WordsBuilder.Live()`, chained before `Digest`, keeps the
builder — its un-flattened per-language sources and its `Debug`/`UseSystemNumbers`
settings — alive as `Words.Live` instead of letting it go, and arms the watch
registry below. Off (the default: `Words.Live` is null), `Digest` behaves as
today and every hook in this section is a no-op; `Live(false)` lets the builder
go again.

**Re-flattening.** With the sources retained, `Words.SwitchLanguage(code)`
re-flattens for the new language and assigns `Words.Known` — the same flatten the
builder runs at startup, without touching disk. The assignment is the trigger:
everything downstream hangs off the `Words.Known` swap, so a plain
`Words.Known = dictionary` relocalizes just as well.

**LazyWords is the proxy.** [`LazyWords`](Words.cs) already held a key, resolved
it against `Words.Known` on first read, and cached — most of a live proxy. It
implements `INotifyPropertyChanged` and `IKnowWords`, the one-method seam
(`Refresh()`) the registry calls; its `Refresh()` drops the cache and raises
`PropertyChanged(Value)`, so the next read re-resolves and any binding over it
re-pulls. That is the whole proxy — no new type. A *literal* `LazyWords` — the
implicit `operator LazyWords(string)`, or one whose `Value` was assigned — holds
text, not a key, and never resolves, so it never watches and `Refresh` leaves it
be.

**Registration is at resolution, not construction.** Anything implementing
`IKnowWords` joins the registry through `Words.Watch(this)`; a view model that
wants to re-raise its own notifications on a swap implements it and registers
from its constructor. `LazyWords` registers not from its constructor but when
`Value` is read — the moment it actually touches `Words.Known`. That suits its
laziness and, more usefully, keeps out the literals that never resolve: an
unread holder stays out of the registry until something reads it. Registering
again is cheap and re-homes the watcher to the calling thread, so a holder read
from a new thread is refreshed there.

**One proxy per key.** `LazyWords.Of(key)` hands out the one holder for a key
that every caller shares while anything holds it — held weakly, so a key nobody
binds any more goes, and its husk is overwritten the next time the key is asked
for. A window with forty labels over forty keys carries forty proxies and a swap
raises forty `PropertyChanged`, not one per visual.

**Living bindings.** A rendered string follows a swap only if the markup hands
XAML something that stays connected, so in live mode `{l:Words key}` returns a
binding, not a string: a one-way `Binding` to the shared proxy, path `Value`. It
does so where a binding can land — a dependency property on WPF, a styled
property on Avalonia, or a WPF style setter, which applies the binding per
element — and hands the string, resolved once, anywhere else: a
`ConverterParameter`, a `StringFormat`, a plain CLR property. A WPF template
asks with a placeholder for the target and keeps what it gets for every element
it makes, so a string there would be frozen in the language the template first
loaded in, or as `#key#` if that was before the words; in a template the
extension hands the binding live or not, and each element resolves its own.
Avalonia builds a template's content per element, so its extension sees the real
target there already. Avalonia's loader
would take a binding handed to an object-typed property (`Content`) as the
content itself, so there the extension binds the property directly and hands
back the current text. Avalonia also holds a binding's source weakly, and the
shared holder's other owners are weak too, so the extension parks the holder on
the target, which keeps it for as long as it lives; WPF's binding holds its
source itself. Off, each shape is its snapshot self, so live mode costs nothing
when it is not asked for.

**Bound keys and converters.** `{l:Words}` takes a whole binding as well as a
key, through one `object` constructor that branches on what it got:

- `{l:Words {Binding KeyName}}` — a binding with no converter. The bound value
  *is* the key, looked up whenever it changes and, live, on every swap.
- `{l:Words {Binding Status, Converter={StaticResource WordsFormat}, ConverterParameter=op.status}}`
  — a binding carrying its own converter (a template fill here, an
  [`EnumDescriptionConverter`](../Localization-Wpf/EnumDescriptionConverter.cs)
  or `FlagsDescriptionConverter` elsewhere). `{l:Words}` never has to know which:
  the inner binding's converter says how to localize, so the enum-vs-template
  choice is the author's, not the extension's.

Off, each shape is its snapshot self: the bound key gains the lookup as its
converter (and a one-way mode, if it named none), and the converted binding is
handed on untouched. The result is a binding either way, so its target must be
able to hold one. A `MultiBinding`, or WPF's `PriorityBinding`, is handed on
untouched in both modes. On Avalonia a compiled binding is wrapped like a
reflection one, and an object-typed target is bound directly, as for a key. The
extension builds what it hands out once, on first use: the binding it was given
can change only before its first use, and an extension in a template may be
asked again for each instance.

**The XAML analyser.** `Key` names no constructor argument. Marked
`[ConstructorArgument("key")]`, a `string` property naming the `object`
constructor's argument, it made the IDE's XAML analyser balk at a key passed as
a string, `{l:Words some.key}`, though that built and ran. The mark is for XAML
serialization, which nothing does with `{l:Words}`; both twins go without it.

**The converter hoist.** Wrapping a converted binding in a `MultiBinding` with
the trigger is *not* enough, and the reason is easy to miss: a `MultiBinding` does
not re-run a child's converter when a sibling leg changes — each child caches its
converted value and recomputes only when its own source moves, so pulsing the
trigger hands back the source leg's stale string. Live, `{l:Words}` therefore
**hoists** the inner binding's converter: it reads `.Converter`,
`.ConverterParameter`, `.ConverterCulture` and `.StringFormat` off the binding
(which arrives before it is instanced, so they move cheaply, no clone), clears
them, uses the bare source as leg 0, adds the trigger as leg 1, and its
multi-converter re-applies the hoisted converter to the current source value —
or, for a bound key, looks the key up — with the string format on the
`MultiBinding`, applied after it as it was. A swap pulses the trigger, the
multi-converter runs again, and the value re-localizes; a source change drives it
the same way. Avalonia takes the same hoist, so the twins match; its
`ConverterCulture` arrived after 11.0, which the package still takes, so it moves
only where it exists. This is display-only: the hoisted converters are one-way
(`ConvertBack` throws), which localization is.

**The trigger.** `TriggerWords.Instance`, in Core: one keyless `IKnowWords` for the
whole process, carrying no text, only the pulse — a `Pulse` count that raises
`PropertyChanged` on every swap. The extension registers it
(`TriggerWords.Watch()`) when it builds a live multi-binding; off, it never
pulses. An app can give its own multi-bindings the same leg. Being one, it has
one home: a `Watch()` from a thread with no synchronization context (a pool
thread, a service at startup) leaves it where a UI thread put it. A WPF app
with a second UI thread, such as a splash screen or a tool window on its own
Dispatcher, still moves it with that thread's `Watch()`; one trigger per
synchronization context waits until somebody needs it (Planned upgrades).

**Rendering controls.** A control that renders Words itself rather than handing a
string to a property — `WordsInline`, on both frameworks — implements
`IKnowWords` directly and rebuilds its content on `Refresh`, no binding in the
middle. Same registry, same weak hold, same threading rules.

**The registry.** A `ConditionalWeakTable` in Core, keyed by the watcher: an
entry lives exactly as long as whatever else holds its watcher and no longer,
with nothing to sweep, and registering twice is one entry. This is deliberately
*not* a `static event Words.KnownChanged` — a static event roots every
subscriber and is the textbook managed leak. `Words.Watch` is the entry point;
off, it returns immediately, so the call site needs no condition of its own.

**Threads.** `Words.Known`'s setter is volatile and callable from any thread, and
a refresh touches UI objects. The registry notes the thread and the
`SynchronizationContext` a watcher registered on: a swap on that thread refreshes
it inline, a swap from another posts the refresh to that context — one post per
context, however many watchers — and sets the new cultures on the thread it
lands on, so a switch raised on a background thread reaches each binding on the
dispatcher it belongs to, cultures and all.

**Robustness.** The refresh walks a snapshot of the registry (a refresh may
register new watchers), and a watcher whose `Refresh` throws is reported to
`Words.Logger` (`WORDS:REFRESH`) and skipped, so one bad listener never aborts
the relocalization.

**Nothing here is new capability.** Every piece of this is reachable by hand with
the library as it stood: `Words.Known` is swappable from any thread, a builder
kept alive re-flattens for another language, and any object can read
`Words.Known` on demand and raise its own change notification. An app determined
to switch live could wire all of it itself. Live mode is that wiring done once —
weakly held, thread-marshalled — so the app gets the shortcut instead of the
plumbing. That is also why it stays opt-in: it earns its keep only for the app
that wants it, and costs the others nothing.

**What does not follow.** A string a view model composed and kept stays a
snapshot, and so does a binding never wrapped in `{l:Words}` — a plain
`{Binding X, Converter={StaticResource WordsFormat}, …}`, a `StringFormat` —
because Core cannot reach a `Converter=` it was never handed. WPF's default
binding culture (`Digest(…, includeFrameworkElements: true)`) is a one-shot
metadata override and does not move with a switch; a live app sets `Language` on
its windows itself, or formats through `WordsInline` and `Words.Format`, which
use the thread culture the switch sets.

**Tests.** Headless, all of it: opt in, digest, take a `LazyWords`, read it,
`SwitchLanguage`, and its `Value` is the new language and its `PropertyChanged`
fired; a plain `Words.Known` assignment does the same; off by default, a holder
never registers and a swap notifies nothing — the snapshot behavior above,
pinned; a literal never watches; `Of` shares one holder per key; a holder with no
other referent is collectable and the registry neither resurrects nor refreshes
it; a watcher that throws is logged and the next one still refreshed; a switch
from another thread posts once to the watcher's context. On each framework, a
property bound through `{l:Words}` follows a switch — a WPF setter, an
Avalonia `Content`, and a key in a control template, made before or after the
words were loaded, too — a `StringFormat` keeps the string, `WordsInline` renders
again, and off, the markup is the snapshot. Over a binding, on each framework: a
bound key follows its source and a switch, a converted binding converts again on
a switch and on a source change, and off, both follow their source only; an
Avalonia compiled binding and object-typed target are wrapped too; a WPF setter
takes a bound key, and a WPF template loaded twice converts in both. The trigger
pulses on every swap and is still when off.

## Link colour from the theme

A link the markdown renders takes its colour from the theme, so it reads on a
dark background as well as a light one, and a theme switch repaints it as it
does a `dynres:` image.

**The brush.** The link's foreground is the resource `WordsLinkBrush` (a brush or
a colour), looked up from the link itself, so it resolves through the window, the
application and, on Avalonia, the theme variant — and follows when any of them
changes. WPF holds a resource reference on the `Hyperlink`; Avalonia binds the
`Hyperlink`'s foreground to the resource. The packages' converters dictionaries
define a default — blue on WPF, a light and a dark variant on Avalonia — so an app
that merges them, as it already does, gets a link that reads in both themes. An
app that defines its own `WordsLinkBrush`, in its theme dictionaries or the one it
swaps in, overrides it; both samples do, and their theme switch shows it. The
underline stays as it was, and so does the console's ANSI blue.

**Nothing defined.** An app that defines no brush and merges nothing still gets
blue: the link carries it as a fixed fallback until the lookup finds something,
and returns to it if the resource goes. A `Hyperlink` an app writes in its own
Avalonia markup keeps the class's blue default; only rendered Words follow the
resource.

**Tests.** On each framework a link with no resource anywhere is blue; on WPF a
link follows its theme dictionary swapped for another, on Avalonia the theme
variant, and the Avalonia package's default reads differently light and dark; a
code span inside a link takes the link's colour; both converters dictionaries hold
the default.

## Code spans

A value can show an API name, a file name or a line of `words.ini` as written,
in a code span.

**The syntax.** CommonMark's: a run of backticks opens a span and the next run
of the same length closes it, so `` `{l:Words key}` `` shows the extension, and
double backticks hold a single one. A span binds tightest — the parser lifts
every span out before it reads any other markup — so nothing inside is markup: no
emphasis, no entities, no shortcodes, no links, no images, and a `*` in a span
neither opens nor closes the emphasis around it. A span may sit in emphasis or a
link label, and in an image's alt text or a title it reads as written. One
leading and trailing space are trimmed when both are there. An unclosed run is
literal backticks.

**Blocks.** Unlike CommonMark, a line break inside a span is kept, not turned
into a space: a value has one only because a trailing `\` asked for it. A span
that starts and ends on a line break drops one at each end, so backticks on lines
of their own fence a block:

````ini
[how.greeting]
value=Write it like this:\
```\
[greeting]\
value=Hello\
```
````

The fence takes no info string; a word after the opening backticks is code.

**What is not protected.** A span protects its text from markdown only.
References expand at lookup, before markdown sees the value, so a `{>key}` shown
in a span still escapes its brace, `{{>key}`; and a value handed to `Format` is a
format string, braces in a span included.

**The renderers.** `MarkdownParser` has `protected virtual TInline Code(string
text)`, defaulting to `Run(text)`, so a parser of one's own written before it
keeps compiling and shows the text plain. WPF and Avalonia render a monospace run
on a subtle background, from the resources `WordsCodeFont` and
`WordsCodeBackground` (a brush or a colour), looked up and followed the way a
link's colour is. The converters dictionaries define both; where neither is
found, the same look stands in: Consolas on a translucent grey that reads on a
light theme and a dark one. A span keeps the colour of the text around it, a
link's included. The console renders a span dim (ANSI 2), bold picking up again
after a span inside it, and plain text drops the backticks.

**Tests.** Spans with emphasis, links, images, entities and shortcodes inside
read back verbatim; a span inside italics leaves the italics whole, and sits
inside bold and a link label; a double-backtick span holds a single backtick; an
unclosed run, or one closed by a run of another length, stays literal; one space
or one line break is trimmed at each end, and inner line breaks are kept; a span
in alt text and a title reads as written; a parser with no `Code` of its own
renders a run. On each framework the span carries the monospace family and the
tint with no resources anywhere, takes both resources from where it lands and
follows a swap, and on Avalonia follows the theme variant; both converters
dictionaries hold the two defaults. The console writes the dim sequence, resumes
bold after it, and writes nothing with ANSI off.

## Language codes

A language code is the BCP 47 subset `language(-Script)?(-REGION)?`: a
language of 2 or 3 letters, a script of 4 letters, and a region of 2 letters or
3 digits. That is `en`, `ceb`, `es-419`, `zh-Hans-CN` and `sr-Latn-RS`. Variants
and extensions wait until somebody asks. Each subtag is cased by its kind, so
`sr-latn-rs` reads `sr-Latn-RS`, and codes compare in that form. `LanguageCode`
is the type, and its `TryParse` is the one check behind the parser's fields,
`NormalizeLanguageCasing`, the authoring reader, the command line, Wordsmith's
language manager and Wordsmith's startup.

**Falling back.** A code falls back by truncation, one subtag at a time:
`zh-Hant-TW` reads `zh-Hant`, then `zh`, then the default (`LanguageCode.Chain`).
`Flatten` takes each key from the first of those with a value; under `Debug` a
shorter code's words are branded 🕮 and the default's 📚, the first found
included where the language has no words of its own. The plural rules look
a code up the same chain. A region falls back past its script, never across to a
sibling, so Traditional never reads Simplified. The family fallback, `en-GB`
to `en`, is the two-subtag case of this.

Windows names its Chinese cultures by region alone, `zh-TW` and `zh-CN`, while
its other languages written in more than one script carry the script
(`sr-Latn-RS`). So a Chinese region that names no script falls back through the
one it writes, as CLDR's likely subtags have it: `zh-TW`, `zh-HK` and `zh-MO`
read `zh-Hant`, then `zh`, and `zh-CN` and `zh-SG` read `zh-Hans`, then `zh`. A
script the code names comes first, so `zh-Hans-HK` reads `zh-Hans`. Other
regions, and other languages, fall back by truncation alone, and the rest is
the file's to name: a language's default script goes under its plain code
(`pa`, not `pa-Guru`), as BCP 47 advises, and a region another platform names
without the script it writes (Linux's `pa_PK`, which writes Arabic) gets
words of its own, `pa-PK`, or the app passes a code with the script.

**A culture's name.** `Flatten` and `ToWords` also take a culture's name, which
can say more than a code: `ca-ES-valencia`, `en-US-POSIX`. It reads as the
longest run of leading subtags that is a code (`LanguageCode.TryRead`), so an
app that digests `CultureInfo.CurrentUICulture.Name` starts in any locale.
`IWords.Language` is that code. `ToWords` takes the name's culture where .NET
knows the name, so `ca-ES-valencia` keeps its variant, and else the code's: a
POSIX `en_US` gets `en-US`'s, and `sr_Latn_RS`, which .NET refuses, gets
`sr-Latn-RS`'s. A name that starts with no code at all is still an
`ArgumentException`.

**A field that is no code.** A field like `value-english=` is read past, along
with its continuation lines, and reported through
`IWordsParserConsumer.VisitBadLanguage`. A runtime warns `WP:LANG`; the
authoring reader gripes that Save will drop it too. A key's `param-name=` names
a parameter, not a language, so it is read as written; the top-of-file
`param-xx=` belongs to language `xx`. An ini that borrows the format for
suffixes of its own turns `WordsParser.LanguageSuffixes` off, as Wordsmith's
settings file does for `scheme-decode=`.

**Tests.**
- The grammar accepts each subtag kind in any case and cases it, and refuses a
  subtag too long or too short, subtags out of order, and `_` as a separator.
  A culture's name reads its leading code.
- Each code's chain, a Chinese region's through its script included, and the
  `DefaultSpeaks` matrix with scripts.
- `Flatten` falls back through three levels, branded, never across scripts, and
  takes a culture's name; it reads `zh-TW` in Traditional and `zh-CN` in
  Simplified, and `zh-Hans-HK` by the script it names; `ToWords("sr-latn-rs")`
  builds, and `ToWords("en_US")` has the `en-US` culture.
- The plural rules look codes up through the chain.
- A runtime reads a three-part code and skips `value-english` with its
  continuation. The authoring reader gripes about it.
- Parameters are read as written, and a settings file keeps its own suffixes.
- An XLIFF target of three parts survives a save and a reload, and one that
  says more reads as its leading code.
- The command line writes and lists in any code.
- Wordsmith's manager takes `ceb`, `es-419` and `zh-hans-cn`, refuses `DE`
  beside `de`, and its startup reads `sr-Latn-RS`, or anything else, without
  throwing.

## The default's language

A key's `value=` is the default, the words an app reads when a language has
none of its own. It has no language code, but it is always written in some
language, and a file can say which one.

**The declaration.** A keyless `value=!xx` in the top-of-file language section
names the default's language. The top-of-file `value-xx=` lines are the
languages' own names (endonyms) and `comment-xx=` lines their names in the
default's language (exonyms), English unless the declaration says otherwise.
An exonym is optional, and Wordsmith writes one only where it was given. To
the parser the declaration is just a label of the default, as `value-xx=` labels
language `xx`. The `!` is what keeps it off `GetLanguages()`, as for any
`!Label`, so a runtime that predates it lists nothing new. The language it names
is usually registered too, so it stays on the menu and keeps its culture. It
need not be. Being a label, it is the last file's: a later file's keyless
`value=` without the `!` labels the default anew, and the declaration is gone.

**Where the default speaks.** The default speaks its own language and every
code that falls back to it (*Language codes*). An `en` default speaks for `en`
and `en-AU`, and a `zh-Hant` default for `zh-Hant-TW`. An `en-AU` default
speaks for `en-AU` alone, so `en-US` and `en` still miss their words
(`WordsParser.DefaultSpeaks`). In those languages, falling back to the default
misses nothing. `WordsBuilder.DefaultLanguage` reads the declaration (the last
file loaded wins), and `Debug()` leaves the default unbranded where it speaks:
no 📚. Fallbacks to a shorter code keep their 🕮.

**Tests.** A file declaring `en` brands nothing in `en` or `en-AU` and still
brands `de`, and lists exactly its three languages. An `en-AU` default brands
`en-US`. `DefaultSpeaks` covers the matrix, case included. A later file's plain
label clears the declaration, and a later declaration replaces it.

## Key names

A key's name is segments of letters, digits, `_` and `-`, each starting with
one of the first three, joined by dots: `menu.file-open`. Letters and digits
are any script's, as Unicode's identifiers have them (UAX #31): a segment is
XID_Continue characters and `-`, starting with an XID_Start character, a
decimal digit or a connector such as `_`. That is UAX #31's default identifier
in a declared profile, where a digit or a connector may start a segment and a
dash continue one, so `errors.404`, `हिंदी.शब्द`, a Persian word with its
non-joiner and letters past the 16-bit plane are all names. A name is in NFC,
so it compares by its code points and two spellings of `é` are never two keys;
a platform without ICU (invariant globalization) cannot tell, and takes a name
as written. A constant is `$` and one segment, `$unit`, and has no children. A
header is `[name]`, or `[.child]` under the last full header, and the key it
resolves to must be a name. `WordsParser.IsKeyName` is the check, and
`IsKeySegment` its one segment; the analyzer and the Rider plugin port it.

**Why there is a grammar.** Until plural forms nothing hung on it, and a header
took anything up to `]`. Now `#` marks a form, so `[lang.c#]` beside
`[lang.c]` read as a form of `lang.c`, and `lang.c#` itself rendered as a
missing key where 1.4.0 read `C#`. A space, `=`, `:`, `;`, a brace or `>` is
no better: the reference and selector syntax, `{>key}` and `{0#key}`, could
never name one. Wordsmith never let anyone type them.

**What a runtime does.** A block whose name is none is skipped, its fields with
it, and warned about once (`WP:NAME`, with the key it resolved to); so are the
`[.child]` headers under it, and under a constant. An empty header, `[]`, is a
header too, though no key's: a runtime skips it, warns `WP:NAME` with an empty
key, and resolves a `[.child]` under it against nothing. The parser itself reads
past every field under it, since a consumer couldn't tell them from the
top-of-file fields. A file's top-of-file labels
are read, whichever block the file loaded before it ended in. The one check
serves every reader: the authoring reader keeps such a block, griping that a
runtime skips it, so an editor can rename it (the editor spec's *The
document*), all but `[]`, which has no fields to keep and which a save drops
with a gripe; the command line writes no key that is none; an import makes a
foreign name one.

**Tests.** The grammar accepts dotted segments in any script, dashes inside,
and a one-segment constant; it refuses an empty name or segment, a leading dot
or dash, `$` inside or a constant's child, and each of the characters above.
Devanagari's spacing marks, a Persian non-joiner, letters past the 16-bit plane
and a digit of any script are its; a leading mark or joiner, an emoji, a lone
surrogate, a letter NFKC would change and a decomposed `café` are not. Checked
once against the ICU that Windows ships (Unicode 12.1): its XID_Start and
XID_Continue agree on every code point both assign, but the four characters
Unicode 15.1 added to XID_Continue (the joiners and two katakana middle dots).
A runtime skips `[lang.c#]` and its continued value, warns once, and reads
`[lang.c]` and the block after; it skips the children of a constant and of a
skipped block; a second file's labels are read after a first that ended in a
skipped block. The authoring reader keeps the block, with its gripe, and saves
it back unchanged; the command line refuses to write one and removes one; an
import maps WinForms' and bracketed names and tells two apart, and keeps
हिंदी's marks.

## Plural forms

Pick the word for a count in the dictionary, not in code. The app that writes
`count == 1 ? "file" : "files"` has encoded English's rule: Maltese gives 2 a
form of its own, puts 3 to 10 in the plural and goes back to the singular from
11; Arabic has six forms; French counts zero as singular. The translator who
knows the rule cannot reach the code that applies it, so the choice moves into
the value, where the translator is.

**The forms.** A key's plural forms are variants of its value, marked with the
category after the language: `value-en#other=Words`, or `value#other` for the
default. The plain value is the `one` form, implied — every key already has it,
and it is what `{>word}` and the indexer render — and a language with one form
writes only that. Comment, context and stale stay one per key. To the runtime a
form is an entry beside its key's value, keyed `word#other`, so it digests per
language as a value does, with one difference: a key's forms come whole from
one level (*Which form a count reads*). `Debug` brands a form that fell back
as it brands a value, and marks 🎲 a count whose category has no form, on the
text it reads instead (*Which form a count reads*). The mark is the forms' alone: a block
whose name holds a `#` is no key (*Key names*), skipped with `WP:NAME`, and a form that is none is warned about
(`WP:FORM`) — one on a label, `#one` (the plain value is that form), or a
category CLDR does not have — which is left out.

```ini
[word]
value=Word
value#other=Words
value-it=Parola
value-it#other=Parole
value-mt=Kelma
value-mt#two=Kelmtejn
value-mt#few=Kelmiet
```

The categories are Unicode CLDR's six — zero, one, two, few, many, other — and
which numbers fall in which is CLDR's rule for the language, carried as a table
in Core since .NET exposes none; the integer rules cover nearly every real call,
so fractions may wait. The table follows the current CLDR release, newer forms
included: French, Italian and Spanish use `many` for exact millions ("un milione
di file"), which older tables lack. An English file writes `other` and nothing
more, and a Maltese translator adds `few`, and `two` for a word that keeps a
dual, as *kelma* does, without a line of code changing.
`PluralRules.Select(language, n)` names a count's category and
`PluralRules.Categories(language)` the ones a whole number reaches, so Russian's
`other`, which takes only fractions, is not among them. A code the table does
not know falls back through its shorter codes (*Language codes*: `pt-BR` is
`pt`; `pt-PT` has its own rule), a language it does not know has only `other`,
as CLDR's root does, and the invariant culture counts as English. Until fractions
come, a number with a fractional part is `other`, a `float` or `double` included
where a `decimal` would round it whole (`1.0000001f`); the sign is ignored, and a
whole value counts as whole whatever its scale or type, `Int128` and
`BigInteger` among them, keeping the low digits a rule reads past `decimal`'s
range, and past the 15 digits `decimal` keeps of a `double` (`1e15 + 1` is
`one` in Russian). The tables come back read-only.

**Which form a count reads.** The category's form, else, for an optional
category, the form it reads instead (below), else the key's `other` form, else
its plain value. `other` is the form a language always has for "more", so it
stands in for a category a translation lacks: Italian without its `many` reads
"1.000.000 messaggi", not "messaggio". A translation's words are a unit: a
key's forms come whole from the first of the language, each shorter code and
the default that has any of the key's words, plain or form, so a translation never
borrows another level's forms beside its own value. Filipino, whose nouns do
not change with the count, writes `value-fil=file` alone and reads "4 file",
where the default's `other` would read "4 files", in English; Maltese leaves
out `#other`, its plain word. A key with no words in the language takes a
shorter code's, or the default's, forms included, branded under `Debug` as values
are. The unit is the forms': a translation that writes forms and no plain value
reads its plain value from the level below, branded, like any missing word, so
its count of 1 shows what is missing rather than a form in its place (owner's
call, 2026-10-07). A language with one category, such as Japanese, speaks only its plain
value, its own or the one it falls back to. `Words.FormKey(provider, language,
key, form)` names the entry a count in a form reads, for a tool that shows it:
the editor's hints and previews.

Under `Debug`, a count that reads another category's text is marked 🎲, so a
translator sees that Russian's `few` is missing where its `other` stands in:
"🎲коробки". The mark goes on each key with forms in any language, a plain
`[solo]` counted being no plural key, and on the text the count reads, the
`other` form or the plain value, ahead of any brand that text carries
("🎲🕮…"). The plain value is the `one` form, so a count of 1 is never marked;
an optional category reading its stand-in is no miss (below), though a
stand-in category that is missing reads marked, as it does counted itself
(Maltese `two` reads `few`'s mark); a language with one category speaks its plain value
alone; and a key borrowed whole from a default that does not speak the
language is marked 📚 alone, since every form of it is missing. `Flatten` adds
each marked text as the category's own entry, so the count indexer, the
selector and `FormKey` find it without knowing `Debug` is on.

**Optional categories.** Some categories usually read like another, so a
translation may leave them out. `PluralRules.Optional(language)` names them,
each with the category a missing one reads. Maltese `two` reads `few`, since
only a handful of its words keep a dual, most of them time (*jumejn*,
*sentejn*), its `many`, 11 to 19, reads `other`, and its `other`, 20 up,
reads `one`, the plain value: from 11 up a Maltese count takes the singular.
`other` reads nothing but the plain value, where every form ends up anyway.
Hebrew `two` reads `other`, its dual kept by time words too. The exact
millions of French, Italian, Spanish, Portuguese and Catalan, of Ladin,
Sicilian and Venetian, which share the rule, and of Breton, whose `many` is
the same, read `other`: the difference is a "de" or a "di". The table is Words'
own judgement, not CLDR's, kept beside its rules. A translator still writes an
optional form where a word needs one, `value-mt#two=Kelmtejn`, and it reads as
any form does. The editor counts only the categories that are not optional as
missing words. Russian's `other`, which takes fractions and reads like its
`few`, joins when fractions do.

**The selector.** A third reference beside `{$constant}` and `{>key}`, with the
same mark as the forms: `{0#word}` names a parameter and a key. The parameter is
not printed; its value selects, under the dictionary's language, which of
`word`'s forms is spliced in. The count prints wherever the author puts `{0}`,
so `value={0} {0#word}` reads "1 Word" and "2 Words"; one parameter may select
several words; a named parameter selects the same way, `{Count#word}`; `{0#.n}`
is relative to the block as `{>.sub}` is; and a missing key renders `#word#` as
ever. A selector is a reference, and the circular cut applies: a key does not
select among its own forms. A sentence that changes shape as a whole keeps its
forms in a sub-key and selects there, or is read whole through the count indexer
below. A numbered selector past the arguments throws a `FormatException`, as
`{3}` would. A name no member answers, or a name in a positional call, warns
and picks `other`; `null`, a bound value not there yet, picks `other` quietly;
any other value that is no number picks `other` and warns (`WORDS:COUNT`).

**Where it resolves.** The selector resolves in the `Format` family, which holds
the arguments — `Format`, `FormatByName`, `FormatParams`, `ConvertValue`,
`FormatKnown`, and the `IWords` overloads of `RenderKey` and `RenderText` given
arguments — before `string.Format` sees the template: each `{n#key}` is replaced
by the form its argument selects, rendered, so a form may itself carry a
`{>key}` or a `{0}`, and selected through in turn, so a form may select too.

References and selectors expand in one pass over the key's words, each against
the key whose text it was written in. A referenced key's own `{>.sub}` and
`{0#.n}` are that key's, and relatives chain: under `[a]`, `{>.b}` reads `a.b`,
whose `{>.c}` reads `a.b.c`. A form's text is its key's, so `word#other`'s
`{>.sub}` is `word.sub`, whether a selector, the count indexer, a
`{>word#other}` or a lookup of `word#other` itself reached it. What a reference
brings in is never scanned again.
One recursion path runs through it all: a reference is circular when its key is
being rendered, and a selector when any of its key's words are, plain or a form.
So a key that selects its own forms renders the circular mark wherever it is
referred to, while a form may still refer to its key's plain value
(`value#other={>item}s`). `RenderText`'s text is the caller's, not its base
key's words: the base key anchors its relatives and nothing else, so the text
may refer to that key or select among its forms, given arguments or not.

An escaped pair collapses in the same pass, so `{{0#word}` is a brace and text,
no selector; `string.Format` then wants its own `{{`, as for `{{>key}`. A
`{{{{` pair still reaches `string.Format` as `{{`. That a formatted brace takes
two escapes is a bug, planned (*One escape for a brace*). The `Format` family renders the
key from `Provider`, as `RenderKey` does; an `IWords` of one's own whose indexer
dresses words up is read through only for a key its provider lacks. What it
answers is rendered already, so only its selectors are left, each resolved
against that key, and a `{{` in it is `string.Format`'s, and stays.

On the named path `{0}` is the object itself, as `PreFormatByName` slots it, so a
converter's bound count selects with `{0#word}`. A dictionary given as the value
supplies its values by name, never its own members, so `{Count}` reads the
entry and not the dictionary's `Count`. That is any dictionary by a `string`
key, of any value type: an `IDictionary`, or an `IReadOnlyDictionary<string, T>`
or `IDictionary<string, T>` of one's own that is nothing else. A tool holding samples by name
selects as an app would. A bare `null` fills every name with nothing, as in
1.4.0. The template overloads 1.4.0 shipped with an `IReadOnlyDictionary`
parameter (`Words.FormatByName`, `Words.PreFormatByName`) stay for callers built
against it, hidden from completion; they take a `null` too, and read exactly
as the `object` overloads do, so whichever one a call binds to doesn't matter.

The indexer leaves a selector in place — it has no argument to select with — so
a plural template reached through `Words.Known[key]` and the caller's own
`string.Format` throws, which is the right signal: that template wanted
`Words.Format`. The provider-level `RenderKey` leaves it too: a provider has no
language to select in.

**The count indexer.** A caller holding a key and a number needs neither
`Format` nor a selector: `Words.Known[key, n]` is the lookup with a count, and
returns the key's own form for `n`, rendered as any value is —
`Words.Known["word", 1]` is "Word", `Words.Known["word", 2]` is "Words". It takes
a `decimal`, which every integer converts to (a `double` needs a cast). It is a
member of `IWords` with a default implementation, and so is `IWords.Language`,
the code whose rules pick, which goes by `IWords.UICulture`'s name: a
dictionary of one's own speaks the thread's UI culture, and needs nothing.
`CulturedWords` keeps the code it was built for, since .NET turns a code it
does not know into the invariant culture, which counts as English: Cebuano,
Ladin and the legacy `iw` and `tl` are among the table's languages it does not
know, and they still count by their own rules.

**Readers.** The pair grammar admits `#form` after the language, and the form
travels with the field type, lowercased (`value#other`), so a consumer that
knows `value` learns `value#other` the same way: Core digests it, the
authoring side round-trips it, and a runtime that knows no forms skips every
`#` line and loads the plain values. Wordsmith shows the forms one at a time
(the editor spec's *Plural forms*), and the samples on their Format
parameters page.

**Tests.** A headless test digests a file with English, Russian, French and
Japanese, formats a counted key at 1, 2, 5, 11, 21, 22, 25 and 101 under English
and Russian, and reads the expected forms. A category without a form falls to
`other`, then to the plain value; a translation's own words keep the default's
forms out, as a shorter code's keep them out of a longer one, and a key with no words
in the language takes the default's, branded; under `Debug` a count reading
another category's text is marked 🎲, Maltese `two` through its missing `few`
too, but not at 1, nor an optional category whose stand-in is there, nor a
one-category language, nor a key borrowed whole from a default that does not
speak the language, while a default that speaks it is marked like a
translation; a missing optional form reads
the one it stands for, and a written one reads as any; Japanese reads only its
plain value; a named selector picks the same
form as a numbered one; a key selecting its own forms renders the circular mark;
forms refer, format and select in turn, and a `{{` pair stays; every formatting
path selects; the indexer returns the template with the selector intact, and the
count indexer returns the key's own form for the count. The table's spot checks
follow current CLDR, and each rule's categories are what whole numbers reach;
an optional category, and the one it reads, are both its language's.
The parser lowercases a form, continues it and warns about forms that are none.
Checked once against the ICU that Windows ships (CLDR 35): the table agrees
except where CLDR has changed since. Where things resolve: a form's relative
reference, chained twice, through a selector, the count indexer, a
`{>word#other}` and a lookup of the form itself; a referenced key's selector
picks from its own sub-key; a key referring to one that selects its own forms
renders the circular mark, while a form may refer to its plain value; an
escaped selector is none; `RenderText` given arguments may refer to its base key
and select its forms. `FormatByName` fills a `null` with nothing on every path
and reads a dictionary, of any value type and one's own too, by its values; `RenderKey` with
arguments reads the provider as it does without; a template an `IWords` of
one's own answers still selects, its `{{` left to `string.Format`; every number
type counts, a float's fraction is `other`, and a double's sixteenth digit
picks; the runtime's
default language is trimmed and cased; `blo`, `cv`, `kok` and `sgs` count by
CLDR 48; the tables refuse to be changed; `CulturedWords`' indexers are
`[Localized]`; a pool thread's `TriggerWords.Watch()` leaves the trigger on its
UI thread.

---

# Planned upgrades

Not built. Each section here is the shape the feature takes when it is, or the
question it has to answer first.

## A library written in another language

A library's defaults are written in its own language, and its host's may be
written in another: a German library under an English app. The runtime has
one default's language, the last file's declaration (*The default's
language*), so it cannot tell the two apart. In English, either the library's
German defaults read unbranded, as if they spoke English, or the library's
declaration replaces the host's and the host's own English defaults are
branded 📚 where they speak. Wordsmith checks each file against its own
declaration, and flags a library only for a language a host lists, not for
the one the host's default is written in. Undecided: whether each file's
default keeps its own language at runtime, so a fallback is branded where
the languages differ, and whether Wordsmith flags a library written in
another language than its host's default.

## FormatByName's dictionary overloads

The template overloads 1.4.0 shipped with an `IReadOnlyDictionary` parameter
stay, hidden from completion (`[EditorBrowsable(Never)]`), and read as the
`object` overloads do (*Plural forms*). The goal is to steer callers to the
`object` overload and warn anyone who reaches a dictionary one, and neither
attribute does that alone. `[Obsolete]` warns on every call that binds there, a
dictionary's or a bare `null`'s, though the `object` overload would read it the
same. `OverloadResolutionPriority(-1)`, which would send those calls to the
`object` overload, makes `Words.FormatByName(null, template, dict)` ambiguous
from C# 13, needs C# 13 for Core's net8.0 build, and is ignored by a C# 12
caller. Nothing breaks today, so it waits for a way that warns only the calls
it should.

## One trigger per synchronization context

`TriggerWords` is one for the process, and lives on the UI thread whose
`Watch()` came last (*Live language switching*). A WPF app with a second UI
thread, a splash screen or a tool window on its own Dispatcher, moves it there
with that thread's `Watch()`. The shape: a trigger per synchronization context,
each multi-binding taking its own thread's, and a swap pulsing each on its
own context. The context-less half is built: a `Watch()` from a thread with no
context leaves the trigger where it was.

## One escape for a brace

A brace is escaped once in words, `{{`, and rendering collapses it, so
`{{>key}` shows as `{>key}`. A value handed to `Format` is rendered first and
then read by string.Format, which wants an escape of its own: `{{0}` renders
to `{0}` and is filled, `{{>key}` renders to `{>key}` and throws, and a
literal brace in a formatted value takes `{{{{`. That is a bug: whoever writes
a value should not have to know whether the code formats it, and a
translator cannot. The shape of the fix: on the `Format` path, rendering
leaves an escaped brace for string.Format to collapse, so one `{{` is one
brace either way. Undecided: `}`, which Words does not escape and
string.Format does, so `{{0}}` would read `{0}` formatted and `{0}}` plain.
A file that wrote `{{{{` for a formatted brace would then read two, so the
fix's release notes say so. Wordsmith's parameter finder reads the runtime
as it is, `{{0}` a use (the editor spec's *Parameters*), and follows the fix.

## Describe without the type

`Describe` reads an enum member's name, number, key (`[Words]`) and attribute
text, looks up the words beside the key, and assembles what the format's
letters ask for. It used to reflect on every call, asking for each attribute
by name whether the member had it or not, and to look up every key beside the
member though `G` reads one; and Wordsmith, which has the keys and never the
type, could not describe at all (the editor spec's *Parameters*, Types).

**The seam.** `IDescribable` is what the engine reads: `Name`, `Number`, the
number as text so any integer type's value fits, `Key`, and `Text(slot)`, the
text an attribute gave a slot, by the slot's letter (Slots, below).
`Describe(this IDescribable, format, words)` is the engine, and it looks up
only the keys its letters ask for, each once: `G` the key alone, `T` the key's
`.tooltip`, and so on. `Describe(this Enum, …)` takes the member's describable
from `Describable.Of`, built once per member and kept per type, and runs the
same engine. A value's member is its first name that is not obsolete, as
`Enum.GetNames` orders them, so an old name kept for compatibility never
stands in for the new one. The cache holds what the type says and never the
words, so a language switched live reads afresh.

**Without an enum.** `Describable.OfKey(key)` describes a key with no type
behind it: its name the last segment, no number and no attribute text, so the
letters read the key's `.tooltip` and kin as they would for a real member.
Wordsmith's `enum(prefix)` input builds one per key under the prefix.

**Flags.** A `[Flags]` combination is no member. `Describe` on one reads as it
always has: the joined names, and for `i` the whole number.
`Describable.Members(value)` gives the members `Enum.ToString` names for it,
which settles overlapping values the way .NET does, each one's describable
from the cache, and `Describe` on that list gives one text per member. Both
frameworks' flags converters stand on the pair and keep their options:
leaving out `None`, the delimiter, a list or one string.

**Slots.** A slot is a format letter, the suffix it reads beside the key, and
the attribute text it falls back to where the key has none. The canon: `G`
and `n`, the key's own words, then the general text an attribute gives, then
the description, then the name (`N`: the key's words, then the name); `D` and
`d`, `.desc`, then `[Description]`; `S`, `.sub`, then `[Tooltip]`; `T`,
`.tooltip`, then `[Tooltip]`; and `s` the name and `i` the number, which read
no key. The four that read beside the key are capitals, `G`, `D`, `S` and
`T`, and the docs name them so; `n` and `d` are the same slots under the
letters older formats use. `DescribeSlot`, a `byte` enum, names the canon by
its letters, `DescribeSlot.Tooltip = (byte)'T'`, cast to `char` where a slot
is asked for, so `(char)DescribeSlot.Tooltip` and `'T'` are one slot. Any
other letter or digit is an app's own, once it is on `[Words]` and keeps more
beside each member: `Describable.Slot('H', ".hint")` makes `H` read
`key.hint`, and the registry can point an attribute at it as at any slot. A
letter that is a slot already, built in or added, is refused, and so is a
suffix that is not a dot and one segment of a key's name.

`.unit` left the canon on the same footing: a suffix to another value is one
app's need, not every enum's, and one line puts it back,
`Describable.Slot('U', ".unit")`. The samples' words add it, and their Enums
page shows each brew's unit, a slot of one's own on show.

**A letter no slot answers** reads as `G`, marked `#!X#`, and warns
`WORDS:SLOT` with the letter. It used to read nothing, so a typo in a format,
or an app reading `U` without adding it, vanished without a word; marked, it
leaves something to search the gripes and the docs for. Quoted text, `''`
being a quote, and anything that is no letter or digit are written as they
are. The release that brings this says both in its notes: `U` is no longer
built in, and a stray letter shows.

**The registry.** What fills a slot is registered, not searched for: the
registry maps an attribute type to what it gives, the key or a slot's text,
and how its text is read. It starts with `[Words]` for the key,
`[Description]` for `D` and `[Tooltip]` for `S` and `T`. Building a member's
describable asks its field for its attributes once and looks each one's type
up; an attribute the registry does not know is passed over, and one it knows
is never probed for on a member that lacks it. An app registers an attribute
it already has, `Describable.Fill<HintAttribute>('T', hint => hint.Text)`, so
`Describe` reads it while moving to `[Words]` is not an option yet, and its
enums are not touched at all. `Fill` takes `G`, `D`, `S`, `T` or a slot the
app added, `n` and `d` registering for their capitals; the name and the
number read no attribute. An attribute that names
the key is registered with `FillKey`. Where two attributes give the same
thing, the one registered first wins, so a member's `[Words]` beats an app's
own key, and `[Tooltip]` an app's own tooltip.

An enum type can instead key its members itself, so it needs no attribute at
all: by a prefix, `Describable.Keys<Brew>("enums.brew")`, each member's key
the prefix and its name; or by a function, for an enum one does not own or
whose keys do not follow its names,
`Describable.Keys<HttpStatusCode>(code => $"http.{(int)code}")`, a `null`
leaving a member keyless. The function is asked once per member, when the
type is first described. A member's own key still wins, and a type has one
way to key its members, the latest given. Registering belongs at startup. A registration is a new registry
with an empty cache, so a type described before reads again, and a describe
already under way finishes on the registry it began with. This is the
migration `[Tooltip]` stood in for: the Core readme's advice to move a custom
attribute's text into `[Tooltip]` is now registering that attribute, and
`[Tooltip]` stays, obsolete, for the code that already uses it.

**A question: in a template.** string.Format hands an enum argument to
`Enum.ToString`, so `{0:T}` throws rather than describing. An `IDescribable`
that is also `IFormattable` would let a template take one, `{0:T}` reading
the tooltip, and the preview would pass Wordsmith's own the same way. Handing
every enum argument over as its describable inside the `Format` family goes
further. It changes what `{0}` prints for a member with a `[Description]`,
and `D` means Enum's decimal to one and the description to the other. So it
would be opt-in, if at all. Until this is answered, Wordsmith's input sends
the general text, `Describe()` with no letters.

**Tests.** Every built-in letter reads as before, quoting and punctuation
included, for a `[Words]` member, a `[Description]` one, a bare one and one
sharing its value with an obsolete name; `G` looks up the key alone and `T`
its `.tooltip` alone, each once, and `s` and `i` none; a member is read once
however often it is described; a registered attribute fills its slot, one
attribute can fill two, an unregistered one is passed over, and the first
registered wins; `[Words]` beats an app's own key, which beats a key prefix;
a function keys an enum one does not own member by member, once each, a
`null` leaving a member its name, and the latest for a type stands;
a slot of one's own reads its suffix, then its attribute; a letter taken, no
letter and a bad suffix are refused; a letter no slot answers reads as `G`
marked, and warns; the samples' `U` reads a brew's unit; a combination's
members are those .NET names, an overlap and an undefined value among them,
while `Describe` on the combination reads as before; a registration after a
describe reads the type again; the cache holds no words, so another language
reads its own; and a describable built from a key alone reads its slots. The
`Describe` and converter tests stand unchanged.
