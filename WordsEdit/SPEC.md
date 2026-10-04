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
  Save and Reset clear it — and Undo and Redo, which put back the dirtiness
  their entry recorded (Undo). Nowhere else assigns the flag.
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
  as intentional, never as errors, and never strip the `!`. The labels are
  each language's own name (`value-xx`, the endonym) and its name in the
  default's language (`comment-xx`, the exonym). The exonym is optional: a
  file writes `comment-xx` only where one was given, by the file or the
  manager, never a copy of the endonym. Lists name a language by its exonym,
  or by its endonym without one. An import names languages by their culture,
  with the English names only where the default is English or undeclared.
- The **default's language**, from a keyless `value=!xx` leading that table
  (the runtime spec's *The default's language*): what the default is written
  in. Where the default speaks the selected language, its own code or, for a
  bare code, a regional variant of it, an empty entry falls back to the
  default and misses nothing (Badges). A file without the line says nothing.
  `value=xx` without the `!` loads as the same declaration with a gripe, and
  saves with the `!`.
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
  registration — declare it and the gaps appear. Nor has a file a gap in a
  language its default speaks (The document): an empty entry there falls back
  to the default and misses nothing, so an `en` default leaves `en` and
  `en-AU` plain, while `en-US` under an `en-AU` default still shows its gaps.
- **Filters**: substring search, stale-only, needs-review-only, missing-only —
  composable; ancestors of a match stay visible so the path is readable. The
  search reads what a translator searches for: a key's name, its default and
  selected-language words, the context and comments around them, and a
  comment node's text. The three toggles and the clear button are a vertical
  toolbar from the command table, in a popup beside the search box; while a
  filter narrows the tree the popup's button wears the number of hidden rows
  as a badge and the clear button clears the lot in one click; a selection
  the filter hides moves up to the nearest row still showing, unless Back or
  Forward brought it there (Navigation). The
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
  Markdown previews). Its parameter samples format in the default's language
  when the file declares one, as the translation's do in the selected one.
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
- Where the default speaks the selected language, the empty value box shows
  the default greyed as its hint: what the entry reads as until it is given
  words of its own.
- Changing the dropdown re-contextualizes the whole window: tree badges and
  empty-value emphasis refresh to the new language (file by file — see
  Badges), and the stale filter re-evaluates against it.
- **Spelling**, in both panes: a box checks its text once it has the focus
  and stops when a new node's text arrives, so moving through the tree never
  waits on the speller (seconds per kilobyte of markdown). The translation
  boxes check in the selected language's dictionary; the default, the
  context and the freeform comments in the default's language when the file
  declares one, else the system's. A light beside the language comes on when this system has no
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
One row may be the default's language: a tick in the pane sets it and moves
it from the row that had it, a mark beside the code shows it in the list, and
the field for the names written in the default's language is headed by it
("Name in English"), or "English Language Name" while no row is ticked. That
field may stay blank (the file then writes no `comment-xx`); the code and the
endonym may not. On OK
every file declares it (a file that declared none gains it only when the
choice changed), a recode carries it along and a removal takes it.
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

**The window.** The same config file remembers the main window as it last
closed — its normal size, and whether it was maximized — and the next run
opens it so, cut to the screen's work area and no smaller than its minimum
(`EditorConfig.Window`, a `WindowPlace`). A close the save question cancels
is no close; a language restart remembers the window before the new editor
reads it. Where the window sits is still the system's choice.

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
`translate="no"`; parameters ride as Words extension elements. XLIFF insists
on a `source-language`. The file's default language is it, `en` for a file
that declares none, and the `source-language` option overrides both. On the
way in, the attribute declares the default's language. The default's
language is a feature like any other: resx has no slot for it and says so.

**Surface.** Import sits beside Open, Export beside Save. Import opens a picker
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
File (Open, Import, Merge, Save, Export, Reset, Exit), Edit (Undo and Redo,
the node and key operations, then the flags), View (the filters, the previews, Find, then the
translation language and Wordsmith's own as submenus), Tools (Languages,
Project Settings, Test Parameters) — with an access key on each menu and
gesture text on each entry, so everything is reachable by name and by
keyboard, not only by icon. The toolbars are toolbar controls populated from
the same commands and carry only what is convenient: the node operations
under the tree, the key operations under the baseline pane, the filters as
a vertical toolbar in the popup beside the search box, Back and Forward on
the box's other side, Rename at the right of the selected node's name, each pane's header (Test Parameters, the key's
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
gestures once, from the rows that carry one, and the mouse buttons a row
names beside them; Find is a routed command and carries Ctrl+F of its own. The captions are looked up by literal key in the
table, so the editor's own words name every one of them.

