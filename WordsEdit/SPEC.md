# Wordsmith Editor — what the tool is supposed to do

A desktop editor for `words.ini` localization dictionaries. Two people use it:
the **developer**, who creates keys, writes default text, and annotates intent;
and the **translator**, who receives a file, works through what changed, and
sends it back. Everything the tool does serves that round trip.

This spec fixes the behavior; layout and controls are the implementation's to
choose. Everything up to *Planned upgrades* describes what the editor does
today; that last part describes what it does not do yet. `words`, the command
line that ships beside it, has its own ([../WordsCli/SPEC.md](../WordsCli/SPEC.md)).

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
  Save and Reset clear it (`MarkClean()`) — and the history, which puts back
  the dirtiness it recorded (`PutBack`): Undo and Redo, and a keystroke typing
  its run back to where it started (Undo). Nowhere else assigns the flag, and
  the compiler holds it: its setter is private.
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
  manager, never a copy of the endonym. The labels are each file's own
  (`WordsFile.Labels`): a library beside its host keeps its `!`, and one
  file's exonym is never written into another. A manager edits one file's
  table (Languages), so a host's relabel never strips a library's `!`, nor
  does a library's put the host's language out of sight. Lists name a language by its exonym,
  or by its endonym without one. An import names languages by their culture,
  with the English names only where the default is English or undeclared.
- The **default's language**, from a keyless `value=!xx` leading that table
  (the runtime spec's *The default's language*): what the default is written
  in. Where the default speaks the selected language, its own code or one
  that falls back to it (`en` for `en-AU`, `zh-Hant` for `zh-Hant-TW`), an
  empty entry falls back to the default and misses nothing (Badges). A file without the line says nothing.
  `value=xx` without the `!` loads as the same declaration with a gripe, and
  saves with the `!`.
- A **key tree**: dotted block keys (`view.section.key`), each held by its
  file under the name the file writes, so two files can hold one key and no
  key carries its file's name (*Keys without the file's name*). Files are
  identified by path. `$keys` are constants (no translations). A key's name
  is the runtime's grammar (its spec's *Key names*): segments of any script's
  letters and digits, `_` and `-`, joined by dots and in NFC, and a constant
  one segment. A block the
  file names otherwise loads all the same, with a gripe that a runtime skips
  it, and saves back as it was; renaming it is the fix. A key carries:
  default value, context (programmer → translator), comment (translator-facing),
  format parameters' definitions (`param-x=type:Description`, Parameters), a
  needs-review flag, and a **banner** (the freeform `;` comment run above its
  header).
- A file-level **preamble** (comments above the language labels) and
  **trailer** (comments after the last block).
- Per language, each key carries: value, context, comment, and an optional
  **stale** timestamp meaning "the default changed after this translation".

A **library file** declares its languages the `!Label` way — present but
unlisted. Opened alongside a main file in the host app, its extra languages
stay off the app's menu; opened solo in the editor, its `!` labels populate
the editor's language list so the file is workable on its own. Beside its
host, the editor's list names a language as a file lists it where one does,
so the host's languages show however a library hides them. Every file
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
  languages are intentional; and an orange book beside it when a host lists
  a language the library has no words for — neither its code, nor one it
  falls back to, nor its default's language — a tooltip naming each with the
  hosts that list it, since an app offering it reads the library's default;
  declaring it, `!` and all, clears the flag), the file's load gripes as a count that opens
  the list, constant, needs-review, stale (in the selected language),
  overwritten-by-later-file, a parameter mismatch (in the selected language:
  the translation drops or adds a parameter beside its default's, or writes a
  brace that opens nothing, as the default may in any language, Parameters
  → Translation check); keys whose default or selected-language value is
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
- **Filters**: substring search, stale-only, needs-review-only, missing-only,
  mismatch-only — composable; ancestors of a match stay visible so the path is readable. The
  search reads what a translator searches for: a key's name, its default and
  selected-language words, the context and comments around them, and a
  comment node's text. The four toggles and the clear button are a vertical
  toolbar from the command table, in a popup beside the search box; while a
  filter narrows the tree the popup's button wears the number of hidden rows
  as a badge and the clear button clears the lot in one click; a selection
  the filter hides moves up to the nearest row still showing, unless Back or
  Forward brought it there (Navigation). The
  stale filter is per selected language
  and means stale, nothing more: this is the translator's work queue. The
  missing filter takes the empty values (file by file — see Badges), and the
  mismatch filter the keys whose translation in the selected language drops
  or adds a parameter, or that write a brace opening nothing. The needs-review filter is the programmer's work queue in reverse — the
  translator raises a hand by setting the unlocalised `stale=` flag
  (recycled as the "raise hand" action), and the programmer filters for it.
- **Structure edits**: add/rename/remove nodes and keys; renames rewrite every
  descendant key; drag-and-drop moves subtrees. A new or renamed node's name
  is one segment of a key's name (`WordsParser.IsKeySegment`), the check the
  runtime skips a block by, so nothing Wordsmith writes is skipped; nor is it a
  sibling's, compared ordinally as the runtime tells keys apart, so a joiner
  (U+200C) makes another name, though one differing in case alone is refused. A group node can gain key data
  ("add key information") and a key can exist on any node except a file.
  They are reachable from the button strip, the tree's context menu and the
  keyboard (F2 rename, Delete remove, Ctrl+Shift+S stale-all; Ctrl+N, Ctrl+O
  and Ctrl+S new, open and save, Ctrl+F the search). Removing a node that takes keys
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
- **Parameters**: below the comment, the key's parameters carry the
  conversation on as a thread of pairs (Parameters → In the pane): each
  definition the programmer's message, its placeholder, type and
  description, with the translator's input right under it; then each
  parameter the default uses and nobody defined, a faint `+ {0}` with its
  input; then the +. The inputs fill both previews as they are typed, read as
  their parameters' types in the invariant culture and formatted through
  `Format`, plural selectors and `{>reference}` and `{$constant}` tokens
  across files included, as a host app loading multiple dictionaries would,
  so the placeholders are proven before shipping. They are the session's:
  they dirty nothing and enter no undo, and are kept per key until Reset. A
  key with no thread shows only the +; a constant shows none.
- **Stale-all-languages**: one action for "I changed the default, every
  translation needs another look".
- **Stale as the default changes**: typing the default, its plain value or
  a form, marks every translation that has words and no stale mark yet stale
  with the time (`WordsKey.TranslationsToStale`), from the run's first
  keystroke. A mark already there stays: its words and its date say how long
  the translation has gone unreviewed and why, which "machine translated"
  overwritten by a date would lose. An empty translation is missing, not
  stale. The stamps are part of the typing's undo entry (Undo: Fields), and
  `words set` does the same (the command line's spec,
  [../WordsCli/SPEC.md](../WordsCli/SPEC.md)).

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
  Badges), and the stale and mismatch filters re-evaluate against it.
- Under the title, beside the stale stamp, a note names what the translation
  drops or adds beside its default's parameters (Parameters → Translation
  check).
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
  along the way — a missing `{>reference}`, a circular one, a brace that opens
  nothing (`WORDS:FORMAT`), an image that did not resolve, a link that would not
  parse, a sample that would not format —
  behind a tool button beside the preview toggle: a count when there is
  something, the list on click. The editor installs one collecting logger as
  `Words.Logger` and gives the preview parser the same one, so both channels
  land in the list.
- The Project Settings dialog edits what a dictionary names — the paths in
  its `param` slots, for the dictionary and for each language — and the two
  tables of whichever of those files is picked, with the rules' gripes shown
  as they are typed. Settings are the editor's to read and write, so it
  rewrites a settings file as two plain tables: comments in a hand-written
  one do not survive the dialog. On OK the tables are written first; one
  that will not write is told, and the dialog stays open with its edits so
  OK can be tried again, the slots unchanged until it is.

## Languages

