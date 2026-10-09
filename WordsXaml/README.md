# WordsXaml — Rider extension for `{l:Words …}` keys

A Rider plugin that gives XAML **autocomplete + tooltip preview** and an
unknown-key warning for the Words markup extension (xmlns `https://github.com/pzahra/words`, formerly
`pattech.words`) — `{l:Words some.dotted.key}`, `{l:Words Key=some.dotted.key}` or
`{l:Words 'some.dotted.key'}` — and the same autocomplete + tooltip in C# string literals passed to a
`[WordsKey]` parameter or property (`Words.Known["some.dotted.key"]`), resolved against the solution's
`*words.ini` files. Built against the **Rider 2025.3** SDK and installable on anything newer
(`since-build=253`, no upper bound). What it does, exactly, is in the analyzer's spec,
[../LocalizationAnalyzer/SPEC.md](../LocalizationAnalyzer/SPEC.md) (*The Rider plugin* onwards).

## Why not Roslyn?

Roslyn only sees C#; it never runs inside `.axaml`. XAML editing in Rider is powered by the
ReSharper/Rider SDK (the R# engine), so all the in-XAML UX lives there. This project targets that SDK.

## Layout

A minimal, backend-only Rider plugin: a Gradle project (IntelliJ Platform Gradle Plugin) that wraps the
.NET/ReSharper backend and gives a `runIde` sandbox. No rdgen protocol or Kotlin frontend — all logic is
in the backend.

```
build.gradle.kts            IntelliJ Platform plugin; builds the backend + copies it into dotnet/
settings.gradle.kts
gradle.properties           dotNetPluginId, riderSdkVersion (paired with the SDK nupkg); the version is
                            AnalyzerVersion, read from ../Versions.props
gradlew / gradlew.bat       Gradle 9.1 wrapper
src/
  main/resources/META-INF/plugin.xml    JVM-side descriptor (depends com.intellij.modules.rider)
  dotnet/                               THE BACKEND (was src/ before the Gradle conversion)
    WordsXaml.Core/          SDK-FREE, tested core.
      Ini/WordsEntry.cs         one resolved key (values per language, plural forms, source line)
      Ini/WordsIniParser.cs     the *words.ini grammar, as the runtime's WordsParser reads it
      Ini/WordsIndex.cs         key -> entry map, fuzzy Match(), RenderPreview() (one-line, truncated)
      Ini/WordsQuickDoc.cs      the tooltip body: key, preview, forms, file
      Sites/MarkupKeys.cs       the key in {l:Words …} text: positional, Key=, quoted; at a caret
      Sites/LiteralKeys.cs      the key inside a C# string literal's quotes
    WordsXaml/               THE PLUGIN. Thin SDK glue over the core.
      ZoneMarker.cs             declares required product zones (XAML PSI + feature services)
      Index/WordsIndexService.cs   solution component; rebuilds the index when an .ini changes
      Keys/WordsLookupItems.cs     the completion list for a key site (XAML or C#), with its own ranges
      Keys/WordsQuickDocPresenter.cs   the quick-doc page for a key
      Xaml/WordsMarkupContext.cs   "am I on a key in {l:Words …}?" for a node or a caret
      Xaml/WordsCompletionProvider.cs  completion in {l:Words |}
      Xaml/WordsQuickDocProvider.cs    hover/Ctrl-Q tooltip in XAML
      Inspections/UnknownWordsKeyAnalyzer.cs       finds keys with no [section] (an IMarkup analyzer)
      Inspections/UnknownWordsKeyHighlighting.cs   the squiggle, its severity registered
      CSharp/WordsKeySites.cs      "is this string literal bound to a [WordsKey] target?"
      CSharp/WordsKeyCompletionProvider.cs   completion in such a literal
      CSharp/WordsKeyQuickDocProvider.cs     hover/Ctrl-Q tooltip on such a literal
    WordsXaml.Tests/         xUnit tests for the core
```

## The grammar

The runtime's reader is the contract: `WordsParser.Load` walking the lines and
`WordsParserToWordsProvider` keeping the values (`Localization-Core`, and `SPEC.md`'s *Key names*,
*Plural forms* and *Language codes*). The plugin can't reference the runtime, so `WordsIniParser` copies
its patterns as written there — keep them in step, as `LocalizationAnalyzer`'s `WordsIniKeys` does.

A section starting with `.` extends the last **fully-qualified** header (evo-words.ini uses this):

```ini
[material]
[.metals]      -> material.metals
[.composites]  -> material.composites   (sibling; still hangs off [material])
[gate.mode]
[.peak]        -> gate.mode.peak
```

Dot-sections never become the new base, so consecutive `[.x] [.y]` both resolve against the same parent.

- **Headers** are the runtime's `^\[([^]]*)\]`: at the line start, the name as written, no trimming. The
  resolved name must be a key's name (`WordsParser.IsKeyName`: dotted segments of letters, digits, `_`
  and `-`, or a constant, `$unit`); one that isn't — `[lang.c#]`, `[ spaced ]` — is no key, and its
  fields go with it. So are the `[.child]` headers under it or under a constant (`$unit.child` is no
  name), and a `[.x]` before any full header (`.x` is no name either). A block named twice in a file is
  one key.