**Disabled, not hidden.** A toolbar button whose command does not apply is
greyed, not removed, and a greyed button still says what it would do; the
pane headers keep their gripe badges, which act on what they sit beside.
Buttons and toggles share one template in the icon's colours — orange under
the mouse, a blue frame around a toggle that is on — rather than the theme's
tool button and switch, so the two kinds match in size and weight; the
filter popup's button is the same toggle. Every toolbar gives back the room
the theme keeps for its overflow button until it does overflow
(`ToolBarOverflow`, set by one implicit toolbar style).
Back, Forward, Undo and Redo are rows like any other (Navigation, Undo).

**Tests.** Every command the view model and the tree expose is in the menu
once (the two badge commands excepted); the toolbars and the context menu
draw from the menu's rows; every caption, tooltip and option renders without
a key leaking; a key, or a mouse button, is bound once; a toggle mirrors its
state whichever way it changes and a flag toggle reads the selected key; a
choice mirrors its owner's options and pick, and a pick of Wordsmith's
language is a request; Exit asks the window.

## Navigation

The tree is the map, and the editor remembers where the user has been on
it: a search, a filter, a context-menu jump or a click moves the selection,
and Back takes it where it was.

**A history of selections.** Every change of the selected node, edited or
merely visited, is a move in a history (`SelectionHistory`, which the tree
view model keeps as node labels); the same node twice in a row is one entry.
Back and Forward step along it without pushing, like a browser's. A
selection made by hand is matched against its neighbours first: selecting
the node Back points to *is* Back — the current entry crosses to the forward
side — and selecting the node Forward points to is Forward. Walking A, B, A,
B by hand therefore does not pile up entries, and stepping onto the next
node by click rather than by the Forward button does not erase what lay
ahead. Only a selection matching neither neighbour pushes, and a push while
behind the end drops the forward run. An entry whose label no longer
resolves — the node removed or renamed, its file unloaded — is dropped when
it is reached, and so is one left beside its own twin once what stood
between them went. The history keeps fifty entries, the oldest going first,
and Reset clears it; a moment with nothing selected is not a move.

**Arriving.** Back and Forward open the path to the node they arrive at and
show it through the filters: it and its ancestors are exempt for as long as
it is the selection, where the filters otherwise move a hidden selection up
to a shown ancestor (The tree), and the hidden-row count leaves it out. When
the selection moves on, the filters have it again. A filter that moves the
selection up has made a move, so Back returns to the row it hid.

**Surface.** `BackCommand` and `ForwardCommand` on the tree view model, as
rows of the command table: Alt+Left and Alt+Right, the mouse's back and
forward buttons (a row may name a mouse button, which the window binds
beside the keys through a `MouseButtonGesture`), entries in the View menu
beside Find, and a toolbar group on the search box's left.

**Tests.** The history is driven headless: select, select, Back lands on
the first and Forward on the second; selecting the node Back points to is a
Back, the forward run kept, and selecting the node Forward points to is a
Forward; walking two nodes by hand, or selecting one twice, adds nothing; a
selection matching neither neighbour drops the forward run; a removed
node's entry is dropped when reached; Reset empties it; a Back onto a
filtered-out node shows it, and moving on hides it again; arriving opens
the path; the history is bounded, and a gone entry takes its twin with it;
and the mouse's back button runs its command through WPF's own input
bindings, on the press alone.

## Undo

Every edit to the document can be taken back and put back. The
confirmations on the destructive actions (removing a node that takes keys
with it, removing key information, making a key a constant, removing a
language) stay.

