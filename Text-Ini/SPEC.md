# PatTech.Text.Ini — what the ini driver is supposed to do

A transactional driver for ini files: it loads a file whole, edits only the
elements asked for, checks the result, and writes it once, written ahead to
a hidden log beside it so a failure or a crash partway is finished on the
next open. A library of its own that knows nothing of Words, targeting what
Core does (`net10.0`, `net8.0`). It is not packaged yet; when it is, it gets
a version in `Versions.props` and a tag of its own (`ini/`).

Authoring references it for every ini the project edits in place; Core, a
read-only engine, does not (the editor spec's *Ini files through the
driver*, [../WordsEdit/SPEC.md](../WordsEdit/SPEC.md)).

## A transaction

A transaction is one file.

```csharp
using var file = IniTransaction.Open(path, IniDialect.Sections(rules));
file.Set("editor", "tips", "off");
file.RemoveSection("seen");
file.Commit();
```

**A document.** The file loads whole, as lines, each with its own break (CR,
LF or CRLF; a last line without one stays so), in its own encoding and
byte-order mark. An edit replaces only the lines of the element it names;
every other line stays byte for byte. A line added after an unbroken last
line, or written over it, gives it a break and goes without one; a line left
last by a removal keeps its own. A line added takes the break of the file's
first line that has one, else the dialect's, else the system's.
Edits are held in memory: a key set three times is written once, and a
transaction disposed without `Commit` leaves the file untouched.

**Elements.** Comment blocks, blank runs, headers, fields (every line of a
continued value), and a comment sharing a line with an entry. A line the
dialect does not recognise is no element: no edit touches it, and it stays
where it stands.

**The section `""`** holds the fields above the first header; a `[]`
header, where the dialect takes one, opens it again further down. A new
`""` starts at the head; renamed, the head gains a header; moved, its fields
are gathered under a `[]`. A dialect that takes no `[]` leaves such a header
and the lines under it unrecognised.

**Replaced whole.** A document can be replaced with the caller's text and
encoding, committed and checked on disk the same way. A file absent when
opened is new: replaced whole, or built from edits.

**Decoding** is strict. A byte-order mark names the encoding; without one,
UTF-8 unless the rules name another. A byte the encoding does not read, or a
NUL, makes the file no text: refused for edits, still replaceable whole.

**A section named twice** is one section, the last of a key declared twice
winning. A key set replaces its first declaration and removes the others; a
section removed or renamed acts on every occurrence; one moved is gathered
whole.

**Where an edit lands.** A key added goes after its section's last field, or
under its header; the head with none starts the file, a blank line parting
it from what follows. A section moved goes after the other's last field,
after a blank line. A section going takes its header, its fields and the
blank runs that then part nothing, and, if it ended the file, the blank run
above its header; comment blocks and unrecognised lines stand. An element
read before an edit (a comment block to edit or move) is refused once it no
longer stands where it stood, or once another just like it has come to stand
there.

**Its dialect** is the file's grammar and rules. Section names compare
without case or exactly, and keys may compare by a rule of their own. Two
ship, and either can be extended:
- `IniDialect.Sections(rules)`: `[section]` headers and `key=value` lines.
  Set or clear a key, replace a section in the order given, add one at the
  end after a blank line, remove, move or rename one, and add, edit, move or
  remove a comment block. The rules say which encodings a file may be in and
  whether a byte-order mark is kept, dropped or refused.
- `IniDialect.Dotted(rules, cuts)`: dotted names, a `[.child]` header
  chaining off the last full one (`[group.a]` then `[.b]` is `group.a.b`), a
  `[]` ending the chain, and a cut strategy (*Cutting*). A new section goes
  beside its nearest sibling: into the chain whose base it extends, after
  the header there sharing the most leading segments, the last of them; else
  after the chain holding the section sharing the most; else after the last
  block. It goes past a blank line after the block it follows, and has one
  of its own before what follows where that block had one.

