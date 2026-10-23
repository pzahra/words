# PatTech.Text.Ini — what the ini driver is supposed to do

A transactional driver for ini files: it loads a file whole, edits only the
elements asked for, holds the edits until they are committed, checks the
result, and writes it once, written ahead to a hidden log beside it so a
failure or a crash partway is finished on the next open. A library of its own,
with a version line of its own, so it can ship as its own package; it knows
nothing of Words. It targets what Core does (`net10.0`, `net8.0`). It is not
packaged yet; when it is, it follows the others: a version in
`Versions.props` and a tag of its own (`ini/`) that its release runs on.

Words is its first user: Authoring references it for every ini the project
writes, and Core, a read-only engine, does not (the editor spec's *Save as a
patch*, [../WordsEdit/SPEC.md](../WordsEdit/SPEC.md)). Built so far: the
first step of its order, the document, the sections dialect, values and
comments, lists, the transaction, the commit and recovery; the dotted
dialect and its cuts are next.

## A transaction

A transaction is one file.

```csharp
using var file = IniTransaction.Open(path, IniDialect.Sections(rules));
file.Set("editor", "tips", "off");
file.RemoveSection("seen");
file.Commit();
```

**A document.** The transaction loads the file whole, as lines, each with
its own break (CR, LF or CRLF, as read, and a last line without one left
so), in its own encoding and byte-order mark, located by its dialect into
sections and their keys, comments and blank lines kept where they stand. An
edit names the element it changes, a key in a section or a section whole,
and replaces only the lines that hold it; every other line stays, byte for
byte, but one: a line added after a last line that had no break gives that
line one, and the new last line goes without, so a file that ended without
a break still does. A line added takes the break of the file's first line
that has one; in a file with none, the dialect's where its rules name one,
or else the system's. The edits are held in memory: a key set three times is
written once, and a transaction disposed without `Commit` leaves the file as
it was, as it never touched it.

**Elements.** The dialect cuts the lines into elements: a block of comment
lines, a run of blank lines, a section header, a comment sharing a line with
another element, a field, and a field's value, every line of it where it
runs on. A line the dialect does not recognise is no element: an edit never
touches it, and it stays where it stands, byte for byte, while the elements
around it change.

**The section `""`.** Fields above the first header are in the section named
`""`, so a section's name is never null, and a `[]` header, where a dialect
takes it, opens that section again further down, as any section named twice
is one. Its first occurrence has no header and stays at the head of the
file: a new section `""` starts there, above the first header, and a key set
in it lands there. Renamed, the head gains a header of the new name; moved,
its fields are gathered under a `[]` at the new place. A dialect that takes
no `[]` leaves such a header and the lines under it to the next as lines it
does not recognise.

A document can also be replaced whole, with text and an encoding the caller
gives, for a file written in a form of the caller's own rather than patched:
the commit, the check of the file on disk and the log are the same. A file
absent when opened is a new file, and has nothing to patch: it is replaced
whole, or built from edits on nothing.

The file is decoded strictly. A byte-order mark names its encoding; without
one, it reads as UTF-8, the best most dialects can take, unless its
dialect's rules name another. One with a byte its encoding does not read is
no text to patch: it is refused for edits, and can still be replaced whole,
the check of the file on disk going by its bytes.

**A section named twice** is one section, as ini reads it: its keys are
every occurrence's, the last of a key declared twice winning. An edit acts
on the one section. A key set replaces its first declaration and removes
the others, so an occurrence left with nothing goes, as any section the
edits emptied does; a section removed or renamed goes, or is renamed, in
every occurrence; one moved is gathered whole at its new place.

**Where an edit lands.** A key added goes after its section's last field,
or under its header when it has none; the head with none starts the file,
a blank line parting it from what follows. A section moved goes after the
other's last field, after a blank line. A section going, removed, moved or
emptied, takes its header, its fields and the blank runs that part nothing
left standing, and one that ended the file the blank run above its header
too; its comment blocks and the lines no element stand. An element read
before an edit, a comment block to edit or move, is refused once it no
longer stands where it stood.

**Its dialect** is the file's grammar and its rules, how names compare
among them: without case, or exactly. The library carries two, and a user
extends either:
- `IniDialect.Sections(rules)`: `[section]` headers and `key=value` lines:
  set or clear a key, replace a section whole in
  an order the rules give, add a section at the end after a blank line,
  remove one, move one after another, rename one, and add, edit, move or
  remove a comment block. The rules say which encodings the file may be in and whether
  it carries a byte-order mark: one may be ANSI or UTF-8 with none, refusing
  UTF-16 and UTF-32; another keeps the encoding it was read in.
