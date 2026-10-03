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
restarting, the model Wordsmith itself follows (its own spec, *Wordsmith's own
words*: picking a language "restarts the editor with the same files open"). The
builder's un-flattened sources are released once `Digest` has run; nothing keeps
them.

**Why.** The common app ships one language per process, or relaunches to switch.
Holding every language's source in memory and wiring every rendered string for
live replacement is a cost that app should not pay by default. Live switching is
the opt-in below.

---

# Planned upgrades

Not built. Each section here is the shape the feature takes when it is.

## Live language switching

Switch the display language in a running process and have every string on screen
follow — for the app that wants it, and only for that app. The whole feature is
opt-in, chosen before `Digest`, and invisible when it is not: off, the runtime
behaves exactly as *The dictionary is a snapshot* describes, byte for byte.

**Turning it on.** `WordsBuilder` gains a `Live()` (name provisional) called
before `Digest`. It does two things: it keeps the builder's un-flattened
per-language sources — and its `Debug`/`UseSystemNumbers` settings — alive past
`Digest` instead of releasing them, and it arms the watch registry below. Off
(the default), `Digest` behaves as today and every hook in this section is a
no-op.

**Re-flattening.** With the sources retained, `Words.SwitchLanguage(code)`
(provisional) re-flattens for the new language and assigns `Words.Known` — the
same flatten the builder runs at startup, without touching disk. The assignment
is the trigger: everything downstream hangs off the `Words.Known` swap, so a
plain `Words.Known = dictionary` relocalizes just as well.

**LazyWords is the proxy.** [`LazyWords`](Words.cs) already holds a key, resolves
it against `Words.Known` on first read, and caches — most of a live proxy
already. It gains `INotifyPropertyChanged` and implements `IKnowWords`, a
one-method seam (`Refresh()`) the registry calls; its `Refresh()` drops the
cache and raises `PropertyChanged(Value)`, so the next read re-resolves and any
binding over it re-pulls. That is the whole proxy — no new type. A *literal*
`LazyWords` — the implicit `operator LazyWords(string)`, or one whose `Value`
was assigned — holds text, not a key, and never resolves, so it never watches
and `Refresh` leaves it be.

**Registration is at resolution, not construction.** Anything implementing
`IKnowWords` joins the registry through `Words.Watch(this)`; a ViewModel that
wants to re-raise its own notifications on a swap can implement it and register
from its constructor. `LazyWords` is the built-in implementer, and it registers
not from its constructor but at first *resolution* — the moment `Value` actually
reads `Words.Known`. That suits its laziness and, more usefully, keeps out the
literals that never resolve. An unread holder stays out of the registry until
something reads it.

**Living bindings.** A rendered string follows a swap only if the markup hands
XAML something that stays connected, so in live mode `{l:Words …}` returns a
binding, not a string — and it is the one door for every case, taking either a
key or a whole binding through a single `object` constructor that branches on
what it got:

- `{l:Words some.key}` — a string. Returns a `Binding` to the interned
  `LazyWords` proxy for that key: one shared proxy per distinct key, not one per
  usage, so a window with forty labels over forty keys carries forty proxies and
  a swap raises forty `PropertyChanged`, not one per visual.
- `{l:Words {Binding KeyName}}` — a binding with no converter. The bound value
  *is* the key; a dynamic lookup, re-run on every swap.
- `{l:Words {Binding Status, Converter={StaticResource WordsConverter}, ConverterParameter=op.status}}`
  — a binding carrying its own converter (a template fill here, an
  [`EnumDescriptionConverter`](../Localization-Wpf/EnumDescriptionConverter.cs)
  or `FlagsDescriptionConverter` elsewhere). `{l:Words}` never has to know which:
  the inner binding's converter says how to localize, so the enum-vs-template
  choice is the author's, not the extension's.

Off, each shape is its snapshot self — the string resolves once, the wrapped
binding is returned untouched with its converter intact — so live mode costs
nothing when it is not asked for.