**One entry per action.** A document can be large and most edits touch very
little of it, so the stack (`UndoStack`, on `MainWindowViewModel`) holds
actions, not copies of the document: each entry (`UndoEntry`) carries
exactly what its action changed and undoes it in place with the action's
own inverse, redoing it the same way. Nothing reloads and the tree keeps its
expansion. An entry finds what it changes by label — entries are undone in
order, so every label still means what it meant — and a comment, which
shares its label with its siblings, by its parent and its place. Entries
stack: undo takes back the latest, redo puts it back, and a new edit drops
whatever was waiting to be redone.

- **Fields.** Typing into a text field — a key's default value, context or
  comment, an entry's value, context or comment, a comment node's text — is
  a `FieldEdit`: the node, the language (none for a key's own fields or a
  comment), the field (`DocumentField`), and its text before and after,
  nothing more, so each field undoes on its own; [plural
  forms](../Localization-Core/SPEC.md) will add fields, not kinds of entry.
  A property change does not say what it replaced, so the tree keeps the
  selected node's text fields as they last stood and reports each change
  (`FieldEdited`) with both texts. A note — a key's or an entry's comment —
  raises Needs Review as it is typed, and only as it is typed: not while a
  command or an undo runs. Its entry says so, and undoing the typing lowers
  the hand again.
- **Commands.** Each toggle — Needs Review, Stale in the selected language,
  Constant — is a `KeyEdit` of its own (`ReviewEdit`, `StaleEdit`,
  `ConstantEdit`): the key, the flag, its value before and after; Constant
  also carries its relabel and any translations it cleared. Stale All
  Languages, on the selected key, is a `KeyEdit` too (`StaleAllEdit`), the
  stamps it replaced kept with it. Adding key information is `KeyAdded`;
  removing it is `KeyRemoved`, which keeps a copy of the key and puts a copy
  back. A Test Parameters session is one `ParametersEdit`, the key's
  parameters before and after, made only when they differ.
