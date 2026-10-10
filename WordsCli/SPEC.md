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
carries it whole; since a tool writing a BOM puts one there too, a line on
stderr says it was kept. A `param-` text is written as given, since its words
before the first `:` are a type only where they name one (the editor spec's
*Parameters*), so a text that names none reads back whole:

- `get <file> <key> [field]` prints a field's value, unescaped, or the whole
  block as Save writes it, under a full header, when no field is named.
- `set <file> <key> <field> <value>` sets one field, adding the key, or the
  field, where it is missing. A default that changes, the plain value or a
  form, marks every translation with words and no stale mark yet stale with
  the time, as typing it in Wordsmith does (the editor spec's *The baseline pane*); one set to
  the text it holds marks nothing. `--stale [text]` marks the language's entry
  stale with it (the default's mark keeps no words), so the review filter
  surfaces a machine-written value. Options go anywhere after the call:
  `--stale` takes the next argument as its words only once the value is in,
  and `--stale=text` always does. A translation it writes is checked against
  the default by the editor's rule (the editor spec's *Parameters*,
  Translation check): one that drops a parameter the default uses, or uses
  one the default does not and no `param-` defines, is written all the same,
  with a note naming them. The file is all it sees, so a side with a
  reference out of it may carry anything, and is not held to the other.
- `remove <file> <key> [field]` drops one field, every declaration of it, or
  the whole key. A field is there to drop when the file declares it, with
  words or empty, though `get` reads an empty one as none, exit 1.
- `list <file> [prefix]` prints the keys in the order their blocks first
  appear, leaving out a bare group header as the editor drops it; the prefix
  is plain text, so `menu.` lists a group. `--missing xx` lists only those
  where `xx` misses its words, by the badges' rule (the editor spec's *Plural forms*, Badges): no
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
field declared twice is made one, or a translation drops or adds a parameter. The exit code is 0 for done, 1 when the
key or field is not there, 2 for a bad call, a file that is missing, can't be
read or written, does not parse or is no text the patcher reads, or a refused
edit; a file's failure names the file, which the system's own message may not,
and a missing one is no misuse, so the calls are not shown. Redirected streams
speak UTF-8 with `\n`; a console keeps its own.

## Surgical edits

`IniPatcher` reads the file's bytes by the BOMs
Wordsmith's Load reads (the editor spec's *Saving*): UTF-8 with or without one,
UTF-16 or UTF-32 with one, UTF-32 LE's checked first since it starts with
UTF-16 LE's. Text holding a NUL is refused: UTF-16 without its BOM reads as
UTF-8 with a NUL in every other byte, and would otherwise be written into. A
value holding one is refused too, as it would leave the file unreadable. The
text is parsed, noting where each header and field sits. The one field's lines (its declaration and its continuations) are
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
link is followed to its file, and a Unix file keeps its mode. As with Save,
what lands is a new file: a Windows hidden attribute does not carry over, and
a hard link to the old one keeps the old text.

## Where it lives

The patcher and `WordsField` — a field's name, parsed,
read and written on the model — are in Authoring, tested headless. The parser
reports where it is through `IWordsParserConsumer.VisitLine`, a member with a
default body, so existing consumers do not change. The command line itself,
`WordsCli`, is a thin console project over it on plain `net10.0`, with
invariant globalization: it runs wherever .NET does, references Authoring and
Core and never the editor, and Authoring stays free of anything Windows-only.

## How it ships

With the editor, at its version (`WordsmithVersion`): each
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

## The agent skill

The packaged `SKILL.md` tells agents the tools exist and
where to get them: Wordsmith for a person editing on Windows, the command line
for an agent or a script on any platform, both from the editor's GitHub
Releases. An agent checks whether the command line is installed (`words
--version`), uses it for any change to a `words.ini` when it is, marks the
translations it writes stale, and edits by hand, keeping the format's
continuation and escaping rules, only when it is not.

## Tests