- **Fields** are `field-lang#form=text` (or `:`), at the line start; the space after `=` is dropped.
  Only `value` fields are kept. A repeated `value=` for the same key and language **overwrites**, as the
  runtime's does (it warns `WB:KOVR`); 0.1.0 concatenated them.
- **Continuations**: a trailing `\` joins with a newline, a trailing `_` with nothing — unless it is
  doubled: `\\`, `__` and `''` are escapes, one character each, and never continue the line. The
  runtime's test pairs any `\` or `_` with the character after it, so `a_\` doesn't continue either. A
  continued line is text, never a header or field.
- **Comments** are `;` lines, indented or not. A `#` line isn't a comment to the runtime, but it isn't a
  field or header either, so it is skipped just the same.
- **Plural forms** — `value#other=`, `value-ru#few=` — are forms of the key, kept in `WordsEntry.Forms`,
  never a language. Only CLDR's categories count, and not `#one` (the plain value is that form).
- **Language codes** are the runtime's `language(-Script)?(-REGION)?`: `en`, `ceb`, `es-419`,
  `zh-Hans-CN`, cased by kind (`sr-latn-rs` reads `sr-Latn-RS`). A field whose suffix isn't one
  (`value-english=`) is read past, continuation and all.

A key declared in more than one file is one index entry, its values merged per language with the later
file winning; it keeps the first declaration's location.

## Preview

Kept deliberately simple: the invariant `value=` — unescaped and joined as the runtime reads it — is
collapsed to one line and truncated (`RenderPreview(key, maxLength)`). Cross-refs (`{>key}`) and
`[icon:x]` tokens are shown **verbatim** — not resolved. The tooltip adds the key's plural forms, if it
has any (`forms: #few #other`), and the file that declares it.

## Status

