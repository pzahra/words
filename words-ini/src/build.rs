//! Build-time help: a build script turns a words.ini into a module with a
//! function per key, so a key that is not in the file, or a placeholder left
//! out, fails to compile.
//!
//! ```no_run
//! // build.rs
//! fn main() {
//!     words_ini::build::generate("assets/words.ini");
//! }
//! ```
//!
//! ```ignore
//! // main.rs
//! include!(concat!(env!("OUT_DIR"), "/words.rs"));
//!
//! fn main() {
//!     println!("{}", words::pool::status(3, "Main"));
//! }
//! ```

#![allow(clippy::needless_doctest_main)] // a build script is its main

use crate::dictionary::Dictionary;
use crate::parse::Store;
use crate::template::{self, Kind};
use crate::words::{key_of, relative};
use std::collections::{BTreeMap, BTreeSet, HashSet, VecDeque};
use std::fmt::Write as _;
use std::path::Path;

/// From a build script: reads the words.ini at `ini` (relative to the package),
/// writes `$OUT_DIR/words.rs` holding the module `words`, asks Cargo to build
/// again when the file changes, and reports what [`Dictionary::check`] finds as
/// build warnings. Panics, failing the build, when the file cannot be read or
/// two keys would make the same Rust name.
pub fn generate(ini: impl AsRef<Path>) {
    if let Err(problem) = try_generate(ini.as_ref(), "words") {
        panic!("words-ini: {problem}");
    }
}

/// [`generate`] with a module name of one's own, written to
/// `$OUT_DIR/<module>.rs`; or why it could not.
pub fn try_generate(ini: &Path, module: &str) -> Result<(), String> {
    let path = std::path::absolute(ini).map_err(|e| format!("{}: {e}", ini.display()))?;
    println!("cargo:rerun-if-changed={}", path.display());
    let text = std::fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
    //a warning is one line to Cargo, and a continued value's problem holds line breaks
    for problem in Dictionary::parse(&text).check() {
        println!("cargo:warning=words.ini: {}", problem.replace('\r', "\\r").replace('\n', "\\n"));
    }
    let file = path.to_str().ok_or_else(|| format!("{}: the path is not UTF-8", path.display()))?;
    let source = source(module, &format!("include_str!({file:?})"), &text)?;
    let out = std::env::var_os("OUT_DIR").ok_or("OUT_DIR is not set: call this from a build script")?;
    let target = Path::new(&out).join(format!("{module}.rs"));
    std::fs::write(&target, source).map_err(|e| format!("{}: {e}", target.display()))
}

/// The module's source for the words.ini `text`, which the module embeds with
/// the expression `include` (`include_str!("…")`); or why it cannot be made:
/// two keys whose functions would share a name, or a key whose placeholders
/// skip a number.
pub fn source(module: &str, include: &str, text: &str) -> Result<String, String> {
    let dictionary = Dictionary::parse(text);
    let store = dictionary.store();
    let mut root = Node::default();
    for key in dictionary.keys() {
        let segments: Vec<&str> = key.split('.').collect();
        let (name, parents) = segments.split_last().expect("a key has a segment");
        let mut node = &mut root;
        for segment in parents {
            let ident = ident(segment);
            let (child_segment, child) = node.modules.entry(ident.clone()).or_insert_with(|| (segment.to_string(), Node::default()));
            if child_segment.as_str() != *segment {
                return Err(format!("`{child_segment}` and `{segment}` would both be the module `{ident}`"));
            }
            node = child;
        }
        let ident = ident(name);
        if let Some(other) = node.functions.get(&ident) {
            return Err(format!("`{other}` and `{key}` would both be the function `{ident}`"));
        }
        if parents.is_empty() && RESERVED.contains(&ident.as_str()) {
            return Err(format!("`{key}` would be the function `{ident}`, which the module keeps for itself"));
        }
        node.functions.insert(ident, key.to_string());
    }

    let mut out = String::from("// Made by words-ini from a words.ini: change the words.ini, not this.\n\n");
    let doc = "a function per key: `[pool.status]` is\n/// `pool::status`, a placeholder is a parameter, and a key with plural forms\n/// takes its count first";
    out.push_str(&module_head("pub", &ident(module), doc, include, &dictionary.keys()));
    write_node(&mut out, store, &root, 1)?;
    out.push_str("}\n");
    Ok(out)
}