A dialect that extends one may say which names are keys, how a field is
written (its whole lines), where a new key and a new section go, and what
more the check compares.

**Values and comments**, each a rule the dialect picks:
- **Separators**: `=`, `:`, or either.
- **Continuations**: a line ending in a marker runs on, `\` keeping the line
  break, `_` joining the lines. A marker meant as itself is doubled, or, as
  Words reads it, a marker takes the character after it, so `one\_` and
  `one_\` end in a pair and run on to nothing. A line that runs on takes the
  next line whatever it holds: a header, a comment, a blank.
- **Quotes**: `'…'` or `"…"`, their spaces and breaks the value's own,
  running on until the closing quote; a doubled quote is one.
- **Comments**: lines starting `;`, `#`, or either. A run of them is one
  block, edited, moved and removed as one, standing while its neighbours
  move or go.
- **Comments after an entry**: where allowed, a marker after a space and
  outside quotes starts one, and an edit to the value keeps it; spaces after
  the separator part one from an empty value (`x= ; note`).
- **Lines**: whether a header or a field may be indented (else it is
  unrecognised), whether anything may follow a header's `]` (read past),
  whether the spaces inside a header's brackets are trimmed from its name
  (else they are its own), and whether a value's trailing spaces are its own.

A value reads through its continuations and quotes into its text. An edit
writes it back in its own form while that form holds it, else in the
dialect's first. A NUL, in a value, a name or a comment, is refused.

**Lists.** `IniList` splits `"comma separated", arrays` into trimmed items,
a quoted item taking commas, spaces and doubled quotes as its own; joined
back, an item is quoted only where it needs to be.

**Cutting.** In the dotted dialect, each edit asks the cut strategy
(`ICutStrategy`) about the groups it touched, from the names of the sections
holding keys. `GroupCuts` bases a group no header names once two sections
would chain off it, a section past a deeper group that heads its own
counting there. The cut changes header lines only:
- a section chains off a base already there;
- a group worth it gains a bare base, its sections chained off it, but for a
  deeper chain among them, which keeps its own cut;
- a deeper group gains its own base, taking its sections from an ancestor's
  chain;
- a bare base left basing nothing goes.

A bare base is structure: the check neither asks for one nor refuses one. A
chain no edit touched keeps its cut, and successive edits end cut as one.

**Renaming** changes one section's name, its keys going with it; the
sections chained off it keep their names and are re-cut. A subtree renamed
section by section chains under the new name. A rename onto a name taken is
refused.

**Trimmed in memory.** A section the edits emptied goes, replaced with no
pairs too, but a header that bases a chain stays, bare. A section empty by hand, or holding only
comments, stays.

**Checked before it lands.** The edited document is parsed again and
compared with what was read plus the edits: every section's pairs, bare
bases aside, and whatever more an extending dialect compares. A difference
refuses the edit, saying what would have changed, and leaves the document
as it was. A file that will not encode is refused at `Prepare`.

**A batch** makes many edits as one: each placed and cut as it would be
alone, the document checked once, after the last, against what it was
before the first plus what they all mean. Refused, by the check or by an
edit, none of them stands. What the edits mean is what they asked: a key
one added that a later one reads away is still expected. A batch inside one
is part of it, and a document is replaced whole, or prepared, outside one.

## The commit

A commit is written ahead to a log, `.<name>.tmp` beside the file, hidden on
Windows. Once the log is whole, the commit stands.

**The file is the lock.** A read through the library opens the file letting
others read and none write, then looks for its log, settling one it finds.
A commit holds the file the same way from its check to its write. Windows
enforces the sharing; elsewhere it is advisory.

1. **Prepare.** The new bytes are made and encoded. The log is created and
   held, shared with none: one held already is another commit, waited for a
   moment and then refused; one left over is settled first. The file is held
   (one another program holds is tried for half a second, then refused) and
   must still be what was read, by hash, or still absent: else refused.
