# Words

Gives you Words.

[![tests](https://github.com/pzahra/words/actions/workflows/tests.yml/badge.svg)](https://github.com/pzahra/words/actions/workflows/tests.yml)
[![Core](https://img.shields.io/nuget/v/PatTech.Localization.Core?label=Core)](https://www.nuget.org/packages/PatTech.Localization.Core)
[![WPF](https://img.shields.io/nuget/v/PatTech.Localization.WPF?label=WPF)](https://www.nuget.org/packages/PatTech.Localization.WPF)
[![Avalonia](https://img.shields.io/nuget/v/PatTech.Localization.Avalonia?label=Avalonia)](https://www.nuget.org/packages/PatTech.Localization.Avalonia)
[![Analyzer](https://img.shields.io/nuget/v/PatTech.Localization.Analyzer?label=Analyzer)](https://www.nuget.org/packages/PatTech.Localization.Analyzer)

You write your strings in a `words.ini` file. Words reads them, picks the right
language, fills in the parameters, follows the references, renders the markdown,
and hands the result back wherever you asked for it — code, XAML, or AXAML.
There is even a compiler warning for the day you inevitably try to sneak a raw
`"hello"` past the translators.

## What's in the box

| Project                                                | What it does                                                                                                                                                                                                        |
|---|---|
| [Localization-Core](Localization-Core/readme.md)       | The engine. Parses `words.ini`, resolves languages and fallbacks, formats parameters. Enough on its own for a console app or a framework we haven't met yet. Ships as `PatTech.Localization.Core`.                  |
| [Localization-Wpf](Localization-Wpf/readme.md)         | Puts Words in the XAML. Ships as `PatTech.Localization.WPF`.                                                                                                                                                        |
| [Localization-Ava](Localization-Ava/readme.md)         | Puts Words in the AXAML. Ships as `PatTech.Localization.Avalonia`.                                                                                                                                                  |
| [LocalizationAnalyzer](LocalizationAnalyzer/readme.md) | The Words police. Provides `[Localized]` and warns (PTL001) when an unlocalized string is handed to something that wanted Words. Ships as `PatTech.Localization.Analyzer`, and comes along automatically with Core. |
| [WordsEdit](WordsEdit/readme.md)                       | Wordsmith, the WPF editor for `words.ini` files. For when the translators would rather not hand-edit an INI file.                                                                                                   |
| [WordsCli](WordsCli/readme.md)                         | `words`, the command line: changes one field of a `words.ini` and leaves every other byte alone. For scripts, build steps and coding agents, on Windows, Linux and macOS. Ships with Wordsmith.                       |
| Sample-Wpf, Sample-Ava                                 | Twin tours of Words in the XAML and AXAML respectively, from the same words: a page per topic — markdown, references, links, image schemes, format parameters, enums, live switching, diagnostics — each demo labelled with what it uses, its markup and its words shown under "How it's made", cut from the real files. A language dropdown switches the app in place, and a dot marks the topics you haven't visited yet. |
| [Sample-Shared](Sample-Shared/SPEC.md)                 | What the twins share: the words, the topic list, the view models, and the config that remembers your language, theme and visits.                                                                                   |
| Sample-Console                                         | Words in the terminal: `dotnet run --project Sample-Console` shows the markdown rendered with ANSI styling, clickable links, emoji, and a deliberate missing key griping to the logger.                             |
| LocalizedSample                                        | A console app whose whole job is to trip the analyzer. It builds with a PTL001 warning on purpose.                                                                                                                  |

## Quick start

1. Put a `words.ini` in your assets:
   
   ```ini
   value-en=!English (common)
   
   [main.title]
   value=Words
   comment=it gives you words
   ```

2. Load it once at startup:
   
   ```csharp
   WordsBuilder.Create()
       .Load("path/to/assets/words.ini")
       .Digest("en");   // installs it as Words.Known
   ```

3. Ask for Words:
   
   ```csharp
   string title = Words.Known["main.title"];
   ```

For the XAML and AXAML versions of step 3, see the
[WPF](Localization-Wpf/readme.md) and [Avalonia](Localization-Ava/readme.md)
readmes. For everything the `words.ini` format can do — languages, fallbacks,
constants, references, parameters, multiline values — see the
[Core readme](Localization-Core/readme.md).

Working with coding agents? Set `<WordsAgentSkill>true</WordsAgentSkill>` in a
project that references Words and the next build drops an agent skill into
`.claude/skills/pattech-words/`, teaching them the whole API — see the
[Core readme](Localization-Core/readme.md#teach-your-agents). It also tells
them about [`words`](WordsCli/readme.md), so they change your `words.ini`
one field at a time instead of improvising the escape rules.

## Why its own format?

`words.ini` is a *runtime* format: the app reads it directly — no build step, no
compiler, no satellite assemblies — which is the one thing the usual suspects
aren't built for. XLIFF is an *interchange* format (the I is for Interchange),
the translation industry's handoff file, almost always converted to something
else before an app runs off it; resx works at runtime but ties you to XML and
the .NET tooling, one file per culture. So Words keeps a lean, hand-editable,
diffable file it feeds off as-is — and it carries things neither of those
models: cross-file references and constants, typed format parameters, a
markdown dialect, and plural forms.

Plural forms make the case on their own. Neither resx nor XLIFF has a slot for
one, so the app that wants "1 file" and "2 files" ends up writing
`count == 1 ? "file" : "files"`: English's rule, compiled in, where no
translator can reach it. Maltese has five forms; Polish says 22 one way and 25
another. In `words.ini` the forms live in the value. Wordsmith lists each form
with the counts it takes, so nobody has to know what CLDR means by "few" to
fill it in. The translator knows the rule; now the code doesn't have to.

Meeting translators on *their* formats is a separate job, which import and
export handle; it needn't be the format the app runs on.

## Building

[Words.slnx](Words.slnx) is the solution. Open it in Visual Studio, or:

```
dotnet build Words.slnx
dotnet test Words.slnx
```

That builds the libraries, the analyzer, Wordsmith, the command line, and the
samples, then runs the editor tests (xUnit) and the analyzer tests (MSTest).

The libraries and samples consume the analyzer as the NuGet package
`PatTech.Localization.Analyzer`. If you change the analyzer, `dotnet pack`
the `LocalizationAnalyzer.Package` project and push the result to your local
feed so the rest of the solution picks it up.

## Versioning

Three things ship on their own schedules, so three numbers live in
[Versions.props](Versions.props): `ApiVersion` for the Core, WPF and Avalonia
packages (one API surface, released together), `AnalyzerVersion` for the
analyzer (also the version the API packages depend on), and `WordsmithVersion`
for the editor. Bump the one you changed, pack, and tag the release afterwards;
Source Link stamps the commit into every assembly's informational version.
