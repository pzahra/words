# Words Analyzer

The Words police. It makes sure your Words are actually Words.

This is the analyzer for [Words](https://www.nuget.org/packages/PatTech.Localization.Core),
the .NET localization library. Most people don't install it explicitly — Words
brings it along — but it stands alone for any project that wants its strings
supervised.

## What it does

Mark a parameter, property or field with `[Localized]` and the analyzer checks
that what a method call passes it, or an assignment stores in it, is localized
too: a Words lookup, another `[Localized]` member, or a call to a method marked
`[return: Localized]`, its promise that what it returns is localized (a promise
taken at its word: the returns aren't checked, and nor yet are a constructor's,
an indexer's or an attribute's arguments). Hand it a raw string literal and the
build tells you:

```
warning PTL001: Parameter `message` in method `WriteLocal` expects a localized value
```

``` csharp
using PatTech.Localization;

WriteLocal(Words.Known["main.greeting"]); // fine
WriteLocal("hello");                      // PTL001, straight to jail

static void WriteLocal([Localized] string message) { ... }
```

It sees through parentheses, `await`, conditionals, and switch expressions, and
flags only the arms that misbehave — so the one bad branch lights up, not the
whole statement.

The other side of the lookup is the key. Mark a parameter, property or field
with `[WordsKey]` and every constant string handed to it must be a key some
`*words.ini` declares — Words 1.5.0 marks its own, so `Words.Known["main.greetng"]`
and `Words.FormatKnown("main.greetng", name)` are caught too. Add your ini as
an AdditionalFile and a typo gets you:

```
warning PTL002: 'main.greetng' is not a known words key
```

``` xml
<AdditionalFiles Include="Assets\words.ini" />
```

Without a `*words.ini` among the AdditionalFiles, PTL002 stays quiet.

## Getting it

You usually don't have to. `PatTech.Localization.Core` depends on this package,
so anything already using Words gets `[Localized]`, `[WordsKey]` and both rules
for free. To put the attributes on strings in a project that doesn't otherwise
use Words, reference it directly:

``` xml
<PackageReference Include="PatTech.Localization.Analyzer" Version="1.4.0" />
```

It adds the PTL001 and PTL002 analyzers and a small `PatTech.Localization.dll`
that defines `[Localized]` and `[WordsKey]`, and nothing else — no other package
dependencies come along.

## The rest of the suite

- **[PatTech.Localization.Core](https://www.nuget.org/packages/PatTech.Localization.Core)** — the engine: `words.ini` files, lookups by key, languages and fallbacks, `{0}`/`{Name}` parameters, `{>key}` references, a markdown dialect.
- **[PatTech.Localization.WPF](https://www.nuget.org/packages/PatTech.Localization.WPF)** — Words in the XAML: the `{l:Words key}` markup extension, markdown inlines, converters and image schemes.
- **[PatTech.Localization.Avalonia](https://www.nuget.org/packages/PatTech.Localization.Avalonia)** — the same, for Avalonia's AXAML.
- **Wordsmith** — the desktop editor for `words.ini` files, published on [GitHub Releases](https://github.com/pzahra/words/releases).