A language manager adds, removes, relabels and reorders one file's
languages: the file the selection sits in, or the only one loaded, named in
its title ("Languages of strings"); with several loaded and nothing selected
there is no file to name, and the command is greyed. Every change is that
file's alone. A library declares what its hosts need of it and no more, and
its `!` is its own, so a host's relabel never strips it, a library's never
hides the host's language, and an addition to one file never lists a
language in another. The session's list is the union of the files' tables,
in the files' order, a language named as a file lists it where one does,
and it follows each change, an unload's too: the languages left read as the
files left name them. The manager works on a working copy: the list
shows each of the file's languages by code, labelled as the file labels it,
`!` and all, with a trash beside it and a + under it; the pane edits the
highlighted row's code and names live — checked against the rules and the
other rows, a field flagged once it has been typed in (the file's row from
the start), OK greyed while any row is wrong — and drag reorders. Nothing
reaches the file until OK; Cancel or Escape forgets it all. On OK the copy is
applied to the file: a removal (confirmed at the trash) deletes the
language's entries from the file's keys, and its settings reference
(`param-xx`) from the file, the session keeping the language while another
file declares it or a key has words in it; an addition joins the file's
table and, where the session did not know the language, gives every key an
empty entry in it; a relabelling may re-code a language, which shifts the file's keys' entries and
carries the file's label and settings reference along, a swap's through its
throwaway code too, while another file declaring the old code keeps it; the
order follows the rows. A new row that takes a removed language's code
replaces it: the old language parks on a throwaway code, the new one comes
in, and the old one goes, words and all — so even the only language can be
replaced. Whatever the table refuses is told after OK, never skipped in
silence. The order these go in is the table's to plan (`LanguageTable.Apply`):
the manager hands it the rows, each with the label it came from, and the
table makes the operations through what stands in front of it, the commit's
recorder (*Undo*, Languages), so that nothing is refused that need not be.
One row may be the default's language: a tick in the pane sets it and moves
it from the row that had it, a mark beside the code shows it in the list, and
the field for the names written in the default's language is headed by it
("Name in English"), or "English Language Name" while no row is ticked. That
field may stay blank (the file then writes no `comment-xx`); the code and the
endonym may not. A code is checked by the runtime's grammar (its *Language
codes*): `ceb`, `es-419` and `zh-Hans-CN` all pass. It is saved cased by kind,
so `en-us` beside `en-US` is a code taken twice. The tick starts on the
language the file declares its default in, and on OK the file declares the
ticked one when the tick moved, nothing when it did not — a file that
declared none gains one only then, and other files keep theirs; a recode
carries it along and a removal takes it.
The table's `Rename` can also absorb a language into one the file already
declares (where both hold a value the target's is kept, the source value is
parked in the entry's `context-xx` field where the translator can copy/paste
from it, and the entry is stale-marked so the review filter surfaces the
collision; the target keeps its label and reference); the manager never asks
for that, since no two rows may share a code — two rows swapping codes go
through a throwaway code instead. The manager's highlighted row is its own
while it is open and becomes the tree's language on OK, so browsing the list
does not re-contextualize the window behind it.
A host may list a language its library has no words for: neither its code,
nor one the code falls back to, nor its default's language. An app offering
it reads the library's default there, which may be what was meant, so the
library is flagged rather than refused (Badges), and declaring the language
in the library, `!` and all, clears it. A library whose default is written
in another language than its host's is a question for later (the runtime
SPEC's *A library written in another language*).

**Tests.** Headless, a host and its library loaded together: the manager
edits the selection's file; a host's relabel leaves the library's `!`, a
library's relabel leaves the host's language listed, in the host's file and
in the session's list, and the host's addition lists nothing in the library;
the default's language is declared only when the tick moved, and another
file's stays; unloading the host leaves the library's names and order in the
session's list. A host's addition flags the library, the tooltip naming the
language and the host (not one hiding it), until the library declares it hidden, and an undo
raises the flag again. The table's own operations — add, remove, relabel,
recode, reorder — round-trip file by file and leave every other file's
table as it was; a library's fallback or its default's language covers what
a host lists, and a host's hidden language asks nothing of it. The table's
plan: a swap and a three-way cycle go through, the words following their
language and no throwaway code left; a new row takes a removed language's
code without its words; the default is declared only when the tick moved, a
default in a language the table does not list kept; the table takes the
rows' order and says what it refused; and every operation goes through the
edits it is given.

## Merge

The translator round trip in bulk: pick a base file plus a language→file map,
and produce one merged file taking each language's entries from its source.
The merged result is written to disk and loaded into the session. Merging
requires the files to agree on their key sets. The first file ticked is the
base until another is chosen; unticking the base passes it on. Each file
offers only the languages it declares: the union's others are empty
backfill, and taking one would empty the base's words for it, so the session
refuses a source that does not declare its language. Each language is
labelled as the file it comes from labels it. Split, the
other direction, shares the dialog: one file and one of its declared
languages, written on their own — that language's entries with the defaults
for reference, its label and settings reference as the file has them — and
loaded, ready to be worked on separately and merged back.

## New

