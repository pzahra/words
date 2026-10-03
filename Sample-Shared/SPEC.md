# Words samples — what they show, and how they are put together

Sample-Wpf and Sample-Ava are twins: the same tour of the library, from the same
words, one in WPF and one in Avalonia. Sample-Shared holds what is framework-free
— the words, the topic catalogue, the config, the view models of the shell and
every page — so the twins cannot drift apart where they need not.
Sample-Console (the console renderer) and LocalizedSample (the analyzer's test
subject) are out of scope here. The library's own specs are
[the runtime's](../Localization-Core/SPEC.md) and [the editor's](../WordsEdit/SPEC.md).
Everything up to *Planned upgrades* is what the samples do today.

## The shell

One window. Across the top: the title, the language picker and the theme toggle.
Picking a language switches the whole window in place — the samples run in live
mode — and picking a theme swaps it; both are saved. Below: the topics as a list
on the left, and the selected topic's page on the right, scrolling on its own.
The window opens on the topic it was last left on, or the first.

**Why.** One long scroll mixed the features together, the only guidance was in
markup comments nobody running the app sees, and the theme switch sat by the
language picker though only the images cared. A page per topic keeps each
feature with its own explanation, and the controls that affect every page sit
above them all.

## Topics and their markers

The catalogue (`SampleTopics`, in Sample-Shared) lists the topics in order, each
with an id and a revision number. A topic's button carries a dot until the topic
has been visited; visiting saves the topic's revision to the config, and the dot
goes. When a page's content changes, its revision goes up by one in the same
commit, and the dot comes back for everyone who saw the older page. The dot's
tooltip says what it means. A topic's caption is the key `topic.<id>`, bound as
`{l:Words {Binding CaptionKey}}`, so the list itself follows a language switch.

## Cards

Each demonstration on a page is a card (`DemoCard`, one per sample): a heading,
a sentence or two of guidance on when to use the feature and what to watch for,
the live result, and a row of chips naming what it uses — the extension, the
inline, the converter, the scheme. The card takes one key, `<topic>.<card>`: its
value is the heading, `.guide` beneath it is the guidance, and `.demo` is the
demonstration text where the card shows one. Heading and guidance render as
Words with markdown, so the explanation is itself a demonstration and follows a
language switch. The dialect has no code spans yet (the runtime spec plans
them), so guidance names an API in bold and leaves the exact spelling to the
chips. References resolve in guidance as anywhere, so a brace meant literally is
written twice: `{{>key}` shows as `{>key}`.

## The words

One `sample.ini`, embedded in Sample-Shared, carries every word both samples
show. Each sample loads it, then its own `framework.ini` of two constants:
`$framework`, the name in the title, and `$embedded`, the base URI of an
embedded asset — `avares://Sample-Ava/Assets/` or
`pack://application:,,,/Sample-Wpf;component/Assets/`. Values reference them —
`Words for {$framework}`, `![Speech bubbles]({$embedded}bubbles.png)` — so the image schemes
are the only words that differ, and they differ in one place. A new topic's
words go in once.

## The config

`%LocalAppData%\Words\<sample>\config.ini` (`SampleConfig`, in Sample-Shared),
`key=value` lines as Wordsmith keeps its own: `language`, `theme`, `topic` (the
one last open), and `seen.<id>` (the revision last visited). Read at startup,
written as each value changes; a missing file is a first run, a key the config
does not know is kept, and a file that cannot be written is let be — the sample
runs on without remembering. `--lang=xx` and `--theme=dark|light` on the
command line still choose for one run, without saving. With neither saved nor
asked, the samples start in Italian — the point is to see translated words —
and in the light theme (Avalonia: the system's).

## Pages

Each topic is a page view model and a page view. The view models are
Sample-Shared's and need no framework: `ShellViewModel` makes the pages, and
each sample's window view model derives from it to supply the one part that is
framework code, switching the theme; the live page learns the theme as a plain
flag the shell pushes down. The views are each sample's own, card for card, so
the twins read side by side: WPF finds a page's view through an implicit data
template, Avalonia through its view locator.

| Topic | Cards |
|---|---|
| Getting started | words in markup (`{l:Words}`); a missing key, shown as `#key#` |
| Markdown | inline styles; entities and emoji; the kitchen sink; the playground (`WordsMarkdown`) |
| References | one key inside another (`{>key}`); constants (`{$constant}`, from `framework.ini`); dot-relative blocks and references; a loop, cut at `# ∞ #` |
| Hyperlinks | tooltips, autolinks and app commands (`RegisterGlobalNavigateHandler`); the app's report of them (`Params`) |
| Images | every scheme, `staticres:` beside `dynres:` under the theme toggle |
| Format parameters | positional, as a child binding; named, read off an object; numbers and dates in the language's culture, with `UseSystemNumbers` |
| Enums | a `[Words]` enum in a picker (`WordsEnumDescription`); its tooltip, subtitle and description; a `[Flags]` value joined and listed |
| Live switching | a bound key the view model picks; a converted binding; a kept string beside one recomposed through `IKnowWords` |
| Diagnostics | the fallback brands (`Debug`); the log of what `Words.Logger` heard |

Every converter on the Enums page is wrapped in `{l:Words}`, so it runs again on
a switch. Two cards change a setting of the live builder — `UseSystemNumbers` on
Format parameters, `Debug` on Diagnostics — and the shell digests the language
showing again, so the whole window follows as it does a switch; neither is
saved. At startup each sample assigns `Words.Logger` a `GripeLog`, which keeps
the last hundred gripes for the Diagnostics page. Getting started, References
and Images each cause one on purpose: the missing key, the loop and a picture
that does not exist.

---

# Planned upgrades

Not built. Each section is the shape the work takes when it is.

## How each card is made

A card's "How" expander shows the exact markup and the exact `sample.ini`
entries behind it, read from the real files so they cannot drift from what runs:
the page's markup, embedded as a resource, cut between marker comments named for
the card, and the ini blocks looked up by the card's keys. Then the readmes and
the repository README describe the tour as it is.
