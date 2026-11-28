//! What a `words!` call has to give, worked out from the file. No `proc_macro`
//! types here, so it is tested as any code is.

use crate::build::{has_forms, placeholders};
use crate::dictionary::Dictionary;

/// What a key's words read: a count first when it has plural forms, then how
/// many arguments by position, then the names, as the generated functions take
/// them.
#[derive(Debug, PartialEq, Eq)]
pub(crate) struct Shape {
    pub count: bool,
    pub positional: usize,
    pub named: Vec<String>,
}

/// Where an error points: at the key, or at one of the arguments.
#[derive(Debug, PartialEq, Eq)]
pub(crate) enum Blame {
    Key,
    Positional(usize),
    Named(usize),
}

/// What `key` reads, or why it reads nothing: no such key, or placeholders that
/// skip a number.
pub(crate) fn shape(dictionary: &Dictionary, key: &str) -> Result<Shape, String> {
    let keys = dictionary.keys();
    if !keys.contains(&key) {
        return Err(match closest(key, &keys) {
            Some(near) => format!("words.ini has no key `{key}`: did you mean `{near}`?"),
            None => format!("words.ini has no key `{key}`"),
        });
    }
    let (positional, named) = placeholders(dictionary.store(), key)?;
    Ok(Shape { count: has_forms(dictionary.store(), key), positional, named })
}

/// Whether a call giving `positional` arguments by position and `named` by name
/// fits `shape`; else what is wrong, and where to point.
pub(crate) fn fit(key: &str, shape: &Shape, positional: usize, named: &[String]) -> Result<(), (String, Blame)> {
    let wanted = usize::from(shape.count) + shape.positional;
    if positional > wanted {
        return Err((format!("`{key}` takes {}: this argument is one too many", describe(shape)), Blame::Positional(wanted)));
    }
    if positional < wanted {
        let given = match positional {
            0 => "none was given".to_string(),
            1 => "1 was given".to_string(),
            n => format!("{n} were given"),
        };
        return Err((format!("`{key}` takes {} by position, and {given}", describe_positional(shape)), Blame::Key));
    }
    for (at, name) in named.iter().enumerate() {
        if named[..at].contains(name) {
            return Err((format!("`{{{name}}}` is given twice"), Blame::Named(at)));
        }
        if !shape.named.contains(name) {
            let reads = match shape.named.as_slice() {
                [] => "none by name".to_string(),
                names => names.iter().map(|n| format!("`{{{n}}}`")).collect::<Vec<_>>().join(", "),
            };
            return Err((format!("`{key}` reads no `{{{name}}}`: it reads {reads}"), Blame::Named(at)));
        }
    }
    if let Some(missing) = shape.named.iter().find(|name| !named.contains(name)) {
        return Err((format!("`{key}` reads `{{{missing}}}`, which this call does not give: add `{missing} = …`"), Blame::Key));
    }
    Ok(())
}

//what a key takes, in words: a count, then 2 by position ({0}, {1}), and {Name} by name
fn describe(shape: &Shape) -> String {
    let mut parts = Vec::new();
    if shape.count || shape.positional > 0 {
        parts.push(format!("{} by position", describe_positional(shape)));
    }
    if !shape.named.is_empty() {
        parts.push(format!("{} by name", shape.named.iter().map(|n| format!("`{{{n}}}`")).collect::<Vec<_>>().join(", ")));
    }
    if parts.is_empty() { "no arguments".to_string() } else { parts.join(", and ") }
}

fn describe_positional(shape: &Shape) -> String {
    let items: Vec<String> = (0..shape.positional).map(|n| format!("{{{n}}}")).collect();
    let arguments = match shape.positional {
        0 => String::new(),
        1 => format!("1 argument ({})", items[0]),
        n => format!("{n} arguments ({})", items.join(", ")),
    };
    match (shape.count, arguments.is_empty()) {
        (true, true) => "a count".to_string(),
        (true, false) => format!("a count, then {arguments}"),
        (false, true) => "no arguments".to_string(),
        (false, false) => arguments,
    }
}

//the key nearest a misspelt one, if any is near: within a third of its length
fn closest<'k>(key: &str, keys: &[&'k str]) -> Option<&'k str> {
    let limit = (key.chars().count() / 3).max(2);
    keys.iter().map(|k| (distance(key, k), *k)).filter(|(d, _)| *d <= limit).min().map(|(_, k)| k)
}

//Levenshtein's distance, by characters
fn distance(a: &str, b: &str) -> usize {
    let b: Vec<char> = b.chars().collect();
    let mut row: Vec<usize> = (0..=b.len()).collect();
    for (i, ca) in a.chars().enumerate() {
        let mut diagonal = row[0];
        row[0] = i + 1;
        for (j, cb) in b.iter().enumerate() {
            let above = row[j + 1];
            row[j + 1] = (diagonal + usize::from(ca != *cb)).min(row[j] + 1).min(above + 1);
            diagonal = above;
        }
    }
    row[b.len()]
}

