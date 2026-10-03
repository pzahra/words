# Words runtime — what the library guarantees

The runtime is the library an app actually ships: `Localization-Core` and the
framework packages on top of it (`Localization-Wpf`, `Localization-Ava`).
Wordsmith, the editor, has its own spec ([../WordsEdit/SPEC.md](../WordsEdit/SPEC.md));
this one fixes the runtime's behavior. Everything up to *Planned upgrades* is
what the library does today; the last part is what it does not do yet.

## The dictionary is a snapshot

`Words.Known` is the process-wide dictionary, built once at startup:
`WordsBuilder.Create().Load(…).Digest(code)` flattens every loaded source into
one language (exact → family → default), installs it, and applies its cultures
to the threads. From then on it is a *snapshot*. A value handed out is a plain
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

**Known gap: the XAML analyser.** The IDE's XAML analyser has not caught up with
the `object` constructor. It does not understand it, and balks at a key passed
as a string, `{l:Words some.key}`, though that builds and runs as it always
did. Not yet looked into; one lead is that `Key`, the property
`[ConstructorArgument("key")]` names, is still a `string`.

**The converter hoist.** Wrapping a converted binding in a `MultiBinding` with
the tickle is *not* enough, and the reason is easy to miss: a `MultiBinding` does
not re-run a child's converter when a sibling leg changes — each child caches its
converted value and recomputes only when its own source moves, so pulsing the
tickle hands back the source leg's stale string. Live, `{l:Words}` therefore
**hoists** the inner binding's converter: it reads `.Converter`,
`.ConverterParameter`, `.ConverterCulture` and `.StringFormat` off the binding
(which arrives before it is instanced, so they move cheaply, no clone), clears
them, uses the bare source as leg 0, adds the tickle as leg 1, and its
multi-converter re-applies the hoisted converter to the current source value —
or, for a bound key, looks the key up — with the string format on the
`MultiBinding`, applied after it as it was. A swap pulses the tickle, the
multi-converter runs again, and the value re-localizes; a source change drives it
the same way. Avalonia takes the same hoist, so the twins match; its
`ConverterCulture` arrived after 11.0, which the package still takes, so it moves
only where it exists. This is display-only: the hoisted converters are one-way
(`ConvertBack` throws), which localization is.

**The tickle.** `WordsTickle.Instance`, in Core: one keyless `IKnowWords` for the
whole process, carrying no text, only the pulse — a `Pulse` count that raises
`PropertyChanged` on every swap. The extension registers it
(`WordsTickle.Watch()`) when it builds a live multi-binding; off, it never
pulses. An app can give its own multi-bindings the same leg.

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
takes a bound key, and a WPF template loaded twice converts in both. The tickle
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

---

# Planned upgrades

Not built. Each section here is the shape the feature takes when it is.

## Plural forms

Pick the word for a count in the dictionary, not in code. The app that writes
`count == 1 ? "file" : "files"` has encoded English's rule: Russian puts 1, 21
and 31 in one form, 2 to 4 in another and 5 to 20 in a third; Arabic has six
forms; French counts zero as singular. The translator who knows the rule cannot
reach the code that applies it, so the choice moves into the value, where the
translator is.

**The forms.** A key's plural forms are variants of its value, marked with the
category after the language: `value-en#other=Words`, or `value#other` for the
default. The plain value is the `one` form, implied — every key already has it,
and it is what `{>word}` and the indexer render — so a category with no form of
its own falls to the plain value, and a language with one form writes only that.
Comment, context and stale stay one per key.

```ini
[word]
value=Word
value#other=Words
value-it=Parola
value-it#other=Parole
value-ru=слово
value-ru#few=слова
value-ru#many=слов
```

The categories are Unicode CLDR's six — zero, one, two, few, many, other — and
which numbers fall in which is CLDR's rule for the language, carried as a table
in Core since .NET exposes none; the integer rules cover nearly every real call,
so fractions may wait. An English file writes `other` and nothing more, and a
Russian translator adds `few` and `many` without a line of code changing.

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
below.

**Where it resolves.** The forms are entries of the dictionary like values:
digested per language with the same exact → family → default resolution, and
rendered through the same reference expansion, so a form may itself carry a
`{>key}` or a `{0}`. The selector resolves in the `Format` family, which holds
the arguments, before `string.Format` sees the template: each `{n#key}` is
replaced by the form its argument selects, then formatting proceeds. The indexer
leaves a selector in place — it has no argument to select with — so a plural
template reached through `Words.Known[key]` and the caller's own `string.Format`
throws, which is the right signal: that template wanted `Words.Format`. A
non-numeric argument selects `other` and warns.

**The count indexer.** A caller holding a key and a number needs neither
`Format` nor a selector: `Words.Known[key, n]` is the lookup with a count, and
returns the key's own form for `n`, rendered as any value is —
`Words.Known["word", 1]` is "Word", `Words.Known["word", 2]` is "Words". It is a
member of `IWords` with a default implementation, so a dictionary of one's own
needs nothing.

**What changes.** The pair grammar admits `#form` after the language, and the
form travels with the field type, so a consumer that knows `value` learns
`value#other` the same way: Core to digest it, the authoring side to round-trip
it. Wordsmith shows the forms as extra value boxes at first, and later shows
each language the categories CLDR says it uses, so a Russian translator sees
`few` and `many` beside the value and an English one sees `other`. Nothing else
moves: a file with no `#` forms parses, digests and renders byte for byte as
today.

**Tests.** A headless test digests a file with English and a four-form language,
formats a counted key at 1, 2, 5, 11, 21, 22, 25 and 101 under each, and reads
the expected forms; a category without a form falls to the plain value; a named
selector picks the same form as a numbered one; a key selecting its own forms
renders the circular mark; the indexer returns the template with the selector
intact, and the count indexer returns the key's own form for the count.
