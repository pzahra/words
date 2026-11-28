//! The macros of words-ini. `embed!` names the words.ini once, and `words!`
//! reads a key from it, the key and its arguments checked while the app
//! compiles. Use them through words-ini, which re-exports them.
//!
//! The reader's own sources come in by path, so the check is the reader's rule
//! for rule: the same keys, the same placeholders, the same count first.

#![forbid(unsafe_code)]

#[allow(dead_code)]
#[path = "../../src/build.rs"]
mod build;
#[allow(dead_code)]
#[path = "../../src/code.rs"]
mod code;
#[allow(dead_code, clippy::wrong_self_convention)]
#[path = "../../src/dictionary.rs"]
mod dictionary;
#[allow(dead_code)]
#[path = "../../src/parse.rs"]
mod parse;
#[allow(dead_code)]
#[path = "../../src/plural.rs"]
mod plural;
#[allow(dead_code)]
#[path = "../../src/template.rs"]
mod template;
#[allow(dead_code)]
#[path = "../../src/words.rs"]
mod words;

mod call;

use call::Blame;
use dictionary::Dictionary;
use proc_macro::{Delimiter, Group, Literal, Punct, Spacing, Span, TokenStream, TokenTree};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::SystemTime;

/// Embeds a words.ini and defines `words!`, which reads a key from it, the key
/// and its arguments checked while compiling. Call it once, at the crate root,
/// before the modules that use `words!`, with the file's path relative to
/// Cargo.toml:
///
/// ```ignore
/// words_ini::embed!("assets/words.ini");
///
/// fn main() {
///     println!("{}", words!("pool.status", 3, Name = "Main"));
/// }
/// ```
///
/// It defines the module `words`, as the build script's has it but without a
/// function per key: `words::words()`, `words::use_locale(…)`,
/// `words::dictionary()`, `words::KEYS` and `words::TEXT`.
///
/// `words!` takes the key as a string literal, then the arguments: by
/// position for `{0}`, `{1}`…, and `Name = value` for `{Name}`. A key with
/// plural forms takes its count first. Each argument is anything that
/// implements `Display`. A key the file lacks, an argument too many or too few,
/// or a name the words don't read, fails to compile, pointing at the key or the
/// argument. An argument holding a comma outside brackets, such as a turbofish
/// with two types, wants parentheses.
#[proc_macro]
pub fn embed(input: TokenStream) -> TokenStream {
    embed_tokens(input).unwrap_or_else(|error| error)
}

/// What `words!` expands to, the words.ini's path first; `embed!` defines
/// `words!` to call it.
#[doc(hidden)]
#[proc_macro]
pub fn __words(input: TokenStream) -> TokenStream {
    words_tokens(input).unwrap_or_else(|error| error)
}

fn embed_tokens(input: TokenStream) -> Result<TokenStream, TokenStream> {
    let usage = "embed! takes the words.ini's path, relative to Cargo.toml: embed!(\"assets/words.ini\")";
    let mut segments = split(input.into_iter().collect());
    if segments.len() == 2 && segments[1].is_empty() {
        segments.pop(); //a trailing comma
    }
    let (relative, span) = match segments.as_slice() {
        [segment] => string_literal(segment).ok_or_else(|| error(usage, span_of(segment)))?,
        _ => return Err(error(usage, Span::call_site())),
    };
    let manifest = std::env::var_os("CARGO_MANIFEST_DIR").map(PathBuf::from).unwrap_or_default();
    let path = manifest.join(&relative);
    let dictionary = load(&path).map_err(|problem| error(&problem, span))?;
    let file = path.to_str().ok_or_else(|| error("the words.ini's path is not UTF-8", span))?;
    let literal = Literal::string(file).to_string();

    let doc = "read with `words!`, which checks\n/// each key and its arguments while the app compiles";
    let mut source = build::module_head("pub(crate)", "words", doc, &format!("include_str!({literal})"), &dictionary.keys());
    source.push_str("}\n\n");
    source.push_str(&format!(
        "#[allow(unused_macros)]\nmacro_rules! words {{\n    ($($args:tt)*) => {{ ::words_ini::__words!({literal}, $($args)*) }};\n}}\n"
    ));
    Ok(tokens(&source))
}

