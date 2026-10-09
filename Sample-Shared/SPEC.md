# Words samples — what they show, and how they are put together

Sample-Wpf and Sample-Ava are twins: the same tour of the library, from the same
words, one in WPF and one in Avalonia. Sample-Shared holds what is framework-free
— the words, the topic catalogue, the config, the view models of the shell and
every page — so the twins cannot drift apart where they need not.
Sample-Console (the console renderer) and LocalizedSample (the analyzer's test
subject) are out of scope here. The library's own specs are
[the runtime's](../Localization-Core/SPEC.md) and [the editor's](../WordsEdit/SPEC.md).

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
the live result, a row of chips naming what it uses — the extension, the
inline, the converter, the scheme — and how it is made (*How each card is made*,
below). The card takes one key, `<topic>.<card>`: its
value is the heading, `.guide` beneath it is the guidance, and `.demo` is the
demonstration text where the card shows one. Heading and guidance render as
Words with markdown, so the explanation is itself a demonstration and follows a
language switch. Guidance written before the dialect had code spans names an
API in bold and leaves the exact spelling to the chips; the code card and the
links card spell their resources in code spans, as later guidance may. References
resolve in guidance as anywhere, a code span included, so a brace meant
literally is written twice: `{{>key}` shows as `{>key}`.

## The words

One `sample-words.ini`, embedded in Sample-Shared, carries every word both samples
show. Each sample loads it, then its own `framework-words.ini` of two constants:
`$framework`, the name in the title, and `$embedded`, the base URI of an
embedded asset — `avares://Sample-Ava/Assets/` or
`pack://application:,,,/Sample-Wpf;component/Assets/`. Values reference them —
`Words for {$framework}`, `![Speech bubbles]({$embedded}bubbles.png)` — so the image schemes
are the only words that differ, and they differ in one place. A new topic's
words go in once. It speaks English and Italian throughout, and Maltese on the
Format parameters page alone, for its plural forms; picked from the menu,
Maltese reads the English default everywhere else.

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
| Markdown | inline styles; entities and emoji; code spans and blocks (`WordsCodeFont`, `WordsCodeBackground`); the kitchen sink; the playground (`WordsMarkdown`) |
| References | one key inside another (`{>key}`); constants (`{$constant}`, from `framework-words.ini`); dot-relative blocks and references; a loop, cut at `# ∞ #` |
| Hyperlinks | tooltips, autolinks and app commands (`RegisterGlobalNavigateHandler`), coloured by the theme (`WordsLinkBrush`); the app's report of them (`Params`) |
| Images | every scheme, `staticres:` beside `dynres:` under the theme toggle |
| Format parameters | positional, as a child binding; plural forms the same count picks (`{0#key}`, `value#other`), sharing its slider; named, read off an object; numbers and dates in the language's culture, with `UseSystemNumbers` |
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

## How each card is made

Under its chips, each card has a *How it's made* expander: the exact markup and
the exact words behind the card, read from the real files so they cannot drift
from what runs (`CardSources`, in Sample-Shared).

**The markup.** Each sample embeds its page views a second time, as written,
under `pages/`. A card's markup is the lines between the marker comments named
for it — `<!-- card: markdown.code -->` and the next `<!-- /card -->` — with
their common indent taken off. Every card on every page has its markers.

**The words.** From `sample-words.ini`, then the sample's `framework-words.ini`, each block
whose key is the card's key or beneath it, then each block those reference with
`{>key}`, `{>.sub}` or `{$constant}`, followed until nothing new turns up; an
escaped `{{>key}` is not followed. A card that looks keys up in code names them
in `MoreKeys` — the Enums cards name their `[Words]` enums' — and those join the
card's own. A block brings the comments right above its header; a comment set
apart by a blank line is a section's, and stays out. Blocks come in file order,
the card's own first, a blank line between blocks the file keeps apart, and a
`[.name]` header shown without its base is written out in full, so the cut
means what the file does. A file with nothing for the card is left out.

**The view.** The card's template asks the page view model for the How, by the
card's key and `MoreKeys`, and shows each file under a label naming it —
`card.how.from`, `From `{File}`:`, the file name a code span — in a read-only
text box, so the reader can select and copy it, with long lines wrapped. The box
takes the code spans' look from the same resources, `WordsCodeFont` and
`WordsCodeBackground`, and keeps it while hovered or focused.

**Why not markdown.** A code block rendered through `WordsMarkdown` read well,
but a text block cannot be selected, and a How is exactly what a reader wants to
copy. The label keeps the How's markdown.

**Tests.** The library suite cuts a page between its markers and dedents it;
takes a card's own blocks first, then what they reference, a relative header
written out in full and an escaped reference not followed; keeps a header-like
line inside a continued value and a block's own comment, and drops a section's;
adds `MoreKeys`; leaves out a file with nothing to show; and cuts the real
`sample-words.ini`'s code card whole.