- The dotted dialect: section names in dotted segments, a `[.child]` header
  chaining off the last full one (`[group.a]` then `[.b]` is `group.a.b`),
  and a cut strategy that decides how the chains are cut (*Cutting*), which
  moving and renaming a section go through.

**Values and comments.** How a value runs past its line, and what a
comment is, are the dialect's too, each a rule it picks, so one library
reads a `words.ini`, a `schema.ini` and a settings file as each is written:
- **Separators**: `=`, `:`, or either.
- **Continuations**: a line ending in a marker runs on: `\` with a line
  break kept, `_` in the same line. A marker meant as itself is escaped, and
  what escapes it is the dialect's: doubled (`\\`, `__`), or, as Words reads
  it, a marker taking whatever character follows it, so a line runs on only
  when it ends in a marker nothing took (`one\_` and `one_\` end in a pair,
  and run on to nothing). A line that runs on takes the next line as its
  continuation whatever that line holds: a header, a comment, a blank. A
  dialect takes either marker, both or neither.
- **Quoted strings**: a value in `'…'` or `"…"`, its spaces at either end
  its own and its line breaks raw, running on over as many lines as it
  takes until its closing quote; the quote meant as itself is doubled. A
  dialect takes either quote, both or neither.
- **Comments**: lines starting `;`, `#`, or either. Comment lines in a run
  are one block, and blank lines part blocks. A block is an element like any
  other, edited, moved and removed as one, and standing where it is while
  its neighbours move or go: which section a comment is about is its
  client's to say, not the library's.
- **Comments after an entry**: whether a comment may start on a line that
  holds an entry (`width=900 ; at 100%`). Where it may, the marker starts one
  only after a space and outside quotes; elsewhere it is part of the value.
  An edit to that value keeps the comment after it.

A value is read through its continuations and quotes into the text it
holds, and the edits and the check deal in that text. An edit writes a
value back in the form it was written in, continued or quoted, while that
form can hold it; a new value, or one its form cannot hold, takes the
dialect's first choice. A line edited is the only line that changes.

**Lists.** `IniList` splits a value that is a list, `"comma separated",
arrays`, into its items: parted by commas, trimmed, an item in quotes taking
commas, spaces and doubled quotes as its own. Joined back, an item is quoted
only where it needs to be.

**Cutting.** In the dotted dialect, how the chains are cut is the cut
strategy's (`ICutStrategy`, and `GroupCuts`, which bases a group whose
chained sections are worth a header of their own). An edit that adds,
renames, moves or removes a section asks the strategy about the group the
section lands in or leaves, its siblings under the same parent, and the
strategy may change those siblings' headers and nothing else:
- chain a section off a base already there: with `[group.a]`, `[.b]` and
  `[.c]`, adding `group.a.d` adds `[.d]` to the chain;
- leave a section a full header of its own: with `[group.a]`, adding
  `group.b` may leave `[group.b]` beside it;
- add a bare header as a new base and chain the siblings off it: the same
  `group.b` may instead make `[group]`, with `[.a]` and `[.b]` under it;
- drop a bare base the edits left basing nothing.

A header line is all it changes; a section's keys, values and comments stay
as they are. A bare base, a header with no keys of its own that bases a
chain, is structure: the check neither asks for one nor refuses one. A chain
no edit touched keeps its cut as written. The strategy decides from the
document as edited, at commit, so successive edits are cut as one.

**Renaming** is one section's: its name changes, its keys go with it, and a
section chained off it keeps its own name, the strategy re-cutting the chain
it was in (a full header, or a new base). A client renaming a subtree
renames each section in it, in the one transaction, and the strategy chains
them under the new name. A rename onto a name another section has is
refused; a swap goes through a third name.

**Trimmed in memory.** A section the edits emptied goes, and a bare base
they left basing nothing. A section left empty by hand, or holding only
comments, stays.

**Checked before it lands.** The document, edited, is parsed again by its
dialect and compared with what it read, the edits applied: every section's
pairs, bare bases aside, and whatever more a dialect that extends it
compares. Anything else refuses the edit with what would have changed, and
leaves the document as it was before that edit, so a caller can try another
way or give up. A file that will not encode in its encoding (a lone
surrogate, a character an ANSI page lacks) is refused when its bytes are
made, at `Prepare`, before a byte lands.

