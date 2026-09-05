# Words

Gives you Words.

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
| Sample-Wpf, Sample-Ava                                 | Small apps that show Words being put in the XAML and AXAML respectively. Sample-Ava is the full tour: formatting, links, image schemes, format parameters, a live markdown playground, and a language dropdown that relaunches the app in the selected language. |
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
   Words.Known = WordsBuilder.Create()
       .Load("path/to/assets/words.ini")
       .ToWords("en");
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
[Core readme](Localization-Core/readme.md#teach-your-agents).

## Building

[Words.slnx](Words.slnx) is the solution. Open it in Visual Studio, or:

```
dotnet build Words.slnx
dotnet test Words.slnx
```

That builds the libraries, the analyzer, Wordsmith, and the samples, then runs
the editor tests (xUnit) and the analyzer tests (MSTest).

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
