# Wordsmith Editor — what the tool is supposed to do

A desktop editor for `words.ini` localization dictionaries. Two people use it:
the **developer**, who creates keys, writes default text, and annotates intent;
and the **translator**, who receives a file, works through what changed, and
sends it back. Everything the tool does serves that round trip.

The front-end is open for rewriting. This spec fixes the behavior; layout and
controls are the rewrite's to choose.

## Architecture rules

- **The golden rule of MVVM: ViewModels extract Intent from the View so that
  something else can do the processing.** The "something else" is
  `PatTech.Localization.Authoring` — parsing, the document model, merging, and
  writing all live there and are tested directly against that API. ViewModels
  translate gestures into calls on the document; views bind and nothing more.
- The runtime packages stay lean: nothing the editor needs beyond parse events
  (`IWordsParserConsumer`) belongs in Localization-Core.
- Short names.
- The main window is three panes: **tree** (left), **baseline** (middle),
  **translation** (right).

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

Load → save must never lose data; it may (and does) normalize formatting.

Preserved exactly: every field and language entry, key order, freeform
comments, preamble, trailer, constants, `!` labels, stale values, and the
top-of-file `param`/`param-xx` settings-file references (see Markdown previews).

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

Save→load→save is byte-stable (pinned by `MainWindowViewModel_IdempotencyTest_FileContents`).

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
- **Badges** on nodes: file, constant, needs-review, stale (in the selected
  language), overwritten-by-later-file; keys whose default or selected-language
  value is empty render emphasized. The selected-language half applies only
  when the key's file **registers** that language — declares it in its
  top-of-file table, listed or `!`-hidden. A hidden language is still a
  promise, so its gaps show; but in a project of several dictionaries, a file
  that does not register the selected language at all has no gap to show, and
  its keys stay plain. A code found only on stray fields is a gripe, not a
  registration — declare it and the gaps appear.
- **Filters**: substring search, stale-only, needs-review-only — composable;
  ancestors of a match stay visible so the path is readable. The stale filter
  is per selected language: this is the translator's work queue. The
  needs-review filter is the programmer's work queue in reverse — the
  translator raises a hand by setting the unlocalised `stale=` flag
  (recycled as the "raise hand" action), and the programmer filters for it.
- **Structure edits**: add/rename/remove nodes and keys; renames rewrite every
  descendant key; drag-and-drop moves subtrees. A group node can gain key data
  ("add key information") and a key can exist on any node except a file.

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
  values through `Format` to prove the placeholders work before shipping. `{>reference}` and `{$constant}` tokens work across files for this purpose, simulating a host app loading multiple dictionaries.
- **Stale-all-languages**: one action for "I changed the default, every
  translation needs another look".

## The translation pane (right)

Starts with the **language dropdown** (fed by the file that owns the selected
key) and mirrors the baseline's feature set for everything tagged with the
selected code: value, context, comment, the stale timestamp (with a toggle),
and the markdown preview.

- Language codes found on fields but not registered at the top of the file are
  auto-added to the list with a `!` label and a gripe — wrong, probably, but
  still selectable so the stray entries can be inspected and fixed.
- Changing the dropdown re-contextualizes the whole window: tree badges and
  empty-value emphasis refresh to the new language (file by file — see
  Badges), and the stale filter re-evaluates against it.

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
  file. The earlier `param-<scheme>=<folder>` experiment is gone, without a
  compatibility path.
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
  with no rule keeps the alt-text fallback. The constraint that does not
  move: the resulting path stays clamped to the scheme's folder (the
  `FolderImageResolver` refusal of `..`, rooted and UNC paths, no variable
  expansion), and nothing is fetched remotely.
- **Hyperlinks.** A scheme maps to `popup` — report the target, the default
  for anything unlisted — or `shellexec` — confirm, then hand the target to
  the shell, the default for `http`, `https` and `mailto`. A `<scheme>-decode`
  rule, same syntax, rewrites the URI first, so a custom scheme can open as
  the page or command it stands for, or show that command.
- **Language rules win key by key.** A scheme listed in `wordsmith-xx.ini`
  replaces that scheme's rule while previewing `xx`; every other scheme falls
  through to `wordsmith.ini`, then to the defaults. The default-text preview
  uses the dictionary file alone.
- Field names are `\w+`, so a scheme with a dash in its name cannot be
  written; none of the built-ins has one.
- **Gripes.** Rendering a preview collects everything Words complains about
  along the way — a missing `{>reference}`, a circular one, an image that did
  not resolve, a link that would not parse, a sample that would not format —
  behind a tool button beside the preview toggle: a count when there is
  something, the list on click. The editor installs one collecting logger as
  `Words.Logger` and gives the preview parser the same one, so both channels
  land in the list.
- A dialog edits the settings files a dictionary names — the image and
  hyperlink tables, and which files the `param` slots point at. How much of
  that is a picker and how much a table is the rewrite's to choose.

## Languages

A language manager adds, removes, and relabels languages. Adding one backfills
an empty entry on every key; removing one deletes its entries after
confirmation. Consider an option to shift a language (re-code it, e.g. `en-GB`
absorbing into `en`); where both codes hold a value, keep the target's, park
the source value in the entry's `context-xx` field where the translator can
copy/paste from it, and stale-mark the entry so the review filter surfaces
the collision.

## Merge

The translator round trip in bulk: pick a base file plus a language→file map,
and produce one merged file taking each language's entries from its source.
The merged result is written to disk and loaded into the session. Merging
requires the files to agree on their key sets. This feature can also be used to split one language out of the file so it can be worked on separately.

## Saving

Save rewrites every loaded file through `WordsSession.Save` — `IniWriter.WriteFile`
with the file's own language table, preamble and image schemes, in the order
its tree node walks. A file that cannot be written is reported and the others
still save. The editor tracks dirtiness; closing with unsaved changes prompts.
Reset returns to the empty session (one default `en` language).

## Wordsmith's own words

The editor eats its own dogfood. Every user-facing string of Wordsmith —
labels, tooltips, dialog titles, confirmations, notices, the conflict and
error messages the view models compose — lives in the editor's own
`words.ini`, loaded through the library at startup and rendered with the WPF
package: `{l:Words}` for plain text, `WordsInline` where a value carries
markdown. `IDialogs` and every other text-taking seam mark their parameters
`[Localized]`, and the analyzer runs on the project, so a hardcoded string is
a build warning (PTL001), never a silent one. The file is also the editor's
standing test subject: open it in Wordsmith, add a language, translate,
relaunch in it. [The Core skill](../Localization-Core/SKILL.md) is the how-to.

## Out of scope for the rewrite (known future work)

- A split UI: the engine lives in `WordsSession.Split` (its output is
  exactly the shape `Merge` consumes back), but nothing in the editor calls
  it yet — wire it up during the front-end rewrite.
- Surfacing the provider's gripes (undeclared languages, unrecognized fields)
  in the editor UI; today they only accumulate on `WordsFile.Errors`.

Both are sequenced, with everything else outstanding, in [TODO.md](TODO.md).