## The commit

A commit is written ahead to a log, `.<name>.tmp` beside the file
(`.words.ini.tmp` for `words.ini`), hidden on Windows, as Office keeps its
own beside a document. The log is the commit: once it is whole, the
transaction stands, and whatever stops the file's write is finished from it.

**The file is the lock.** A read through the library opens the file
letting others read and none write, and only then looks for its log: one
there, it lets the file go and settles the log first, so it never reads a
commit half written, nor one that ended pending while it looked. A commit holds the file open for writing, letting others read and
none write or move it, from the moment it checks the file to the moment it
has written it, so nothing that honours the system's sharing changes the
file in between. Windows enforces sharing; elsewhere it is advisory, and
only those who take part are kept out. A reader that lets writers in (most
editors, a runtime reading the file at startup) can still catch a write
half done; the log puts it right for the next reader through the library.

1. **Prepare.** The edits are made into the new bytes, encoded, a file that
   will not encode refused. The log is created new and held open, shared
   with none, until the commit ends: one there and held is another commit
   running, waited for a moment, then refused; one there and not held is
   settled first, as an open settles it. The file, where there is one, is
   opened and held as above, a file held by someone else tried again a few times over half a
   second and then refused, and read through that handle: it must still be
   what was read, the same bytes by hash, or still absent for a new file,
   which is not made yet, the log alone holding its place. One changed since
   (another editor, a checkout, another process) is refused, naming it.
2. **Write ahead.** The log gets the file's hash before, the new bytes whole,
   and last a trailer with their length and hash, and is flushed to disk.
   The new bytes go whole, not the edits, since a replay cannot lean on the
   file it is repairing. A log that fails to write or flush is refused: it
   goes, and the file was never touched.
3. **Write the file.** Through the held handle, or a new file created now,
   written over with the new bytes, cut to their length, and flushed.
   Written in place, it keeps what it is: its hard links, its attributes (a
   hidden file stays hidden), its permissions and its Unix mode's ordinary
   bits (a system may clear setuid and setgid on a write).
4. **Let go.** The file is let go, and the log deleted.

`Commit` prepares when it has not been; a caller that must know the file
will take its write before it does something else calls `Prepare` first,
and holds the file until `Commit` or `Dispose`. A transaction prepared and
disposed lets the file go and deletes the log, nothing written.

**Recovery.** Opening a file first looks for its log, and settles it before
the file is read:
- Held by another process: a commit is running. The open waits a moment,
  then is refused, naming it.
- Without its trailer, or one that does not match: the log was never whole,
  so the file was never touched. It goes.
- Whole, and the file at its hash after: the write finished. It goes.
- Whole, and the file at its hash before, or absent for a new file: the
  write never began. The file is written, or created, from the log, and the
  log goes.
- Whole, and the file at neither hash: a write broken off, or as easily
  someone else's change since the crash. The file's bytes are kept first,
  written to `.<name>.recovered.tmp`, flushed, and renamed
  `.<name>.recovered` once whole; one already there is never written over,
  the next taking `.<name>.recovered-2` and on. Then the file is written
  from the log, and the recovery says where its bytes were kept. A
  `.recovered.tmp` left by a crash goes; a recovery cut off after keeping
  the bytes keeps them again on the next, a second copy rather than a lost
  one.

Settling takes the log as a commit does, shared with none, and holds the
file as a commit does. A log that will not settle (the file still held) is
kept for the next open, and the file is refused until it settles, naming it.

**What a crash is.** A process killed or failing anywhere in a commit or a
recovery is finished or undone by the next open. A machine losing its power
is not promised: the contents are flushed, but a log's name or a rename may
not outlive it, and a file found half written with no log is put right from
a backup.

**Outcomes.** A commit ends one of three ways:
- **Landed**: the file holds the new bytes. A log that would not delete is
  no failure, and is noted on the result: the next open finds the file at
  its hash after and deletes it.
