# Wordsmith Editor — what the tool is supposed to do

A desktop editor for `words.ini` localization dictionaries. Two people use it:
the **developer**, who creates keys, writes default text, and annotates intent;
and the **translator**, who receives a file, works through what changed, and
sends it back. Everything the tool does serves that round trip.

This spec fixes the behavior; layout and controls are the implementation's to
choose. Everything up to *Planned upgrades* describes what the editor does
today; that last part describes what it does not do yet.

## Architecture rules

- **The golden rule of MVVM: ViewModels extract Intent from the View so that
  something else can do the processing.** The "something else" is
  `PatTech.Localization.Authoring` — parsing, the document model, merging, and
  writing all live there and are tested directly against that API. ViewModels
  translate gestures into calls on the document; views bind and nothing more.
- The runtime packages stay lean: nothing the editor needs beyond parse events
  (`IWordsParserConsumer`) belongs in Localization-Core.
- Short names.
- Dirtiness has one door. `ViewModelSaveBase` owns `IsDirty`: a command that
  edited the document calls `MarkDirty()`, a property that *is* document state
  sets itself with the `dirty: true` overload of `ChangeProperty`, and only
  Save and Reset clear it. Nowhere else assigns the flag.
- The main window is three panes: **tree** (left), **baseline** (middle),
  **translation** (right).