**The converter hoist.** Wrapping a converted binding in a `MultiBinding` with
the tickle is *not* enough, and the reason is easy to miss: a `MultiBinding` does
not re-run a child's converter when a sibling leg changes — each child caches its
converted value and recomputes only when its own source moves, so pulsing the
tickle hands back the source leg's stale string. `{l:Words}` therefore **hoists**
the inner binding's converter: it reads `.Converter`, `.ConverterParameter` and
`.ConverterCulture` off the binding (which arrives before it is instanced, so the
three move cheaply, no clone), clears them, uses the bare source as leg 0, adds
the process-wide *tickle* as leg 1, and its `IMultiValueConverter` re-applies the
hoisted converter to the current source value. A swap pulses the tickle, the
multi-converter re-runs the real converter, and the value re-localizes; a source
change drives it the same way. The tickle is a single keyless `IKnowWords`
registered once for the whole process — it carries no value, only the pulse.
`LazyWords`, the tickle and the registry live in Core; the `Binding`,
`MultiBinding` and the markup extension are the framework packages', and Avalonia
may skip the hoist entirely — its reactive bindings re-project through a converter
when a merged dictionary-change observable ticks.

This is display-only: the hoisted converters are one-way (`ConvertBack` throws),
which localization is. And the limit narrows to the irreducible one — a binding
never wrapped in `{l:Words}` stays a snapshot, because Core cannot reach a
`Converter=` it was never handed.

**Rendering controls.** A control that renders Words itself rather than handing a
string to a property — the inline/markdown renderers, `WordsInline` — is the last
shape: it implements `IKnowWords` directly and rebuilds its content on `Refresh`,
no binding in the middle. Same registry, same weak hold, same threading rules.

**The registry.** A static weak list in Core: a proxy joins by weak reference, so
it lives exactly as long as the binding (or service) that holds it and no longer.
This is deliberately *not* a `static event Words.KnownChanged` — a static event
roots every subscriber and is the textbook managed leak. Dead references are
swept two ways: a `Words.Known` swap refreshes the live proxies and drops the
husks it passes, and `Words.Watch` compacts amortized — every so many calls it
checks for entries whose target has gone (a disposed ViewModel, an unloaded
view) and clears them, so husks stay bounded between swaps even under heavy
churn. No GC hook is needed, and none exists worth using; the interned per-key
table needs no sweep of its own, since a dead key's husk is overwritten the next
time that key resolves. `Words.Watch` is the entry point; off, it returns
immediately, so the call site needs no condition of its own.

**Threads.** `Words.Known`'s setter is volatile and callable from any thread, and
a refresh touches UI objects. The registry captures `SynchronizationContext.Current`
when a proxy registers and posts that proxy's `Refresh` back to it, so a switch
raised on a background thread reaches each binding on the dispatcher it belongs
to.

**Robustness.** The refresh walks a snapshot of the registry (a refresh may
construct new proxies), and a proxy whose `Refresh` throws is reported to
`Words.Logger` and skipped, so one bad listener never aborts the relocalization.

**Nothing here is new capability.** Every piece of this is reachable by hand with
the library as it already stands: `Words.Known` is swappable from any thread, a
builder kept alive already re-flattens for another language, and any object can
already read `Words.Known` on demand and raise its own change notification. An
app determined to switch live could wire all of it itself. Live mode is that
wiring done once — weakly held, swept, thread-marshalled — so the app gets the
shortcut instead of the plumbing. That is also why it stays opt-in: it earns its
keep only for the app that wants it, and costs the others nothing.

**Tests.** A headless test drives the whole loop without a UI: opt in, digest,
take a `LazyWords`, read it, `SwitchLanguage`, and its `Value` is the new
language and its `PropertyChanged` fired. Off by default, a proxy never registers
and a `Words.Known` swap notifies nothing — the snapshot behavior above, pinned.
The weak contract gets its own test: a proxy with no other referent is
collectable, and the registry neither resurrects nor refreshes it.

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
