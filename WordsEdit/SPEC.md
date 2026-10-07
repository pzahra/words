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
  in. Where the default speaks the selected language, its own code or one
  that falls back to it (`en` for `en-AU`, `zh-Hant` for `zh-Hant-TW`), an
  empty entry falls back to the default and misses nothing (Badges). A file without the line says nothing.
  `value=xx` without the `!` loads as the same declaration with a gripe, and
  saves with the `!`.
- A **key tree**: dotted block keys (`view.section.key`), prefixed in memory
  with the file's label — its name, disambiguated when two loaded files
  share one (`strings`, `strings-2`), since files are identified by path. `$keys` are constants (no translations). A key's name
  is the runtime's grammar (its spec's *Key names*): segments of letters,
  digits, `_` and `-`, joined by dots, and a constant one segment. A block the
  file names otherwise loads all the same, with a gripe that a runtime skips
  it, and saves back as it was; renaming it is the fix. A key carries:
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
`!` labels, per-language stale values (freeform text, kept as written), the
top-of-file `param`/`param-xx` settings-file references (see Markdown previews),
and plural forms (`value#few`, `value-xx#few`), every CLDR category written —
one a runtime never reads, such as `#one`, is kept with a gripe.

Not preserved: unknown field types and unknown `param` data-types (dropped or
coerced to `String`, with a gripe); a form that names no CLDR category, and a
form on a language label (dropped, with a gripe); a field repeated within one
key (last wins, a repeated `value=` also warns); and the languageless
`stale=`, kept as a review flag with no stored text. A bare `[group]` header
reloads as an empty key (below).

Canonicalized by the writer (`IniWriter`): line wrapping (from any point with
120 or more characters left on its value line, the writer folds at the last
word break the run of non-space through its 80th character offers — before
non-word characters a word follows, never after a `\` or `'` or inside a
surrogate pair — and goes on from the fold, or from the next character when
the run offers none; one forward scan, so a long URL costs what a sentence does),
escaping (`__`, `''`, leading-whitespace `_` marker), newline continuations,
field order (a plural form follows its plain value, the forms in CLDR's order:
zero, one, two, few, many, other), and block headers — a block extending the
last full header is written as one dot-relative `[.suffix]`; an
`ICutStrategy` decides where extra full-header cuts go. The default (`GroupCuts`) writes a bare `[group]` header at a keyless
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
  descendant key; drag-and-drop moves subtrees. A new or renamed node's name
  is one segment of a key's name (`WordsParser.IsKeySegment`), the check the
  runtime skips a block by, so nothing Wordsmith writes is skipped. A group node can gain key data
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
  why they will not format — formatted as the default preview formats it,
  plural selectors included; its edits land in the key as they are made, and
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
endonym may not. A code is checked by the runtime's grammar (its *Language
codes*): `ceb`, `es-419` and `zh-Hans-CN` all pass. It is saved cased by kind,
so `en-us` beside `en-US` is a code taken twice. On OK
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
first one it was read with (the system's for an imported file), and its own
encoding: UTF-8, UTF-16 or UTF-32 by its BOM, BOM kept, and UTF-8 without one
when it had none (or was imported). So a file round-trips byte for byte
whichever its checkout or editor gave it; merge and split write with their
source's. The encoder refuses what it can't encode, such as a lone surrogate,
rather than writing a replacement character. A file that cannot be written,
for either reason, is reported, left as it was on disk, and the others still
save. Merge and split check the tree covers the file's keys, as Save does. The editor tracks dirtiness; the window title names the
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
that one run. Any of them is read as far as it is a code (`ca-ES-valencia`
is `ca-ES`) and falls back through its shorter codes to English; one that
starts with no code at all starts in English. A language never stops the
editor from starting. A submenu under View lists the languages the file labels;
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
reload-in-place. The store takes copies of the document's keys, so the
caller's document stays as read and loading it twice gives two files their
own. `WordsSession.Import` is `Read` then `Load` at the native path
the importer names: the pick with the ini extension for a one-file format, the
stem's — `Strings.ini` beside `Strings.*.resx` — for one file per culture.
Importing an ini is loading it, line break and encoding kept.
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
freeform comments; settings references; plural forms. Values every format
keeps, so they are not a feature. The loss preview (`format.Loses(source)`) is what *this*
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
So are plural forms, which neither has a slot for: to a translation tool a
form is no unit of its own, so both drop them, counted, with a gripe. A
foreign name that is no key's (the runtime spec's *Key names*) is made one,
segment by segment, with a gripe: what is no letter, digit, `_` or `-` becomes
`_`, and an empty or dash-led segment gains one, so WinForms' `$this.Text`
loads as `_this.Text`. A name that is already a key's, a `$constant` included,
loads as it is. Two names made one key are told apart with a number (`a_`,
`a_-2`) and a gripe, never one overwriting the other.

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
the node and key operations, then the flags), View (the filters, the previews, Find, then each
pane's plural form, then the translation language and Wordsmith's own, all
four submenus), Tools (Languages,
Project Settings, Test Parameters) — with an access key on each menu and
gesture text on each entry, so everything is reachable by name and by
keyboard, not only by icon. The toolbars are toolbar controls populated from
the same commands and carry only what is convenient: the node operations
under the tree, the key operations under the baseline pane, the filters as
a vertical toolbar in the popup beside the search box, Back and Forward on
the box's other side, Rename at the right of the selected node's name, each pane's header (its plural form, Test Parameters, the key's
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
languages, each pane's plural form) is a submenu of ticked rows in the menu
and a combo box on a toolbar, or a popup button of ticked rows (the forms), its
options mirrored from the owner's; an option may be greyed, marked or out of
reach, and a choice that does not apply greys whole. The window binds the table's
gestures once, from the rows that carry one, and the mouse buttons a row
names beside them; Find is a routed command and carries Ctrl+F of its own. The captions are looked up by literal key in the
table, so the editor's own words name every one of them.