fn words_tokens(input: TokenStream) -> Result<TokenStream, TokenStream> {
    let mut segments = split(input.into_iter().collect());
    if segments.last().is_some_and(Vec::is_empty) && segments.len() > 2 {
        segments.pop(); //a trailing comma
    }
    let Some((path, _)) = segments.first().and_then(|segment| string_literal(segment)) else {
        return Err(error("words! is defined by embed!: call embed!(\"assets/words.ini\") at the crate root", Span::call_site()));
    };
    let Some(key_segment) = segments.get(1).filter(|segment| !segment.is_empty()) else {
        return Err(error("words! takes a key, then its arguments: words!(\"pool.status\", 3)", Span::call_site()));
    };
    let Some((key, key_span)) = string_literal(key_segment) else {
        return Err(error(
            "a key in words! is a string literal; a key chosen at run time reads words::words().format(key, …)",
            span_of(key_segment),
        ));
    };

    let mut positional: Vec<(TokenStream, Span)> = Vec::new();
    let mut named: Vec<(String, Span, TokenStream)> = Vec::new();
    for segment in &segments[2..] {
        match segment.as_slice() {
            [] => return Err(error("an argument is missing between two commas", key_span)),
            [TokenTree::Ident(name), TokenTree::Punct(equals), value @ ..]
                if equals.as_char() == '=' && equals.spacing() == Spacing::Alone && !value.is_empty() =>
            {
                let name = name.to_string();
                named.push((name.strip_prefix("r#").unwrap_or(&name).to_string(), span_of(segment), value.iter().cloned().collect()));
            }
            _ => positional.push((segment.iter().cloned().collect(), span_of(segment))),
        }
    }

    let dictionary = load(Path::new(&path)).map_err(|problem| error(&problem, key_span))?;
    let shape = call::shape(&dictionary, &key).map_err(|problem| error(&problem, key_span))?;
    let names: Vec<String> = named.iter().map(|(name, _, _)| name.clone()).collect();
    if let Err((problem, blame)) = call::fit(&key, &shape, positional.len(), &names) {
        let span = match blame {
            Blame::Key => key_span,
            Blame::Positional(at) => positional[at].1,
            Blame::Named(at) => named[at].1,
        };
        return Err(error(&problem, span));
    }

    //the lookup the generated function would make, the arguments spliced in
    //with their own spans, so a type error points at the argument
    let mut arguments = TokenStream::new();
    let mut key_literal = Literal::string(&key);
    key_literal.set_span(key_span);
    arguments.extend([TokenTree::Literal(key_literal)]);
    let mut positional = positional.into_iter();
    if shape.count {
        let (count, span) = positional.next().expect("fit counted the count");
        arguments.extend([comma()]);
        arguments.extend(borrowed(count, span));
    }
    let rest: Vec<(TokenStream, Span)> = positional.collect();
    let method = match (shape.count, rest.is_empty() && named.is_empty()) {
        (false, true) => "get",
        (true, true) => "count",
        (true, false) => "count_with",
        (false, false) => "format_with",
    };
    if !(rest.is_empty() && named.is_empty()) {
        let mut items = TokenStream::new();
        for (value, span) in rest {
            items.extend(as_display(value, span));
            items.extend([comma()]);
        }
        arguments.extend([comma(), punct('&'), group(Delimiter::Bracket, items)]);
        let mut pairs = TokenStream::new();
        for (name, _, value) in named {
            let span = value.clone().into_iter().next().map_or_else(Span::call_site, |token| token.span());
            let mut pair = TokenStream::from(TokenTree::Literal(Literal::string(&name)));
            pair.extend([comma()]);
            pair.extend(as_display(value, span));
            pairs.extend([group(Delimiter::Parenthesis, pair), comma()]);
        }
        arguments.extend([comma(), punct('&'), group(Delimiter::Bracket, pairs)]);
    }
    let mut expansion = tokens(&format!("crate::words::words().{method}"));
    expansion.extend([group(Delimiter::Parenthesis, arguments)]);
    Ok(expansion)
}