//the module's own items, as the generated module and embed! both hold them,
//through the last of them: the closing brace is the caller's to write
pub(crate) fn module_head(visibility: &str, module: &str, doc: &str, include: &str, keys: &[&str]) -> String {
    let keys = keys.iter().map(|key| format!("{key:?}")).collect::<Vec<_>>().join(", ");
    format!(
        "\
/// The words of the embedded words.ini, {doc}.
{visibility} mod {module} {{
    #![allow(dead_code, missing_docs, non_snake_case, non_upper_case_globals, unreachable_pub, clippy::all)]

    /// The words.ini this module was made from, embedded.
    pub static TEXT: &str = {include};

    /// Every key the file has words for.
    pub const KEYS: &[&str] = &[{keys}];

    static DICTIONARY: ::std::sync::OnceLock<::words_ini::Dictionary> = ::std::sync::OnceLock::new();
    static WORDS: ::std::sync::OnceLock<::words_ini::Words> = ::std::sync::OnceLock::new();

    /// Every language the embedded file holds.
    pub fn dictionary() -> &'static ::words_ini::Dictionary {{
        DICTIONARY.get_or_init(|| ::words_ini::Dictionary::parse(TEXT))
    }}

    /// The words every function here reads: the environment's language
    /// (`LC_ALL`, `LC_MESSAGES`, `LANG`), unless [`use_locale`] chose first.
    pub fn words() -> &'static ::words_ini::Words {{
        WORDS.get_or_init(|| dictionary().from_env())
    }}

    /// Chooses the language before anything reads words, as a `--lang` option
    /// does; `false` once words have been read, or chosen.
    pub fn use_locale(locale: &str) -> bool {{
        WORDS.get().is_none() && WORDS.set(dictionary().for_locale(locale)).is_ok()
    }}
"
    )
}

//whether a key has a plural form in any language: then its count comes first
pub(crate) fn has_forms(store: &Store, key: &str) -> bool {
    store.languages.values().any(|words| words.keys().any(|entry| entry.starts_with(key) && entry[key.len()..].starts_with('#')))
}

//names the module's own items take, which no key at its root may
const RESERVED: [&str; 7] = ["TEXT", "KEYS", "DICTIONARY", "WORDS", "dictionary", "words", "use_locale"];

#[derive(Default)]
struct Node {
    //each function's key, by its name
    functions: BTreeMap<String, String>,
    //each module's segment and node, by its name
    modules: BTreeMap<String, (String, Node)>,
}

fn write_node(out: &mut String, store: &Store, node: &Node, depth: usize) -> Result<(), String> {
    let indent = "    ".repeat(depth);
    //always a path, so a parameter named words cannot stand in for the function
    let root = if depth == 1 { "self::".to_string() } else { "super::".repeat(depth - 1) };
    for (name, key) in &node.functions {
        let (positional, named) = placeholders(store, key)?;
        let has_forms = has_forms(store, key);

        let mut taken = HashSet::new();
        let mut unique = |wanted: String| {
            let mut name = wanted;
            while !taken.insert(name.clone()) {
                name.push('_');
            }
            name
        };
        let count = has_forms.then(|| unique("count".to_string()));
        let positional: Vec<String> = (0..positional).map(|n| unique(format!("arg{n}"))).collect();
        let named: Vec<(String, String)> = named.into_iter().map(|name| (unique(ident(&name)), name)).collect();

        let mut parameters: Vec<String> = Vec::new();
        parameters.extend(count.iter().map(|count| format!("{count}: impl ::core::fmt::Display")));
        parameters.extend(positional.iter().chain(named.iter().map(|(ident, _)| ident)).map(|p| format!("{p}: impl ::core::fmt::Display")));
        let as_display = |ident: &str| format!("&{ident} as &dyn ::core::fmt::Display");
        let positional_args = positional.iter().map(|p| as_display(p)).collect::<Vec<_>>().join(", ");
        let named_args = named.iter().map(|(ident, name)| format!("({name:?}, {})", as_display(ident))).collect::<Vec<_>>().join(", ");
        let call = match (&count, positional.is_empty() && named.is_empty()) {
            (Some(count), true) => format!("{root}words().count({key:?}, {count})"),
            (Some(count), false) => format!("{root}words().count_with({key:?}, {count}, &[{positional_args}], &[{named_args}])"),
            (None, true) => format!("{root}words().get({key:?})"),
            (None, false) => format!("{root}words().format_with({key:?}, &[{positional_args}], &[{named_args}])"),
        };

        if !out.ends_with("{\n") {
            out.push('\n');
        }
        write_doc(out, &indent, store, key);
        let _ = writeln!(out, "{indent}pub fn {name}({}) -> ::std::string::String {{", parameters.join(", "));
        let _ = writeln!(out, "{indent}    {call}");
        let _ = writeln!(out, "{indent}}}");
    }
    for (name, (segment, child)) in &node.modules {
        let _ = writeln!(out, "\n{indent}/// The keys under `{segment}`.");
        let _ = writeln!(out, "{indent}pub mod {name} {{");
        write_node(out, store, child, depth + 1)?;
        let _ = writeln!(out, "{indent}}}");
    }
    Ok(())
}