**Disabled, not hidden.** A toolbar button whose command does not apply is
greyed, not removed, and a greyed button still says what it would do; the
pane headers keep their gripe badges, which act on what they sit beside.
Buttons and toggles share one template in the icon's colours — orange under
the mouse, a blue frame around a toggle that is on — rather than the theme's
tool button and switch, so the two kinds match in size and weight; the
filter popup's button and the forms' are the same toggle, and a click that
closes either popup on its own button does not reopen it (`PopupToggle`).
Every toolbar gives back the room
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
  comment), the field (`DocumentField`), for a value the plural form typed
  into (Plural forms), and its text before and after, nothing more, so each
  field and each form undoes on its own.
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
field, the stale toggle) switches the translation language to it, and a
value typed into a plural form picks that form in its pane, so what is
about to change is in view; the next Ctrl+Z undoes it. A change already
in view — its node selected, in its language and form — undoes at once, and so does
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

## Plural forms

The runtime spec's *Plural forms* gives a key a form per CLDR category beside
its plain value (`value-mt#few=Kelmiet`). The editor shows one form at a time in
each value box, and a selector in each pane picks which. The runtime,
authoring's round trip and the editor are built, in that order (Order, below).

**The selector.** The baseline pane's header toolbar and the translation
pane's each lead with one: a popup button (`Counter`) holding the forms as ticked
rows, the way the filter popup holds the filters. A combo box would say which
form is showing at a glance, but it costs a narrow header a box as wide as
"other", several buttons' worth. The popup button says it where it matters
instead: on the plain value it is bare. On any other form it wears the
category as a badge, the way the filter button wears its count, and the
pane's title names it ("Translation · few"), so typing into `few` never looks
like typing into the plain value. Each row reads the category and the numbers
it takes in that language ("few: 0, 3–10, 103–110…"), since the names alone
say little to a translator. A dot marks the forms that have words, and a form
the badge counts as missing reads bold, as a key wanting words does in the
tree, so the rows say what the badge is about. The plain row is captioned as
the language's `one`. The selector is a `ChoiceItem` like
the two languages: a submenu of ticked rows in the View menu, and the popup on
the toolbar. That is a second toolbar template for a choice, not a new kind of
row. The popup stays open while rows are picked, as the filters' does, and
closes on a click anywhere else.