- **Structure.** Adding a node or a comment is `NodeAdded`; removing one is
  `NodeRemoved`: the parent, the position, the node itself — its subtree,
  comments and all — copies of the keys beneath it, and a comment's text
  (the preamble's lives in its file, which the removal empties) — one entry,
  however much it takes. A rename and a drag are both a `Move` — the old
  and new place: parent, position, label and name — since a drag may or may
  not rename and a rename is a move that changes only the last segment;
  undoing one renames back through the session and the node moves back. A
  drop where the node already stood changes nothing and makes no entry; a
  file dragged among files is precedence, not content, and makes none
  either.
- **Settings references.** The Settings dialog's change to a file's
  `param=` and `param-xx=` slots is a `FileSettingsEdit`, the slots before
  and after, made only when they differ. The tables themselves are written
  to their own files when the dialog closes, and stay outside undo.
- **Languages.** A Language Manager commit is one `LanguagesEdit`: the
  manager makes its table operations through it (`ChangeLanguages`, on the
  main view model), each keeping its inverse, undone in reverse order — an
  addition by removing the language, a relabel by restoring the entry it
  replaced, a reorder by moving it back, a removal by putting the entries it
  dropped back on each key and the language back in its place, a recode
  onto a free code by recoding back, exact since the entries moved whole, a
  change of the default's language by giving each file the one it had —
  and then every file's table, and its default's language, is put back
  whole. A recode onto a code already in the table merges two languages'
  entries and has no tidy inverse; the manager never asks for one (no two
  rows share a code), but a commit that makes one clears the stack instead
  — the last resort for any document-wide action that cannot keep a
  reversible state.

**Recording.** Fields report themselves; every other entry is made by its
command through one door (`Perform`, on the main view model): the command
makes its change and returns its entry, or nothing when it changed nothing,
and while it runs the field reports are not typing — a constant clearing its
translations is not typing. A dialog a command opens belongs to the command,
so a Test Parameters session or a Settings Okay is one entry. Each entry
records whether the document was dirty before its action, and undo restores
`IsDirty` to that; a save moves the marks, so an undo or redo that leaves the
saved state stars the title and one that comes back to it clears it.

**Coalescing.** Consecutive edits to the same field of the same node and
language fold into one entry, the first text before and the last after, so
undo takes back the typing, not a character. A different field, another
entry, an undo or a redo, or moving to another node ends the run, and a run
typed back to where it started leaves no entry at all.

**Navigate first.** An undo should not surprise: an entry whose change is
out of view does not undo yet — it goes there. The node is selected and
shown, it and its ancestors exempt from the filters for as long as it is
the selection (Navigation), and an entry tied to a language (an entry's
field, the stale toggle) switches the translation language to it, so what
is about to change is in view; the next Ctrl+Z undoes it. A change already
in view — its node selected, in its language — undoes at once, and so does
an entry that shows nowhere in the tree (a Language Manager commit, a
Settings Okay). The step is a move like any other, so Back returns from it.
Redo mirrors it. Once an entry is undone or redone the selection follows it
— an undone removal selects the restored node, an undone addition its
parent, an undone move the node in its old place — shown through the
filters the same way, so what just changed stays in view, and a field edit
then focuses its text box, a free action, so the next keystroke lands where
the change did.

**Boundaries.** Save does not clear the stack (a saved state can still be
undone; the title stars again). Reset, Load, Import, Unload, Merge and Split
do: they change which files the document is, and an entry for other files is
no help. Each of them puts a file node into the tree or takes one out, and
that is what clears it; a file dragged among files does not. A Language
Manager commit that merges codes clears it too.

**Text boxes.** The main window's editing boxes keep no undo of their own
(`IsUndoEnabled` off): a box turns Ctrl+Z and Ctrl+Y into the routed Undo
and Redo before the window's keys see them, and the window catches those on
their way down to the box (`DocumentUndo`) and runs the document's, so a
field focused after an undo answers the next Ctrl+Z with the stack. The
search box keeps its own, as do the dialogs' boxes, which are other
windows.

**Surface.** `UndoCommand` and `RedoCommand` on `MainWindowViewModel`, with
`CanExecute` from the stack depth, as rows of the command table: Ctrl+Z and
Ctrl+Y, entries at the head of the Edit menu (and so in the tree's context
menu), their captions in `words.ini`. They have no toolbar buttons: beside
Back and Forward they left the search box too little room (Planned
upgrades: The search strip).

**Tests.** Every kind of entry has an undo twin, in one run through the
example file: adding a node, a key and a comment; typing a default, a
translation, a note that raises the hand and a comment; a rename; each
toggle and Stale All; a constant clearing a translation; removing the
preamble, a key, and a node with keys and comments beneath it; a parameters
session; a drag under another parent, among siblings, and of a comment; a
settings Okay; and each Language Manager operation — adding, removing,
relabelling, recoding, reordering, swapping two codes, and declaring, recoding
and moving the default's language. Each is one entry; undoing it gives back
the document before it — every file's saved text and
every row of the tree — with a clean title, and redoing it the document
after; the whole run undone is the file as loaded, and redone the last of
it. A typing run is one entry until another field, another node or an undo
comes between, and typing back to the start leaves none; a note that raised
the hand undoes both, and undoing the clearing of a note leaves a hand that
was lowered down. An undo out of view selects the node through a search
that hides it and switches the language, changing nothing until the second
call, which applies it, focuses the field and keeps the node in view; Back
returns from the navigation; an undo in view applies at once and focuses
the field, and so does its redo. Save keeps the stack, undoing past it
stars the title and coming back clears it; Load, Import, Unload, Split,
Merge and Reset clear it and a file reorder does not; a commit that changes
nothing records nothing, and a merging recode clears the stack. In a pair of
laid-out text boxes routed the window's way, Undo in the editing box runs
the document's undo and Redo asks the document whether it can, while in
the search box Undo is the box's own.

---

# Planned upgrades

Not built yet. Each section here is the shape the feature takes when it is.

## Import and export, next

**Options.** `FormatOptions` has a seam and no door yet: xliff's
`source-language` is the file's default language until a dialog asks. The spreadsheet importer is what
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

## The search strip

Back and Forward share the tree's header with the search box and the
filter button, and the tree's column is the narrowest. With Undo and Redo
as buttons there too, the box was about 78px wide in a 960px window and
about 35px at the minimum width — too narrow to read what was typed. For
now Undo and Redo are the Edit menu's and the keys' alone, and the window
opens 1080px wide: the box is about 180px in the default window and about
100px at the minimum. To give Undo and Redo buttons back, or the box more
room, undecided between:

- **Search below the buttons.** The strip becomes two rows: the buttons on
  top, the search box and the filter button under them at the column's
  full width. The tree gives up a row's height.
- **A collapsed search box.** The strip shows the magnifier alone; a click
  on it, or Ctrl+F, grows the box over the buttons, and it collapses again
  when it loses the focus. A search left in force needs to stay visible
  while the box is collapsed, for example as a mark on the magnifier like
  the filter button's hidden-row count.
- **Move the buttons.** Undo and Redo to the empty left side of the middle
  pane's header (mirroring Rename on its right), or all four to the menu
  bar's empty right side.
- **A toolbar editor.** Which rows go on which toolbar becomes the user's
  choice, saved with the editor's settings; the command table already makes
  every row the same kind of thing, so a toolbar is a list of rows. That
  answers the crowding by letting a user drop what they don't use, and the
  growing number of commands besides — but it is a feature of its own, not
  a layout fix.

## Undo inside a text box

Today an editing box keeps no undo of its own and every Ctrl+Z goes to the
document (Undo: Text boxes). A box's own undo stack cannot be read, so it
cannot become document entries, but it need not be thrown away either:
while a box has the focus, Ctrl+Z and Ctrl+Y could be the box's, undoing
its typing within its own context the way any text box does, and only when
it has nothing left to undo would Ctrl+Z reach the stack. A focus change
ends the box's context: its own history is cleared, so coming back to the
box never undoes typing the stack may already have taken back, and the
typing run on the stack ends with it — whatever the box undid inside the
run is already folded into the run's last text, and a run undone back to
its start leaves no entry. The window's routing (`DocumentUndo`) would ask
the focused box whether it can undo before handing the command to the
document.

## A command line for tools

A build step, a script or a coding agent that needs to change one entry
should not have to open the editor, and should not have to hand-edit a
format with continuation and escaping rules either. Saving through Wordsmith
normalizes the whole file (Round-trip guarantees), which is fine for a file
Wordsmith already wrote and noisy for one written by hand. The command line
changes the entry it is asked to change and leaves every other byte alone.

**Verbs.** One file per call; a field is named as in the file (`value`,
`value-fr`, `context-fr`, `stale-fr`); a value is an argument, or `-` for
stdin, so a multi-line value needs no shell quoting:

- `get <file> <key> [field]` prints a field's value, unescaped, or the whole
  block when no field is named.
- `set <file> <key> <field> <value>` sets one field, adding the key, or the
  field, where it is missing. `--stale [text]` marks the language's entry
  stale with it, so the review filter surfaces a machine-written value.
- `remove <file> <key> [field]` drops one field, or the whole key.
- `list <file> [prefix]` prints the keys; `--missing xx` only those where
  `xx` misses its words, by the editor's rule (Badges: an entry the default
  speaks for misses nothing).