//the key, and its default words as an example
fn write_doc(out: &mut String, indent: &str, store: &Store, key: &str) {
    let _ = writeln!(out, "{indent}/// `{key}`");
    let other = format!("{key}#other");
    let shown = std::iter::once("").chain(store.codes.iter().map(String::as_str)).find_map(|code| {
        let words = store.languages.get(code)?;
        words.get(key).or_else(|| words.get(&other))
    });
    if let Some(words) = shown {
        let _ = writeln!(out, "{indent}///\n{indent}/// ```text");
        for line in words.split('\n') {
            let _ = writeln!(out, "{indent}/// {}", line.replace("```", "'''"));
        }
        let _ = writeln!(out, "{indent}/// ```");
    }
}

//the arguments a key's words read, in every language, its forms' and what its
//references and selectors bring in included: how many by position, and the
//names in the order they first appear. A number no language reads below the
//highest is refused: it is a typo far more often than an argument left unread,
//and {1000} for {1} would make a function of 1001 parameters
pub(crate) fn placeholders(store: &Store, key: &str) -> Result<(usize, Vec<String>), String> {
    let mut codes: Vec<&str> = vec![""];
    codes.extend(store.codes.iter().map(String::as_str).filter(|code| !code.is_empty()));
    let forms_of = |key: &str| -> Vec<String> {
        let mut entries = vec![key.to_string()];
        let mut forms: Vec<String> = store
            .languages
            .values()
            .flat_map(|words| words.keys())
            .filter(|entry| entry.starts_with(key) && entry[key.len()..].starts_with('#'))
            .cloned()
            .collect();
        forms.sort();
        forms.dedup();
        entries.extend(forms);
        entries
    };

    let mut numbers = BTreeSet::new();
    let mut named: Vec<String> = Vec::new();
    let mut note = |argument: &str| {
        if argument.starts_with(|c: char| c.is_ascii_digit()) {
            numbers.extend(argument.parse::<usize>());
        } else if !named.iter().any(|name| name == argument) {
            named.push(argument.to_string());
        }
    };
    let mut seen = HashSet::new();
    let mut queue: VecDeque<String> = forms_of(key).into();
    while let Some(entry) = queue.pop_front() {
        if entry.starts_with('$') || !seen.insert(entry.clone()) {
            continue;
        }
        let base = key_of(&entry).to_string();
        for code in &codes {
            let Some(value) = store.languages.get(*code).and_then(|words| words.get(&entry)) else {
                continue;
            };
            for piece in template::parse(value) {
                match piece.kind {
                    Kind::Item { argument, .. } => note(&argument),
                    Kind::Selector { argument, key: target } => {
                        note(&argument);
                        queue.extend(forms_of(&relative(target, &base)));
                    }
                    Kind::Reference(target) => queue.push_back(relative(target, &base)),
                    _ => {}
                }
            }
        }
    }
    let positional = numbers.last().map_or(0, |highest| highest + 1);
    if let Some(skipped) = (0..positional).find(|n| !numbers.contains(n)) {
        return Err(format!(
            "`{key}` reads {{{}}} but no language reads {{{skipped}}}, so its function would take {positional} arguments: is a number mistyped?",
            positional - 1
        ));
    }
    Ok((positional, named))
}

//a Rust name for a key's segment or a placeholder's name: ASCII letters,
//digits and _ stay, and so do other scripts' letters; anything else is _. A
//name that cannot start as it does starts with _, and a keyword is raw, or
//takes a _ where it cannot be
fn ident(name: &str) -> String {
    let mut ident: String = name.chars().map(|c| if is_name_char(c) { c } else { '_' }).collect();
    if !ident.starts_with(|c: char| c == '_' || (is_name_char(c) && !c.is_ascii_digit())) {
        ident.insert(0, '_');
    }
    match ident.as_str() {
        "_" | "self" | "Self" | "super" | "crate" => ident.push('_'),
        keyword if KEYWORDS.contains(&keyword) => ident.insert_str(0, "r#"),
        _ => {}
    }
    ident
}