- **Refused**: nothing written, the file as it was, the log gone. A typed
  exception names why: the file changed on disk, held by someone else,
  denied (read-only, or not the user's to write), another commit running, a
  log that will not settle, a check, an encoding, the log's own write. Everything about the file refuses at `Prepare`;
  after it, only the log's write can.
- **Pending**: the log is whole, so the commit stands, but the file's write
  failed, or broke off and failed again when tried once more. A typed
  exception says so, and where the log sits: the next open through the
  library finishes it.

**What lands.** The same file, rewritten. A symbolic link, anywhere in the
path the last part included, is followed to its file, and the log sits
beside that file. A hard link reads the new text, as the file is written in
place; but each name of a hard-linked file finds only its own log, so a
commit cut off through one name is finished by an open through that name.

**Failures are data.** The library has no words of its own to say a thing
in: a refusal or a pending commit is a typed exception carrying the file,
what changed on disk or what the check found, and where any log or kept
bytes sit, and its caller says it in its own. Each is an `IOException`, so
a caller that catches what a file throws catches it too.

## Where it comes from

The pieces are Authoring's today, and move down: the cut strategies from
`IniWriter`, taken from keys to section names; the held-file retry and the
link followed from `IniPatcher`, following a link at the path's end too.
The temporary sibling and its Unix mode do not: the file is written in
place.

**Order.**
1. The document, the sections dialect, values and comments, the
   transaction, the commit and recovery.
2. The dotted dialect and the cut strategies.

## Tests

- The documents: every line outside an edit kept byte for byte, each with
  its break and the file's encoding; a line added after an unbroken last
  line gives it the break and goes without one; a line added takes the
  file's first break, and in a file with none the dialect's, else the
  system's; a file with no byte-order mark read as UTF-8, or as its rules
  say; fields above the first header in the section `""`, a `[]` further
  down opening it again where the dialect takes it and left alone where it
  does not, a new `""` starting at the head, and the head renamed and moved;
  a line no element stays where it stands, byte for
  byte, while the elements around it are edited, moved and removed; a key
  set many times written once; a transaction
  disposed uncommitted leaves the file untouched; a refused edit leaves the
  document as it was; a document replaced whole, over a file and over none;
  the sections dialect's encodings refused as their rules say; a file with a
  byte its encoding does not read refused for edits and replaced whole;
  names differing only in case one section without case and two exactly,
  and a rename by case alone.
- A section named twice: its keys read as one, the last of a key winning; a
  key set lands in the first occurrence and an occurrence left empty goes;
  a section removed, renamed or moved acts on every occurrence, gathered
  where it moves.
- Values and comments, each rule on and off: a continued value, a quoted
  one over several lines and an escaped marker read as their text and write
  back in their own form; `one\_` and `one_\` run on to nothing with
  markers that take what follows; a continued line takes a header, a comment
  or a blank as its continuation; a value its form cannot hold takes the
  dialect's first; a comment block stands while the element below it moves
  or goes, and moves or goes itself when asked; a comment
  after an entry outlives an edit to its value, and a marker in quotes or
  without a space before it is the value's; a list splits and joins, quoted
  items and all.
- Cutting: a section added to a chain joins it; a strategy that bases a new
  group re-heads its siblings and changes no other line; a bare base the
  edits left basing nothing goes; an untouched chain keeps its cut; several
  edits in one transaction cut as one; a renamed section's chained sections
  keep their names, re-cut, and a subtree renamed section by section chains
  under the new name; a rename onto a name taken is refused; the check passes
  a bare base either way.
- Trimming: a section the edits emptied goes, one empty by hand or holding
  only comments stays.
- The commit: a file changed after it was read is refused, nothing written
  and no log left; a second commit on the file waits for the first's log,
  then is refused; a read through the library waits out a commit's write,
  and one that looked for the log just before a commit ended pending finds
  it once it holds the file and settles it; a log that fails to write or
  flush is refused, the file untouched; a new file prepared and disposed, or
  killed before its log is whole, leaves no file; a
  file held a moment is written once it lets go, one held for good is
  refused at `Prepare`; a write that fails after the log is whole is pending
  and finished by the next open; a log that will not delete still lands; a
  prepared transaction disposed writes nothing; a hard link, a hidden file
  and a Unix mode's ordinary bits are kept; a symbolic link, the path's last part included,
  is followed.
- Recovery, from each log by hand and by a process killed at each step: a
  log without its trailer goes and the file is untouched; a whole log over a
  file already written goes; one over a file at its hash before writes it,
  and one over a new file never made creates it;
  a file at neither hash, broken off or changed, is kept as `.recovered` first,
  a second as `.recovered-2`, never written over; a recovery killed while
  keeping the bytes or while writing the file is finished by the next; a log
  still held refuses the open.