- **Core (tested):** everything under `WordsXaml.Core` — parser (the runtime's grammar above), index,
  fuzzy match, truncated preview, tooltip body, and the key's place in `{l:Words …}` text and in a C#
  literal. `dotnet test src/dotnet/WordsXaml.Tests` → 88 passing.
- **Plugin (compiles against the real 2025.3 SDK):** XAML completion, quick-doc and the unknown-key
  inspection; C# `[WordsKey]` completion and quick-doc. `dotnet build src/dotnet/WordsXaml` → 0 errors
  (warnings are SDK NU1701/MSB3277 noise).

### 1.4.0

The plugin now ships on the analyzer's track, at its number: an `analyzer/X.Y.Z` tag attaches
`WordsXaml-X.Y.Z.zip` to that GitHub Release. This is the release first built as 0.2.0.

- The parser reads what the runtime reads (*The grammar*): the key-name check on headers and their
  children, the runtime's continuation and escape rules, a repeated `value=` overwriting, plural forms
  kept out of the languages, three-part language codes.
- `{l:Words Key=…}` and `{l:Words '…'}` get completion, quick-doc and the inspection, as the positional
  spelling does (the runtime made `Key=` first-class when it dropped `[ConstructorArgument]`).
  `{l:WordsExtension …}` counts too.
- Quick-doc is a real `IQuickDocProvider` now (0.1.0 had a stub), and the unknown-key highlighting has
  an analyzer that raises it and a registered severity (0.1.0 had neither).
- C# sites: inside a string literal bound to a `[WordsKey]` parameter, property or field — the marker
  Core puts on `IWords`' indexers, `Format`/`FormatByName`/`RenderKey`…, `WordsAttribute` and
  `LazyWords` — Ctrl+Space offers the keys and hover/Ctrl+Q shows the tooltip. A member that overrides
  or implements a marked one counts, as for analyzer rule PTL002. No squiggle in C#: PTL002 already
  reports unknown keys there, and a second one would only double it.

Every SDK type/member/attribute used was confirmed by reflecting over the installed 2025.3 SDK
assemblies (`jetbrains.psi.features.core`, `jetbrains.psi.features.src`, the platform packages) — not
guessed — and the wiring of each new piece follows a JetBrains provider of the same kind, read from its
IL. The completion provider's registration
(`[Language(typeof(XamlLanguage), Instantiation.DemandAnyThreadSafe)]`) is byte-for-byte what JetBrains'
own XAML items providers use, and its C# twin what their in-string C# provider uses.

### How it maps to the 2025.3 PSI / completion API

- A `{l:Words …}` usage is an `IMarkup` node (`…Psi.Xaml.Tree.MarkupExtensions`). Its `Value` is the
  positional argument, or an `IAttributeListMarkupValue` of `IMarkupAttribute`s for `Key=…`, and a quoted
  argument is an `IQuotedValue` around another — three shapes for one key. So the key is read from the
  node's **text** by the core's `MarkupKeys` (tested), and the node only says where the extension starts
  (`GetDocumentStartOffset()`); the name is matched in the text too, alias and all.
- The providers derive from `ItemsProviderOfSpecificContext<XamlCodeCompletionContext>` (and
  `<CSharpCodeCompletionContext>`) and override `IsAvailable` / `AddLookupItems`. XAML reads the
  document around `BasicContext.CaretDocumentOffset` (an in-progress `{l:Words fo|` needn't parse, and
  the reparsed tree's offsets aren't the document's); C# finds the literal in `BasicContext.File` at the
  caret. Items are `TextLookupItem(insertText, typeText, isDynamic:false)` with
  `InitializeRanges(new TextLookupRanges(keyStart..caret, keyRange, false), …)` — the key's own range, so
  quotes stay put and the typed prefix is the whole typed key.
- Quick-doc: `[QuickDocProvider(-10)]` `IQuickDocProvider`s, wired as JetBrains' JSON schema tooltip is
  (`SOURCE_FILE` → primary PSI file, `PSI_EDITOR_VIEW` → `DefaultSourceFile.DocumentRangeFromMainDocument`),
  since a key is no declared element; the page is `XmlDocHtmlUtil.FullHtml` with the injected
  `IXmlDocHtmlRenderer`, as theirs is. -10 puts it ahead of the declared-element providers, as the resx
  one is; it claims only a caret on a key.
- Inspection: `[ElementProblemAnalyzer(typeof(IMarkup))]`, as JetBrains' `TextAfterMarkupAnalyzer`, the
  highlighting carrying `[RegisterConfigurableSeverity]` under Code Notification. Silent while the
  solution has no keys at all.
- C# sites: `CSharpArgumentNavigator.GetByValue(literal).MatchingParameter` (method, constructor,
  indexer and attribute arguments), `PropertyAssignmentNavigator` (attribute `Key = "…"`),
  `MemberInitializerNavigator` and `AssignmentExpressionNavigator` (property/field assignments); the
  marker is read with `GetAttributeInstances(AttributesSource.Self)`, then through
  `GetAllSuperMembers(false)`. The `CSharp` namespace has its own `ZoneMarker` requiring
  `ILanguageCSharpZone`.

### Hierarchical completion

Instead of listing every fully-qualified key (thousands), completion shows **one tree level at a time**
(`WordsIndex.CompleteSegments`). At the root you see ~a dozen branches (`calibration.`, `params.`, …),
each tagged with its child count; accepting a branch (insert text ends with `.`) reveals the next level;
terminal levels show leaf keys with their value preview. `WordsIndex.CommittedPrefix` derives the current
level from the typed text (up to the last `.`), and ReSharper's matcher filters that level by the typed
key (the items carry the committed prefix, so it matches as a plain prefix).

Trade-off: this favours drill-down over global fuzzy search — typing `capture` at the root won't surface
`params.focal-law-base.capture-delay` until you've drilled to that level. A hybrid (branches + deep
fuzzy matches) is possible if that's wanted.