**What is offered.** Every row, every time: the plain value and CLDR's other
five, in CLDR's order, so the list keeps its shape from language to language
and `few` is always where a translator last saw it. The baseline's rows are
the default's language's (English while the file declares none), the
translation's the selected language's. A row the language counts by is live,
with its numbers, while fractions wait; an optional one (the runtime spec's
*Optional categories*) is live too, and says what it reads while empty ("two:
2 — reads few", Maltese `other` the plain value). A row the language does not count by is greyed: English's
`zero`, `two`, `few` and `many`, Polish's `other`, which only fractions take,
and every row but the plain value in Japanese. A greyed row that has words
anyway, written by hand and griped on load, wears its dot and stays pickable, so
it can be read and cleared; an empty greyed row cannot be picked, so nothing
new is written where no count reads it. A language with a single category and
no stray forms has nothing to pick, and its button greys.

**Which keys count.** A key is plural when it has a form in any language, the
default included. The baseline's selector is how a key becomes plural, so it
is always live. The translation's is live on a plural key and greys on any
other, showing the plain value: whether a key counts is the developer's call,
made where the developer writes `{0#word}` or `Words.Known[key, n]`. Each pane
keeps its pick from key to key, which suits a run through every `few`, but
returns to the plain value on a key that is not plural, and where the picked
row is greyed and empty. Otherwise a new key's default could be typed into
`other` by mistake. The check runs when the key or the language changes, not
on an edit, so a form just cleared stays picked, to be typed again or undone.

**The two follow.** Translating a form means reading the source for the same
numbers, and two languages' categories rarely line up: Maltese `few` (3–10)
is English `other`, and so is Maltese `other` (20 up). Picking a form in the
translation pane moves the baseline's selector to the form that the pick's
first number takes in the default's language. The baseline's own pick moves
nothing else.

**Empty forms.** An empty form's box hints, greyed, what that count reads as
until the form has words of its own, the way the translation box hints the
default: for an optional category the form it reads instead, else the `other`
form, else the plain value, all the language's own once it has words for the
key (the runtime spec's *Which form a count reads*). A language with no words
of its own for the key hints a form only where the default speaks it, as its
plain value does. Clearing a form removes it, and a key whose last form goes
is plural no more.

**Badges.** A plural key misses words in a language that has its own value
for the key and no form for a category it counts by, on top of the empty
value (Badges). Optional categories never count, since they read another
form: Maltese leaves out `two`, `many` and `other`, and the exact millions of French,
Italian and Spanish read `other` without a gap. A language with no words of
its own for the key misses its value, as ever, and the default's forms stand
in until it has some; a language the default speaks misses nothing, as for
the value. The default misses a form too: a plural key wants each category
the default's language counts by, bar the optional ones, in every language's
view, since a key a translation made plural still needs its source's plural.
Which categories a language counts by is the runtime's table, which follows
current CLDR. The rule itself is Authoring's `MissingWords`, which the tree's
badges and the command line's `list --missing` both read.

**Everything else.** The previews render the selected form. A value that
selects (`{0#word}`) previews with the Test Parameters samples, so changing
the sample changes the form spliced in, in the previews and in the dialog's
own result alike. A `FieldEdit` carries the form beside
its field, so typing into a form undoes as any field does (Undo: Fields). The
search reads every form. A recode, a split or a merge carries an entry's forms
with it. Stale stays one per entry and one per key, as do the notes. XLIFF and
resx have no slot for a form, so an export lists the forms as lost
(`WordsFeatures.PluralForms`), the way it lists the default's language.

**Tests.** A plural key's selectors list all six rows: English `other` live
and its `zero`, `two`, `few` and `many` greyed; Maltese `two`, `few`, `many`
and `other` live, each with its numbers, `two` and `many` saying what they
read, and `other` the plain value. The badge shows a missing Maltese `few`,
bold in the rows, not a missing `two`, `many` or `other`,
and none where the default speaks the language. Typing into a form writes
`value-mt#few` and undoes. Picking Maltese `few` moves the baseline to
`other`. A key that is not plural greys the translation's selector and shows
the plain value. A form the language does not use stays reachable, with its
dot and a gripe, and an empty greyed row cannot be picked. Test Parameters
selects as the preview does.

**Order.** The runtime comes first, proven in the samples: the grammar, the
CLDR integer table, the digest, the `{n#key}` selector and the count indexer,
then a key's forms kept to the level of its words, and the optional categories.
Authoring's model and round trip come next: `WordsKey.Forms` and
`WordsEntry.Forms` hold each form's text by category, and `IniWriter` writes
each form after its plain value. The reader keeps every CLDR category it meets
and gripes about one a runtime never reads: `#one`, a category the language
does not count by, any form in a language with one; a word that is no
category is dropped, with a gripe (Round-trip guarantees). Copies, a recode, a
split and a merge carry the forms; the preview providers answer `key#few` as
the runtime flattens it, so a dictionary over them selects; resx and XLIFF
list the forms as lost. The editor follows, as above. All three are built,
each its own commit. The command line came after the authoring step, so it
edits forms like any other field from its first build.

## A command line for tools

A build step, a script or a coding agent that needs to change one entry
should not have to open the editor, and should not have to hand-edit a
format with continuation and escaping rules either. Saving through Wordsmith
normalizes the whole file (Round-trip guarantees), which is fine for a file
Wordsmith already wrote and noisy for one written by hand. `words`, the
command line, changes the entry it is asked to change and leaves every other
byte alone.

**Calls.** One file per call; a field is named as in the file (`value`,
`value-fr`, `value-mt#few`, `context-fr`, `comment-fr`, `stale-fr`,
`param-count`), plural forms included (*Plural forms*, above); a value is an
argument, or `-` for stdin with its last line break dropped, so a multi-line
value needs no shell quoting:

- `get <file> <key> [field]` prints a field's value, unescaped, or the whole
  block as Save writes it, under a full header, when no field is named.
- `set <file> <key> <field> <value>` sets one field, adding the key, or the
  field, where it is missing. `--stale [text]` marks the language's entry
  stale with it (the default's mark keeps no words), so the review filter
  surfaces a machine-written value. Options go anywhere after the call:
  `--stale` takes the next argument as its words only once the value is in,
  and `--stale=text` always does.
- `remove <file> <key> [field]` drops one field, every declaration of it, or
  the whole key.
- `list <file> [prefix]` prints the keys in the order their blocks first
  appear, leaving out a bare group header as the editor drops it; the prefix
  is plain text, so `menu.` lists a group. `--missing xx` lists only those
  where `xx` misses its words, by the badges' rule (*Plural forms*, Badges): no
  words, or on a plural key a form the language requires. A language the file
  does not declare, or one its default speaks, misses nothing, and a line on
  stderr says which.
- `--version` and `--help`.

A key is the file's own, without the session's file-label prefix, and a key's
name (the runtime spec's *Key names*): `set` writes no other, exit 2, though
`get` and `remove` reach a block the file already names otherwise, so it can
be read and cleared. `remove` refuses a constant whose header bases
`[.child]` headers, which no constant can: its bare header would still be the
constant, and without one the children re-base. Values go
to stdout and gripes to stderr: the reader's gripes the edit adds (a field in
an undeclared language, a form the language never reads), and a note when a
field declared twice is made one. The exit code is 0 for done, 1 when the
key or field is not there, 2 for a bad call, a file that does not parse or is
neither UTF-8 nor UTF-16 text, or a refused edit. Redirected streams speak
UTF-8 with `\n`; a console keeps its own.

**Surgical edits.** `IniPatcher` reads the file's bytes (UTF-8 with or
without a BOM, UTF-16 with one) and parses them, noting where each header and
field sits. The one field's lines (its declaration and its continuations) are
replaced with what `IniWriter` writes for that one pair: escaping and folding
as Save would, in the file's own line ending; every other line keeps its own
break, and a file that ended without one still does. A field already holding
the text is not touched. A field declared more than once keeps its first
place and loses the others. A new field goes after the last field of its
block, or under its header when it has none. A new key goes after the end of
the header chain holding its nearest sibling — the key sharing the most
leading segments, the last of them — as a full header: after the chain's last
header or field line, so the comments above the next block stay with it,
since inserting one between a base and its `[.child]` headers would re-base
them. With no sibling it goes after the last chain. A removed key whose
header bases `[.child]` headers keeps that header bare, which reloads as a
group (the tradeoff the writer already makes); a removal leaves no doubled
blank line, and the comments above a removed block stand where they are, as
in the tree. After the edit the result is parsed again and compared with the
model before it, the asked-for change applied: every key's fields, the
language table, the settings references and the comments in order. Anything
else refuses the write, naming what would have changed. The file is written
to a temporary sibling and moved over the original only once that passes; a
link is followed to its file, and a Unix file keeps its mode.

**Where it lives.** The patcher and `WordsField` — a field's name, parsed,
read and written on the model — are in Authoring, tested headless. The parser
reports where it is through `IWordsParserConsumer.VisitLine`, a member with a
default body, so existing consumers do not change. The command line itself,
`WordsCli`, is a thin console project over it on plain `net10.0`, with
invariant globalization: it runs wherever .NET does, references Authoring and
Core and never the editor, and Authoring stays free of anything Windows-only.
The unused argument parser from the original import is gone.

**How it ships.** With the editor, at its version (`WordsmithVersion`): each
`editor/` release carries Wordsmith for Windows and the command line for
Windows, Linux and macOS (x64, and Arm64 for macOS), each a self-contained,
compressed single file, so nothing needs .NET installed. They are built and
packed on a Linux runner: Windows's as a zip, the others as `.tar.gz`, which
keeps the executable bit a zip made on Windows would lose. The SDK signs the
macOS builds ad hoc, which Apple Silicon requires to run them at all; they
are not notarized, so macOS quarantines a download until it is cleared
(`xattr -d com.apple.quarantine`), and the release notes say so. They are
trimmed, from about 37 MB to 11. Core's reflection — enum descriptions, the
markdown constants read from JSON, named format arguments read off an object
— is nowhere the command line reaches, so the trimmer keeps none of it and
warns about nothing; every platform's build is the same code as the Windows
one, which runs every call trimmed as it does untrimmed. Trimming is the
command line's own setting: passed on the command line it would reach Core's
build too, whose trim analyzer flags those patterns whether reached or not.

**The agent skill.** The packaged `SKILL.md` tells agents the tools exist and
where to get them: Wordsmith for a person editing on Windows, the command line
for an agent or a script on any platform, both from the editor's GitHub
Releases. An agent checks whether the command line is installed (`words
--version`), uses it for any change to a `words.ini` when it is, marks the
translations it writes stale, and edits by hand, keeping the format's
continuation and escaping rules, only when it is not.

**Tests.** Headless: a changed field replaces its lines and nothing else; a
new field lands after its block's last, and a group's under its header with
the chain intact; a new key lands after its sibling's chain as a full header,
a lone one after the last block and before the trailer, and one in a file with
no blocks at its end; the pair is written as the writer writes it, continued
and folded, and reads back; a BOM, CRLF and a missing final break survive,
each line of a mixed file keeps its own break, and UTF-16 stays UTF-16; text
that is not UTF-8 does not open; the text a field already holds leaves its
hand-written lines; a field declared twice keeps its first place; only the
gripes an edit adds are reported; an edit that would spill into a continued
last line is refused and changes nothing; a removal drops a field with its
continuations, a block with one blank line, the last block with none left
behind, a reopened key everywhere, and keeps bare a header that bases
children; the parser numbers its visits. The command, in process: each call's
output and exit code, a dash read from stdin, `--stale` with words, without
and before the value, gripes passed on, a refused edit leaving the file, and
`list` with a prefix and `--missing` by the badges' rule.

---

# Planned upgrades

Not built yet. Each section here is the shape the feature takes when it is.
The next release carries *Plural forms* and *A command line for tools*, both
built above.

## Save as a patch

Undecided: whether Wordsmith's Save patches just the fields that changed, the
way the command line does, which would answer the hand-written-formatting
question for the editor too. The patcher could do it field by field today;
what it cannot do yet is move a key, which the tree does by drag.

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

## Machine translation

A button that fills a language from a translation service the user already
has. Wordsmith brings the selection, the context and the checking; the
service and its credentials are the user's.

**What goes.** The user picks a target language and a filter: the keys
missing it (`MissingWords`, as the badges count), the keys stale in it, or
everything under the selected node, combined as the tree's filter combines
them. A language not in the table yet is added through the language manager
first, and then filled. Constants never go, since they aren't translated.

**The request is a template.** The user writes the request once, and
Wordsmith fills it per key from fields:
- `{key}`, `{default}`, `{context}` and `{comment}`;
- the target's existing text, for a stale key;
- the source and target codes and names;
- the form being asked for.

The template is a command line that reads the request on stdin and answers
on stdout, so a script, a vendor's CLI, a local model or `curl` all serve.
Wordsmith keeps no integration per vendor and never holds a secret: the key
lives in the user's tool or environment. If a built-in HTTP transport is
added later, its credentials go to the Windows Credential Manager, never to
`config.ini` (plain text) or anything beside the words files.

**The answer has to map back.** Values can hold line breaks, so a
line-per-key answer cannot be trusted. Undecided between:
- one request per key: robust, but slow, and the service sees no
  neighbouring context;
- a batch whose lines carry the key (`key<TAB>text`, with the value's
  escapes);
- a JSON object keyed by key.

**Nothing is written unchecked.** Each answer's placeholders are compared
with the default's: `{0}`, `{Name}`, `{>ref}`, `{$const}`, `{n#key}`, code
spans and markdown links. An answer that adds, drops or changes one is
flagged and not applied; services routinely translate inside braces. A key
with plural forms asks for the target's categories (`PluralRules.Categories`),
not just the plain value, and an answer missing a form the language needs is
flagged the same way.

**It lands for review.** The answers open in a preview grid (key, default,
proposed text, flags), where rows can be edited, unticked or retried. Apply
writes the ticked rows with `stale-xx=machine <date>`, so the stale filter
surfaces them for a translator to review, as it does any stale translation.
The batch is one undo entry, as any action is (*Undo*).

**Privacy.** The first use says that the app's strings go to the service
the template names, and the user confirms once per template.

**Where it lives.** Authoring does the work, tested headless: selecting the
keys, rendering a request, running the command behind a seam the tests can
fake, parsing the answer and checking it. The editor's view model holds the
intent (filter, template, target) and shows the grid. The pieces exist on
the command line already: `words list --missing`, a script, then
`words set --stale … -` per key is the same loop without the grid, and is
the way to try a service before the button exists.