Headless: a changed field replaces its lines and nothing else; a
new field lands after its block's last, and a group's under its header with
the chain intact; a new key lands after its sibling's chain as a full header,
a lone one after the last block and before the trailer, and one in a file with
no blocks at its end; the pair is written as the writer writes it, continued
and folded, and reads back; a BOM, CRLF and a missing final break survive,
each line of a mixed file keeps its own break, UTF-16 stays UTF-16 and UTF-32
UTF-32, either way round; text that is not UTF-8 does not open, nor UTF-16
without its BOM, and no value writes a NUL; the text a field already holds
leaves its hand-written lines; a field declared twice keeps its first place; a
parameter of no type is written as given; only the gripes an edit
adds are reported; an edit that would spill into a continued last line is
refused and changes nothing; a removal drops a field with its continuations,
an empty one included, a block with one blank line, the last block with none
left behind, a reopened key everywhere, and keeps bare a header that bases
children; the parser numbers its visits. The command, in process: each call's
output and exit code, a dash read from stdin, a lone `\r` its last break, a
leading U+FEFF kept and said, bytes that are no UTF-8 refused, all through the
reader the program puts on a pipe; `--stale` with words, without and before
the value, a changed default staling its translations with words and keeping
a mark there, a translation that drops or adds a parameter written with a
note and none for one that keeps them or refers out of the file, gripes
passed on, a refused edit leaving the file, BOM-less UTF-16
left alone, a missing or read-only file named without the calls, an empty
field removed though `get` calls it none, a `[.child]` header before any base
listed, read and removed but never written, and `list` with a prefix and
`--missing` by the badges' rule.

# Planned upgrades

Not built yet. Each section here is the shape the feature takes when it is.

## What a caller fixes by hand

The command line is a tool for agents first, the one working on this
repository among them, and used in earnest it leaves three things for the
agent to fix by hand after it.

**Cuts.** A new key goes in as a full header after its sibling's chain, a
blank line before it, where the writer would cut it otherwise: the five
`.unit` keys the Enums page gained came out as five blocks standing apart,
`[enums.brew.espresso.unit]` and kin, where Save writes a `[.unit]` in each
chain (the editor spec's *Round-trip guarantees*). A file Save wrote stops
reading as Save writes it, and Wordsmith's own words, whose round trip is
pinned byte for byte, fail their test until the headers are moved by hand.
The full header was chosen because a full header inserted in a chain
re-bases the `[.child]` headers after it; a `[.child]` header does not,
since each one extends the chain's full header and not the one above it. So
a new key that extends a chain's full header goes into that chain, as a
`[.child]`, after its nearest sibling's block and with no blank line, as the
writer chains it; one that extends none is cut as the writer's cut strategy
(`GroupCuts`) would cut it, which for most is today's full header.

**Field order.** A new field goes after its block's last, where the writer
has an order: context, comment, the value and its forms, the parameters,
then each language's value, forms and stale mark. A `context=` added to a
key with a translation lands after `value-it`, and the same round-trip test
fails until it is moved. A new field goes where the writer would put it,
among fields already in its order; in a block written in no order, where
there is no such place, it goes last as today.

**Fresh.** A default that changes stales every translation with words, which
is right when the translations are not being touched, and wrong when the
same caller is about to write each of them: it sets the default, then each
translation, then removes each stale mark it just caused. `--fresh` on a
default's `set` stales nothing, so a correction made in every language at
once leaves the review filter as it was; a mark already there stays, as
always. It is a default's option, a bad call with a translation's field or
beside `--stale`.

**Tests.** A new key extending a chain's full header lands in the chain as a
`[.child]`, after its nearest sibling's block with no blank line, and the
`[.child]` headers after it keep their base; one extending none lands as the
cut strategy cuts it; a key added to Wordsmith's words reads back through
the editor byte for byte. A new `context=` lands before the values, a new
`value-it` after the default's forms and before a later language's value,
and a field added to a block in no order lands last. `--fresh` on a default
stales nothing and keeps a mark already there, and is a bad call on a
translation or beside `--stale`.
