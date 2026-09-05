# WordsXaml — Rider extension for `{l:Words …}` keys

A local Rider plugin that gives Avalonia XAML **autocomplete + tooltip preview** (and an optional
unknown-key warning) for the Words markup extension (xmlns `https://github.com/pzahra/words`, formerly `pattech.words`) (`{l:Words some.dotted.key}`), resolved
against the solution's `*-words.ini` files. Built against the **Rider 2025.3** SDK and
installable on anything newer (`since-build=253`, no upper bound).

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
gradle.properties           pluginVersion, dotNetPluginId, riderSdkVersion (paired with the SDK nupkg)
gradlew / gradlew.bat       Gradle 9.1 wrapper
src/
  main/resources/META-INF/plugin.xml    JVM-side descriptor (depends com.intellij.modules.rider)
  dotnet/                               THE BACKEND (was src/ before the Gradle conversion)
    WordsXaml.Core/          SDK-FREE, tested core.
      Ini/WordsEntry.cs         one resolved key (value variants + source line)
      Ini/WordsIniParser.cs     the *-words.ini grammar ([.suffix] inheritance, \ continuation, locales)
      Ini/WordsIndex.cs         key -> entry map, fuzzy Match(), RenderPreview() (one-line, truncated)
    WordsXaml/               THE PLUGIN. Thin SDK glue over the core.
      ZoneMarker.cs             declares required product zones (XAML PSI + feature services)
      Index/WordsIndexService.cs   solution component; rebuilds the index when an .ini changes
      Xaml/WordsMarkupContext.cs   "am I inside {l:Words …}?" + key-token extraction
      Xaml/WordsCompletionProvider.cs  completion items from WordsIndex.Match()
      Xaml/WordsQuickDocProvider.cs    hover/Ctrl-Q tooltip from WordsIndex.RenderPreview()
      Inspections/UnknownWordsKeyHighlighting.cs   squiggle on keys with no [section]
    WordsXaml.Tests/         xUnit tests for the core
```

## Section inheritance

A section starting with `.` extends the last **fully-qualified** header (evo-words.ini uses this):

```ini
[material]
[.metals]      -> material.metals
[.composites]  -> material.composites   (sibling; still hangs off [material])
[gate.mode]
[.peak]        -> gate.mode.peak
```

Dot-sections never become the new base, so consecutive `[.x] [.y]` both resolve against the same parent.

## Preview

Kept deliberately simple: the invariant `value=` is collapsed to one line and truncated
(`RenderPreview(key, maxLength)`). Cross-refs (`{>key}`) and `[icon:x]` tokens are shown **verbatim** —
not resolved.

## Status

- **Core (tested):** everything under `WordsXaml.Core` — parser (incl. `[.suffix]` inheritance), index,
  fuzzy match, truncated preview. `dotnet test` → 10 passing.
- **Plugin (compiles against the real 2025.3 SDK):** `WordsMarkupContext`, `WordsCompletionProvider`,
  `ZoneMarker`, `WordsIndexService`, `WordsQuickDocProvider`, `UnknownWordsKeyHighlighting`.
  `dotnet build src/WordsXaml` → 0 errors (warnings are SDK NU1701 noise).

Every SDK type/member/attribute used was confirmed by reflecting over the installed
`jetbrains.psi.features.core` 2025.3 assemblies — not guessed. The completion provider's registration
(`[Language(typeof(XamlLanguage), Instantiation.DemandAnyThreadSafe)]`) is byte-for-byte what JetBrains'
own XAML items providers use.

### How it maps to the 2025.3 PSI / completion API

- A `{l:Words …}` usage is an `IMarkup` node (`…Psi.Xaml.Tree.MarkupExtensions`): `NameNode.Id` is the
  extension short name (`"Words"`, alias-independent) and `Value : IMarkupValue` is the positional
  argument — an `IPathValue` whose `GetText()` is the dotted key. `WordsMarkupContext` walks up with
  `GetContainingNode<IMarkup>()`.
- The provider derives from `ItemsProviderOfSpecificContext<XamlCodeCompletionContext>` and overrides
  `IsAvailable` / `AddLookupItems`. The caret node comes from `context.UnterminatedContext.TreeNode`
  (the reparsed tree, so in-progress `{l:Words fo|` still parses), falling back to `context.TreeNode`.
  Items are `TextLookupItem(insertText, typeText, isDynamic:false)` with `InitializeRanges(context.Ranges, …)`.

### Hierarchical completion

Instead of listing every fully-qualified key (thousands), completion shows **one tree level at a time**
(`WordsIndex.CompleteSegments`). At the root you see ~a dozen branches (`calibration.`, `params.`, …),
each tagged with its child count; accepting a branch (insert text ends with `.`) reveals the next level;
terminal levels show leaf keys with their value preview. `WordsIndex.CommittedPrefix` derives the current
level from the typed text (up to the last `.`), and ReSharper's matcher filters that level by the partial
segment.

Trade-off: this favours drill-down over global fuzzy search — typing `capture` at the root won't surface
`params.focal-law-base.capture-delay` until you've drilled to that level. A hybrid (branches + deep
fuzzy matches) is possible if that's wanted.

### The one thing not verifiable from a build

Compilation + JetBrains-identical wiring is strong, but it doesn't *prove* the list pops up in a live
editor. Use `runIde` (below) to see it, and/or add a headless completion test with
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
`{l:Words ` in a `.axaml` — completion should list the keys from the loaded `*-words.ini`.

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

## Packaging for install (optional)

`./gradlew buildPlugin` produces a plugin zip under `build/distributions/`. Install it into a real Rider
via **Settings ▸ Plugins ▸ ⚙ ▸ Install Plugin from Disk…**.

## Cheaper alternative

If maintaining a plugin is more than you want: generate a strongly-typed `Words.Keys` constants file from
the `.ini` files (reuse `WordsXaml.Core` directly) and get plain Roslyn completion on the C# side. Less
nice in XAML, nothing to keep in sync with the Rider SDK.
