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
```

- **Fields** are named as in the file: `value`, `value-fr`, `value-mt#few`,
  `context-fr`, `comment-fr`, `stale-fr`, `param-count`.
- **Keys** are the full dotted name, whatever `[.child]` header the file wrote
  them under.
- **Values** are an argument, or `-` for stdin, so a multi-line value needs no
  shell quoting; the last line break is dropped.
- **`--stale`** marks the language's entry stale as well, so a machine-written
  value turns up in Wordsmith's review filter instead of in production.
- **`--missing xx`** lists what Wordsmith would badge: keys with no words in
  `xx`, or a plural key missing a form `xx` counts by. A language the default
  speaks misses nothing, since its empty entries fall back.

Values go to stdout and gripes to stderr. The exit code is 0 for done, 1 when
the key or field is not there, and 2 for a bad call or a file that does not
parse.

## Where things go

- A changed field keeps its place; its lines are replaced with exactly what
  Wordsmith would write for it, folded and escaped.
- A new field goes after the last field of its block.
- A new key goes after the header chain of its nearest sibling, as a full
  header, so it never re-bases somebody's `[.child]` headers.
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

The patcher it runs on lives in Localization-Authoring (`IniPatcher`), and
[Localization-Tests](../Localization-Tests) covers both it and the calls:

```
dotnet test Localization-Tests --filter "FullyQualifiedName~IniPatcherTests|FullyQualifiedName~WordsCommandTests"
```
