# words-ini

This is Words. It gives you Words, in Rust.

`words-ini` reads `words.ini`, the dictionary format of
[PatTech.Localization](../README.md), for a Rust command line. It reads the
same sections, fallbacks, plural forms, references, constants and escapes, and
means the same thing by them, as the .NET runtime does. What comes out is plain
text for a terminal. The words are embedded in the binary, every key is checked
when the app compiles, and there are no third-party dependencies and no
`unsafe`.

Keys get checked one of two ways: a build script that writes a function per
key, or a macro that checks each call. Pick one per crate.

## Getting it

It lives in this repository, so Cargo fetches it from git:

```toml
[dependencies]
words-ini = { git = "https://github.com/pzahra/words", branch = "words-ini" }
```

The build script needs it a second time, under `[build-dependencies]`, with
the same line. The macros need nothing more. They come in the default
`macros` feature, which a build-script crate can turn off with
`default-features = false`.

## A macro per call

Name the file once, at the crate root, before your modules:

```rust
// main.rs
words_ini::embed!("assets/words.ini");

fn main() {
    println!("{}", words!("pool.status", 3, Name = "Main"));
}
```

`words!` takes the key, then its arguments: by position for `{0}`, `{1}`…, and
`Name = value` for `{Name}`. A key with plural forms takes its count first, as
in `words!("file", 5)`. The compiler stops on a key the file lacks (suggesting
the nearest), on an argument too many or too few, and on a name the words
don't read, pointing at the key or the argument. Edit only the `words.ini` and
the next build checks every call again. A macro can't print warnings, though,
so the file's other problems wait for [the test](#a-test-for-the-file).

The path is relative to Cargo.toml. `embed!` also defines a module `words`,
holding everything the build script's module does except the functions:
`words::words()`, `words::use_locale(…)` and the rest below. A key chosen at
run time, like an enum's, isn't checked by either way; read it with
`words::words().format(key, …)` and test it.

## A function per key

The build script reads the file:

```rust
// build.rs
fn main() {
    words_ini::build::generate("assets/words.ini");
}
```

and the app includes what it made:

```rust
// main.rs
include!(concat!(env!("OUT_DIR"), "/words.rs"));

fn main() {
    println!("{}", words::pool::status(3, "Main"));
}
```

Given this file:

```ini
value=!en
value-ru=Русский

[file]
value=file
value#other=files
value-ru=файл
value-ru#few=файла
value-ru#many=файлов

[pool]
[.status]
value={Name}: {0} {0#file}
```

`words::pool::status(3, "Main")` reads `Main: 3 files`, or `Main: 3 файла`
under a Russian locale. `[pool.status]` became `pool::status`. `{0}` and
`{Name}` became its parameters. Misspell the key, or leave out a parameter,
and the app doesn't compile. A key with plural forms takes its count first, so
`words::file(5)` reads `files`. A dash in a key becomes `_`
(`file-open` is `file_open`), a name that starts with a digit gets a `_`
(`errors.404` is `errors::_404`), and a keyword stays raw (`r#type`). The
build stops if two keys would end up with the same name, and it reports what
[`check`](#a-test-for-the-file) finds as build warnings.

The module holds a few things besides the keys:

- `words::words()` is the `Words` every function reads.
- `words::dictionary()` holds every language in the file.
- `words::KEYS` lists every key.
- `words::TEXT` is the file itself.

## Which language

The environment says, the way POSIX tools listen: the first of `LC_ALL`,
`LC_MESSAGES` and `LANG` that is set. So `ru_RU.UTF-8`, `de_CH@euro` and
`zh_TW` read Russian, German and Traditional Chinese, if the file has words
for them. Where it has none, you get the default words, counted by the rules
of the language the file says they are written in (`value=!en`). An English
file therefore never reads "21 file" just because Russian counts 21 as
singular. Windows sets none of these variables, so there you get the default,
unless one is set.

A `--lang` option chooses before anything reads words:

```rust
if let Some(lang) = args.lang {
    words::use_locale(&lang);
}
```

## Without the build script

`Dictionary` reads any text, and `Words` is one language's words:

```rust
use words_ini::Dictionary;

let dictionary = Dictionary::parse(include_str!("../assets/words.ini"));
let words = dictionary.from_env();
println!("{}", words.format("pool.status", &[&3]));
println!("{}", words.format_named("greeting", &[("Name", &"Ada")]));
println!("{}", words.count("file", 2));
```

An argument is anything that implements `Display`, and a count is whatever
that argument shows: `3`, `-1` and `2.0` count, `1.5` picks `other`.

## When something is missing

Nothing in the file makes a call fail. A missing key reads `#key#`, a
placeholder with no argument reads `#{0}#`, a brace that opens nothing shows
as written, and a reference that loops back to itself reads `# ∞ #`. You'll
spot all of them in the output, and none of them panics.

## A test for the file

```rust
#[test]
fn words_ini_is_sound() {
    assert_eq!(words::dictionary().check(), Vec::<String>::new());
}
```

`check` lists anything a reader would trip over:

- a key with no default value;
- a reference, constant or selector naming words nobody holds;
- a brace that opens nothing;
- whatever the reading refused: a block whose name is no key's, a language
  that is no code, a form that is none.

Keys the app reads by name, rather than through the generated functions, can
be checked with `dictionary.missing(&["key", …])`.

## What it leaves out

The crate leaves out Markdown, UI bindings, live switching, the debug
brands (🕮 📚 🎲) and `Describe`. A .NET format spec like `{0:N2}` prints its
argument plainly. To change a `words.ini`, use the
[`words` command line](../WordsCli/readme.md) or Wordsmith. [SPEC.md](SPEC.md)
has the details.

## License

MIT, like the rest of Words.
