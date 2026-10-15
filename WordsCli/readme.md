# words

The command line for your Words. Because a build script shouldn't need a mouse.

`words` changes one field of a `words.ini` and leaves every other byte where
it found it: your comments, your line breaks, your encoding, your idea of a
reasonable line length. Wordsmith saves a whole file the way Wordsmith likes
it, which is fine for a file Wordsmith wrote and noisy for one you did. This
is for the other times — a build step, a script, a coding agent that needs to
change one entry without learning the format's escape rules the hard way.

## The calls

```sh
words get    strings.ini menu.file value-it          # a field's value, unescaped
words get    strings.ini menu.file                   # the key's whole block
words set    strings.ini menu.file value-it Archivio # sets a field, adding it or the key
words set    strings.ini menu.file value-it - < it.txt   # a value from stdin
words set    strings.ini menu.file value-it Archivio --stale "machine translated"
words remove strings.ini menu.file value-it          # drops a field
words remove strings.ini menu.file                   # drops the whole key
words list   strings.ini menu.                       # the keys under a prefix
words list   strings.ini --missing it                # the keys Italian still misses
words list   strings.ini --stale it                  # the keys whose Italian is marked stale
```

- **Fields** are named as in the file: `value`, `value-fr`, `value-mt#few`,
  `context-fr`, `comment-fr`, `stale-fr`, `param-count`.
- **Keys** are the full dotted name, whatever `[.child]` header the file wrote
  them under: segments of letters, digits, `_` and `-`, or a `$constant` of one
  segment. `set` writes no other name, since a runtime skips one; `get` and
  `remove` still reach it, so you can clear it out.
- **Values** are an argument, or `-` for stdin, so a multi-line value needs no
  shell quoting; the last line break is dropped. Stdin is UTF-8, no guessing:
  anything else is refused rather than quietly turned into `�`, and a leading
  U+FEFF is kept as yours, with a word on stderr in case a tool slipped you a
  BOM.
- **`--stale`** marks the language's entry stale as well, so a machine-written
  value turns up in Wordsmith's review filter instead of in production.
- **A new default** (`value`, or a form like `value#other`) marks every
  translation that has words stale with the time, the way editing it in
  Wordsmith does. A mark already there stays put, so "machine translated"
  doesn't quietly become a date. Setting a default to the words it already
  has marks nothing.
- **A translation that loses a placeholder** gets a word on stderr: one that
  drops a `{0}` the default uses (the app's value would never show), or uses
  a `{1}` the default doesn't and no `param-` defines (the app would throw).
  It is written all the same, since you may be halfway through.
- **`--missing xx`** asks Wordsmith's badge rule about one language: keys with
  no words in `xx`, or a plural key missing a form `xx` counts by. A language
  the default speaks misses nothing, since its empty entries fall back. (The
  badge also flags a default with no words; that is not `xx`'s to miss.)

Values go to stdout and gripes to stderr. The exit code is 0 for done, 1 when
the key or field is not there, and 2 for a bad call, a file that is missing,
can't be read or written or does not parse, or an edit `words` refused.

## Machine translation

`words` doesn't translate anything. It writes down what needs translating,
in a form any translator can fill in, and puts the answers back:

```sh
words request strings.ini --missing it --limit 50 > ask.json   # what Italian lacks, as JSON
# ... a translator fills in every null under "translation" ...
words apply   strings.ini ask.json                            # writes the answers back
```

The request explains itself: each key's default, its plural forms, context,
comment and parameters, Italian's words where it has some, and a `null` for
each thing to fill, with instructions at the top saying how. So the
translator can be a language model with nothing set up (`claude -p`, `llm`, a
chat window you paste it into), or a short script around a translation
service. Services translate strings, not documents, and know nothing of
plural forms, so a script fills the plain values and leaves the forms for a
person. `--stale it` asks for the stale ones instead, or as well, and
`--brief` passes on your own word about tone and terms.

`apply` reads the same document back, code fence and chatter around it
included, and is fussy about what it writes. A key is skipped, with its
reason on stderr, when its answer drops or invents a `{0}` or a `{>reference}`,
dresses a `{0:N1}` in another format (the app would show `#{0}#` for a
`{0:Q}`), loses a `` `code span` `` or a link, or grows an extra one,
fills a form the language doesn't use or a slot nobody asked for (that one
has a translation already, which it would trample), or when the key's
default or its Italian changed since the request. It reads the markdown the
way the app does, so a code span fenced with double backticks, or a link
with a title, is held to the same rules. A link pointing somewhere new is let
through, since the Italian docs may well live at their own URL, but the key
gets its hand raised and stderr says what moved, so somebody checks before
it ships. Everything it writes is marked stale (`machine translated`, or your
own words with `--stale`), so it turns up in Wordsmith's review filter. A
`null` left is fine: that slot just stays missing. It exits 1 when it skipped
anything, and writes nothing at all if the file would refuse one of the edits.

## Where things go

- A changed field keeps its place; its lines are replaced with exactly what
  Wordsmith would write for it, folded and escaped.
- A new field goes where Wordsmith would write it (context, comment, value,
  then each language), or last, if its block was never in that order anyway.
- A new key joins the chain it belongs to as a `[.child]`, next to its nearest
  sibling; with no chain to join it gets a full header after its sibling's
  chain. Either way it never re-bases somebody's `[.child]` headers, and it
  takes its blank lines from the blocks around it.
- A removed key whose header bases `[.child]` headers keeps that header, bare.

Every edit is read back before the file is touched. If anything changed
besides what you asked for, `words` refuses, says what would have moved, and
leaves the file alone. It writes to a temporary file and moves it over the
original, so a crash halfway leaves the original whole.

## Getting it

Each [Wordsmith release](https://github.com/pzahra/words/releases) carries
`words` for Windows, Linux and macOS (x64, and Arm64 for macOS): one
self-contained file, nothing to install. Unpack it onto your `PATH`. The macOS
builds aren't notarized, so clear the quarantine once:

```sh
xattr -d com.apple.quarantine words
```

Or build it: `dotnet run --project WordsCli -- --help`.

## Tests

The patcher it runs on lives in Localization-Authoring (`IniPatcher`), with
the translation document (`TranslationRequest`, `TranslationAnswer`), and
[Localization-Tests](../Localization-Tests) covers them and the calls:

```
dotnet test Localization-Tests --filter "FullyQualifiedName~IniPatcherTests|FullyQualifiedName~WordsCommandTests|FullyQualifiedName~TranslationDocumentTests"
```