//a character a Rust name takes: an ASCII letter, digit or _, or another
//script's letter. Letters are what Rust's names are made of, past ASCII too;
//digits, marks and symbols there go, and so do the circled and squared
//letters, which are symbols that Unicode counts alphabetic
fn is_name_char(c: char) -> bool {
    if c.is_ascii() {
        return c.is_ascii_alphanumeric() || c == '_';
    }
    c.is_alphabetic()
        && !matches!(c, '\u{24B6}'..='\u{24E9}' | '\u{1F130}'..='\u{1F149}' | '\u{1F150}'..='\u{1F169}' | '\u{1F170}'..='\u{1F189}')
}

const KEYWORDS: [&str; 50] = [
    "as", "break", "const", "continue", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod",
    "move", "mut", "pub", "ref", "return", "static", "struct", "trait", "true", "type", "unsafe", "use", "where", "while", "async",
    "await", "dyn", "abstract", "become", "box", "do", "final", "macro", "override", "priv", "typeof", "unsized", "virtual", "yield",
    "try", "gen", "union", "raw",
];

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_name_rust_cannot_take_is_made_one() {
        assert_eq!(ident("file-open"), "file_open");
        assert_eq!(ident("404"), "_404");
        assert_eq!(ident("type"), "r#type");
        assert_eq!(ident("self"), "self_");
        assert_eq!(ident("_"), "__");
        assert_eq!(ident("Größe"), "Größe");
        assert_eq!(ident("日本"), "日本");
        assert_eq!(ident("area²"), "area_");
        assert_eq!(ident("step①"), "step_");
        assert_eq!(ident("Ⓐ"), "__");
        assert_eq!(ident("٤٠٤"), "___");
        assert_eq!(ident("e\u{301}"), "e_");
    }

    #[test]
    fn placeholders_come_from_every_language_and_what_a_value_brings_in() {
        let store = Dictionary::parse(
            "[a]\nvalue={0} {>.b} {Name}\nvalue-it={1} {Name} {Other}\n[.b]\nvalue={Inner} {2#w}\n[w]\nvalue=w\nvalue#other={Form}s\n[c]\nvalue={$k}{>c}\n[$k]\nvalue={Never}",
        );
        let (positional, named) = placeholders(store.store(), "a").unwrap();
        assert_eq!(positional, 3);
        assert_eq!(named, ["Name", "Other", "Inner", "Form"]);
        assert_eq!(placeholders(store.store(), "c"), Ok((0, vec![])));
    }

    #[test]
    fn a_number_no_language_reads_below_the_highest_is_refused() {
        let problem = source("words", "\"\"", "[a]\nvalue={0} {1}\nvalue-it={0} {1000}").unwrap_err();
        assert!(problem.contains("`a` reads {1000} but no language reads {2}"), "{problem}");
        assert!(source("words", "\"\"", "[a]\nvalue={1} {0}").is_ok());
    }

    #[test]
    fn a_parameter_cannot_stand_in_for_the_module_s_words() {
        let module = source("words", "\"\"", "[greet]\nvalue=You wrote {words} words\n[a.b]\nvalue={words}").unwrap();
        assert!(
            module.contains("pub fn greet(words: impl ::core::fmt::Display) -> ::std::string::String {\n        self::words()"),
            "{module}"
        );
        assert!(module.contains("super::words().format_with(\"a.b\""), "{module}");
    }

    #[test]
    fn two_keys_that_would_share_a_name_are_refused() {
        assert!(source("words", "\"\"", "[a.b-c]\nvalue=x\n[a.b_c]\nvalue=y").unwrap_err().contains("b_c"));
        assert!(source("words", "\"\"", "[x-y.a]\nvalue=x\n[x_y.b]\nvalue=y").unwrap_err().contains("x_y"));
        assert!(source("words", "\"\"", "[words]\nvalue=x").unwrap_err().contains("keeps for itself"));
        assert!(source("words", "\"\"", "[a.words]\nvalue=x\n[a]\nvalue=y").is_ok());
    }
}