A key is the file's own, without the session's file-label prefix. Values go
to stdout and gripes to stderr. The exit code is 0 for done, 1 when the key
or field is not there, 2 for a bad call or a file that does not parse.

**Surgical edits.** The file is parsed, and the one field's lines (its
declaration and its continuations) are replaced with what `IniWriter` writes
for that one pair: escaping and folding as Save would, in the file's own line
ending, encoding and BOM. Nothing else moves. A new field goes after the last
field of its block. A new key goes after the end of the header chain holding
its nearest sibling, as a full header: inserting one between a base and its
`[.child]` headers would re-base them. A removed key whose header bases
`[.child]` headers keeps that header bare, which reloads as an empty key (the
tradeoff the writer already makes). After the edit the result is parsed again
and compared with the model before it: anything but the asked-for change
refuses the write, and the file is written to a temporary file and moved over
the original only once that passes.

**Where it lives.** The patcher in Authoring, tested headless. It needs
positions the parser does not report today: the line where each declaration
starts, an additive member of `IWordsParserConsumer` so existing consumers do
not change. The command line itself is a thin console project over it. The
root `CommandLine.cs`, an unused argument parser from the original import, is
the candidate for its arguments, or for deletion. The agent skill
(`SKILL.md`) then tells agents to use it rather than edit `words.ini` by hand.

Undecided: how it ships (beside Wordsmith on GitHub Releases, or as a .NET
tool package, `dotnet words set …`, which would need its own tag and
version), and whether Wordsmith's Save later patches just the fields that
changed in the same way, which would answer the hand-written-formatting
question for the editor too.
