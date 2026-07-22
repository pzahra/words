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
- A **key tree**: dotted block keys (`view.section.key`), prefixed with the
  file name in memory. `$keys` are constants (no translations). A key carries:
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
comments, preamble, trailer, constants, `!` labels, stale values.

Canonicalized by the writer (`IniWriter`): line wrapping (~50 columns),
escaping (`__`, `''`, leading-whitespace `_` marker), newline continuations,
and block headers — a block extending the last full header is written as one
dot-relative `[.suffix]`; an `ICutStrategy` decides where extra full-header
cuts go (the descendant-aware strategy is a known TODO; until then, keyless
groups reload flattened). Comment placement is canonicalized too: a `;` run
between fields hoists above its block; comments in the language section
join the preamble.

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
  value is empty render emphasized.
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
  dialect (the same renderer the host apps use) in place of the raw text.
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
  empty-value emphasis refresh to the new language, and the stale filter
  re-evaluates against it.

## Languages

A language manager adds, removes, and relabels languages. Adding one backfills
an empty entry on every key; removing one deletes its entries after
confirmation. Consider an option to shift a language (re-code it, e.g. `en-GB`
absorbing into `en`); where both codes hold a value, keep the target's and
mark it stale with the source value recoverable — the stale filter then
doubles as the reconciliation queue.

## Merge

The translator round trip in bulk: pick a base file plus a language→file map,
and produce one merged file taking each language's entries from its source.
The merged result is written to disk and loaded into the session. Merging
requires the files to agree on their key sets. This feature can also be used to split one language out of the file so it can be worked on separately.

## Saving

Save rewrites every loaded file through `IniWriter.WriteFile` with its stored
preamble/trailer. The session tracks dirtiness; closing with unsaved changes
prompts. Reset returns to the empty session (one default `en` language).

## Out of scope for the rewrite (known future work)

- Descendant-aware `ICutStrategy` (task chip exists).
- Split/merge/shift move into Localization-Authoring with their tests; the
  ViewModels keep only intent-gathering.
- Test restructure: WPF/Ava/Core tests build off the API assemblies alone
  (one shared test project if they can co-exist); the only tests referencing
  Wordsmith are the editor's own.
- Surfacing the provider's gripes (undeclared languages, unrecognized fields)
  in the editor UI; today they only accumulate on
  `WordsParserToLocalizationProvider.Errors`.