/// A string literal's value, from its source text: `"…"` with Rust's escapes,
/// or a raw `r#"…"#`. None for anything else, a byte string included.
pub(crate) fn unquote(text: &str) -> Option<String> {
    if let Some(raw) = text.strip_prefix('r') {
        let hashes = raw.len() - raw.trim_start_matches('#').len();
        let fence = &raw[..hashes];
        return raw[hashes..].strip_prefix('"')?.strip_suffix(fence)?.strip_suffix('"').map(str::to_string);
    }
    let inner = text.strip_prefix('"')?.strip_suffix('"')?;
    let mut out = String::new();
    let mut chars = inner.chars().peekable();
    while let Some(c) = chars.next() {
        if c != '\\' {
            out.push(c);
            continue;
        }
        match chars.next()? {
            'n' => out.push('\n'),
            'r' => out.push('\r'),
            't' => out.push('\t'),
            '0' => out.push('\0'),
            '\\' => out.push('\\'),
            '\'' => out.push('\''),
            '"' => out.push('"'),
            'x' => {
                let hex: String = [chars.next()?, chars.next()?].iter().collect();
                out.push(char::from(u8::from_str_radix(&hex, 16).ok()?));
            }
            'u' => {
                if chars.next()? != '{' {
                    return None;
                }
                let hex: String = chars.by_ref().take_while(|&c| c != '}').filter(|&c| c != '_').collect();
                out.push(char::from_u32(u32::from_str_radix(&hex, 16).ok()?)?);
            }
            //a line break escaped: it and the spaces after it go
            '\n' | '\r' => {
                while chars.peek().is_some_and(|c| c.is_whitespace()) {
                    chars.next();
                }
            }
            _ => return None,
        }
    }
    Some(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    const INI: &str = "[file]\nvalue=file\nvalue#other=files\n[pool]\n[.status]\nvalue={Name}: {0} {0#file}\n[files]\nvalue={0} in {Pool}\nvalue#other={0} files in {Pool}\n[hi]\nvalue=Hi\n[gap]\nvalue={0} {2}\n";

    fn shape_of(key: &str) -> Result<Shape, String> {
        shape(&Dictionary::parse(INI), key)
    }

    fn names(names: &[&str]) -> Vec<String> {
        names.iter().map(|n| n.to_string()).collect()
    }

    #[test]
    fn a_key_reads_what_the_generated_function_takes() {
        assert_eq!(shape_of("pool.status"), Ok(Shape { count: false, positional: 1, named: names(&["Name"]) }));
        assert_eq!(shape_of("file"), Ok(Shape { count: true, positional: 0, named: vec![] }));
        assert_eq!(shape_of("files"), Ok(Shape { count: true, positional: 1, named: names(&["Pool"]) }));
        assert_eq!(shape_of("hi"), Ok(Shape { count: false, positional: 0, named: vec![] }));
    }

    #[test]
    fn a_missing_key_names_the_nearest() {
        assert_eq!(shape_of("pool.statsu"), Err("words.ini has no key `pool.statsu`: did you mean `pool.status`?".to_string()));
        assert_eq!(shape_of("nothing.like.it"), Err("words.ini has no key `nothing.like.it`".to_string()));
        assert!(shape_of("gap").unwrap_err().contains("no language reads {1}"));
    }

    #[test]
    fn a_call_fits_or_says_what_is_wrong_and_where() {
        let status = shape_of("pool.status").unwrap();
        assert_eq!(fit("pool.status", &status, 1, &names(&["Name"])), Ok(()));
        assert_eq!(
            fit("pool.status", &status, 0, &names(&["Name"])),
            Err(("`pool.status` takes 1 argument ({0}) by position, and none was given".to_string(), Blame::Key))
        );
        assert_eq!(
            fit("pool.status", &status, 2, &names(&["Name"])),
            Err((
                "`pool.status` takes 1 argument ({0}) by position, and `{Name}` by name: this argument is one too many".to_string(),
                Blame::Positional(1)
            ))
        );
        assert_eq!(
            fit("pool.status", &status, 1, &names(&["Nmae"])),
            Err(("`pool.status` reads no `{Nmae}`: it reads `{Name}`".to_string(), Blame::Named(0)))
        );
        assert_eq!(
            fit("pool.status", &status, 1, &[]),
            Err(("`pool.status` reads `{Name}`, which this call does not give: add `Name = …`".to_string(), Blame::Key))
        );
        assert_eq!(
            fit("pool.status", &status, 1, &names(&["Name", "Name"])),
            Err(("`{Name}` is given twice".to_string(), Blame::Named(1)))
        );
        let file = shape_of("file").unwrap();
        assert_eq!(fit("file", &file, 1, &[]), Ok(()));
        assert_eq!(fit("file", &file, 0, &[]), Err(("`file` takes a count by position, and none was given".to_string(), Blame::Key)));
        let hi = shape_of("hi").unwrap();
        assert_eq!(
            fit("hi", &hi, 1, &[]),
            Err(("`hi` takes no arguments: this argument is one too many".to_string(), Blame::Positional(0)))
        );
        assert_eq!(fit("hi", &hi, 0, &names(&["X"])), Err(("`hi` reads no `{X}`: it reads none by name".to_string(), Blame::Named(0))));
    }

    #[test]
    fn a_literal_reads_as_rust_reads_it() {
        assert_eq!(unquote(r#""pool.status""#).as_deref(), Some("pool.status"));
        assert_eq!(unquote(r#""a\\b\"c\n\u{e9}\x41""#).as_deref(), Some("a\\b\"c\né\x41"));
        assert_eq!(unquote(r###"r#"C:\path"#"###).as_deref(), Some("C:\\path"));
        assert_eq!(unquote(r#"r"x""#).as_deref(), Some("x"));
        assert_eq!(unquote(r#"b"x""#), None);
        assert_eq!(unquote("pool"), None);
        assert_eq!(unquote("\"a\\\n    b\""), Some("ab".to_string()));
    }
}
