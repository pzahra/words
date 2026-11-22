# words-ini — what the crate guarantees

The [Core SPEC](../Localization-Core/SPEC.md) says what a `words.ini` holds and
what it means. The crate reads it the same way, and the cases both suites run
prove it (*Shared cases*). This file says what the crate does on its own
account, and what it leaves out.

## Reading a file

`Dictionary::parse` reads a file, and `load` stacks another over it, a later
value replacing an earlier one, as `WordsBuilder.Load` stacks them. The
grammar is Core's `WordsParser`:

- blocks, `[.child]` under the last full header, and `[]`, which reads past
  every field under it;
- `value=` and `value:`, a language suffix cased as a code, and a form
  lowercased;
- continuation lines, a trailing `\` keeping its newline and `_` keeping none,
  each read verbatim;
- `\\`, `__` and `''` each collapsing to one;
- comments, and unknown lines read past.

A leading BOM is dropped, as a file reader drops it. Lines split at `\r\n`,
`\n` and `\r`.

What Core keeps, the crate keeps. A key's `value` and its forms go in, and
`comment`, `context` and `param` are read past. A block whose name is no key's
is skipped with its children, and so is a field whose language is no code,
with its continuation lines. A form that is none is left out (`#one`, a
category CLDR lacks, any form on a label). Each of these is noted in
`warnings()` under Core's code: `WP:NAME`, `WP:LANG`, `WP:FORM`, `WP:STALE`,
`WP:WHO`, and `WB:KOVR` for a value replaced.

The crate checks key names (Core SPEC: *Key names*) with what the standard
library knows of Unicode. ASCII is checked exactly. Past ASCII, a segment
starts with a letter or a digit and goes on with anything but a space or a
control, and no name is checked for NFC. So a name Core refuses for a
non-ASCII character may still be read. A name Core reads is always read.

`languages()` lists the labelled languages that a `!` does not hide, in the
order met. `default_language()` reads the `value=!xx` declaration, trimmed and
cased.

**Tests.**
- Continuations, with a newline and without, escapes collapsing once, and an
  escaped end that does not continue.
- A continuation line read verbatim, though it looks like a comment or a block.
- Relative blocks; `[]` and a `[.child]` under it.
- Language casing; a bad language read past with its continuation; `param-`.
- Forms lowercased, continued and refused.
- A block that names no key skipped with its children, a constant's included.
- A second file's labels read after a skipped block; a later value winning.
- BOM and line endings.
- The key-name grammar, both ways.

## Rendering

`Words` is one language's words, flattened. `get` is Core's indexer:
references and constants render, and format items and selectors stay as
written. `format`, `format_named` and `format_with` are the `Format` family.
`count` and `count_with` are the count indexer, the second also filling items.

Rendering is Core's single pass (Core SPEC: *One pass for every brace*):

- `{{` is one brace, and `}` is always itself.
- A reference renders against its own key, so relatives chain.
- A constant is text.
- A selector picks its key's form under the language's CLDR rule.
- A circular reference or selector reads `# ∞ #`.
- A missing key reads `#key#`, and an item with nothing to fill it `#{0}#`.
- A malformed brace shows as written.

Forms fall back as Core's do (Core SPEC: *Which form a count reads*): the
category, then an optional category's stand-in, then `other`, then the plain
value. A key's forms come whole from the first level holding any of its words.
The CLDR table is Core's, and so is the optional-category table.

An argument is anything `Display`. A count is read from the text an argument
displays as: an optional sign, digits and an optional fraction. A fraction
that is all zeros counts as whole, any other picks `other`, and past 10^12
the low digits are kept, as Core keeps them. Anything else is no number and
picks `other`.

A few things differ from Core, because a `Display` value is not a .NET object:

- **Specs.** A spec, `{0:N2}`, goes unread, and the argument prints as it
  displays. Core formats it by `IFormattable`.
- **Alignment.** `{0,5}` pads to a width counted in characters. Core counts
  UTF-16 units.
- **`{0}` in a named call.** A named call's `{0}` reads the positional
  arguments, so with none it reads `#{0}#`. Core reads the value itself
  there.
- **Names past ASCII.** A placeholder's name, and a field's name, language
  and form, are .NET's `\w` characters, which the standard library cannot
  tell exactly. The crate reads letters, digits, `_` and the combining
  diacritics (U+0300–U+036F, so a decomposed `{Café}` is a name), and reads a
  character past the 16-bit plane as none, as .NET does, seeing two
  surrogates. Other combining marks end a name here and not in Core, and a
  number that is no digit (`²`, `①`) continues one here and not in Core.

**Tests.** The pieces: each kind and its place, `{{`, numbers as string.Format
reads them, names, a decomposed name and a letter past the 16-bit plane, and
every malformed shape. Rendering and fallback are covered by the shared cases.
Beyond those: a spec going unread and an alignment padding; a named call's
`{0}`; a count from integers, floats, a huge number and text.

## Choosing the language

`words(code)` is Core's `Flatten`, read as given. A culture's name reads as its
leading code. The empty string, or a name that starts with no code, reads the
default alone, counted as English, where Core throws.

