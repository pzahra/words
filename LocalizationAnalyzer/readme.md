# Words Analyzer

The Words police. It makes sure your Words are actually Words.

Mark a parameter, property, field, or return value with `[Localized]` and the
analyzer checks that everything assigned to it is localized too — a Words
lookup, another `[Localized]` member, or a method that promises
`[return: Localized]`. Hand it a raw string literal and you get:

```
warning PTL001: Parameter `message` in method `WriteLocal` expects a localized value
```

``` csharp
using PatTech.Localization;

WriteLocal(Words.Known["main.greeting"]); // fine
WriteLocal("hello");                      // PTL001, straight to jail

static void WriteLocal([Localized] string message) { ... }
```

It looks through parentheses, `await`, conditionals, and switch expressions,
and flags only the arms that misbehave.

## What's in this folder

| Project | What it does |
|---|---|
| `LocalizationAnalyzer` | The Roslyn analyzer itself (rule PTL001). |
| `LocalizationAnalyzer.Package` | Packs the analyzer and the `LocalizedAttribute` into the `PatTech.Localization.Analyzer` NuGet package. |
| `LocalizationAnalyzer.Test` | MSTest suite that feeds the analyzer little programs and checks it complains at the right ones. |
| `LocalizationAnalyzer.Vsix` | Debug harness. F5 launches an experimental Visual Studio (`/rootsuffix Roslyn`) with the analyzer installed, breakpoints and all. It only deploys when built inside Visual Studio, so `dotnet build` stays happy. |

## Shipping it

```
dotnet pack LocalizationAnalyzer.Package -c Release
```

Push the resulting `PatTech.Localization.Analyzer` package to your feed.
`PatTech.Localization.Core` depends on it, so most consumers get the analyzer
(and `[Localized]`) for free just by using Words; anything else that wants
supervised strings can reference it directly as a normal `PackageReference`.

## Debugging it

Set `LocalizationAnalyzer.Vsix` as the startup project in Visual Studio and
hit F5. A second Visual Studio opens with the analyzer loaded; open any
project that uses `[Localized]` and your breakpoints in the analyzer will
hit as it types.