### What a build can't check

A build proves the wiring compiles, not that the list pops up in a live editor, the tooltip shows on
hover, the squiggle appears, or Rider asks for completion inside a C# string literal (Ctrl+Space
there; it doesn't auto-pop in strings). 1.4.0 was checked by hand in a live Rider (2026-10-09). Check a
change the same way with `runIde` (below), or add a headless completion test with
`JetBrains.ReSharper.TestFramework`.

## Running & debugging in Rider (`runIde` sandbox)

The Gradle build launches a throwaway sandbox Rider with the plugin loaded — your day-to-day Rider is
untouched. Requirements: JDK 21 (Rider's bundled JBR works), .NET SDK, and internet on first run
(`runIde` downloads the sandbox Rider — ~1.5 GB, cached afterwards).

**One-time / from a terminal:**

```
./gradlew runIde        # builds the backend, assembles the plugin, launches sandbox Rider
```

`gradlew` uses Rider's JBR if you point `JAVA_HOME` at it, e.g.
`JetBrains\JetBrains Rider 2025.3.2\jbr`. Open any Avalonia solution in the sandbox and type
`{l:Words ` in a `.axaml` — completion should list the keys from the loaded `*words.ini`. In C#, put the
caret in `Words.Known["|"]` and press Ctrl+Space.

**From the IDE (recommended loop):** open this folder as a Gradle project in Rider (or IntelliJ), then
run/debug the **`runIde`** Gradle task from the Gradle tool window.

### Debugging the backend (where our code runs)

`runIde` starts a sandbox with a JVM frontend **and** a .NET backend (`Rider.Backend64.exe`); our code
runs in the backend, so a plain "debug runIde" (JVM) won't hit our breakpoints. Attach a .NET debugger:

1. Run `runIde` (normal run is fine).
2. **Rider (recommended):** in the outer IDE, **Run ▸ Attach to Process** → pick the *sandbox*
   `Rider.Backend64.exe` (identify it by the sandbox path in its command line) → set breakpoints in the
   `WordsXaml` sources. The `.pdb` is shipped into the plugin, so they bind.
   **Or Visual Studio 2026:** Debug ▸ Attach to Process ▸ same `Rider.Backend64.exe`.

`buildConfiguration=Debug` (in `gradle.properties`) ensures the backend `.pdb` is produced and copied.

### Version pairing (important)

`runIde` downloads its **own** sandbox Rider of `riderSdkVersion` (`gradle.properties`) — not your
installed Rider. It must match the `JetBrains.ReSharper.SDK` version the backend is built against
(`src/dotnet/WordsXaml/WordsXaml.csproj`): **SDK 2025.3.4.1 ↔ Rider 2025.3.4**. Change both together, or
the host will refuse to load the backend. If `runIde` can't find that exact Rider build, set
`riderSdkVersion` to an available `2025.3.x` and re-pin the nupkg to match.

The **installed** plugin is deliberately not pinned upward: the zip declares `since-build=253` with no
`until-build`, so teammates on newer Rider versions can install it without waiting for a rebuild. The
backend still binds to one SDK's API surface — if a Rider update ever breaks it (load errors or
MissingMethodException in the backend log), bump `JetBrains.ReSharper.SDK` + `riderSdkVersion`
together and rebuild rather than re-pinning `until-build`.

## Installing

Each `analyzer/X.Y.Z` release on GitHub carries `WordsXaml-X.Y.Z.zip`. In Rider 2025.3 or later, install
it with **Settings ▸ Plugins ▸ ⚙ ▸ Install Plugin from Disk…**.

To build one yourself, `./gradlew buildPlugin -PbuildConfiguration=Release` writes the zip to
`build/distributions/`; the release workflow builds it the same way (`.github/workflows/build.yml`).

## Cheaper alternative

If maintaining a plugin is more than you want: generate a strongly-typed `Words.Keys` constants file from
the `.ini` files (reuse `WordsXaml.Core` directly) and get plain Roslyn completion on the C# side. Less
nice in XAML, nothing to keep in sync with the Rider SDK.
