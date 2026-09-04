# Wordsmith

The editor for your Words. Because translators deserve better than Notepad.

Wordsmith is a WPF app that opens one or more `words.ini` files and lays the
keys out as a tree, so humans can edit the values without ever learning the
INI escape rules.

## What it does

- **Edit** — browse the key tree, edit values, contexts, and comments per
  language; add, rename, remove, and drag keys around without breaking their
  children.
- **Languages** — manage the language list, and see at a glance which keys
  have no value in the language you're looking at.
- **Stale tracking** — mark a value stale (per language, or all at once) when
  the source text changes, filter the tree down to what still needs
  re-translating, and clear the flag when the translation catches up.
- **Review flags** — keys with translator comments get flagged for the
  programmer's attention.
- **Constants** — toggle a key into a `$constant` that other keys can
  reference.
- **Merge** — combine per-language files into one multilingual file, as long
  as their key sets line up.
- **Parameters** — try out `param-` values against the format string before a
  user finds out it throws.
- **Round-trip saving** — files are written back in a stable format; loading
  and saving without edits produces the same bytes you started with (the tests
  insist).
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