2. **Write ahead.** The log gets the file's hash before, the new bytes
   whole, and a trailer with their length and hash, and is flushed. A log
   that will not write is refused, the file untouched, the log emptied and
   deleted; one that will do neither and reads whole is pending.
3. **Write the file** in place, through the held handle or created now, cut
   to length and flushed: it keeps its hard links, attributes, permissions
   and Unix mode's ordinary bits.
4. **Let go**: the log deleted while still held, so no later commit's log of
   the same name is the one deleted, then the log and the file let go.

`Commit` prepares first if needed. `Prepare` alone holds the file until
`Commit` or `Dispose`; disposing lets it go and deletes the log.

**Recovery.** An open settles a log before reading the file:
- held: a commit is running; the open waits a moment, then is refused;
- not whole: the file was never touched; the log goes;
- whole, the file at its hash after: finished; the log goes, the file only
  read;
- whole, the file at its hash before, or absent for a new file: written from
  the log;
- whole, the file at neither hash: its bytes are kept first, as
  `.<name>.recovered` (then `-2` and on, never written over), and it is
  written from the log.

A log that will not settle is kept, and the file refused until it does,
saying where any bytes were kept. A
process killed anywhere in a commit or a recovery is finished or undone by
the next open; a power loss is not promised.

**Outcomes.**
- **Landed**: the file holds the new bytes. A log that would not delete is
  noted, and the next open clears it.
- **Refused**: nothing written, no log left that could stand, a typed
  exception saying why
  (changed on disk, held, denied, another commit running, a log that will
  not settle, the check, an encoding, the log's own write). All but the
  log's write refuse at `Prepare`.
- **Pending**: the log is whole, but the file's write failed twice; the next
  open finishes it.

A symbolic link anywhere in the path is followed, the log sitting beside its
target; each name of a hard-linked file finds only its own log.

**Failures are data.** The library has no words of its own: each refusal or
pending commit is a typed `IOException` carrying the file, what changed or
what the check found, and where any log or kept bytes sit.

## Tests

- Documents: lines outside an edit kept byte for byte with their breaks; a
  line added after an unbroken last line, and the break a line added takes;
  decoding by the rules; the section `""` at the head and under `[]`, taken
  or left; unrecognised lines standing; a key set many times written once;
  disposed uncommitted, untouched; a refused edit undone; a batch placed
  edit by edit and checked once, refused whole by its check or by an edit,
  one inside another part of it, no replacing inside one; replaced whole,
  over a file and over none; no text refused for edits; names with and
  without case, keys by their own rule, a rename by case alone; indents, a
  header's tail, a header's spaces and trailing spaces, each rule on and off.
- A section named twice: read as one, set in its first occurrence, removed,
  renamed and moved in every occurrence.
- Values and comments, each rule on and off: continued, quoted and escaped
  values read and written back in their form; `one\_` and `one_\`; a
  continuation taking a header, a comment or a blank; a value its form
  cannot hold; comment blocks standing, moving and going; a comment after an
  entry outliving an edit; lists split and joined.
- Cutting: chains read and ended by `[]`; a new section beside its sibling,
  joining its chain; a new base re-heading its siblings and nothing else; a
  deeper cut taking its run from an ancestor's base, which then goes; a bare
  base left basing nothing going; a header that bases a chain staying bare;
  an untouched chain keeping its cut; several edits cut as one; renames
  keeping chained names, a subtree renamed, a taken name refused.
- Trimming: emptied by edits goes; empty by hand, or comments only, stays.
- The commit: a file changed after reading refused with no log left; a
  second commit waiting, then refused; a read waiting out a write, and one
  finding a log that ended pending; a log that fails refused; a new file
  disposed or killed early leaving no file; a held file written once it
  lets go, one held for good refused; pending finished by the next open; a
  log that will not delete still landing; hard links, hidden files, Unix
  modes kept; symbolic links followed.
- Recovery, by hand and by a process killed at each step: each case above,
  `.recovered` and `.recovered-2` never written over, a recovery killed
  midway finished by the next, a held log refusing the open.
