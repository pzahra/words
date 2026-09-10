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
- [ ] Validate tree root ownership + key-set coverage before write
- [ ] Atomic write (temp sibling + replace) for Save/Merge/Split

### Step 3 — Empty/repeated/unknown-field contract
- [ ] Decide append vs last-wins for repeated `value=`; align Core impl + docs
- [ ] Lossless ordered layer OR narrowed byte-stability/never-lose-data docs
- [ ] Reopened blocks, unknown fields, unknown param types, long `stale=` continuation

### Step 4 — Shared-resource rendering (WPF + Ava)
- [ ] `staticres:`/`pack:` no longer reparent/mutate shared instances
- [ ] `WordsInline` transactional (build then swap)
- [ ] Image dimension validation (finite, non-negative, capped)

### Step 5 — Contract bugs
- [ ] `MarkdownConverter` target-type logic + docs (both modules)
- [ ] Unified converter `ConvertBack` exception type
- [ ] `Hyperlink` same-delegate unsubscribe (per-registration token, `ThrowIfNull`)

### Step 6 — Containment / injection
- [ ] `assets:` decision: harden reparse-point handling OR weaken readme wording
- [ ] Ava: escape markdown in format args by default
- [ ] Readme: replace catch-all `Process.Start` with allowlist
- [ ] Console OSC-8 control-char sanitization

### Step 7 — Global state
- [ ] `Words.Logger`/`Dummy` non-null get-only
- [ ] `GroupCuts` per-write/snapshot + reject `minimumKeys < 1`
- [ ] Document single-startup / UI-thread contract

### Step 8 — Culture / tags (conditional on roadmap)
- [ ] `CultureInfo`-based validation/canonicalization + `Parent` fallback
- [ ] One formatting-culture policy across `WordsInline`/`WordsConverter`