- Dialogs are tool windows: one shell (`DialogWindow`), resizable, close
  only, centred on the owner, and one look — a heading, fields under their
  labels, lists with a trash on each row and a + beneath, the buttons at the
  right with the default first. The shell tells the view model when the
  window has closed, however it closed, so a dialog watching the document
  lets go. A dialog does not open another dialog; a nested step is a pane of
  the window that needs it (the language editor). A report after the fact
  (the export's gripes) is the one exception.

## The document

A session holds one or more files. Each file contributes:

- A **language table** from top-of-file `value-xx=Label` lines. A `!Label`
  declares a language without listing it — subordinate dictionaries legally
  support more languages than the host app offers. The editor must show these
  as intentional, never as errors, and never strip the `!`.
- A **key tree**: dotted block keys (`view.section.key`), prefixed in memory
  with the file's label — its name, disambiguated when two loaded files
  share one (`strings`, `strings-2`), since files are identified by path. `$keys` are constants (no translations). A key carries:
  default value, context (programmer → translator), comment (translator-facing),
  format parameters (`param-x=Type:sample`), a needs-review flag, and a
  **banner** (the freeform `;` comment run above its header).
- A file-level **preamble** (comments above the language labels) and
  **trailer** (comments after the last block).
- Per language, each key carries: value, context, comment, and an optional
  **stale** timestamp meaning "the default changed after this translation".

A **library file** declares its languages the `!Label` way — present but
unlisted. Opened alongside a main file in the host app, its extra languages
stay off the app's menu; opened solo in the editor, its `!` labels populate
the editor's language list so the file is workable on its own. Every file
keeps its own node in the tree, which is also how the split is visualized
when several files are open.

## Round-trip guarantees

The guarantee is a fixed point: save → load → save is byte-stable (see below).
A first load normalizes formatting and drops what the model doesn't represent,
so load → save is not a verbatim copy of arbitrary input.

Preserved (through load → save, and stable thereafter): every recognized field
and language entry, key order, freeform comments, preamble, trailer, constants,
`!` labels, per-language stale values (freeform text, kept as written), and the
top-of-file `param`/`param-xx` settings-file references (see Markdown previews).

Not preserved: unknown field types and unknown `param` data-types (dropped or
coerced to `String`, with a gripe); a field repeated within one key (last wins,
a repeated `value=` also warns); and the languageless `stale=`, kept as a
review flag with no stored text. A bare `[group]` header reloads as an empty
key (below).

Canonicalized by the writer (`IniWriter`): line wrapping (~50 columns),
escaping (`__`, `''`, leading-whitespace `_` marker), newline continuations,
and block headers — a block extending the last full header is written as one
dot-relative `[.suffix]`; an `ICutStrategy` decides where extra full-header
cuts go. The default (`GroupCuts`) writes a bare `[group]` header at a keyless
group gathering two or more keyed blocks, the shape a hand-author uses; the
bare header reloads as an empty key (accepted tradeoff), and a group whose
keys all sit under a deeper cut keeps no header of its own. Pass
`IniWriter.NeverCuts` for plain chaining. Comment placement is canonicalized
too: a `;` run between fields hoists above its block; comments in the
language section join the preamble.

Save→load→save is byte-stable; the tests pin it, for the fixtures and for the
editor's own words file.

## The tree

One tree presents every loaded file:

- **Node kinds**: file, group (a path segment without its own key data), key,
  and **organizer** — a standalone comment node. The writer emits it wherever
  it stands in the tree, so its anchor is simply whatever block follows it on
  the next load: interject a key between a comment and its original block,
  delete the block and the comment rides above the next one, or drag the
  comment itself. Editing the node edits the comment; deleting it deletes the
  comment. The preamble renders as an organizer pinned above the language
  table (the one comment written outside the tree walk); the trailer is just
  a comment standing at the file's end.
- **Badges** on nodes: file (a library file shows a bookshelf: its `!`
  languages are intentional), the file's load gripes as a count that opens
  the list, constant, needs-review, stale (in the selected language),
  overwritten-by-later-file; keys whose default or selected-language value is
  empty render emphasized. The selected-language half applies only
  when the key's file **registers** that language — declares it in its
  top-of-file table, listed or `!`-hidden. A hidden language is still a
  promise, so its gaps show; but in a project of several dictionaries, a file
  that does not register the selected language at all has no gap to show, and
  its keys stay plain. A code found only on stray fields is a gripe, not a
  registration — declare it and the gaps appear.
- **Filters**: substring search, stale-only, needs-review-only, missing-only —
  composable; ancestors of a match stay visible so the path is readable. The
  search reads what a translator searches for: a key's name, its default and
  selected-language words, the context and comments around them, and a
  comment node's text. The three toggles and the clear button are a vertical
  toolbar from the command table, in a popup beside the search box; while a
  filter narrows the tree the popup's button wears the number of hidden rows
  as a badge and the clear button clears the lot in one click; a selection
  the filter hides moves up to the nearest row still showing. The
  stale filter is per selected language
  and means stale, nothing more: this is the translator's work queue. The
  missing filter takes the empty values (file by file — see Badges). The
  needs-review filter is the programmer's work queue in reverse — the
  translator raises a hand by setting the unlocalised `stale=` flag
  (recycled as the "raise hand" action), and the programmer filters for it.
- **Structure edits**: add/rename/remove nodes and keys; renames rewrite every
  descendant key; drag-and-drop moves subtrees. A group node can gain key data
  ("add key information") and a key can exist on any node except a file.
  They are reachable from the button strip, the tree's context menu and the
  keyboard (F2 rename, Delete remove, Ctrl+Shift+S stale-all; Ctrl+O and
  Ctrl+S open and save, Ctrl+F the search). Removing a node that takes keys
  with it, or a key's information, asks first. A control a command has greyed
  out keeps its tooltip, so it still says what it would do.

## The baseline pane (middle)

Everything about the selected key that carries **no locale code**: the key
name (with rename), the default value, context (programmer → translator), the
translator-facing comment, the parameter definitions, and the unlocalised
flags — constant (only a leaf directly under a file), and needs-review
(`stale=`, the raise-hand). This is the developer's side of the conversation.

- **Preview**: the default value can be rendered through the Words markdown
  dialect in place of the raw text (image handling is the editor's own — see
  Markdown previews).
- **Parameter testing**: keys with `param-` declarations can run their sample
  values through `Format` to prove the placeholders work before shipping.
  `{>reference}` and `{$constant}` tokens work across files for this purpose,
  simulating a host app loading multiple dictionaries. The Test Parameters
  window is a table of name, type and sample with a trash on each row and a +
  under them, and shows the formatted result as the samples are edited — or
  why they will not format; its edits land in the key as they are made, and
  Close only closes.
- **Stale-all-languages**: one action for "I changed the default, every
  translation needs another look".

## The translation pane (right)

Starts with the **language dropdown** (fed by the file that owns the selected
key: its declared table, the codes found on its fields, and the language
selected so the choice always shows; the session union with nothing
selected) and mirrors the baseline's feature set for everything tagged with
the selected code: value, context, comment, the stale timestamp (read-only,
with a toggle that sets and clears it), and the markdown preview.

- Language codes found on fields but not registered at the top of the file are
  auto-added to the list with a `!` label and a gripe — wrong, probably, but
  still selectable so the stray entries can be inspected and fixed.
- Changing the dropdown re-contextualizes the whole window: tree badges and
  empty-value emphasis refresh to the new language (file by file — see
  Badges), and the stale filter re-evaluates against it.
- **Spelling**, in both panes: a box checks its text once it has the focus
  and stops when a new node's text arrives, so moving through the tree never
  waits on the speller (seconds per kilobyte of markdown). The translation
  boxes check in the selected language's dictionary, the baseline's in the
  system's. A light beside the language comes on when this system has no
  spell checker for it, since the speller would otherwise check against
  nothing and say so to nobody.

## Markdown previews

Both panes' previews render the Words markdown dialect, but the editor is not
the host app: the renderer's stock image resolvers (WPF `staticres:`, `pack:`,
`resx:`, `assets:`; Avalonia `avares:`, `assets:`, `staticres:`) would resolve
against *Wordsmith's own* resources — wrong app, wrong answers — and a
hyperlink would fire in the wrong process. The preview takes its vocabulary
from a **project settings file** instead of the pre-loaded set:

- With no settings file there are no rules: every image scheme falls back to
  the image's alt text (the renderer already does this for unknown schemes)
  and every link click is only reported. The editor never fetches remote
  images on its own.
- A `words.ini` names its settings file in the otherwise-unused keyless
  `param` slot of the top-of-file language section: `param=wordsmith.ini`
  for the dictionary, and `param-xx=wordsmith-xx.ini` for rules that apply
  only while previewing language `xx` (localized screenshots in a folder of
  their own, say). Paths are relative to the ini, so the settings travel with
  it; the several dictionaries of one project will usually name the same
  file.
- The settings file is Words ini syntax, read with the same parser — so it
  wraps, continues, comments and escapes (`__`, `''`, `\\`) like any ini —
  and carries editor metadata only; the runtime never reads it:

  ```ini
  ; scheme=folder: the URI's path is looked up under that folder
  [images]
  pack=../Images
  shot=../Captures
  ; a scheme the editor does not know needs a decode rule: a regex
  ; replace over the whole URI whose result is the relative path
  shot-decode=/^shot:(\w+)$/i/$1.png
  ; launch what the link resolves to, or show what the command would be
  [hyperlinks]
  https=shellexec
  appcmd=popup
  ```

- **Images.** A scheme maps to a folder; the URI becomes a path under it and
  is looked up as an image file. The built-in schemes need only the folder,
  because their shape is known: `assets:p` and `avares://Assembly/p` give
  `p`, `pack://application:,,,/Assembly;component/p` gives `p`, and
  `resx:Key` or `staticres:Key` name a file stem, tried with the usual image
  extensions. Any other scheme needs a `<scheme>-decode` rule,
  `/pattern/options/replacement` — `options` are the usual regex letters or
  empty, the pattern may not contain an unescaped `/`, the replacement may —
  and a decode rule on a built-in scheme overrides its default shape. A scheme
  with no rule keeps the alt-text fallback. A folder is relative to the
  settings file that names it. The constraint that does not move: the
  resulting path stays clamped to the scheme's folder — the same refusal of
  `..`, rooted and UNC paths, and of variable expansion, that the runtime's
  `FolderImageResolver` makes, kept in Authoring as
  `ProjectSettings.TryResolveImage` so the runtime library itself is
  untouched — and nothing is fetched remotely.
- **Hyperlinks.** A scheme maps to `popup` — report the target, the default
  for anything unlisted — or `shellexec` — confirm, then hand the target to
  the shell, the default for `http`, `https` and `mailto`. A `<scheme>-decode`
  rule, same syntax, rewrites the URI first, so a custom scheme can open as
  the page or command it stands for, or show that command. A click in either
  pane consults the selected language's rules — the one link handler cannot
  tell the panes apart — and the report names the original URI beside a
  rewritten target.
- **Language rules win key by key.** A field written in `wordsmith-xx.ini`
  replaces that field while previewing `xx` — a folder, a decode or a link
  mode each on its own, so a language file may give only `shot-decode` and
  keep the dictionary's folder; every other field falls through to
  `wordsmith.ini`, then to the defaults. The default-text preview uses the
  dictionary file alone.
- Field names are `\w+`, so a scheme with a dash in its name cannot be
  written; none of the built-ins has one.
- **Gripes.** Rendering a preview collects everything Words complains about
  along the way — a missing `{>reference}`, a circular one, an image that did
  not resolve, a link that would not parse, a sample that would not format —
  behind a tool button beside the preview toggle: a count when there is
  something, the list on click. The editor installs one collecting logger as
  `Words.Logger` and gives the preview parser the same one, so both channels
  land in the list.