`for_locale(locale)` reads a locale as an environment names it. It drops the
`.codeset` and the `@modifier`, and reads `_` as `-`. If the file has words for
the language, or for a code it falls back to, the locale reads that language.
Otherwise it reads the default, counted by the rules of the declared default's
language, or as English where none is declared. So no count is read by the
rules of a language the file has no words in. `C`, `POSIX` and the empty string
read the default.

`from_env()` is `for_locale` given the first of `LC_ALL`, `LC_MESSAGES` and
`LANG` that is set and not empty. Nothing else is asked: no OS call, so no
`unsafe` and no dependency. Windows usually sets none of these, so there the
default is read.

**Tests.** A locale with a codeset, with a modifier, and as a plain code; one
the file has no words for, counted by the default's rules; `C`, `POSIX` and
empty; an undeclared default counted as English; a code that is none; a
culture's name; the listing and the declaration.

## Keys checked at build time

`build::generate(path)`, called from a build script, writes the module `words`
to `$OUT_DIR/words.rs`. It asks Cargo to build again when the file changes,
and prints `check()`'s findings as `cargo:warning` lines. `try_generate` takes a
module name of one's own and returns the problem instead of panicking.
`build::source` is the pure part, for tests.

The module holds:

- one function per key, nested by segment (`[pool.status]` is
  `pool::status`), documented with the default's words;
- `TEXT`, the file, embedded with `include_str!`;
- `KEYS`;
- `dictionary()` and `words()`, each made once;
- `use_locale(locale)`, which chooses before the first read and returns `false`
  after it.

A function's parameters are the placeholders its words read in every
language, its forms' and what its references and selectors bring in
included: `arg0`, `arg1`… by position, then each name in the order it first
appears. A key with forms in any language takes its count first. A key with
no placeholders reads `get`; the others read `format_with`, or `count` and
`count_with`, always by a path to the module's own `words()`, so a
placeholder named `{words}` is just a parameter. Names follow Rust's rules:

- ASCII letters, digits and `_` stay, and so do other scripts' letters; `-`,
  and anything else, becomes `_` (`area²` is `area_`, and the circled
  letters, symbols that Unicode counts alphabetic, go too);
- a name that cannot start as it does takes a leading `_`;
- a keyword is raw, or takes a trailing `_` where it cannot be raw.

A name past ASCII may draw rustc's `uncommon_codepoints`,
`mixed_script_confusables` or `confusable_idents` warning, which only the
app's crate root can allow.

The build stops, saying why, on two keys whose names would meet, on a key at
the root named like one of the module's own items, and on a key whose
placeholders skip a number in every language (`{0}` and `{1000}`, a typo far
more often than an argument left unread). `check()`'s findings go to Cargo a
line each, a line break in one written `\n`.

**Tests.** A golden module (`tests/generated/words.rs`) that the build must
make exactly from `tests/fixtures/app.ini`; it is compiled and called, under
a chosen locale, through every kind of function. Names made from segments,
keywords, other scripts and characters Rust refuses; placeholders gathered
through languages, references, selectors and forms, but not constants; a
skipped number and meeting names refused; a `{words}` placeholder.

## Checks for a test

`check()` lists the warnings, each key with no default value, each reference,
constant or selector naming words the value's language does not reach (looked
for along its fallbacks, as rendering looks: `zh-Hant-TW`'s words find
`zh-Hant`'s, `zh`'s and the default's), and each malformed brace, naming the
key and the field (`value-it#few`). `missing(keys)` names the keys the default
has no words for.

**Tests.** A file with one of each problem lists exactly those; a reference
and a selector reached through fallbacks are none, and words in a sibling
language do not count. `missing` names what the default lacks.

## Left out

Core features that need .NET or a UI stay there:

- Markdown and UI bindings;
- live switching;
- `Debug`'s brands and marks (🕮 📚 🎲);
- `Describe`;
- custom formatters and culture-aware number formatting;
- `FormatParams`, `PreFormatByName`, and an `IWords` of one's own.

The crate only reads. Changing a file is the `words` command line's job.

## Shared cases

`tests/fixtures/cases.txt` lists a fixture, a language, a call, a key, the
arguments and the text expected. The crate's `tests/shared.rs` and Core's
`Localization-Tests/WordsIniCasesTests.cs` both run every row, so a change on
either side that reads a case differently fails a test. Core is given a number
argument as a number, as an app would pass a count. A row whose reading
differs by design (*Rendering*) belongs in the crate's own tests, not here.

**Tests.** Plural forms in English, Russian, Japanese, French, Italian and
Maltese, through selectors, named selectors and the count indexer. A
translation that keeps the default's forms out. Selectors through sub-keys,
forms that refer and select, and a key selecting its own forms. References,
relatives, constants, loops, missing keys and items, alignment, escapes and
malformed braces; a decomposed placeholder name, and a name holding a letter
past the 16-bit plane, which is malformed. Fallback through shorter codes and Chinese scripts, and
`pt-PT` counting apart from `pt`.

# Planned upgrades

Not built.

## Releases

The crate's version is in its `Cargo.toml`, and apps take it from this
repository by branch. Undecided: a `crate/` tag line for releases, as `api/`
and `editor/` have, whether CI checks the tag against `Cargo.toml` as it
checks `Versions.props`, and whether the crate goes to crates.io.
