# Words API — review findings & remediation plan

From a Codex adversarial review of each API module (2026-09-10), one module at a
time. Raw per-module reports sit beside this file: [core](core.md),
[authoring](authoring.md), [wpf](wpf.md), [ava](ava.md).

**Working convention:** the Progress checklist at the bottom is the source of
truth for what's done. Update it in the *same commit* that addresses an item,
and keep this file in that commit, so the plan can be picked up on any machine
mid-stream.

Severity as reported: Core 5H/9M/4L · Authoring 7H/7M/3L (heaviest) · Wpf
2H/11M/5L · Ava 2H/11M/4L.

## Corrections to the raw reports (by design, not defects)

- **Nested constants** (authoring #4): constants are **top-level only** and do
  not nest. `[$unit]` is the only shape; `group.$key` is not a thing. The
  finding is void — dropped from the plan.
- **Empty / whitespace-only values** (authoring #5): represented deliberately
  with a `\` line continuation (see the `whitespace-only` block in the
  ExampleFile fixture). The model never needs a truly-empty key to survive, so
  dropping `IsEmpty()` keys (synthetic cut headers) is correct. Not an action
  item — but the backslash-escaping fix below must run *before* the writer adds
  its own continuation backslashes so it never doubles them.

## Decisions (2026-09-10, with the maintainer)

- **Step 3 — repeated `value=` stays overwrite/warn; fix the docs.** A repeated
  `value=` keeps only the last and logs `WB:KOVR` (a later `Load` overlay relies
  on this). That was a design change from the old "append" idea, but breaking it
  isn't worth it and almost nobody relies on append-within-a-key — so leave the
  code and correct the docs: the readme/SKILL examples claiming a repeated field
  "continues the line" are wrong (continuation is a trailing `\`/`_` only).
  Narrow the WordsEdit byte-stability / "never lose data" wording to the
  canonical save→load→save it guarantees, and fix the long `stale=` continuation
  truncation (a real bug). No lossless ordered layer; unknown fields and unknown
  param types documented as not preserved.
- **Step 4 — rendering safety now; dynamic resolution later.** `staticres:` value
  resources (`ImageSource`/`IImage`, `Geometry`) render in a fresh host, safe to
  reuse; a resource that is itself an element is refused with an `IMG:ELEM` gripe
  pointing at a template, because one element instance can't live under two parents
  (and mutating a shared `Shape` leaks). `pack:` already builds fresh. `WordsInline`
  builds its inlines before clearing, so a formatting failure leaves the old content
  standing. Requested image dimensions are clamped (finite, non-negative, capped)
  rather than thrown and surfaced as a missing image. Deferred to Step 4b (with
  `dynres:`): resolving through a framework resource reference (fixes the app-only
  scope), a `dynres:` scheme that tracks live theme swaps, actually building a
  `DataTemplate`/factory for element resources, and a converter as the shared
  sanitation layer — one cluster ("let the framework resolve, render via
  template/converter") that lands together. Hosting arbitrary controls (input
  controls) stays a when-actually-called-for concern.
- **Step 4b — let the framework resolve; render through a converter.**
  `staticres:` and `dynres:` now hand back a `ResourceImage` host that carries
  the key and resolves it from where it lands in the tree, through the
  framework's own reference (WPF `SetResourceReference`; Avalonia
  `TryFindResource`/`GetResourceObservable`, asking for the theme variant in
  effect — a `ThemeDictionaries` entry is invisible to a theme-less lookup,
  which is how the sample's static icon first degraded) — so a window's or user control's
  resource is found, not only the app's. Static pins the first value found
  (`{StaticResource}` semantics); dynamic follows every swap
  (`{DynamicResource}`: a theme change re-renders). The type dispatch moved out
  of the resolvers into `ResourceVisualConverter` (XAML-usable as
  `WordsResourceVisual`): `ImageSource`/`IImage` → `Image`, `Geometry` → `Path`,
  and a `DataTemplate` loads fresh content each time — the reusable form the
  old `IMG:ELEM` gripe pointed at, now actually built. Per Patrick, keep it
  simple: a resource that exists but is the wrong type throws
  (`InvalidCastException`, as a wrong-typed resource would anywhere in the
  framework; from a XAML binding it surfaces as a binding error), and the
  parser's broken-image catch lets that one through. A missing key still
  renders the alt text — a `words.ini` typo must not eat the sentence. Sizing
  moved into a shared `ImageSizing` so the host can size what it resolves
  later, and `ImageOptions` became a record carrying the rendering context the
  parser fills in (`BaseFontSize`, `AltText`). Both samples grew a dark/light
  switch so the live reference has something to show: a theme-scoped
  `ThemeIcon` (sun in Light, moon in Dark — Avalonia `ThemeDictionaries`; WPF a
  swapped `Themes/*.xaml` merged dictionary) rendered through `staticres:` and
  `dynres:` side by side, so only the live one changes. `?foreground=` /
  `?background=` as a resource key (Item 5, 2026-09-11): the ambiguity that kept
  it "(later)" — a typo'd color read as a key — is sidestepped by spelling the
  reference out, `staticres:key`/`dynres:key`, the vocabulary the image schemes
  already teach, so a bare word is still only ever tried as a color.
  `ImageOptions.Foreground`/`Background` became a `BrushOption` (a literal brush
  or a key) whose `ApplyTo(element, property)` sets the literal or wires the
  framework's own reference: WPF `SetResourceReference` on an attached slot whose
  change callback paints the target (and pins it for static); Avalonia
  `GetResourceObservable` with a converter for dynamic, `TryFindResource` for
  the variant in effect on attach for static. A `Color` resource is wrapped in a
  brush (theme dictionaries keep colors as often as brushes); a wrong type throws
  as in 4b; a missing key leaves the element's own value standing — black fill,
  no border — so a typo still cannot make an icon transparent. The samples'
  `ThemeIcon` now takes its brush from the theme too (`ThemeIconBrush`: a gold
  sun, a silver moon), which is what makes `dynres:` in a brush visible.
- **Step 6 — `assets:` is a convenience, not a security boundary.** Keep the
  lexical `../` clamp; drop the readme's "no matter how creatively" promise
  (WPF + Ava). Still do the mechanical hardening: the readme's catch-all
  `Process.Start` → a scheme allowlist, and OSC-8 console control-char
  sanitizing. The residual risk — a symlink or junction planted inside
  `Assets` — needs local filesystem write the app never grants, so on a
  properly installed app it is out of scope, and the scheme only ever loads
  images.
- **Format args stay markdown (by design).** Parameters substituted into a value
  are still markdown-parsed — inserting dynamic command links through args is a
  wanted feature. Not escaped; document that args are author-trusted (don't feed
  untrusted input into a rendered string).
- **Console output — sanitize the dictionary, trust only the converter.**
  words.ini holds display text that may render in any host, so the console
  renderer treats it as untrusted: it strips control characters at the leaves
  (plain text via `Run`, and link/image URIs and alt text, which never pass
  through `Run`) so a value can't forge terminal escape sequences. The
  markdown-to-console converter is the sole legitimate emitter of SGR/OSC-8 — it
  wraps already-sanitized content, so its own escapes survive. Format params still
  carry markdown (a dynamic link becomes a proper OSC-8 hyperlink via the
  converter), but raw control chars are stripped whatever their source.
- **Step 8 — culture tags closed; formatting culture unified.** Script-based
  tags (`zh-Hant-TW` etc.) are not on the roadmap, and two levels of language
  id (`en`, `en-GB`) are the ceiling, so `CultureInfo` canonicalization /
  `Parent` fallback is closed rather than deferred. Done: one formatting culture,
  `CurrentCulture`, across `WordsInline` (positional and named) and
  `Words.Format` — the .NET convention for number/date formatting — while
  `WordsConverter` formats with the culture the binding hands it, as every
  converter does (follow-up, 2026-09-11: it briefly used `CurrentCulture` too,
  until the WPF `Digest` flag below made the binding culture trustworthy;
  Avalonia's is `CurrentCulture` unless a `ConverterCulture` says otherwise, so
  the two agree by default). Language selection (`ToWords`) sets the formatting
  culture to the chosen language by default, and `UseSystemNumbers()` on the
  builder is the named alternative — English words, system numbers: the
  dictionary then carries the language as its UI culture and
  `Words.SystemCulture` (the process's starting culture, captured before Words
  touches anything) as its formatting culture, and `SetCulture` applies each to
  its own slot. Startup is one call: `WordsBuilder.Digest(lang[, out languages])`
  builds the dictionary and installs it as `Words.Known`, turning the setter's
  hidden culture side-effect (Theme E) into a deliberate, named verb; `ToWords`
  stays as the build-only path. The `showFallback` debug flag moved off the
  `ToWords`/`Flatten` parameters onto a fluent `Debug(bool)` switch — otherwise
  a WPF `Digest(lang, bool)` overload would silently bind to the core
  `showFallback` bool, since C# never consults extensions while an instance
  method fits. WPF then overloads `Digest` with `includeFrameworkElements` to
  also repoint `FrameworkElement.Language` (which drives ordinary bindings)
  without digging for the `OverrideMetadata` incantation — two of them, it turns
  out: a default is not inherited down the tree, and flow content is
  `FrameworkContentElement`, which owns its own `en-US` metadata via `AddOwner`
  and cannot be overridden again, so `TextElement` (the root of every inline and
  block) gets the second, or a bound `Run` would stay at `en-US`.
- **Step 9 — framework-free logic hoisted to Core (2026-09-11).** The WPF and
  Avalonia modules are twins by design, but a survey showed the small files
  differ only by their framework interfaces (nothing to share) while the big
  ones hid identical pure logic. That logic now lives once, in Core, and is
  tested once: `ImageQuery` (the query parse — numbers typed, brush values raw
  — plus `Parse(ref Uri)`'s by-hand split, `PathOf`, `CleanDimension` and
  `MaxDimension`), `ResourceReference` (the `staticres:`/`dynres:` spelling),
  `SafePath.Under` (the canonicalize-and-clamp behind `assets:`, now also
  tolerant of a root spelled with a trailing separator, which the WPF
  `FolderImageResolver` was not), `Words.FormatParams` (the null / array /
  named-object rule the inlines and converters each had a copy of) and
  `Words.ConvertValue` (the converters' whole body, `#value#` truncation
  included), and `AltPlaceholder` on the generic parser base — there were four
  copies, the console parser's among them. Each framework `ImageOptions` keeps
  its public shape and is built `From` an `ImageQuery`; `BrushOption.Parse`
  reads the reference and only parses the color itself. Left where they are,
  deliberately: the converter shells (only the interface differs), the
  `MarkdownParser.Image` flow (lifting it means a `TElement` type parameter and
  a generic resolver interface, for ~30 lines each), and every resource
  mechanism.

## Themes

- **A. Authoring data-loss / corruption** — the real priority.
  - Literal `\` unescaped in `IniWriter.WritePair`: a value ending in `\` is
    written raw and, on reload, the trailing `\` continuation swallows the next
    line including a block header — merges/deletes blocks. (auth #2, confirmed)
  - `Shift` collision drops source context/comment/stale and overwrites an
    occupied target `Context`; equal values still lose source metadata.
    (auth #3) — extends beyond the value-parks-in-context design we shipped.
  - Save neither validates nor is atomic: no check the tree's root owns the file
    or covers all its keys (a stale tree silently deletes keys); file overloads
    truncate before serializing, so any exception leaves a partial file.
    Merge/Split share the path. (auth #7)
  - Repeated/reopened blocks can't round-trip; unknown fields & parameter types
    dropped/coerced; long `stale=` truncated (no continuation case). (auth #6, #8)
- **B. Contract violations vs docs.**
  - `WordsProviderBase`'s `IWordsProvider` indexer always throws
    `NotImplementedException`, even for keys that exist. (auth #1)
  - Repeated `value=` overwrites, doesn't append — contradicts Core readme/SKILL.
    (core #1, auth #6)
  - `MarkdownConverter` target-type test reversed in both UI modules. (wpf #3, ava #13)
  - Byte-stability overclaimed in WordsEdit docs.
- **C. Rendering safety (both UI modules).**
  - `staticres:`/`pack:` return the shared resource instance, then reparent &
    mutate it — reuse throws (one control, two parents) or leaks state; frozen
    resources throw. (wpf #2, ava #7)
  - `WordsInline` clears inlines before parsing, so an exception leaves it blank.
    (wpf #10, ava #11)
  - Image width/height accept NaN/∞/negative/huge → throws surfaced as "missing
    asset". (wpf #8, ava #10)
- **D. Containment / injection.**
  - `assets:` clamp is lexical only — symlink/junction inside `Assets` escapes;
    `Ordinal` prefix ignores case-insensitive FS. (wpf #1, ava #8)
  - Ava substitutes format params before parsing → markdown/link injection;
    readme's catch-all `Process.Start` can shell-open `file:`. (ava #2)
  - OSC-8 console links emit unsanitized control chars. (core #12)
- **E. Global mutable state.** `Words.Known` (+culture side-effect),
  `Words.Logger`/`Dummy` mutable fields, `MarkdownParser.Default` + registry,
  the single `Hyperlink` handler. *Judgment:* library is assign-once-at-startup,
  single UI thread — most race framing is theoretical. Real regardless: the
  same-delegate `Hyperlink.Dispose()` unsubscribe bug, and `GroupCuts` caching
  stale results across mutable trees. (core #2/#8, wpf #4/#5, ava #6, auth #11)
- **F. Culture / language tags.** `CurrentUICulture` vs `CurrentCulture` vs
  binding culture inconsistency; tags not `CultureInfo`-based (`zh-Hant-TW`
  rejected, script uppercased, one-hyphen fallback). *Judgment:* only if those
  locales are on the roadmap. (core #9, auth #15, wpf #13, ava #14)

## Plan of action

Ordered by risk; each step is independently shippable (~1–3 API commits).

1. **Stop Authoring data-loss.** Escape literal `\` in `WritePair` (order:
   escape `\` → double `'`/`_` → wrap/continuation); field-by-field `Shift`
   collision rules that never overwrite occupied metadata; implement the
   `IWordsProvider` indexer via `TryGetValue`.
2. **Make save safe.** Validate tree root owns the file and covers every key
   before opening the destination; serialize to a temp sibling and atomic-
   replace. Covers Save/Merge/Split.
3. **Decide the empty/repeated/unknown-field contract** and align Core's
   repeated-`value=` behavior + docs to it: either a lossless ordered document
   layer (reopened blocks, unknown fields, order) or narrow the "never lose
   data" / byte-stability docs to what's guaranteed.
4. **Shared-resource rendering (both UI modules together).** Clone
   `Freezable`/drawing values or accept a factory/`DataTemplate`; never reparent
   or mutate a shared instance. Make `WordsInline` transactional (build then
   swap). Validate image dimensions (finite, non-negative, capped).
5. **Small contract bugs.** `MarkdownConverter` target-type logic + docs (both);
   unify converter `ConvertBack` exception type; `Hyperlink` same-delegate
   unsubscribe (token per registration, `ThrowIfNull`).
6. **Containment + injection.** Decide if `assets:` is a security boundary — if
   so, resolve reparse points component-by-component + OS-appropriate comparer,
   else weaken the readme wording. Escape markdown in Ava format args by
   default; replace the readme's catch-all `Process.Start` with an allowlist.
7. **Tighten global state to the documented model.** `Words.Logger`/`Dummy`
   non-null get-only; `GroupCuts` per-write (or snapshot) + reject
   `minimumKeys < 1`; state the single-startup/UI-thread contract explicitly.
8. **Culture/tags (only if those locales are on the roadmap).** `CultureInfo`
   validation/canonicalization + `CultureInfo.Parent` fallback; one
   formatting-culture policy across `WordsInline`/`WordsConverter`.

**Test debt:** adversarial round-trip fixtures (the destructive cases),
repeated-`staticres:` use, symlink/junction escape, Ava hyperlink hit-testing
(none today), net8/net10 × Avalonia-version matrix.

## Progress

Tick items as they land; keep this file in the addressing commit.

### Step 1 — Authoring data-loss
- [x] Escape literal `\` in `WritePair` (+ round-trip tests: terminal/repeated backslash, newline-then-header)
- [x] Field-by-field `Shift` collision rules (no silent metadata loss)
- [x] `IWordsProvider` indexer via `TryGetValue`

### Step 2 — Safe save
- [x] Validate tree root ownership + key-set coverage before write
- [x] Atomic write (temp sibling + replace) for Save/Merge/Split

### Step 3 — Repeated fields, and doc accuracy
- [x] Keep repeated `value=` overwrite+warn (last-wins); no semantic code change
- [x] Correct core readme/SKILL: a repeated field overwrites and warns (not a continuation)
- [x] Narrow WordsEdit byte-stability / never-lose-data docs; fix the long `stale=` continuation truncation

### Step 4 — Shared-resource rendering (WPF + Ava)
- [x] `staticres:` value resources in a fresh host; element resources refused with an `IMG:ELEM` gripe (no reparent/mutate). `pack:` already builds fresh
- [x] `WordsInline` transactional (build then swap)
- [x] Image dimension validation (finite, non-negative, capped)

### Step 4b — Dynamic / framework-resolved resources (follow-on, with `dynres:`)
- [x] Resolve `staticres:` through a framework resource reference (fixes app-only scope)
- [x] `dynres:` scheme: a live reference that tracks theme (dark/light) swaps
- [x] Build a `DataTemplate`/factory for element resources (WPF `LoadContent`; Ava `IDataTemplate.Build`)
- [x] Converter as the shared sanitation layer (resolved resource → safe visual), XAML-usable
- [x] `?foreground=`/`?background=` accept a brush resource as `staticres:key`/`dynres:key` (`BrushOption`; dynamic follows theme swaps) — 2026-09-11

### Step 5 — Contract bugs
- [x] `MarkdownConverter` target-type logic + docs (both modules)
- [x] Unified converter `ConvertBack` exception type
- [x] `Hyperlink` same-delegate unsubscribe (per-registration token, `ThrowIfNull`)

### Step 6 — Containment / injection
- [x] `assets:` readme wording weakened (convenience, not a boundary); lexical clamp kept
- [x] Document format args render as markdown by design (dynamic command links); no escaping
- [x] Readme + samples: catch-all `Process.Start` -> scheme allowlist (http/https/mailto)
- [x] Console: sanitize control chars at the leaves (text, URIs, alt); converter is the sole terminal-escape source

### Step 7 — Global state
- [x] `Words.Logger` non-null (null-rejecting setter); `ITakeException.Dummy` get-only
- [x] `GroupCuts` snapshots its key set + rejects `minimumKeys < 1`
- [x] Document single-startup / UI-thread contract (core readme)

### Step 8 — Culture / tags (deferred)
- [x] Tag canonicalization / `Parent` fallback — **won't do** (2026-09-11): two levels of language id (`en`, `en-GB`) are the ceiling; no script subtags
- [x] One formatting culture (`CurrentCulture`) across `WordsInline`/`WordsConverter`/`Words.Format`; `Digest` one-call startup (Theme E) with a WPF `includeFrameworkElements` overload; `showFallback` → fluent `Debug(bool)`
- [x] Follow-up (2026-09-11): `WordsConverter` formats with the binding's culture; `UseSystemNumbers()` + `Words.SystemCulture`; the WPF `Language` override reaches `TextElement` flow content; Wordsmith loads via `Digest`

### Step 9 — Shared logic hoisted to Core (2026-09-11)
- [x] `ImageQuery` (query parse, `Parse(ref Uri)`, `PathOf`, `CleanDimension`/`MaxDimension`) + `ResourceReference`; framework `ImageOptions` built `From` it
- [x] `SafePath.Under(root, relative | Uri)` behind `assets:` (WPF `FolderImageResolver`, Ava `AssetsImageResolver`)
- [x] `Words.FormatParams` (inlines) and `Words.ConvertValue` (converters' body)
- [x] `AltPlaceholder` on `MarkdownParser<TInline>` — one copy, four callers
- [x] Core tests own the pure logic (`ImageQueryTests`, `WordsFormatParamsTests`); the twins' duplicates removed