File > New (Ctrl+N) starts a file, so someone trying Wordsmith out need not
write one by hand first. It asks nothing: a new root, `anonymous-words`, joins
the tree with no path, selected, the rename box (F2's, *The tree*) open on its
name at once, so the first thing typed names it and Escape keeps the name. The root is just the
file: its name is the file's, and no key carries it. A second New before the
first is saved is `anonymous-words` too, never numbered: the two read alike until
one is renamed or saved, and Save tells them apart where it matters, refusing a
path another file has.
It is unsaved work, as an import is: nothing reaches the disk until Save, and
closing asks.

**Its text.** The editor hands Authoring its words and Authoring makes the file
(`WordsSession.New`, with a header and a language): the header as its preamble's
comment lines, `; ` before each line of it, then `value=!en` and
`value-en=English`, which make English the default's language, then the blank
line Save writes between the table and the first key, so the first Save writes
just that.
The header (`file.new-preamble`, in Wordsmith's language) is boilerplate a
familiar hand leaves in as readily as a newcomer reads it, not a tutorial: what
the file holds, that it ships in the app's assets for `WordsBuilder` to load,
that Wordsmith or `words` edits it, and a link to the Core readme for the
format. It names no file: the name is the file's to change, and a header
naming it would be wrong after the first rename, so nothing need rewrite a
comment that is the user's once it is written.

**Saving it.** The first Save of a file with no path asks where, as Save As
does, proposing its name with `.ini` (`anonymous-words.ini`, or the name it
was given). The dialog's own overwrite ask covers a file on disk. A path another
file in the session already has is refused with a message, and the dialog asks
again: two files never share a path. Cancelling leaves the file unsaved, dirty
and pathless, and a Save of several files stops there, the ones before it
saved. The file is written before it takes the path, so a write that fails is
told and leaves it pathless. Once saved it has a path like any loaded file,
and later Saves go there. Its root follows the name picked, as a loaded
file's is its file's, so a first Save under another name renames it, and no key
changes.

**Renaming a root.** A pathless file's root renames in place to any file name,
dots and all, another file's too, and the name is what its first Save proposes.
A saved file's root does not rename: its name is its file's, so Rename is greyed
on it, and F2 does nothing there. To give it another name, Save As writes it at
a new path and the root follows (*Saving*, Save As); the file at the old path
stays on disk as it was last saved, for the user to keep or delete. Wordsmith
never deletes a words file of the user's. Words files are expected to live in the user's own git
repository, where the new file shows at its next status and the old one is
removed by hand, or with `git mv` before the Save As. A root that changes its
name, whichever way, changes no key, and the undo history and Back's keep all
they hold (*Keys without the file's name*).

Why Save As and not a move: a move is a write and a delete, and the delete is
the hazard. A path that reaches the same file another way, a directory junction
or a symbolic link above it, was read as another path, so the move wrote the file
and then deleted it, losing both. Save As only writes.

Why: a location asked up front is a question before there is anything to save,
and most people expect the one asked at Save. It also removes the hazard of a
New over a path already loaded, which replaced that file's unsaved work.

**Tests.**
- New asks nothing.
- The new root is `anonymous-words`, selected, in rename, dirty, English by
  default and without gripes. Its preamble names no file, links the Core readme
  and shows as the pinned organizer.
- A second New is `anonymous-words` too.
- The name typed names it; renaming the pathless root changes no key, only what
  Save proposes, and the undo history keeps what it holds.
- A saved root offers no rename: Rename is greyed and F2 does nothing on it.
- Save As on a saved file writes it at the new path and every other file where
  it is, as Save does, the root and its keys following and the window clean,
  and leaves the file at the old path as it was on disk, unsaved changes and
  all left out of it. A cancelled dialog changes nothing, and a path another
  file in the session has is refused.
- The first Save proposes the name, and writes the starter byte for byte at the
  chosen path, the root following its name, leaving the window clean. Later
  Saves ask nothing.
- A cancelled first Save leaves it dirty and pathless, and stops a Save of
  several files there, the ones before it saved.
- A path the session already has is refused, and the dialog asks again.
- In the session: a new file has no path and is at none, cannot save until it
  has one, renames in place to any file name, another file's too, and takes a
  path no other file has, named as a file loaded from there; a saved file does
  not rename; one started with a header and a language writes them, and
  nothing else.

## Saving

Save rewrites every loaded file through `WordsSession.Save` — `IniWriter.WriteFile`
with the file's own language table, preamble and settings references, in the
order its tree node walks — and with its own line break, `\n` or `\r\n`, the
first one it was read with (the system's for a file imported from another
format), and its own encoding: UTF-8, UTF-16 or UTF-32 by its BOM, BOM kept,
and UTF-8 without one when it had none (or came from another format; an ini
picked through Import is a load, and keeps both). So a file round-trips byte
for byte whichever its checkout or editor gave it; merge and split write with
their source's. The encoder refuses what it can't encode, such as a lone surrogate,
rather than writing a replacement character. A file that cannot be written,
for either reason, is reported, left as it was on disk, and the others still
save. Each file is written to a temporary sibling and moved over the original,
so a failure partway leaves it whole; what lands is a new file, so a Windows
hidden attribute does not carry over, a hard link to the old file keeps the
old text, and a symbolic link is replaced by the file rather than followed (the
command line follows it). Merge and split check the tree covers the file's keys, as Save does. The editor tracks dirtiness; the window title names the
loaded files and stars while dirty, and closing with unsaved changes prompts.
Reset returns to the empty session (one default `en` language).

**Save As** saves as Save does, every loaded file, but writes the selected
node's file at a path the dialog picks, proposing its own, as the first Save of
a new file does (*New*, Saving it): the dialog's overwrite ask covers a file on
disk, a path another file in the session has is refused and the dialog asks
again, and the file is written before it takes the path, so a write that fails
is told and leaves it where it was. The other files are written where they are,
since unsaved work is the session's, as the window's dirty mark and the undo
history's saved point are, so Save As leaves the window clean as Save does, and
a file that fails is told as Save tells it. Once written, the selected file is
at the new path, its root follows the name, and later Saves go there. The file
at the old path is left as it was on disk; Save As deletes no file of the
user's. It is a saved file's only rename
(*New*, Renaming a root). Its own path is a plain Save. It has no gesture yet:
Ctrl+Shift+S is Stale All Languages.

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
the command line's edits to it included, name exactly the keys the source asks for, and open in Wordsmith to add a
language, translate, relaunch in it. What stays hard-coded is file syntax and
key caps, not words: `[images]`, `shellexec`, `F2`.
[The Core skill](../Localization-Core/SKILL.md) is the how-to.

**The window.** The same config file remembers the main window as it last
closed — its normal size, and whether it was maximized — and the next run
opens it so, cut to the screen's work area and no smaller than its minimum
(`EditorConfig.Window`, a `WindowPlace`). A window closed from the taskbar
remembers the state it would come back to, so maximized, then minimized, then
closed opens maximized. A close the save question cancels
is no close; a language restart remembers the window before the new editor
reads it, and tells the new editor its language on the command line too.
Where the window sits is still the system's choice. A setting changes its own
line of the config and leaves the rest, comments included; a config that
will not read or write (locked, read-only) costs the settings, never a close
or a restart.

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
flows through the exact pipeline the ini loader does: each file its own keys,
two of one name kept apart, empty-key dropping, language backfill,
reload-in-place. The store takes copies of the document's keys, so the
caller's document stays as read and loading it twice gives two files their
own. `WordsSession.Import` is `Read` then `Load` at the native path the
importer names: the pick with the ini extension for a one-file format, the
stem's — `Strings.ini` beside `Strings.*.resx` — for one file per culture.
Importing an ini is loading it, line break and encoding kept; a file from
another format has neither to keep, so it takes the system's line break and
UTF-8 without a BOM. Importers inherit the whole of loading for free, and are
tested the same way.
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
way in, the attribute declares the default's language: the first file of the
set to declare one names it, and a file declaring another keeps its sources
out of the default, with a gripe naming it, while its targets still load, so
no default is ever text in a language it does not say. A unit only that file
gives has no default, and shows as missing it. The default's
language is a feature like any other: resx has no slot for it and says so.
So are plural forms, which neither has a slot for: to a translation tool a
form is no unit of its own, so both drop them, counted, with a gripe. A
foreign name that is no key's (the runtime spec's *Key names*) is made one,
segment by segment, in NFC, with a gripe: what is no letter, digit, `_` or `-`
becomes `_`, and an empty segment, or one led by a dash or a mark, gains one,
so WinForms' `$this.Text` loads as `_this.Text` and `हिंदी 😀` as `हिंदी__`. A name that is already a key's, a `$constant` included,
loads as it is. Two names made one key are told apart with a number (`a_`,
`a_-2`) and a gripe, never one overwriting the other, and a name met again in
another file of the set finds the key it took first. An XLIFF unit is its
`<file>`'s `original` and its `id`, since an id is only its file's, so two
originals' `id="1"` are two keys, numbered, and the same unit in each
language's file is one, whatever each file names it. Its name is the
`resname` any file of the set gives it, the resource's own, read ahead of the
units so a file that gives none still finds it, and its `id` only when none
does; a unit named two ways keeps the first name, with a gripe. A unit met
twice for one language keeps the first, with a gripe, whatever its name the
second time. A gripe names a unit by its name, id and original, which is for a
reader only: two units are never told apart by how a gripe would name them.
A note's channel is its own `annotates`: one on the target in a file with no
`target-language` is ignored with the targets, with a gripe, never made the
default's. Each language's file of a set carries the source's notes again, so
a note is read once per unit and channel, however many files repeat it.
Inline codes read as the text they stand for: the native code a
`ph`, `bpt`, `ept` or `it` holds, else the `equiv-text` it or an `x`, `bx` or
`ex` carries; a `mrk` keeps the text it marks, and a `g` its text without its
codes. A code with no text to read is dropped with a gripe, and whitespace
between codes is text whether or not the unit says `xml:space`.

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
pinned — a numbered name finding its key in each file of a set, and XLIFF's
resname over its id, two originals' ids apart, a repeated unit, a unit one
file names and another doesn't, two units a gripe would name alike, a file in
another source language kept out of the default, a target's notes in a file
with no target language ignored, the source's notes read once across a set,
and the inline codes among them. Every exporter round-trips against itself — import, export, import, and
the fields the format holds are unchanged — and the native fixed point is
re-checked to prove Save still never touches a foreign writer. The editor's
tests drive Import and Export through `FakeDialogs`: the native path, the ask,
dirtiness, the plan, the loss and the overwrite confirmation.

## Menu and toolbars

A menu bar carries every command the editor has, grouped the usual way —
File (New, Open, Import, Merge, Save, Save As, Export, Reset, Exit), Edit (Undo and Redo,
the node and key operations, then the flags), View (the filters, the previews, Find, then each
pane's plural form, then the translation language and Wordsmith's own, all
four submenus), Tools (Languages,
Project Settings) — with an access key on each menu and
gesture text on each entry, so everything is reachable by name and by
keyboard, not only by icon. The toolbars are toolbar controls populated from
the same commands and carry only what is convenient: the node operations
under the tree, the key operations under the baseline pane, the filters as
a vertical toolbar in the popup beside the search box, Back and Forward on
the box's other side, Rename at the right of the selected node's name, each pane's header (its plural form, the key's
flags, its preview) and, above the translation pane, Languages beside the
translation language as a combo box.
Files in and out, Merge, Reset and Project Settings live in the menu alone,
the files with their keys (Ctrl+N, Ctrl+O, Ctrl+I, Ctrl+S, Ctrl+E).

**One row per command.** A command is defined once — its `ICommand`, its
caption, its icon and its gesture — as a `CommandItem` in the command table
(`CommandTable`, on `MainWindowViewModel`), and the menu, the toolbars and the
tree's context menu (the Edit menu again) render from the table through item
templates, so a new command is a row and nothing else. A row with a state
behind it (`ToggleItem`: a filter, a preview, a flag on the selected key) is
checkable in the menu and a toggle on a toolbar; its command flips the state
and the row reads it back, and the owner tells the row when the state, or
whether it applies, changed elsewhere, so the tick, the popup's button and
the pane's toggle agree. A flag toggle reads the tree's copy of the selected
key's flags (`SelectedNeedsReview`, `SelectedIsStale`, `SelectedIsConstant`),
which follows its badges and is told whatever changed them: a command, an
undo, or a note's typing that raises the hand. A pick among options
(`ChoiceItem`: the two languages, each pane's plural form) is a submenu of
ticked rows in the menu and a combo box on a toolbar, or a popup button of
ticked rows (the forms), its options mirrored from the owner's; an option
may be greyed, marked or out of reach, and a choice that does not apply
greys whole. The window binds the table's gestures once, from the rows that
carry one, and the mouse buttons a row names beside them; Find is a routed
command and carries Ctrl+F of its own. The captions are looked up by literal
key in the table, so the editor's own words name every one of them.

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
(`ToolBarOverflow`, set by one implicit toolbar style); turned off, a
toolbar has the theme's room back.
Back, Forward, Undo and Redo are rows like any other (Navigation, Undo).

**Tests.** Every command the view model and the tree expose is in the menu
once (the two badge commands excepted); the toolbars and the context menu
draw from the menu's rows; every caption, tooltip and option renders without
a key leaking; a key, or a mouse button, is bound once; a toggle mirrors its
state whichever way it changes and a flag toggle reads the selected key,
hearing of a flag a typed note or another command set; a choice mirrors its
owner's options and pick, and a pick of Wordsmith's language is a request;
Exit asks the window.

## Navigation

The tree is the map, and the editor remembers where the user has been on
it: a search, a filter, a context-menu jump or a click moves the selection,
and Back takes it where it was.

**A history of selections.** Every change of the selected node, edited or
merely visited, is a move in a history (`SelectionHistory`, which the tree
view model keeps as places, a file and a path in it, `NodeRef`); the same node
twice in a row is one entry.
Back and Forward step along it without pushing, like a browser's. A
selection made by hand is matched against its neighbours first: selecting
the node Back points to *is* Back — the current entry crosses to the forward
side — and selecting the node Forward points to is Forward. Walking A, B, A,
B by hand therefore does not pile up entries, and stepping onto the next
node by click rather than by the Forward button does not erase what lay
ahead. Only a selection matching neither neighbour pushes, and a push while
behind the end drops the forward run. An entry whose place no longer
resolves — the node removed or renamed, its file unloaded or reloaded — is dropped when
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
expansion. An entry finds what it changes by its file and its path there —
entries are undone in order, so every path still means what it meant — and a
comment, which shares its path with its siblings, by its parent and its place. Entries
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
  the hand again. Typing the default stamps its translations stale the same
  way (The baseline pane): the stamps go with the step that made them, so
  undoing the typing, or stepping back past that step, takes them back.
- **Commands.** Each toggle — Needs Review, Stale in the selected language,
  Constant — is a `KeyEdit` of its own (`ReviewEdit`, `StaleEdit`,
  `ConstantEdit`): the key, the flag, its value before and after; Constant
  also carries its new name and any translations it cleared. Stale All
  Languages, on the selected key, is a `KeyEdit` too (`StaleAllEdit`), the
  stamps it replaced kept with it. Adding key information is `KeyAdded`;
  removing it is `KeyRemoved`, which keeps a copy of the key, plural forms
  and all, and puts a copy back. A parameter defined, adopted, renamed, retyped or taken away in the pane is one `ParametersEdit`, the key's
  parameters before and after, made only when they differ.
- **Structure.** Adding a node or a comment is `NodeAdded`; removing one is
  `NodeRemoved`: the parent, the position, the node itself — its subtree,
  comments and all — copies of the keys beneath it, and a comment's text
  (the preamble's lives in its file, which the removal empties) — one entry,
  however much it takes. A rename and a drag are both a `Move` — the old
  and new place: file, parent, position, path and name — since a drag may or may
  not rename and a rename is a move that changes only the last segment;
  undoing one renames back through the session and the node moves back. A
  drop where the node already stood changes nothing and makes no entry; a
  file dragged among files is precedence, not content, and makes none
  either. A drop whose node or target an undo or redo took out of the tree
  while it was dragged does nothing.
- **Settings references.** The Settings dialog's change to a file's
  `param=` and `param-xx=` slots is a `FileSettingsEdit`, the slots before
  and after, made only when they differ. The tables themselves are written
  to their own files when the dialog closes, and stay outside undo.
- **Languages.** A Language Manager commit is one `LanguagesEdit`: the
  manager makes its table operations on its file through a
  `LanguageTableChange` (`ChangeLanguages`, on the main view model), which keeps what they can
  change as it stood before the commit and after it, and puts it back whole
  either way, never by replaying inverses, which can write a key's fields
  back in another order: every file's table — its codes,
  labels, settings references and default's language — the session's list,
  its order and names, and each key's entries, in the order the key writes
  its fields, copies for each code the commit touched. A recode onto a code
  the file already declares merges two languages' entries and has no tidy
  inverse; the manager never asks for one (no two
  rows share a code), but a commit that makes one clears the stack instead
  — the last resort for any document-wide action that cannot keep a
  reversible state. Undone or redone, a commit checks the panes' form picks
  again as the commit did: the same key in the same language may count by
  other rules now, and a pick they lack goes back to the plain value.

**Where it lives.** The entries and the stack are the editor's: an entry
knows where its change shows, a node by its file and its path, and moves the tree's
nodes itself. What it keeps of the document and puts back is Authoring's,
tested headless: a language commit's tables and entries
(`LanguageTableChange`), a key copied out and restored (`WordsSession.KeysUnder`,
`WordsSession.Restore`), a file's settings references (`SettingsSlots`), a key's
parameters and entries (`WordsKey.CopyParameters`, `PutBackParameters`,
`CopyEntries`, `PutBackEntries`), and a form written, an empty one being none
(`Write` on a key's or an entry's forms).

**Recording.** Fields report themselves; every other entry is made by its
command through one door (`Perform`, on the main view model): the command
makes its change and returns its entry, or nothing when it changed nothing,
and while it runs the field reports are not typing — a constant clearing its
translations is not typing. A dialog a command opens belongs to the command,
so a Settings Okay is one entry, and the dialog
dirties nothing itself: `Perform` stars the title when the entry is made.
Each entry records whether the document was dirty before its action, and
undo restores `IsDirty` to that; a save moves the marks, so an undo or redo
that leaves the saved state stars the title and one that comes back to it
clears it. A save that writes some files and not others leaves the disk in
no state the history passes through, so it moves every mark to dirty: each
undo and redo stars the title until a save gets every file out.
An entry that throws as it applies stays where it was, and the stack is
cleared, the title starred: the document may be half changed, and no entry
can be trusted to step from there. The error goes on.

**Coalescing.** Consecutive edits to the same field of the same node and
language fold into one entry, the first text before and the last after, so
undo takes back the typing, not a character. Inside the entry the typing
keeps its steps, a word or a pause apart, for an editing box to undo one at
a time (Text boxes). A different field, another entry, an undo or a redo, a
save, or moving to another node ends the run, and a run typed back to where
it started leaves no entry at all, and the title as the run found it. Its
stale stamps go with it, since the default is what they were made against
again; a hand a note raised stays up.

**Depth.** The stack is unbounded. An entry holds only what its action
changed — a field's text before and after, a removed subtree's keys — and
the boundaries below empty it, so a session's history stays small next to
its document. A cap would drop the oldest entries from the bottom of the
stack; nothing has asked for one yet.

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
then focuses its text box, a free action, with the selection where the step
left it (Text boxes) or the caret at the end, so the next keystroke lands
where the change did.

**Boundaries.** Save does not clear the stack (a saved state can still be
undone; the title stars again). Reset, Load, Import, Unload, Merge and Split
do: they change which files the document is, and an entry for other files is
no help. Each of them puts a file node into the tree or takes one out, and
that is what clears it; a file dragged among files does not. A Language
Manager commit that merges codes clears it too. A root that changes its name
(*New*), renamed in place, saved first under another name or saved as another
file (*Saving*, Save As, the old file left where it was), does not: no key
changes, and an entry names its file, not the name.

**Text boxes.** The main window's editing boxes undo as any text box does,
from the document's history. Each is a `WordsBox`: a `TextBox` with WPF's
own stack off (`IsUndoEnabled`), whose class command bindings for Undo and
Redo, which a subclass's take ahead of `TextBox`'s, answer Ctrl+Z and
Ctrl+Y from the history it is bound to (`TextHistory` on the main view
model, an `ITextHistory`). The box keeps no history of its own, so nothing
undoes twice. A typing run keeps the steps a box's own undo would: a
keystroke joins the run's last step unless a pause (`FieldEdit.Pause`, a
second and a half), a word's start (a letter typed after a space), a change
of direction (typing after deleting, or deleting after typing), a moved
caret, or a change of more than one character (a paste, a cut, a line
break, a selection typed over) comes between, and then it starts a step of
its own. Each keystroke tells the history where it found the box's
selection and where it left it (`Typed`), the history taking only the
keystroke it just folded in, so a text the document hands a box is no
keystroke. Ctrl+Z in a box steps back through the run a step at a time,
and then on into the entries before it; Ctrl+Y steps forward again. A step
taken back puts the selection where its first keystroke found it, and a
step put back where its last left it, not at the start, where setting
`Text` drops the caret. A step is part of its run: the run is still one
entry to every other caller. The Edit menu's Undo takes back what stands
of it whole, and its Redo puts back first what a box stepped back of the
latest run, then entries whole. A run stays the latest entry until its
last step is undone; typing after stepping back drops the steps stepped
back, as a new edit drops the entries undone. Each step keeps its own
dirtiness, so a save between two steps is clean there and nowhere else,
and a note's hand comes down with the step that raised it. The search box
is a plain `TextBox` and keeps WPF's own stack, as do the dialogs' boxes,
which are other windows.

**Surface.** `UndoCommand` and `RedoCommand` on `MainWindowViewModel`, with
`CanExecute` from the stack (Redo's also from a run a box stepped part way
back), as rows of the command table: Ctrl+Z and Ctrl+Y outside the editing
boxes, entries at the head of the Edit menu (and so in the tree's context
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
comes between, and typing back to the start leaves none, nor a star where
there was none; a parameter added and removed in one session is no entry;
undoing the removal of a key, or of a node above it, puts their plural forms
back; a save ends the run, and after a save that wrote one file and not
another every undo and redo stars the title, and Close asks; undoing a
declaration of the default's language drops a form pick the restored rules
lack; a drop whose node a redo took away does nothing; an entry that throws
as it applies clears the stack and stars the title; a note that raised
the hand undoes both, and undoing the clearing of a note leaves a hand that
was lowered down. Typing the default stamps the translations with words and
no mark, once a run, keeping a mark there; undoing it takes the stamps back
and redoing puts them back, a box's step back past the first step takes them
too, and a run typed back to where it started leaves no stamps. An undo out of view selects the node through a search
that hides it and switches the language, changing nothing until the second
call, which applies it, focuses the field and keeps the node in view; Back
returns from the navigation; an undo in view applies at once and focuses
the field, and so does its redo. Save keeps the stack, undoing past it
stars the title and coming back clears it; Load, Import, Unload, Split,
Merge and Reset clear it and a file reorder does not; a commit that changes
nothing records nothing, and a merging recode clears the stack. Headless, in
Authoring: a language change put back before and after gives the file's saved
text byte for byte, says when it changed nothing and when it merged, and has no
after until it is closed; a key copied out comes back as it was, the copy its
own; a key's parameters and entries, and a file's settings references, are
copied and put back; a form written empty is none. In laid-out
boxes (`TextBoxUndoTests`), an editing box answers Undo and Redo from its
history and tells it each keystroke's selections, and a plain box beside it
keeps its own; typed into one bound to a key, Ctrl+Z steps back a word at a
time with the caret put back, then reaches the entry before the run, and
Ctrl+Y walks it forward again. A pause, a change of direction, a moved
caret and a paste each start a step. The Edit menu's Undo takes the run
whole, from part way back too, and its Redo puts back what a box stepped
back. Typing after stepping back drops those steps; a save between two
steps is clean there and nowhere else; a note's hand comes down only with
the step that raised it; and only the keystroke's own text tells where it
left the selection. The box takes `TextBox`'s style.

## Plural forms

The runtime spec's *Plural forms* gives a key a form per CLDR category beside
its plain value (`value-mt#few=Kelmiet`). The editor shows one form at a time in
each value box, and a selector in each pane picks which.

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
selects (`{0#word}`) previews with the parameters' inputs, so changing the
count typed changes the form spliced in; with none typed it reads `other`.
A `FieldEdit` carries the form beside its field, so typing into a form
undoes as any field does (Undo: Fields). The search reads every form. A recode, a split or a merge carries an entry's forms
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
dot and a gripe, and an empty greyed row cannot be picked. A count typed
picks the form the previews splice in, and none reads `other`, quietly.

**Where it lives.** The grammar, the CLDR table, the digest, the `{n#key}`
selector and the count indexer are the runtime's. Authoring's
`WordsKey.Forms` and `WordsEntry.Forms` hold each form's text by category,
and `IniWriter` writes each form after its plain value. The reader keeps every
CLDR category it meets and gripes about one a runtime never reads: `#one`, a
category the language does not count by, any form in a language with one; a
word that is no category is dropped, with a gripe (Round-trip guarantees).
Copies, a recode, a split and a merge carry the forms; the preview providers
answer `key#few` as the runtime flattens it, so a dictionary over them
selects; resx and XLIFF list the forms as lost. The command line edits a form
like any other field.

## Parameters

A parameter is what the code fills in: `{0}`, `{Name}`, or the count a
selector reads, the `1` in `{1#N}`. The programmer says what each one is, in
the file; the translator tries values under it to see their words come out
right; and every translation is held to what its default uses.

**In the file.** `param-x=type:Description`, the definition, and the
programmer's alone: `x` is the parameter as the text names it (`param-0`,
`param-Name`, `param-1` for `{1#N}`), the type is what the code passes (Types,
below), and the description says what it is, for the translator:
`param-0=int:the files deleted`. The type is optional, `str` without one, so
`param-0=the file` (the field's first intention) is text. The words before the
first `:` are a type only where they name one, so `param-0=the file: its full
path` stays whole, where 1.3.0's reader took "the file" for an unknown type
and lost it; the writer writes the type wherever it is not `str`, or the
description's own first words would read as one. No value is saved: the
sample a 1.3.0 file holds (`Type:sample`) comes up as a description; its
type names (String, Integer, Double, TimeSpan,
DateTimeOffset) read as the short ones below and are written short. A value
kept in the file, if one turns out wanted, is a field of its own beside the
definition, never mixed into it again, and is an experiment for later. The
runtime skips `param-` as it always has.

**Types.** Six, in any case, each with the input that reads it:
- `str`, free text;
- `int`, a whole number;
- `real`, a number with a fraction, a `double`;
- `time`, a span, a `TimeSpan` (`1:30:00`);
- `date`, a moment, a `DateTimeOffset` (`2026-10-10 09:30`), UTC unless it
  says otherwise;
- `enum(prefix)`, one of the keys directly under the prefix:
  `param-0=enum(enums.brew):the coffee` offers `espresso`, `cappuccino`,
  `latte` and `affogato`, the members a `[Words("enums.brew.…")]` enum names,
  and not the slots beside a member, `.tooltip` and the rest, which are its
  own. The prefix is a key's name, found as a reference finds one,
  through every loaded file; a prefix with nothing under it gripes, and its
  input offers nothing.

The prefix sits in parentheses so the type stays one word before the first
`:`. Numbers, spans and moments are typed in the invariant culture, as the
file's own are, so `1.5` reads the same for every translator; the previews
format them in their pane's culture. An `enum` input describes the picked
member through the runtime's engine, from a describable built off its key,
`Describable.OfKey` (the runtime spec's *Describe without the type*). It sends what an app passing
`brew.Describe()` shows: the member's words in the pane's language, or its
name, the key's last segment, where it has none. A template's `{0:T}` reads
the member's `.tooltip`, as `Format` describes any describable it is handed
(the runtime spec's *Describe in a template*). What lives
only on the type, `[Description]`, `[Tooltip]` and the number, stays empty,
and a `[Flags]` combination is no member. The runtime's registry is one per
process, so Wordsmith cannot hold the slots each app adds side by side; the
general text needs none, and an input that reads an app's own slots waits on
solving that.

**In the pane, a conversation.** The baseline pane reads as a chat already:
the programmer's context on the left, inset from the right, the translator's
comment on the right, inset from the left. The parameters carry it on below
the comment, as a thread of pairs. Each definition is the programmer's
message, left-aligned in their bubble: its placeholder (`{0}`), its type, a
combo box (with the prefix beside it for `enum`), and its description, a `WordsBox` spell-checked in the default's
language as the context is, with a trash. Right under it, right-aligned, is
the translator's answer, the input for that parameter. The parameters found
and not defined follow, each a faint `+ {0}` with its input under it, and the
+ comes last, where the next pair would go. So the context and the comment,
the exchange the document keeps, sit where they always do whatever a key's
parameters, and the comment that raises the hand never slides out of view;
each value sits under the definition it answers, with no label to match up;
and what is saved comes first, the scratch after. A key with no definitions
and nothing found shows no thread, only the + under the comment; a constant,
which formats nothing, shows neither.

**Defined by hand, found to help.** The definitions are the contract, what
the code passes, and only the programmer adds them, because the key's text
cannot say. A key that is only ever referenced needs none: its parameters
are defined where the code formats the key that refers to it. And the code
may pass what the default never uses, a count that only Maltese counts with,
or a value some languages need and English does not, so a definition the
default does not use is no mistake. It stays as any other, the placeholder
label dimmed as a hint, with no gripe and no badge. Detection only helps.
What is found is every parameter the default uses, printed (`{0}`, `{0:N2}`,
`{Name}`) or counting (`{0#word}`), over its plain value and every form, with
references and selected forms expanded as the runtime expands them, through
every loaded file as the previews resolve them, so a `{>files}` whose words
print `{0}` uses `{0}`. Numbered ones come first, by number, `{01}` being
`{1}` as string.Format reads it, then named ones as they first appear. The
words are read in the runtime's own pieces, each brace once (the runtime
spec's *One pass for every brace*), so the finder and the app agree: `{{0}` is a
brace and text, no use; a malformed brace, `{0:{>fmt}}`, holds none; and a
constant is text, so its words bring in no parameter. A parameter found and not
defined shows faintly under the definitions, `+ {0}`, and a click makes it
one, its type guessed: `int` for a selector's count, `str` otherwise. The
+ adds the first of those, or else the lowest number no definition has, so
`{0}` and never `P0`; its placeholder is editable, for `{Count}`, its input
going with it; a name no parameter can have, or another definition's (`{01}`
is `{1}`), marks the box and is not taken.

**The inputs.** One per definition, then one per parameter found and not
defined, so a key nobody has defined still previews filled; each reads as its
type does (Types), a box for the five and a list for `enum`. What the
translator types is not the document's: an input dirties nothing and
enters no undo (each box keeps WPF's own), and the inputs are kept per key
for the session, so stepping away and back finds them; Reset and closing
drop them. While a preview's toggle is on, the inputs are converted by their
parameters' types and sent to it: the default preview formats in the
default's language and the translation preview in the selected one,
selectors picking their forms by the count, as an app's `Format` would. An
empty input previews as its placeholder
written out, so the preview still renders and shows what is left to fill; a
selector with no count reads `other`, quietly. An input its type cannot read
previews as its placeholder too, and marks its box, the error brush with the
type's complaint as the tooltip, and heads the preview's gripes, as a text
that will not format does.

**Translation check.** A translation's parameters are found the same way,
over its plain value and every form, its references expanded in its own
language. One the default uses and the translation never does is dropped:
the app's value never shows. One the translation uses that the default does
not and no definition names is extra: an app passing what the default needs
shows it marked, `#{1}#` or `#{Name}#`, with a `WORDS:FIELD` warning. So the default's use is what a translation must keep,
and the definitions widen what it may use: Maltese counting with a `{0}`
English never prints is fine where `param-0` says the code passes it. Only
use is compared, so a translation may print where the default counts (a
language whose words do not change with the count prints `{0}` and needs no
`{0#word}`), a form may leave out what another keeps (an English `one` that
reads "a file"), and a format may differ (`{0:d}` for `{0:D}`). A reference
that cannot be followed, into a file not loaded, may carry anything, so
nothing is dropped from a translation with one, and nothing is extra beside
a default with one. A translation with forms and no plain value reads the
plain value it falls back to, as the runtime does; a translation without
words is missing rather than mismatched, and a constant is checked for
nothing.

A brace that opens nothing, `{0:{>fmt}}` or a lone `{`, shows as written in the
app, with a warning (the runtime spec's *One pass for every brace*), so it is
caught where it is written. A translation's badges its key in the selected
language, as a mismatch does, and the default's in every language; the note names
each, the default's apart, and the preview's gripes name it. Wordsmith saves it
all the same, since a file it loaded must save, and a file that loads with one
says so among its load gripes, naming the key and the field. A constant's words
are text, and hold none.

A mismatch badges the key's node for the selected language, as missing words
do: orange braces, whose tooltip says what is dropped and what is extra.
Mismatch View, beside Missing View, filters for it, and a note under the
translation's title says the same, with what each costs as its tooltip. An
edit checks its own key again, and every key whose check reads another's
words, a reference or a selector in its default or its translation, the
plain value a translation's forms fall back to among them, since what they
bring in may have changed (a constant brings in text alone, and a key with
no words to check follows none); a definition added, renamed, retyped or taken away checks its key
again too. A full pass, as a language picked or an undo makes one, checks
every key. The filters follow the badges an edit changes at once: a follower
whose mismatch went leaves Mismatch View, one that gained one joins it, while
the row being edited is shown through them, as where Back arrives is
(Navigation), until the selection moves on. The rule is Authoring's, as `MissingWords` is, and so is
which keys an edit checks again (`ParameterUse.Follows`): the badges read
it, `words set` notes on stderr a translation it writes that drops or adds
one (and writes it, as its other notes do; it sees its one file, so a
reference into another is one it cannot follow) and refuses a value with a
brace that opens nothing, and machine translation's
"nothing is written unchecked" and the agent macro will go through it,
widening it there to references, code spans and links.

**Undo.** A description types and undoes as any field does (Undo: Fields), its
`FieldEdit` naming the parameter as it names a form; a definition added,
adopted, renamed, retyped or taken away is one entry, a `ParametersEdit` of
the key's definitions before and after. The inputs are no part of the
history, but they follow the names: a rename carries its input to the new
name, its undo carries it back and its redo on again, whatever it holds by
then, since the renamed definition keeps its place and the names that differ
place by place are the ones renamed.

**Elsewhere.** `WordsParameter` holds a `Description` and a
`WordsParameterType`, one of the six; the inputs are the editor's session's,
never the model's, read by `WordsOperations.ReadInputs` and formatted by
`FormatSample`. XLIFF carries a parameter, its type as an attribute, short,
and its description as the text; resx lists parameters as lost. `words set`
writes a `param-` text as given, with no `String:` before one whose first
words name no type, since the reader keeps such a text whole.

**Tests.**
- What a default finds with `{0}`, `{0:N2}`, `{Name}`, `{1#word}`,
  `{{{{2}}`, `{>ref}` whose words print `{3}`, a `word#other` that prints
  `{4}`, `{{5}`, and forms of its own: 0, 1, 3, 4, 5 and Name, in that
  order, with 1 guessed `int`; `{{{{2}}` is none, as the runtime's `Format`
  reads it, and a reference into a file not loaded finds nothing.
- A found parameter adopted becomes a definition with its guess; the + adds
  the first found and undefined, else the lowest free number; a definition
  the default does not use stays, hinted, with no gripe and no badge; a key
  with no definitions gets none of either.
- Typing a description writes `param-0=` with the type it has and undoes; a
  definition added, adopted, renamed, retyped or taken away is one entry; a
  rename's undo takes its input back to the old name (`42` on `0`, renamed
  `Count`), its redo on again, and an input typed meanwhile goes with it.
- `param-0=the file: its full path` reads as one `str` description and
  writes back as it was; `param-0=INT:` reads as an `int` without one; a
  description that starts `real:` round-trips; 1.3.0's `Integer:` reads as
  `int` and writes `int:`; `enum(enums.brew)` keeps its prefix as written.
- Each input reads its type in the invariant culture and refuses what it
  cannot; an `enum` lists the keys directly under its prefix and not their
  `.tooltip` and kin, sends the picked member's words in each pane's
  language, or its name where it has none, and a prefix with nothing under it
  gripes.
- The inputs reach both previews converted, each in its culture; an empty
  one previews as `{0}`; one its type cannot read marks its box and heads
  the gripes; typing one leaves the window clean and the stack as it was;
  they outlive a key switch and go with Reset.
- A dropped and an extra parameter badge the key for that language, filter,
  and are named; a translation printing where the default counts, or a form
  leaving out what another keeps, is no mismatch; a translation counting
  with a defined `{0}` the default never uses is fine, and an undefined
  `{2}` is extra; a reference whose translation prints `{0}` is a use of it;
  a side with a reference it cannot follow is not held to the other; `words
  set` notes a mismatch and writes it.
- A brace that opens nothing badges its key and joins Mismatch View, a
  translation's in its language and the default's in every one, the note
  naming the default's apart; the preview's gripes name it, the file saves it
  as written, and the file loaded again gripes, naming the key and the field.
- An edit to a key checks the keys whose words follow it again; a definition
  added or renamed clears an extra, and its undo brings it back. Headless, a
  key follows another through its default, a default's form, a translation's
  form, or the plain value a regional translation's forms fall back to, and
  not through an escaped brace or a constant, nor with no words to check.
- In Mismatch View, a `{0}` restored takes a follower out at once and dropped
  again brings it back, while the edited row stays until the selection moves
  on, and then goes.
- The menu's inventory has no Test Parameters, and Mismatch View sits beside
  Missing View in the menu and the filter popup.

## Keys without the file's name

Each `WordsFile` holds its own keys, in order, by the names the file writes
(`view.key`, `$constant`), and no key carries its file's name: the session
keeps no store of its own. Two files can hold the same key, a split and its
base, the files a merge reads, a library and its host, and each saves its
own. Nothing outside the editor ever read a file's name in a key: the runtime
stacks the files it loads into one dictionary, the command line patches one
file by its own names, and the writer and the exporters write each name as it
is. Whatever names a key names its file beside it:
- **The session.** Adding, removing, renaming, moving and making a constant
  take the file and a key of its own (`AddKey(file, name)` and the rest).
  `AllKeys` reads every file's in order, and `FileAt` finds a file by its path.
- **The tree.** A root carries its file (`KeyNode.File`), and a node's full
  label is its path in that file, the root's empty. The root shows the file's
  true name (`WordsFile.Name`), its file name without the extension, dots and
  all (`Strings.Designer`), never numbered: a name for display, carried by
  nothing in the document. Where two loaded files share a name, each shows in
  brackets the nearest part of its path that tells it from the others, the
  first folder, walking up from its own, where their paths differ:
  `strings (ui)` beside `strings (core)`, and `C:\a\ui\strings.ini` beside
  `C:\b\ui\strings.ini` shows `strings (a)` and `strings (b)`. A name no other
  file has shows none, nor does a file with no path yet. The session works it
  out (`ShownName`), so the tree and every list naming a file, the merge and
  export dialogs and the messages, read alike, and it follows a load, an
  unload, a rename and a Save As at once.
- **Undo and navigation.** An entry names its file and a path in it, and so
  do a `NodeRef` (a record, Back's entries too, `SelectionHistory<T>`) and a
  `Place`. The file is the session's object: a file in or out empties the undo
  history (*Undo*, Boundaries), so no entry outlives its file, and Back drops
  an entry whose file went as it reaches it (*Navigation*). A file reloaded is
  a new one, so Back forgets it, as closing it unsaved and loading it again
  would.
- **Previews and checks.** The preview provider stacks the files' stores in
  tree order, the later winning, as `WordsBuilder` stacks the files it loads
  (`Provider(files)`), and puts the selected key above them
  (`Provider(files, shown)`), so a later file's key of its name never stands
  in for it. A reference is a key's name as written, and reads as the runtime
  reads it, with no prefix to probe for. The parameter and translation checks,
  the `enum` input's prefix and a key's followers look through the same stack.
- **Writing.** `KeyTree` builds a file's tree from the file alone, its root
  labelled by nothing and each node by its path. The writer, the exporters,
  merge and split walk that tree and write each key's name as it is, with
  nothing to strip, and Save refuses a tree that is another file's, or whose
  root is labelled, before a byte lands.

So a root's name is its file's alone. Renaming one in place, or saving it
first or again under another name, changes no key and keeps the undo
history and Back's (*New*), and two files of one name differ in the tree and
nowhere else. `Rename` renames a file with no path to any file name, another
file's too, and refuses a saved one, whose name is its path's; `MoveTo` names
the file as one loaded from there.

A file not saved yet has no path to tell it by, so *New* never numbers: a
second New is `anonymous-words` too, and two unsaved roots of one name read
alike until one is renamed or saved. That is no harm, since nothing tells files
apart by name: Save does where it matters, refusing a path another file has
(*New*, Saving it), and once saved each shows the folder that tells it apart.

**Tests.** Two files holding the same key stay apart, each saving its own; a
root renamed in place, or saved first or again under another name, keeps every
key, the undo history and Back's; two `strings.ini` in sibling folders show
`strings (ui)` and `strings (core)`, two whose folders share a name show the
nearest one that differs, a lone one shows its name alone, a name with dots
shows them, and unloading one takes the brackets off the other; a second New
before the first is saved is `anonymous-words` as well, and saving it to the
first's path is refused; a preview reads a bare reference through the files in
tree order, the later winning, and the selected key above them all; a tree
labelled by its paths writes, and another file's tree or one whose root is
labelled is refused; a split, a merge, an export and every round trip write as
before, byte for byte; and the tests name a key by its file and its own name.

---

# Planned upgrades

Not built yet. Each section here is the shape the feature takes when it is.

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

## Machine translation

A loop that fills a language from a translation service the user already
has. Wordsmith and the command line bring the selection, the context and the
checking; the user's own tool does the translating. The two meet in one JSON
document: the command line writes it for the keys a language needs, a
translator fills it in, and the command line reads it back into the file.
Wordsmith's Tools menu runs the same loop with a review grid in the middle.
Wordsmith keeps no integration per vendor and never holds a secret: a key
lives in the user's tool or its environment.

**What goes.** A target language and a selection, as the tree's filters make
one: the keys missing it (`MissingWords`, as the badges count), the keys
stale in it, or both, any key either takes, under a prefix or the selected
node. A language not in the table yet is added first, through the language
manager or a `words set` of its label. Constants never go, since they aren't
translated.

**The request is a document.** One JSON object that explains itself, so a
translator needs nothing else:

```json
{
  "words": "translation/1",
  "instructions": "Translate each item's default from English into Italian…",
  "brief": "Informal: tu, not Lei. Wordsmith stays Wordsmith.",
  "file": "words.ini",
  "source": { "code": "en", "name": "English", "forms": { "value": "1", "value#other": "0, 2…" } },
  "target": { "code": "it", "name": "Italian", "forms": { "value": "1", "value#other": "0, 2…" } },
  "items": [
    {
      "key": "files.saved",
      "default": { "value": "{0} file saved", "value#other": "{0} files saved" },
      "context": "The status line after Save.",
      "parameters": { "0": { "type": "int", "description": "the files saved" } },
      "translation": { "value": null, "value#other": null }
    }
  ]
}
```

`words` names the document and its version, and a reader refuses another.
`instructions` is the brief every translator gets, in English as the field
names are: fill each null under `translation` with the item's default in the
target language; keep every `{…}` token and code span as it is, and each link
target unless the page has its own in the target language, and the line breaks; read `context`, `comment` and `parameters` for where the
text shows and what each token becomes; improve a `current` translation
rather than start over; return the whole document as JSON and change nothing
else; leave a null that cannot be filled. `brief` is the user's own, for the
tone and the terms. A slot is named as in the file less the language, `value`
for the plain value and `value#other` for a form, and is written back as
`value-it#other`. A key's slots are the plain value and, on a plural key,
every form the target counts by and does not leave optional
(`MissingWords.Requires`); a stale entry asks for all of them, any other for
those without words, so a plural key whose plain value is translated asks
for its forms alone. `source.forms` and `target.forms` say which counts read
each slot, as the form selector's rows do (*Plural forms*). `default` holds
the default's words and forms; `context`, `comment` and `parameters` are
there when the key has them, and `current` holds the target's words where it
has some. A key whose default has no words, and a constant, ask nothing.
Text is a value's own, unescaped, as `words get` prints it: no doubled `_` or
`'`, no continuation, real line breaks.

**The answer is the same document**, its nulls filled. Only `target.code`
and each item's `key`, `default`, `current` and `translation` are read back,
so a script may drop the rest. It is read from its first `{` to its last
`}`, so a model's code fence or a sentence around it does no harm, and
comments and trailing commas are let by. A null left, or blank text, is a
slot not translated, and no error. Broken Unicode, a lone surrogate JSON
escapes as `\ud800`, is no text: the answer is refused, naming the item. A document is one batch: the command line takes a
limit, the editor a batch size (50 keys by default), so an answer fits a
model's output and a failure costs one batch.

**Which service.** The document is built for a language model first. It
carries its own instructions, so `claude -p`, `llm`, `ollama run` or a chat
window it is pasted into answers it whole, plural forms and placeholders
included; an agent with the command line at hand can also read the file's
other translations for the terms they use. A machine translation service,
DeepL, Google Cloud Translation, Azure AI Translator or Amazon Translate,
translates strings rather than documents: a short script maps the items to
its batch call, passes `context` where the service takes one, protects the
`{…}` tokens with the service's tag handling and fills `value`. Those
services know no plural categories, so a plural key's forms stay null and
show up as missing, for a person. Translation management systems (Crowdin,
Phrase, Lokalise and the like) trade XLIFF, which Export already writes, and
are not this loop's concern. Wordsmith ships no adapter.

**On the command line**, built (the command line spec's *Calls*): `list
--stale xx` beside `--missing xx`; `request` for the keys `list` would list,
up to `--limit n`, with a `--brief`, exiting 1 with nothing to ask; and
`apply`, which writes each passing key's filled slots and its entry's stale
mark, `machine translated` or `--stale`'s words, in one patch saved once,
raises the hand of a key whose link target changed, naming the change,
names each key it skips and why, and exits 1 when it skips any. An agent or
a script loops `request --limit 50`, translate, `apply`, until
`request` selects nothing or `apply` writes nothing, since a skipped key is
still missing.

**Nothing is written unchecked.** Each key's answer is compared with its
default across all its slots, as the translation check compares parameters
(*Parameters*). It must keep the parameters the default uses and add none
that no definition names; keep every reference, constant and selector the
default holds and hold no `{…}` token the default does not, since services
translate inside braces (`{>menu.file}` comes back `{>menu.archivio}`); keep
each format item's alignment and format as the default writes them, since
a format its argument does not take (`{0:Q}` for a count) shows marked in the
app, though a parameter the default does not use, which a definition
allows, may come bare; write no brace that opens nothing, which the app shows
as written (*Parameters*, Translation check); and keep the default's code spans
as they are. Code
spans and links are read as the app reads them, by the runtime's
`MarkdownParser`, so a span fenced by two backticks holds one, a link may carry
a title, and a target that is no absolute URL is no link, since the app leaves
its label unlinked; markdown the app cannot read (a link in a link's label) is
flagged. A link target is different: some pages
have one per language, so an answer may change one, but no service decides that
alone. A changed link target is written, named, and raises the key's hand
(needs-review, the `stale=` flag), so a person checks the URL whatever else they
review; a link dropped or added is a lost or extra token, and flagged: each
filled slot holds as many links as its default's, a form's being the default's
same form, else its `other`, else its plain value. A slot the key
has no use for in the target, one the request did not ask for (it has words
and is not stale, which the answer would overwrite; the check asks as the
request would now, its words being as they were), a key whose default or current words changed
since the request, a key that is gone and a constant are flagged too, but
only where the answer filled something: a key it left alone is no error. A flagged key is not
written, none of its slots: stricter than `set`, which writes a person's
translation with a note.

**In Wordsmith.** Tools > Machine Translation… opens a dialog: the target
(the selected language), the selection (Missing, Stale, under the selected
node), the brief, and the translator. The translator is a command line, run
with the request on stdin and read for the answer on stdout, or the user by
hand: the dialog copies the request or saves it to a file and takes the
answer pasted or opened, so a chat window serves with nothing set up. A
command runs as a shortcut's target does, a program and its arguments, with
no shell unless it names one (`cmd /c`, `pwsh -Command`). The batches run one
after another, with progress and Cancel, which stops the one running; a batch
whose command fails or whose answer is no document is reported with the exit
code and stderr, and the next one runs. The command, the brief per language,
the batch size and the mark are remembered in the editor's settings.

**It lands for review.** The answers open in a grid, a row per slot: the key,
the form, the default, the proposed text and the flags. A row is edited in
place, unticked or asked for again (a retry sends the selected rows alone); a
flagged key's rows start unticked and say why, and ticking one is the
person's call. A row whose link target changed starts ticked, saying so, and
applying it raises the key's hand. Apply writes the ticked rows into the open file as edits, each
entry marked stale with the mark, as one undo entry (*One entry for many edits*). Save writes the
file as ever, and the stale filter surfaces the rows for a translator to
review, as it does any stale translation. The command line writes the file;
the editor writes its document, so undo and an unsaved file stay whole.

**Privacy.** The first run of a command says that the file's strings go
wherever the command sends them, and the user confirms once per command. If
a built-in HTTP transport is added later, its credentials go to the Windows
Credential Manager, never to `config.ini` (plain text) or anything beside the
words files.

**Where it lives.** Authoring builds the request and reads the answer, tested
headless (built): `TranslationRequest` writes the document for the keys a
caller selects, by `MissingWords` and `TranslationRequest.Stale`, with
`Utf8JsonWriter`; `TranslationAnswer` reads it with `JsonDocument`, and
`TranslationItem.Check` checks each key, with no reflection, since the
command line is trimmed; its markdown goes through `MarkdownMarks`, a
`MarkdownParser` that keeps each code span and link target, and the parser
reads its entity and emoji tables with `JsonDocument` too, so a trimmed
command line keeps them whole. The counts a form reads are Authoring's
`FormNumbers`, in the editor's digits for its rows and invariant ones for the
document. `apply` writes the passing slots through the patcher; the editor's
grid writes the ticked ones as `FieldEdit`s. The
command runs behind a seam, `ITranslator`, so the tests fake it; the process
behind it is the editor's. The Core skill gains the loop, the agent its own
translator.

**Tests.** Built, in `TranslationDocumentTests` and the command line's: the
request: selected as `list --missing` and `--stale` select, under a prefix and
up to a limit; constants never; a language the file does not want selects
nothing and exits 1; an item carries the default and its forms, the context,
the comment, the parameters and the target's current words; a plural key's
slots are the plain value and each form the target requires, Maltese's `few`
and not `two`, `many` or `other`, and it asks for those without words, or all
of them when stale, with the counts each reads; text is unescaped. The answer:
filled slots written with the mark in the writer's order, nulls and empty
strings left; a dropped or added parameter, a translated reference, a format
item given another format or alignment (one kept, its number written `00`,
passes), a lost code span or link, one fenced by two backticks and one with a
title among them, a slot with more links than its default's, markdown the app
cannot read, a slot of no use in the target, a slot not
asked for (Maltese's translated plain value beside its asked `few`), a default changed
since and a key gone each skip their key and are named; broken Unicode is
refused, naming the item; a changed link target is
written, named and raises the hand, while a translated title and a span's
padding change nothing; a refused edit writes
nothing; the request sent back untouched writes nothing and exits 0; an answer
in a code fence reads, and what is no document says why. To build, the
editor's, with a fake translator: a batch's answer fills the grid, a flagged
key's rows unticked with the reason, an edited row applied as edited, Apply
one undo entry that marks stale; a failed batch reported and the next one run;
Cancel stops; a pasted answer fills the grid.

**Order.** Authoring's document and check with the command line's `list
--stale`, `request` and `apply` first, since an agent can run the loop with
them at once and the skill can say so (built); then the editor's dialog by
hand and its grid, its Apply one batch entry (*One entry for many edits*);
then the command runner and the batches.

## One entry for many edits

An action that is a run of edits the history already knows, each to its own
key, field or language, should undo as one: Machine translation's Apply
writes a field, marks it stale and may raise a hand, row after row, and is
one undo entry (*Machine translation*, It lands for review). A `BatchEdit`
holds those entries, in the order they were made, and is one entry on the
stack.
- **Recording.** A batch is a scope on the one door (*Undo*, Recording):
  inside `Batch(action)`, each `Perform` the action makes, its own or a
  command's, adds its entry to the open batch instead of the stack. When the
  scope closes, the batch goes on the stack as one entry, or nothing when no
  edit was made, and one edit alone goes on as itself. A batch opened inside a
  batch adds to the outer one. So an action is made of the commands there are,
  and a command is recorded as it always is.
- **Applying.** Undone, the batch applies its entries last to first; redone,
  first to last, each whole: a field edit in it is never stepped through by a
  box. That each entry finds what it changes is the stack's own
  guarantee, entries applied in order, so every label means what it meant.
  The selection goes where the last entry applied says.
- **Dirtiness.** The batch records whether the document was dirty before it,
  and a save moves its marks, as any entry's; the marks of the entries inside
  it are never read. Pushing a batch ends a typing run.
- **Navigate first.** A batch whose entries share one node goes there first,
  as an entry does, and switches the translation language only when every
  entry that names one names the same: Stale All's stamps, one a language,
  go to their key and leave the language be. One spread across nodes shows
  nowhere, and applies at once.
- **What cannot go back.** A boundary inside a batch clears the stack as it
  does anywhere (*Undo*, Boundaries), and the edits the batch had recorded go
  with it: a Language Manager commit that merges two languages keeps no
  reversible state. What the batch records after it goes on as one entry. An
  action that throws ends the batch: the edits it recorded, each whole, go on
  the stack as one entry, so they can be taken back, and the error goes on.
  An entry that throws as the batch applies clears the stack, as any entry
  does.

Stale All Languages becomes a batch of `StaleEdit`s, one a language, and
`StaleAllEdit` goes. The rest stay as they are, each for its reason: a
Language Manager commit is put back whole from a snapshot, never by replaying
inverses (*Undo*, Languages); a typing run's stale stamps and raised hand
belong to the step that made them, which a box steps through; and a Constant
toggle's cleared translations are no entry of their own yet.

**Where it lives.** The editor's, beside the entries it holds:
`BatchEdit` with them, and the scope on the main view model's `Perform`.

**Tests.** A batch of a written field, a stale mark and a raised hand across
two keys and two languages is one entry: undone, every file's saved text is
as before and the title as it was, and redone as after; a batch with no
edit records nothing, and one with one edit records that edit; a batch inside
a batch is one; a merging recode inside one clears the stack and the edits
before it, and the edits after it are one entry; an action throwing part way
leaves its recorded edits one entry, undone to before; a batch on one key goes
there before it applies, in its language when its entries share one, and one
across keys applies at once; and Stale All undoes and redoes as before, going
to its key in whatever language is showing.

**Order.** With Machine translation's grid, its Apply the first batch, and
Stale All moved onto it in the same step.

## Dirty by file

Unsaved work is the session's today: one dirty mark for the window, and one
saved point in the undo history, so Save and Save As write every loaded file
(*Saving*). Each file should carry its own: a file's mark set by an edit to it
and cleared by writing it, an undo back to its own saved point clearing it, the
title starring while any file is dirty, and closing naming the files with
unsaved work. Save would then write only the dirty files, and Save As the
selected file alone, leaving the others as they are. It is built with Save As
if that stays small, and otherwise after it.
