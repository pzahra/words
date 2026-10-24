# words — what the command line is supposed to do

A build step, a script or a coding agent that needs to change one entry
should not have to open the editor, and should not have to hand-edit a
format with continuation and escaping rules either. Saving through Wordsmith
normalizes the whole file (the editor spec's *Round-trip guarantees*), which
is fine for a file Wordsmith already wrote and noisy for one written by hand.
`words`, the command line, changes the entry it is asked to change and leaves
every other byte alone.

Wordsmith has its own spec ([../WordsEdit/SPEC.md](../WordsEdit/SPEC.md)),
and the format is the runtime's ([../Localization-Core/SPEC.md](../Localization-Core/SPEC.md)).
Everything up to *Planned upgrades* is built. What the command line does for
one of the editor's features, the parameter check among them, is planned in
the editor's, beside the feature it serves.

## Calls

One file per call; a field is named as in the file (`value`,
`value-fr`, `value-mt#few`, `context-fr`, `comment-fr`, `stale-fr`,
`param-count`), plural forms included (the editor spec's *Plural forms*); a value is an
argument, or `-` for stdin with its last line break (`\r\n`, `\n` or `\r`)
dropped, so a multi-line value needs no shell quoting. A redirected stdin is
UTF-8 exactly: bytes that are no UTF-8 are a bad call, never replacement
characters, and a leading U+FEFF is the value's own, so `get` piped into `set`
carries it whole, and a line on
stderr says it was kept. A `param-` text is written as given, one that
names no type reading back whole (the editor spec's *Parameters*):

- `get <file> <key> [field]` prints a field's value, unescaped, or the whole
  block as Save writes it, under a full header, when no field is named.
- `set <file> <key> <field> <value>` sets one field, adding the key, or the
  field, where it is missing. A default that changes, the plain value or a
  form, marks every translation with words and no stale mark yet stale with
  the time, as typing it in Wordsmith does (the editor spec's *The baseline pane*); one set to
  the text it holds marks nothing, its line breaks read as the file keeps them,
  so a shell's `\r\n` for a `\n` is no change. `--stale [text]` marks the language's entry
  stale with it (the default's mark is the hand, `stale=`, which keeps them
  too), so the review filter
  surfaces a machine-written value. Options go anywhere after the call:
  `--stale` takes the next argument as its words only once the value is in,
  and `--stale=text` always does. A translation it writes is checked against
  the default by the editor's rule (the editor spec's *Parameters*,
  Translation check): one that drops a parameter the default uses, or uses
  one the default does not and no `param-` defines, is written all the same,
  with a note naming them. The file is all it sees, so a side with a
  reference out of it may carry anything, and is not held to the other. A
  value with a brace that opens nothing, `{0:{>fmt}}` or a lone `{`, which the
  app shows as written (the runtime spec's *One pass for every brace*), is
  refused, exit 2, the file left as it was, and the braces named; `{{` writes a
  brace, and a constant's words are text.
- `remove <file> <key> [field]` drops one field, every declaration of it, or
  the whole key. A field is there to drop when the file declares it, with
  words or empty, though `get` reads an empty one as none, exit 1, but for the
  hand (`stale=`) and a parameter's definition, whose empty text says what they
  are: an empty line, exit 0.
- `list <file> [prefix]` prints the keys in the order their blocks first
  appear, leaving out a bare group header as the editor drops it, and a `[]`,
  which names none; the prefix
  is plain text, so `menu.` lists a group. `--missing xx` lists only those
  where `xx` misses its words, by the badges' rule (the editor spec's *Plural forms*, Badges): no
  words, or on a plural key a form the language requires. A language the file
  does not declare, or one its default speaks, misses nothing, and a line on
  stderr says which. `--stale xx` lists those whose `xx` entry carries a stale
  mark; with both, which name one language, a key either takes is listed.
  `--stale-without text`, once or more, passes over a mark that starts with
  the text as written, so a loop of `request` and `apply` over `--stale` ends
  on the marks `apply` leaves (`machine translated`, or its `--stale` words)
  without the command assuming which those are. It narrows `--stale` alone, a
  key `--missing` takes is listed whatever its mark, and it is refused without
  `--stale` or empty, which would pass over every mark.
- `request <file> [prefix] --missing xx` and/or `--stale xx` prints a
  translation request (the editor spec's *Machine translation*) for the keys
  `list` would list, less those whose default has no words; `--limit n`
  takes the first n of them, and `--brief text` gives the translator the
  user's own words. Nothing to ask exits 1, printing nothing.
- `apply <file> <answer>`, `-` for stdin, reads the request filled in and
  writes each key whose answer passes the check (the same section, *Nothing
  is written unchecked*): its filled slots as `set` writes them, and its
  entry's stale mark, `machine translated` or the words `--stale` gives,
  and after a colon the name the answer's `translator` gives itself, where
  it gives one: `machine translated: GPT-5`. A
  key skipped is named on stderr with why, and a last line counts the slots
  written, left and skipped; any skipped exits 1. The batch is checked whole:
  a key reads the words it writes for the keys it refers to, a key skipped is
  checked out of it again, and a key it does not write whose translation it
  leaves at odds with its default is named, not skipped. A key answered twice
  keeps its first answer. A changed link target is written with the key's
  hand raised, so a person checks the URL, and named; a hand already up is
  left as it is, its text kept. The writes are one patch, saved once: an edit the
  patcher refuses writes nothing. The answer's language must be one the file
  declares.
- `--version` and `--help`.

A key is the file's own, by the name the file writes, and a key's
name (the runtime spec's *Key names*): `set` writes no other, exit 2, though
`get` and `remove` reach a block the file already names otherwise, so it can
be read and cleared. `remove` refuses a constant whose header bases
`[.child]` headers. Values go
to stdout and gripes to stderr: the reader's gripes the edit adds (a field in
an undeclared language, a form the language never reads), and a note when a
field declared twice is made one, or a translation drops or adds a parameter.
The exit code is 0 for done, 1 when the key or field is not there, `request`
finds nothing to ask or `apply` skips a key, 2 for a bad call, a file that is
missing, can't be read or written (changed since the call read it, held,
read-only, or its write left pending), does not parse or is no text the patcher
reads, an answer that is no translation document, or a refused edit, one
that would change more than asked or write a brace that opens nothing; a file's failure
names the file, and a missing one does not show the calls. Redirected streams
speak UTF-8 with `\n`; a console keeps its own.

## Surgical edits

`IniPatcher` edits the file through the ini driver's words dialect (the
editor spec's *Ini files through the driver*), one transaction a call.

**Reading.** By the BOMs Wordsmith's Load reads: UTF-8 with or without one,
UTF-16 or UTF-32 with one, UTF-32 LE's checked first. Text holding a NUL, as
UTF-16 without its BOM reads, is refused, and so is a value holding one.

**A field set** has its lines, its declaration and continuations, replaced
with what `IniWriter` writes for that one pair, folded and escaped, in the
file's own line ending; every other line keeps its break. A field already
holding the text is not touched; one declared more than once keeps its first
place and loses the others.

**A new field** goes where the writer puts it when its block stands in the
writer's order: the context, the comment, the value and its forms in CLDR's
order, the parameters, the default's stale mark, then each language in the
order of the key's entries, with its value, forms, stale mark, context and
comment; a language or a parameter new to the key comes after those it has.
It goes ahead of the first field ranking after it and of the comment lines
right above that one. In a block in no order it goes after the last field,
or under the header when there is none.

**A new key** goes into the chain whose full header it extends, as a
`[.child]` after the header there sharing the most leading segments; else
after the chain holding its nearest sibling; else after the last chain. It
is then cut as the writer cuts (`GroupCuts`): a group it gives two keys
gains a bare header, its keys re-headed to chain off it; a deeper group
takes its keys from the chain they ran in; a bare header left basing nothing
goes. Those headers are the only other lines that change. It is spaced as
the writer spaces: past a blank line after the block it follows, with one of
its own when it has a value, and none in a file written without them. A
value added to or removed from a key already there leaves the blank lines as
they were.

**A removal** keeps bare a header that bases `[.child]` headers, which
reloads as a group; a block it leaves with no field goes; it leaves no blank
line that parts nothing; the comments above a removed block stand.

**Checked.** Each edit is read back through Core and compared with the file
before it, the change applied: every key's fields, the language table, the
settings references and the comments in order. Anything else refuses the
edit, naming what would have changed.

**Written.** A call's edits, `apply`'s batch with them, commit once through
the driver: to a hidden log beside the file, `.words.ini.tmp`, then to the
file in place, which keeps its attributes, hard links and Unix mode; a link
is followed. A file changed since the call read it, held by another program,
read-only, or under another write is refused, exit 2, saying which. A commit
whose log is whole but whose file write failed exits 2, saying the next call
finishes it. A call whose open settles a log a call before left says so when
that wrote the file or dropped a write.

## Where it lives

The patcher, the words dialect and `WordsField` (a field's name, parsed,
read and written on the model) are in Authoring, tested headless, over the
driver, `PatTech.Text.Ini`. `WordsCli` is a thin console project on plain
`net10.0` with invariant globalization, referencing Authoring and Core and
never the editor.

## How it ships

With the editor, at its version (`WordsmithVersion`): each `editor/` release
carries Wordsmith for Windows and the command line for Windows, Linux and
macOS (x64, and Arm64 for macOS), each a self-contained, compressed and
trimmed single file of about 11 MB, so nothing needs .NET installed. They
are built on a Linux runner and packed as a zip for Windows and `.tar.gz`
for the others, which keeps the executable bit. The macOS builds are signed
ad hoc and not notarized, so a download is cleared with `xattr -d
com.apple.quarantine`, as the release notes say. The trimmer warns about
nothing, and every call runs trimmed as it does untrimmed. Trimming is the
command line's own project setting: passed on the command line it would
reach Core's build too.

## The agent skill

The packaged `SKILL.md` tells agents the tools exist and
where to get them: Wordsmith for a person editing on Windows, the command line
for an agent or a script on any platform, both from the editor's GitHub
Releases. An agent checks whether the command line is installed (`words
--version`), uses it for any change to a `words.ini` when it is, marks the
translations it writes stale, and edits by hand, keeping the format's
continuation and escaping rules, only when it is not.

## Tests

Headless:
- Fields: a changed field replaces only its lines, and the text it already
  holds leaves them as written; a field declared twice keeps its first
  place; a new field in the writer's order, ahead of the comment above the
  next, in a block of empty fields too, last in a block in none, and a
  group's under its header; the pair folded and continued as the writer
  writes it, reading back; a parameter of no type written as given.
- Keys: a new key joining its chain after its nearest sibling, the headers
  after it keeping their base; a group's second key cutting it, a deeper
  group's too, the writer saving the result to the same bytes; a lone key
  after the last block and before the trailer, and at the end of a file with
  no blocks; a blank line of its own only with a value, and none in a file
  without them.
- Removals: a field with its continuations, an empty one included; a block
  with its blank line, the last with none left behind; a block it empties; a
  reopened key everywhere; a header that bases children kept bare.
- Keys read past a `[]`.
- Bytes: a BOM, CRLF and a missing last break kept, each line of a mixed
  file its own break, UTF-16 and UTF-32 kept either way round; text not
  UTF-8, UTF-16 without its BOM and a NUL refused.
- The check and the save: only the gripes an edit adds reported; an edit
  spilling into a continued last line refused, nothing changed; a file
  changed since it was read refused, and another path written whole; Core,
  the words dialect and the writer reading a shared corpus alike; the parser
  numbering its visits.

The command, in process:
- Each call's output and exit code; stdin through the reader a pipe gets: a
  dash, a lone `\r` its last break, a leading U+FEFF kept and said, bytes
  that are no UTF-8 refused.
- `set`: `--stale` with words, without and before the value; a changed
  default staling the translations with words, keeping a mark there; a
  default set to its own text, with `\r\n`, a lone `\r` or through stdin,
  staling none; a translation that drops or adds a parameter written with a
  note, and none for one that keeps them or refers out of the file; a brace
  that opens nothing refused, the file left and the braces named, while
  `{{`, a context and a constant are written.
- Files: gripes passed on; a refused edit leaving the file; BOM-less UTF-16
  left alone; a missing or read-only file named without the calls, the
  read-only one with why; a log a call before broke off dropped first and
  said.
- `remove` of an empty field `get` calls none; a `[.child]` header before
  any base listed, read and removed but never written.
- `list` under a prefix, `--missing` by the badges' rule, `--stale` alone and
  beside `--missing`; `--stale-without` by a mark's start, given twice,
  narrowing `--stale` alone, refused alone or empty, and a loop of `request`
  and `apply` over it ending with nothing to ask.
- `request` for what `list` lists, under a prefix, up to a limit and with a
  brief; exit 1 with nothing to ask.
- `apply`: filled slots written, fenced as a model answers, each key marked
  stale in the writer's order, `--stale`'s words from a file; the request
  sent back untouched writing nothing; a changed link target written with
  the key's hand raised and named (exit 0), a hand already up keeping its
  text; a broken token or a default changed since skipped and named (exit
  1); no document, an undeclared language or a refused edit leaving the
  file (exit 2).

The translation document's own tests are the editor spec's *Machine
translation*. The editor's: keys and fields set in Wordsmith's own words
read back through the editor byte for byte.

# Planned upgrades

Its part in the editor's *Machine translation*, `list --stale`, `request` and
`apply`, is built (*Calls*); the editor's part is planned there.

## A block's first value spaces it

`set` giving the default's value to a key that has none, a key added by its
context first, writes the value into the block and no blank line after it,
where the editor writes one after each block with a value (*Surgical edits*):
the file then reads back through the editor with a line more. A new key set
by its value first is spaced right. The fix: a block that gains a value
gains the blank line after it, in a file spaced so, and one that loses it,
by `remove` or an empty `set`, loses the line. Until then, a new key's value
goes first; the keys the editor's *New* added to Wordsmith's own words went
so, and so did Machine translation's.

## Removing the last key keeps the blank line before it

`remove` of the file's last key takes the blank line that ends the block
before it with it, so a key `set` and then removed at the end leaves the file
a line short, and the next key added there goes unspaced, the file then
reading as one written without blank lines. The fix: the blank line after
the block before stays, as it would with a block after it. Until then, a
key added at the end by mistake is better undone by restoring the file.
