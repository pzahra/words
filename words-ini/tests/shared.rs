//! The cases Core runs too (tests/fixtures/cases.txt): each fixture, language
//! and call reads the same text from the crate as from PatTech.Localization.

use std::fmt::Display;
use std::path::Path;
use words_ini::Dictionary;

fn fixtures() -> &'static Path {
    Path::new(concat!(env!("CARGO_MANIFEST_DIR"), "/tests/fixtures"))
}

//\n is a line break and \\ a backslash
fn unescape(text: &str) -> String {
    let mut out = String::new();
    let mut chars = text.chars();
    while let Some(c) = chars.next() {
        match (c, c == '\\') {
            (_, true) => match chars.next() {
                Some('n') => out.push('\n'),
                Some(other) => out.push(other),
                None => out.push('\\'),
            },
            _ => out.push(c),
        }
    }
    out
}

#[test]
fn every_shared_case_reads_as_core_reads_it() {
    let cases = std::fs::read_to_string(fixtures().join("cases.txt")).unwrap();
    let mut failures = Vec::new();
    let mut count = 0;
    for line in cases.lines().map(|line| line.trim_end_matches('\r')) {
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        let fields: Vec<&str> = line.splitn(6, " | ").collect();
        let [file, language, call, key, args, expected] = fields[..] else {
            panic!("a case is six fields: {line}");
        };
        let text = std::fs::read_to_string(fixtures().join(file)).unwrap();
        let words = Dictionary::parse(&text).words(language);
        let args: Vec<&str> = if args == "-" { Vec::new() } else { args.split(';').collect() };
        let positional: Vec<&dyn Display> = args.iter().filter(|a| !a.contains('=')).map(|a| a as &dyn Display).collect();
        let pairs: Vec<(&str, &str)> = args.iter().filter_map(|a| a.split_once('=')).collect();
        let named: Vec<(&str, &dyn Display)> = pairs.iter().map(|(name, value)| (*name, value as &dyn Display)).collect();
        let actual = match call {
            "get" => words.get(key),
            "format" => words.format_with(key, &positional, &named),
            "count" => words.count(key, args[0]),
            _ => panic!("no call {call}: {line}"),
        };
        count += 1;
        if actual != unescape(expected) {
            failures.push(format!("{line}\n    read: {actual:?}"));
        }
    }
    assert!(count > 50, "the cases were read: {count}");
    assert!(failures.is_empty(), "{} of {count} cases differ:\n{}", failures.len(), failures.join("\n"));
}