- The Project Settings dialog edits what a dictionary names — the paths in
  its `param` slots, for the dictionary and for each language — and the two
  tables of whichever of those files is picked, with the rules' gripes shown
  as they are typed. Settings are the editor's to read and write, so it
  rewrites a settings file as two plain tables: comments in a hand-written
  one do not survive the dialog.

## Languages

A language manager adds, removes, relabels and reorders languages, on a
working copy: the list shows each language's code with a trash beside it and
a + under it, the pane edits the highlighted row's code and names live —
checked against the rules and the other rows, a field flagged once it has
been typed in (a session's row from the start), OK greyed while any row is
wrong — and drag reorders. Nothing reaches the session until OK; Cancel or
Escape forgets it all. On OK the copy is applied: a removal (confirmed at the
trash) deletes the language's entries from every key; an addition backfills
an empty entry on every key; a relabelling may re-code a language, which
shifts its entries; the order follows the rows; every file's table follows.
The table's `Rename` can also absorb a language into one that already holds
the code (where both hold a value the target's is kept, the source value is
parked in the entry's `context-xx` field where the translator can copy/paste
from it, and the entry is stale-marked so the review filter surfaces the
collision); the manager never asks for that, since no two rows may share a
code — two rows swapping codes go through a throwaway code instead. The
manager's highlighted row is its own while it is open and becomes the tree's
language on OK, so browsing the list does not re-contextualize the window
behind it.

## Merge

The translator round trip in bulk: pick a base file plus a language→file map,
and produce one merged file taking each language's entries from its source.
The merged result is written to disk and loaded into the session. Merging
requires the files to agree on their key sets. The first file ticked is the
base until another is chosen; unticking the base passes it on. Split, the
other direction, shares the dialog: one file and one of its declared
languages, written on their own — that language's entries with the defaults
for reference — and loaded, ready to be worked on separately and merged back.

## Saving

Save rewrites every loaded file through `WordsSession.Save` — `IniWriter.WriteFile`
with the file's own language table, preamble and settings references, in the
order its tree node walks — and with its own line break, `\n` or `\r\n`, the
first one it was read with (the system's for an imported file), so a file
round-trips byte for byte whichever its checkout gave it; merge and split write
with their source's. A file that cannot be written is reported and the
others still save. The editor tracks dirtiness; the window title names the
loaded files and stars while dirty, and closing with unsaved changes prompts.
Reset returns to the empty session (one default `en` language).

## Wordsmith's own words

The editor eats its own dogfood. Every user-facing string of Wordsmith —
labels, tooltips, dialog titles, confirmations, notices, the conflict and
error messages the view models compose — lives in the editor's own
`words.ini` (`Resources/words.ini`, embedded and copied beside the exe), loaded
through the library before the first window and rendered with the WPF
package: `{l:Words}` for plain text, `WordsInline` where a value carries
markdown. English is the default value; other languages are labelled at the
top of the file and fall back to it. Wordsmith speaks the language saved in
its own config file (`%LocalAppData%\Wordsmith\config.ini`), the OS language
when nothing is saved, or whatever `--lang=xx` on the command line says for
that one run. A submenu under View lists the languages the file labels;
since `{l:Words}` resolves when a window loads, picking one asks about unsaved
changes, saves the choice and restarts the editor with the same files open.
`IDialogs` and every other text-taking seam mark their parameters
`[Localized]`, and the analyzer runs on the project, so a hardcoded string is
a build warning (PTL001), never a silent one. The file is also the editor's
standing test subject: it must round-trip through the editor byte for byte,
name exactly the keys the source asks for, and open in Wordsmith to add a
language, translate, relaunch in it. What stays hard-coded is file syntax and
key caps, not words: `[images]`, `shellexec`, `F2`.
[The Core skill](../Localization-Core/SKILL.md) is the how-to.

## Import and export

The editor speaks `words.ini`; the world speaks resx, XLIFF, and whatever a
translator's last tool exported. This is the seam that lets the two trade,
without the native format giving up an inch of what it holds.

**The shape is two seams that already existed.** Import is what `WordsParser` →
`WordsParserToLocalizationProvider` does: turn foreign text into the neutral
document surface — keys, known languages, declared languages, preamble, block
comments, gripes — that `WordsSession.Load` absorbs. Export is what
`IniWriter.WriteFile` does: take the document and emit one format. A format is
not new machinery, then, but those two jobs generalized, and the native ini is
the first one written to the interface — its own reference implementation.
Had ini not made a plugin, the interface would have been wrong.

**Where it lives.** In `PatTech.Localization.Authoring`, tested headless against
the document model like everything else there (Architecture rules: the
processing lives in Authoring, the ViewModels only gather intent). A format is
three interfaces. `IWordsFormat` carries a descriptor — id, extensions, and a
`WordsFeatures` flag of what it preserves; its direction is which of the other
two it implements — and an `Init()` that hands back a `words.ini` fragment
(below). `IWordsImporter` adds `Discover`, `NativePath` and `Read`;
`IWordsExporter` adds `Plan` and `Write`; a format that goes both ways — a
*codec* — implements both, and the built-ins live under
`PatTech.Localization.Authoring.Codecs`. A `FormatOptions` bag rides along for
the formats that cannot be read until they are configured. The registry
(`WordsFormats`) holds the formats, finds one by id or by a file's extension,
and stacks their words; the app builds it at startup and hands it to the words
loader and the main view model.

**One load path.** `WordsSession.Load` has a sibling taking an `ILoadedWords`
— the surface `WordsParserToLocalizationProvider` already satisfied, and
`LoadedWords` is for filling by hand — so an importer produces that surface and
flows through the exact pipeline the ini loader does: label disambiguation
(`strings`, `strings-2`), empty-key dropping, language backfill,
reload-in-place. `WordsSession.Import` is `Read` then `Load` at the native path
the importer names: the pick with the ini extension for a one-file format, the
stem's — `Strings.ini` beside `Strings.*.resx` — for one file per culture.
Importers inherit the whole of loading for free, and are tested the same way.
On the way out, an `ExportSource` — the file, its tree and the session, refused
on the same terms as Save — is what `Plan` and `Write` take: its keys in tree
order, its language table, and what it uses of the model.

**Fan-in, fan-out.** A format is a *set* of streams, not one. `words.ini` is one
file carrying every language; resx is one file per culture (`Strings.resx`,
`Strings.fr.resx`); a spreadsheet is one file with a column per language. So
import has `Discover` — pick `Strings.fr.resx` and it gathers the default and
every `Strings.*.resx` beside it — and export has `Plan`, which turns one
document into the set of files to write, one `ExportUnit` each, so the editor
confirms the file list and its overwrites before a byte lands. This is Merge
and Split in another costume: a language maps to a file, which
`WordsOperations` already models.

**Each format brings its own words.** `Init()` returns a `words.ini` fragment,
its languages declared with `!` labels so they stay off the editor's menu; the
editor loads the seam's own (below), every registered format's, then its own
`Resources/words.ini`, then digests (`WordsBuilder` stacks sources
last-in-wins, so the editor's file loads last and a plugin never clobbers it). A format namespaces its keys
`format.<id>.*` and its descriptor names its display string by *key*
(`format.<id>.name`), not literal, so the Import filter and the Export dropdown
are built from the registry and a new format's chrome arrives with it —
localizable, analyzer-guarded, no hardcoded English (a lookup key is key-caps,
not words). A format's *gripes* stay plain diagnostic strings, the
`WordsFile.Errors` convention, not chrome.

**Loss is declared, not suffered.** The Words model is a superset of what most
formats hold, so export drops things — and says so, first. A format declares
(`WordsFeatures`) what it preserves — the context and comment channels, key
and per-language; parameters; stale marks and the review flag; constants;
freeform comments; settings references. Values every format keeps, so they
are not a feature. The loss preview (`format.Loses(source)`) is what *this*
document actually uses minus what the format keeps, so it warns about
parameters only when a key has them. Each feature names its words by key —
`[Words("feature.x")]` on `WordsFeatures`, read by `Describe` — and the seam's
own fragment, loaded ahead of the formats', carries the name; the editor's
file adds the `.sub` variant, the phrasing its loss line lists. The enum lives
in Authoring and the phrasing belongs to the editor, and the stacking lets
each keep its own. `Read` reports what it dropped or guessed in the surface's
`Errors`; `Write` takes a gripe list; both land behind the same gripes dialog
the previews and load errors use.

**Save never routes through it.** Import is one-way: the foreign file becomes an
in-session `WordsFile` whose path is a native `.ini`, so Ctrl+S writes ini and
the byte-stable fixed point (Round-trip guarantees) is untouched. Export is a
separate, deliberate gesture — pick a format, a target; read the loss; write.
Loss is always an act, never a side effect of saving. The weaker guarantee the
foreign formats keep: import then export to the same format is stable for the
fields that format carries; a foreign file taken through ini and back is
normalized, the way a first load normalizes ini itself.

**The built-ins.** `ini`, the native format written to the interface. `resx`: a
file's `<comment>` is its language's context as provided — the neutral file to
the key's context, each `Strings.xx.resx` to that language's entry context — and
the translator-facing comment channels, which resx has no slot for, drop with a
gripe. A constant keeps its `$` in the resource name and comes back a constant;
a culture file carries only what is translated, since an empty satellite entry
would shadow the default rather than fall back to it; typed and binary
resources are skipped on the way in, with a gripe. `xliff` (1.2, the version the
tools speak; 2.0 is refused with a gripe), the one that barely loses: one file
per target language, `<source>`/`<target>` the default and the entry value, the
four note channels `<note>`s told apart by `from` (developer, translator) and
`annotates` (source, target), and the target state carrying what every other
format throws away — an untranslated entry is `needs-translation`, a stale one
`needs-review-translation` with its stale text in a Words-namespace attribute,
the review flag `approved="no"`; a constant keeps its `$` and is
`translate="no"`; parameters ride as Words extension elements. The default
text being languageless while XLIFF insists on a `source-language`, the
`source-language` option names it, `en` unless told otherwise.

**Surface.** Import sits beside Load, Export beside Save. Import opens a picker
filtered by every importer's name and extensions; each pick's extension names
its format (an unclaimed one is told), `Discover` gathers the set, and the set
loads at its native path — asked about first when that `words.ini` is already
on disk, since Save will overwrite it — and presents in the tree as unsaved
work, unless the pick was an ini, which is a load. A set picked twice over
imports once. Export is a dialog: one loaded file (the selection's, to begin
with), one format, one target — the one file, or the neutral one of a set —
and as any of them changes, the plan (each file `Plan` will write, flagged when
it already exists) and the loss (`Loses`, in words, or that nothing is lost).
Export confirms the overwrites, writes each unit atomically through
`IniWriter.WriteAtomic` — a failure partway is told and the files before it
stand — shows what the format griped about, and closes. The captions and the
loss words live in `words.ini`; the format list, names included, comes from
the registry.

**Tests.** Every importer is tested through `ILoadedWords` the way the ini loader
is tested through the parser: a fixture in, the document surface out, gripes
pinned. Every exporter round-trips against itself — import, export, import, and
the fields the format holds are unchanged — and the native fixed point is
re-checked to prove Save still never touches a foreign writer. The editor's
tests drive Import and Export through `FakeDialogs`: the native path, the ask,
dirtiness, the plan, the loss and the overwrite confirmation.

## Menu and toolbars

A menu bar carries every command the editor has, grouped the usual way —
File (Load, Import, Merge, Save, Export, Reset, Exit), Edit (the node and key
operations, then the flags), View (the filters, the previews, Find, then the
translation language and Wordsmith's own as submenus), Tools (Languages,
Project Settings, Test Parameters) — with an access key on each menu and
gesture text on each entry, so everything is reachable by name and by
keyboard, not only by icon. The toolbars are toolbar controls populated from
the same commands and carry only what is convenient: the node operations
under the tree, the key operations under the baseline pane, the filters as
a vertical toolbar in the popup beside the search box, Rename at the right
of the selected node's name, each pane's header (Test Parameters, the key's
flags, its preview) and, above the translation pane, Languages beside the
translation language as a combo box.
Files in and out, Merge, Reset and Project Settings live in the menu alone,
the files with their keys (Ctrl+O, Ctrl+I, Ctrl+S, Ctrl+E).

**One row per command.** A command is defined once — its `ICommand`, its
caption, its icon and its gesture — as a `CommandItem` in the command table
(`CommandTable`, on `MainWindowViewModel`), and the menu, the toolbars and the
tree's context menu (the Edit menu again) render from the table through item
templates, so a new command is a row and nothing else. A row with a state
behind it (`ToggleItem`: a filter, a preview, a flag on the selected key) is
checkable in the menu and a toggle on a toolbar; its command flips the state
and the row reads it back, and the owner tells the row when the state, or
whether it applies, changed elsewhere, so the tick, the popup's button and
the pane's toggle agree. A pick among options (`ChoiceItem`: the two
languages) is a submenu of ticked rows in the menu and a combo box on a
toolbar, its options mirrored from the owner's. The window binds the table's
gestures once, from the rows that carry one; Find is a routed command and
carries Ctrl+F of its own. The captions are looked up by literal key in the
table, so the editor's own words name every one of them.

**Disabled, not hidden.** A toolbar button whose command does not apply is
greyed, not removed, and a greyed button still says what it would do; the
pane headers keep their gripe badges, which act on what they sit beside.
Buttons and toggles share one template in the icon's colours — orange under
the mouse, a blue frame around a toggle that is on — rather than the theme's
tool button and switch, so the two kinds match in size and weight; the
filter popup's button is the same toggle.
Back, Forward, Undo and Redo join the table when they exist (Navigation,
Undo).

**Tests.** Every command the view model exposes is in the menu once (the two
badge commands excepted); the toolbars and the context menu draw from the
menu's rows; every caption, tooltip and option renders without a key leaking;
a key is bound once; a toggle mirrors its state whichever way it changes and
a flag toggle reads the selected key; a choice mirrors its owner's options
and pick, and a pick of Wordsmith's language is a request; Exit asks the
window.

---

# Planned upgrades

Not built yet. Each section here is the shape the feature takes when it is.

## Undo

There is no undo stack; the confirmations on the destructive actions
(removing a node that takes keys with it, removing key information, making a
key a constant, removing a language) stand in for it.

**One door, again.** Every document change already passes through
`ViewModelSaveBase.MarkDirty` (Architecture rules: dirtiness has one door), so
that door is where an edit is recorded: what marks dirty also pushes onto the
undo stack. Nothing else needs to know undo exists.

**Snapshots, not commands.** The document is small — a handful of ini files
— and the writer round-trips byte for byte (Saving), so the state of a file
*is* its written text. An undo entry is the session written to strings
(every file, in its tree node's walk order, with its language table and
settings references) plus the full label of the node the edit is on — the
selection for a pane or menu edit, the moved node for a drag, none for an
edit of the language table — taken before the edit lands. Undo reloads those
strings in place through `WordsSession.Load` (the same path as loading from
disk, which replaces a file by path and drops what is gone), re-presents the
tree, and reselects the label; redo mirrors it with the snapshot taken before
the undo. Not command objects per mutation — one for each edit site, tree
reorders and drags among them: the snapshot is correct by construction and
costs one write of an ini-sized document per edit.

**Coalescing.** Typing into a value, context or comment box raises
`Tree.Edited` per keystroke; consecutive edits to the same field of the same
key and language fold into one entry, so undo takes back the typing, not a
character. Every other edit is its own entry.

**Boundaries.** Save does not clear the stack (a saved state can still be
undone; the title stars again). Reset, Load, Import, Unload, Merge and Split
do clear it: they change which files the document is, and a snapshot of
other files is no help. Undo restores `IsDirty` to what the snapshot had.

**Navigate first.** An undo whose entry is on a node other than the selected
one does not undo yet: it goes there. The node is selected and shown — it and
its ancestors exempt from the filters for as long as it is the selection,
where the filters otherwise evict a hidden selection to a shown ancestor (The
tree) — so what is about to change is in view, and the next Ctrl+Z undoes it.
Redo mirrors it. An entry with no node (a language-table edit) undoes at
once, and so does one whose node is already selected. The step is a
navigation like any other (Navigation), so Back returns from it.

**Surface.** Ctrl+Z / Ctrl+Y bound on the main window, `UndoCommand` and
`RedoCommand` on `MainWindowViewModel` with `CanExecute` from the stack depth,
entries in the Edit menu and the tree's context menu, and a pair of toolbar
buttons beside Back and Forward (Navigation), their captions in `words.ini`.
Language edits made in the Language Manager are entries like any other, taken
when the manager marks the parent dirty.

**Tests.** Every mutation the dirtiness test drives gets an undo twin: the
saved text after undo equals the text before the edit, and redo brings the
edit back. An undo on an unselected node selects it, shown through a filter
that hides it, and changes nothing until the second call. The drag tests and
the merge and split flows check the stack is cleared or kept as this section
says.

## Navigation

The tree is the map, and the editor does not remember where the user has
been: a search, a filter, a context-menu jump or an undo's navigate-first
step (Undo) moves the selection, and there is no way back but to find the
place again.

**A history of selections.** Every change of the selected node, edited or
merely visited, is a move in a history; the same node twice in a row is one
entry. Back and Forward step along it without pushing, like a browser's. A
selection made by hand is matched against its neighbours first: selecting the
node Back points to *is* Back — the current entry crosses to the forward side
— and selecting the node Forward points to is Forward. Walking A, B, A, B by
hand therefore does not pile up entries, and stepping onto the next node by
click rather than by the Forward button does not erase what lay ahead. Only a
selection matching neither neighbour pushes, and a push while behind the end
drops the forward run. An entry whose label no longer resolves — the node
removed or renamed, its file unloaded — is dropped when it is reached. The
history is bounded (a few dozen) and Reset clears it. Arriving by Back or
Forward shows the node the way navigate-first does: exempt from the filters
while it is the selection.

**Surface.** `BackCommand` and `ForwardCommand` on the tree view model,
Alt+Left / Alt+Right and the mouse's back and forward buttons, entries in the
View menu, and a toolbar group by the search with Undo and Redo alongside:
the four move through the document in its two senses.

**Tests.** The history is driven headless: select, select, Back lands on the
first and Forward on the second; selecting the node Back points to is a Back,
the forward run kept, and selecting the node Forward points to is a Forward;
a selection matching neither neighbour truncates the forward run; a removed
node's entry is skipped; Reset empties it; a Back onto a filtered-out node
shows it, and moving on hides it again.

## Import and export, next

**Options.** `FormatOptions` has a seam and no door yet: xliff's
`source-language` is `en` until a dialog asks. The spreadsheet importer is what
earns that door, and it is the format that will prove the seam.

**Spreadsheets.** A CSV has no real consistency, so the one assumption made is
that Excel wrote it: cells quoted with doubled quotes, newlines kept inside the
quotes, and the delimiter and encoding the machine's locale's — a comma, or a
semicolon where the decimal is a comma; UTF-8 with a BOM from "CSV UTF-8", the
ANSI code page otherwise. Everything else is asked. Import opens a dialog
before `Read`: the first rows as a grid, so the user sees what the file is;
whether the first row is a header; the delimiter and the encoding, sniffed and
overridable (mojibake in the preview is the tell); and a role for every column
— the key, the default value, its context, its comment, a language's value,
context or comment, or ignore. A language column carries its code: read from
the header by pattern (`fr`, `fr-CA`, `French (fr)`, `value-fr` — a regex with
a `code` group, the built-in patterns tried in turn, a custom one typed), or
typed when the header is no help. The sniff makes the first guess — a header
when the first row's cells look like names, the key column when its cells are
unique and dotted, a language column when its header yields a code
`CultureInfo` knows — and the dialog is where the user corrects it. `Read`
refuses, with a gripe, a schema with no key or no default column; a row whose
key is empty or repeated is skipped with a gripe, and a row with more cells
than columns is one too.

**The schema is the options, and it saves.** Everything the dialog settles is
the `FormatOptions` bag — `header`, `delimiter`, `encoding`, `column.N=key`,
`column.N=value-fr`, `code-pattern` — and a bag of key=value pairs is a
words-syntax file, so a schema saves as `Strings.csv-schema.ini` beside the
sheet and loads back into the dialog whole. A translator's tool that exports
the same layout every time becomes a one-click import, and the same schema
drives export, so a sheet sent out and the sheet coming back share it: the
weaker round trip (Save never routes through it) made reliable for this
format.

**Export.** One sheet, the wide layout translators work in: a column for the
key, the default, and one per language, with context and comment columns when
the schema asks. Cells that need it are quoted. A leading `=`, `+`, `-` or
`@` is Excel's formula, not text; whether to guard it (a `'` prefix, stripped
on the way back in) is decided when a real value starts with one. No long
layout — one row per key and language — until a tool asks for it.

**Where it lives.** The codec in Authoring, tested headless: `Sniff(path)`
gives the first-guess options and `Preview(path, options, rows)` the grid,
both public, so the editor's dialog view model stays thin and the tests drive
the same calls. The dialog is the CSV codec's own for now; a generic options
surface — a format declaring its option keys and their kinds — waits until a
second format needs one.

**Third-party formats.** A second repository, pinned as a submodule and
PR-reviewed, compiled from source: no runtime loading, no signing, none of the
trust surface a drop-in DLL would open. Each format assembly self-registers
through one entry point the app calls at startup, into the registry
`App.OnStartup` already builds; changing the set is a rebuild, not a hot-swap.
Runtime plugins stay a later question, and the interface is shaped so it could
answer it without changing.