//the words.ini at path, read once a compilation: a file that changed since,
//as an editor's long-lived macro server would see it, is read again
fn load(path: &Path) -> Result<Arc<Dictionary>, String> {
    type Entry = (PathBuf, Option<SystemTime>, u64, Arc<Dictionary>);
    static CACHE: Mutex<Vec<Entry>> = Mutex::new(Vec::new());
    let meta = std::fs::metadata(path).map_err(|e| format!("cannot read {}: {e}", path.display()))?;
    let (modified, length) = (meta.modified().ok(), meta.len());
    let mut cache = CACHE.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    if let Some((_, _, _, dictionary)) = cache.iter().find(|(p, m, l, _)| p == path && *m == modified && *l == length) {
        return Ok(dictionary.clone());
    }
    let text = std::fs::read_to_string(path).map_err(|e| format!("cannot read {}: {e}", path.display()))?;
    let dictionary = Arc::new(Dictionary::parse(&text));
    cache.retain(|(p, _, _, _)| p != path);
    cache.push((path.to_path_buf(), modified, length, dictionary.clone()));
    Ok(dictionary)
}

//the tokens split at the commas between them, a bracket's insides kept whole
fn split(tokens: Vec<TokenTree>) -> Vec<Vec<TokenTree>> {
    let mut segments = vec![Vec::new()];
    for token in tokens {
        match &token {
            TokenTree::Punct(p) if p.as_char() == ',' => segments.push(Vec::new()),
            _ => segments.last_mut().expect("one segment at least").push(token),
        }
    }
    segments
}

//a segment that is one string literal, its value and its span; a macro's
//expression fragment arrives wrapped in an invisible group, looked into
fn string_literal(segment: &[TokenTree]) -> Option<(String, Span)> {
    match segment {
        [TokenTree::Literal(literal)] => call::unquote(&literal.to_string()).map(|value| (value, literal.span())),
        [TokenTree::Group(group)] if group.delimiter() == Delimiter::None => {
            string_literal(&group.stream().into_iter().collect::<Vec<_>>())
        }
        _ => None,
    }
}

fn span_of(segment: &[TokenTree]) -> Span {
    segment.first().map_or_else(Span::call_site, TokenTree::span)
}

//&(value), spanned as the argument, so a type error points at it
fn borrowed(value: TokenStream, span: Span) -> TokenStream {
    let mut parenthesized = Group::new(Delimiter::Parenthesis, value);
    parenthesized.set_span(span);
    let mut ampersand = Punct::new('&', Spacing::Alone);
    ampersand.set_span(span);
    TokenStream::from_iter([TokenTree::Punct(ampersand), TokenTree::Group(parenthesized)])
}

//&(value) as &dyn ::core::fmt::Display, spanned as the argument
fn as_display(value: TokenStream, span: Span) -> TokenStream {
    let mut out = borrowed(value, span);
    out.extend(tokens("as &dyn ::core::fmt::Display").into_iter().map(|mut token| {
        token.set_span(span);
        token
    }));
    out
}

fn tokens(source: &str) -> TokenStream {
    source.parse().expect("the macro writes valid tokens")
}

fn group(delimiter: Delimiter, inside: TokenStream) -> TokenTree {
    TokenTree::Group(Group::new(delimiter, inside))
}

fn punct(c: char) -> TokenTree {
    TokenTree::Punct(Punct::new(c, Spacing::Alone))
}

fn comma() -> TokenTree {
    punct(',')
}

//::core::compile_error!("problem"), pointing at span
fn error(problem: &str, span: Span) -> TokenStream {
    let mut literal = Literal::string(problem);
    literal.set_span(span);
    let mut message = Group::new(Delimiter::Parenthesis, TokenTree::Literal(literal).into());
    message.set_span(span);
    let mut out: Vec<TokenTree> = tokens("::core::compile_error!").into_iter().collect();
    for token in &mut out {
        token.set_span(span);
    }
    out.push(TokenTree::Group(message));
    out.into_iter().collect()
}
