# Wordsmith

The editor for your Words. Because translators deserve better than Notepad.

Wordsmith is a WPF app that opens one or more `words.ini` files and lays the
keys out as a tree, so humans can edit the values without ever learning the
INI escape rules. Scripts and coding agents get [`words`](../WordsCli/readme.md),
the command line that ships beside it.

## What it does

- **New** — File > New starts a `words.ini` that already knows English is
  the default, with a header saying what the file is, where it goes and where
  the format is written down. It's yours once you Save, header and all.
- **Edit** — browse the key tree, edit values, contexts, and comments per
  language; add, rename, remove, and drag keys around without breaking their
  children.
- **Undo, and Back** — Ctrl+Z takes back the last edit, one action at a
  time, and in a text box your typing a word at a time, the caret back where
  it was; if it happened somewhere you aren't looking, the first Ctrl+Z takes
  you there and the second takes it back. Back and Forward retrace where
  you've been in the tree, like a browser.
- **Languages** — manage the language list, and see at a glance which keys
  have no value in the language you're looking at.
- **Plural forms** — "1 file", "2 files", and Maltese's other three: each pane
  picks a form its language counts by, listed with the counts that take it,
  so nobody has to know what CLDR means by "few" to fill it in. A form the
  language needs and lacks is flagged like any missing word.
- **Stale tracking** — change the default and every translation it leaves
  behind is marked stale for you (or mark them yourself, per language or all
  at once), filter the tree down to what still needs re-translating, and
  clear the flag when the translation catches up.
- **Review flags** — keys with translator comments get flagged for the
  programmer's attention.
- **Constants** — toggle a key into a `$constant` that other keys can
  reference.
- **Merge** — combine per-language files into one multilingual file, as long
  as their key sets line up.
- **Import and export** — read `.resx` sets and XLIFF 1.2 files into the tree
  as the `words.ini` they become, and write a loaded file back out in either.
  Export shows the files it will write and what the format has no slot for
  before it writes a byte; Save keeps writing `words.ini`.
- **Parameters** — the programmer says what each `{0}` is right under the
  context, and the translator types a value under it and watches both
  previews fill in, before a user finds out it throws. An enum offers its
  members; a `{0}` nobody defined shows up anyway, one click from defined.
- **Round-trip saving** — files are written back in a stable, canonical format;
  saving an already-canonical file again produces the same bytes, so a real edit
  shows up in the diff as just that edit (the tests insist).
- **Speaks its own Words** — every label, tooltip and message Wordsmith shows
  comes from its own [`words.ini`](Resources/words.ini), loaded through the
  library like any other app's. It speaks your OS language when it has the
  words, English otherwise; `--lang=it` on the command line or the language
  menu picks another. Translate Wordsmith by opening that file in Wordsmith.

## Tests

[WordsEdit.Tests](../WordsEdit.Tests) covers the parsing, writing, merging,
and view-model behavior:

```
dotnet test WordsEdit.Tests/WordsEdit.Tests.csproj
```
