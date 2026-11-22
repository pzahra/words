//! The words.ini line grammar, as Core's `WordsParser` reads it, and the fields
//! it keeps, as `WordsParserToWordsProvider` keeps them.

use crate::code::LanguageCode;
use crate::plural::Category;
use crate::template::is_word_char;
use std::collections::HashMap;

/// Every language's entries, as the files loaded so far hold them. A plural
/// form is an entry beside its key's value, `word#other`; a language's label
/// is its entry under the empty key.
#[derive(Clone, Debug, Default)]
pub(crate) struct Store {
    /// The language codes in the order they were first met; `""` is the default.
    pub codes: Vec<String>,
    pub languages: HashMap<String, HashMap<String, String>>,
    pub warnings: Vec<String>,
    //the entry a form's continuation lines append to; none while the form was refused
    form_entry: Option<String>,
    //the fields of a block whose name is no key's are read past
    skipping: bool,
}

struct Target {
    block: String,
    field: String,
    language: String,
}

impl Store {
    /// Reads `text` to the end. Later files' values replace earlier ones.
    pub fn load(&mut self, text: &str) {
        let text = text.strip_prefix('\u{feff}').unwrap_or(text);
        let mut base_block = String::new();
        let mut current_block = String::new();
        let mut target: Option<Target> = None;
        //a field read past runs on through its continuation lines
        let mut reading_past = false;
        //under [], which names no key, every field is read past
        let mut empty_block = false;
        for line in lines(text) {
            if reading_past {
                reading_past = is_continued(line);
                continue;
            }
            if let Some(field) = target.take() {
                target = self.read_line(field, line, false);
                continue;
            }
            if is_comment(line) || line.chars().all(char::is_whitespace) {
                continue;
            }
            if let Some(name) = block(line) {
                empty_block = name.is_empty();
                if empty_block {
                    base_block.clear();
                    current_block.clear();
                } else if name.starts_with('.') {
                    current_block = format!("{base_block}{name}");
                } else {
                    base_block = name.to_string();
                    current_block = name.to_string();
                }
                self.visit_block(&base_block, name);
            } else if let Some(pair) = pair(line) {
                if empty_block {
                    reading_past = is_continued(pair.text);
                    continue;
                }
                let mut language = pair.language.to_string();
                //a key's param- names a parameter; everywhere else the suffix is a language
                if !language.is_empty() && !(pair.field == "param" && !current_block.is_empty()) {
                    match LanguageCode::parse(&language) {
                        Some(code) => language = code.to_string(),
                        None => {
                            self.visit_bad_language(&current_block, &format!("{}-{}{}", pair.field, pair.language, pair.form));
                            reading_past = is_continued(pair.text);
                            continue;
                        }
                    }
                }
                let field = format!("{}{}", pair.field, pair.form.to_lowercase());
                let declared = Target { block: current_block.clone(), field, language };
                target = self.read_line(declared, pair.text, true);
            }
        }
    }

    //a field's line, its continuation marked off and its escapes collapsed; the
    //target stays while the line is continued
    fn read_line(&mut self, target: Target, text: &str, first: bool) -> Option<Target> {
        let continued = is_continued(text);
        let mut text = text.to_string();
        if continued {
            let newline_kept = text.ends_with('\\');
            text.pop();
            if newline_kept {
                text.push('\n');
            }
        }
        let text = unescape(&text);
        if first {
            self.visit_declaration(&target, text);
        } else {
            self.visit_continuation(&target, &text);
        }
        continued.then_some(target)
    }

    fn visit_block(&mut self, base: &str, name: &str) {
        let key = if name.starts_with('.') { format!("{base}{name}") } else { name.to_string() };
        self.skipping = !is_key_name(&key);
        if self.skipping {
            self.warnings.push(format!("WP:NAME:`{key}`"));
        }
    }

    fn visit_bad_language(&mut self, block: &str, name: &str) {
        if self.skipping && !block.is_empty() {
            return;
        }
        self.warnings.push(format!("WP:LANG:`{block}.{name}`"));
    }

    fn visit_declaration(&mut self, target: &Target, value: String) {
        let Target { block, field, language } = target;
        if self.skipping && !block.is_empty() {
            return;
        }
        match field.as_str() {
            "value" => self.store(block, language, value),
            "comment" | "context" | "param" => {}
            "stale" => self.warnings.push(format!("WP:STALE:`{block}.{field}-{language}`")),
            form if form.starts_with("value#") => {
                self.form_entry = self.form_key(block, &form[6..], language);
                if let Some(entry) = self.form_entry.clone() {
                    self.store(&entry, language, value);
                }
            }
            _ => self.warnings.push(format!("WP:WHO:`{block}.{field}-{language}`")),
        }
    }

    fn visit_continuation(&mut self, target: &Target, value: &str) {
        let Target { block, field, language } = target;
        if self.skipping && !block.is_empty() {
            return;
        }
        let entry = if field == "value" {
            Some(block.clone())
        } else if field.starts_with("value#") {
            self.form_entry.clone()
        } else {
            None
        };
        if let Some(entry) = entry {
            self.languages.entry(language.clone()).or_default().entry(entry).or_default().push_str(value);
        }
    }

    fn store(&mut self, entry: &str, language: &str, value: String) {
        if !self.codes.iter().any(|code| code == language) {
            self.codes.push(language.to_string());
        }
        let words = self.languages.entry(language.to_string()).or_default();
        let replaced = words.insert(entry.to_string(), value).is_some();
        //a later file relabelling a language is the stacking pattern at work
        if replaced && !entry.is_empty() {
            let new = &words[entry];
            self.warnings.push(format!("WB:KOVR:`{entry}`-`{language}` = {new}"));
        }
    }

    //a form's entry, key#form, or none with a warning: a label has no forms, the
    //plain value is the one form, and only CLDR's categories are forms
    fn form_key(&mut self, block: &str, form: &str, language: &str) -> Option<String> {
        match Category::from_name(form) {
            Some(category) if category != Category::One && !block.is_empty() => Some(format!("{block}#{category}")),
            _ => {
                self.warnings.push(format!("WP:FORM:`{block}.value-{language}#{form}`"));
                None
            }
        }
    }
}

//a text's lines as a TextReader reads them: split at \r\n, \n or \r
fn lines(text: &str) -> impl Iterator<Item = &str> {
    let mut rest = Some(text);
    std::iter::from_fn(move || {
        let current = rest?;
        match current.find(['\r', '\n']) {
            Some(end) => {
                let after = if current[end..].starts_with("\r\n") { end + 2 } else { end + 1 };
                rest = (after < current.len()).then(|| &current[after..]);
                Some(&current[..end])
            }
            None => {
                rest = None;
                (!current.is_empty()).then_some(current)
            }
        }
    })
}

//whether a line goes on: it ends in a \ or a _ that no \ or _ before it pairs off
fn is_continued(line: &str) -> bool {
    let bytes = line.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        if matches!(bytes[i], b'\\' | b'_') {
            if i + 1 == bytes.len() {
                return true;
            }
            //the pair's second half is any character, which may take several bytes
            i += 1 + line[i + 1..].chars().next().map_or(1, char::len_utf8);
        } else {
            i += 1;
        }
    }
    false
}

//\\, __ and '' are each one of theirs
fn unescape(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut chars = text.chars().peekable();
    while let Some(c) = chars.next() {
        out.push(c);
        if matches!(c, '\\' | '_' | '\'') && chars.peek() == Some(&c) {
            chars.next();
        }
    }
    out
}

fn is_comment(line: &str) -> bool {
    line.trim_start_matches(char::is_whitespace).starts_with(';')
}

//[name], name holding no ]; what follows the ] is read past
fn block(line: &str) -> Option<&str> {
    let rest = line.strip_prefix('[')?;
    rest.find(']').map(|end| &rest[..end])
}

struct Pair<'a> {
    field: &'a str,
    language: &'a str,
    form: &'a str,
    text: &'a str,
}

//field(-lang(-sub)*)?(#form)? [:=] text, the field, each subtag and the form \w runs
fn pair(line: &str) -> Option<Pair<'_>> {
    let word =
        |from: usize| -> usize { line[from..].char_indices().find(|&(_, c)| !is_word_char(c)).map_or(line.len(), |(i, _)| from + i) };
    let field_end = word(0);
    if field_end == 0 {
        return None;
    }
    let mut at = field_end;
    let mut language = "";
    if line[at..].starts_with('-') {
        let start = at + 1;
        let mut end = word(start);
        if end > start {
            while line[end..].starts_with('-') && word(end + 1) > end + 1 {
                end = word(end + 1);
            }
            language = &line[start..end];
            at = end;
        }
    }
    let mut form = "";
    if line[at..].starts_with('#') {
        let end = word(at + 1);
        if end > at + 1 {
            form = &line[at..end];
            at = end;
        }
    }
    let rest = line[at..].trim_start_matches(char::is_whitespace);
    let rest = rest.strip_prefix([':', '='])?;
    Some(Pair { field: &line[..field_end], language, form, text: rest.trim_start_matches(char::is_whitespace) })
}

/// Whether `key` is a key's name (Core SPEC: Key names): dotted segments, each
/// starting with a letter, a digit or `_` and going on with those and `-`, or a
/// constant, `$` and one segment. ASCII is checked exactly; past it a segment
/// starts with a letter or a digit and goes on with anything but a space or a
/// control, and no name is checked for NFC.
pub(crate) fn is_key_name(key: &str) -> bool {
    match key.strip_prefix('$') {
        Some(constant) => is_segment(constant),
        None => key.split('.').all(is_segment),
    }
}

fn is_segment(segment: &str) -> bool {
    let mut chars = segment.chars();
    let Some(first) = chars.next() else {
        return false;
    };
    let starts = if first.is_ascii() { first.is_ascii_alphanumeric() || first == '_' } else { first.is_alphanumeric() };
    starts
        && chars
            .all(|c| if c.is_ascii() { c.is_ascii_alphanumeric() || c == '_' || c == '-' } else { !c.is_whitespace() && !c.is_control() })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn store(text: &str) -> Store {
        let mut store = Store::default();
        store.load(text);
        store
    }

    fn get<'a>(store: &'a Store, language: &str, entry: &str) -> Option<&'a str> {
        store.languages.get(language)?.get(entry).map(String::as_str)
    }

    #[test]
    fn a_trailing_backslash_keeps_a_newline_and_an_underscore_none() {
        let s = store("[a]\nvalue=one\\\n  two_\nthree\n[b]\nvalue=x");
        assert_eq!(get(&s, "", "a"), Some("one\n  twothree"));
        assert_eq!(get(&s, "", "b"), Some("x"));
    }

    #[test]
    fn escapes_collapse_once_and_an_escaped_end_does_not_continue() {
        let s = store("[a]\nvalue=\\\\server__name ''q'' a\\\\\n[b]\nvalue=x__\n");
        assert_eq!(get(&s, "", "a"), Some("\\server_name 'q' a\\"));
        assert_eq!(get(&s, "", "b"), Some("x_"));
        assert!(is_continued("a\\\\\\"));
        assert!(!is_continued("a\\_"));
        assert!(is_continued("é_"));
        assert!(!is_continued("_é"));
    }

    #[test]
    fn a_continuation_line_is_read_verbatim() {
        let s = store("[a]\nvalue=x\\\n; not a comment\\\n[not.a.block]\n");
        assert_eq!(get(&s, "", "a"), Some("x\n; not a comment\n[not.a.block]"));
    }

    #[test]
    fn relative_blocks_resolve_under_the_last_full_header() {
        let s = store("[a]\nvalue=A\n[.b]\nvalue=AB\n[.c]\nvalue=AC\n[d]\n[.e]\nvalue=DE");
        assert_eq!(get(&s, "", "a.b"), Some("AB"));
        assert_eq!(get(&s, "", "a.c"), Some("AC"));
        assert_eq!(get(&s, "", "d.e"), Some("DE"));
    }

    #[test]
    fn the_empty_header_reads_past_every_field_under_it() {
        let s = store("value=!en\n[]\nvalue=gone\\\nstill gone\n[.c]\nvalue=nothing\n[k]\nvalue=K");
        assert_eq!(get(&s, "", ""), Some("!en"));
        assert_eq!(get(&s, "", "k"), Some("K"));
        assert_eq!(s.languages[""].len(), 2);
        assert!(s.warnings.contains(&"WP:NAME:``".to_string()));
    }

    #[test]
    fn languages_are_cased_and_a_bad_one_read_past_with_its_continuation() {
        let s = store("[k]\nvalue-EN-gb=Colour\nvalue-english=no\\\nmore\nvalue-sr-latn-rs=Boja\nparam-count=Number:How many");
        assert_eq!(get(&s, "en-GB", "k"), Some("Colour"));
        assert_eq!(get(&s, "sr-Latn-RS", "k"), Some("Boja"));
        assert!(s.warnings.contains(&"WP:LANG:`k.value-english`".to_string()));
        assert_eq!(s.codes, ["en-GB", "sr-Latn-RS"]);
    }

    #[test]
    fn a_form_is_lowercased_continued_and_refused_when_none() {
        let s = store("[w]\nvalue=word\nvalue#OTHER=wo\\\nrds\nvalue#one=no\nvalue#lots=no\\\nnot this\n[]\n");
        assert_eq!(get(&s, "", "w#other"), Some("wo\nrds"));
        assert_eq!(get(&s, "", "w#one"), None);
        assert_eq!(get(&s, "", "w#lots"), None);
        assert_eq!(get(&s, "", "w"), Some("word"));
        assert_eq!(s.warnings.iter().filter(|w| w.starts_with("WP:FORM")).count(), 2);
        let s = store("value#other=x\n");
        assert!(s.warnings[0].starts_with("WP:FORM"));
    }

    #[test]
    fn a_block_that_names_no_key_is_skipped_with_its_children() {
        let s = store(
            "[lang.c#]\nvalue=sharp\\\nstill\n[.x]\nvalue=no\n[lang.c]\nvalue=C\n[$unit]\nvalue=m\n[.kid]\nvalue=no\n[after]\nvalue=A",
        );
        assert_eq!(get(&s, "", "lang.c"), Some("C"));
        assert_eq!(get(&s, "", "$unit"), Some("m"));
        assert_eq!(get(&s, "", "after"), Some("A"));
        assert_eq!(s.languages[""].len(), 3);
        assert!(s.warnings.contains(&"WP:NAME:`lang.c#`".to_string()));
        assert!(s.warnings.contains(&"WP:NAME:`$unit.kid`".to_string()));
    }

    #[test]
    fn a_second_files_labels_are_read_after_a_skipped_block() {
        let mut s = store("[bad name]\nvalue=x");
        s.load("value-it=Italiano\n[k]\nvalue-it=K");
        assert_eq!(get(&s, "it", ""), Some("Italiano"));
        assert_eq!(get(&s, "it", "k"), Some("K"));
    }

    #[test]
    fn a_later_value_wins_and_warns() {
        let s = store("[k]\nvalue=one\n[k]\nvalue=two");
        assert_eq!(get(&s, "", "k"), Some("two"));
        assert!(s.warnings[0].starts_with("WB:KOVR"));
    }

    #[test]
    fn lines_split_as_a_text_reader_splits_them_and_a_bom_is_dropped() {
        let s = store("\u{feff}value=!en\r\n[a]\r\nvalue=A\\\r\nB\r[b]\rvalue=C\r\n");
        assert_eq!(get(&s, "", ""), Some("!en"));
        assert_eq!(get(&s, "", "a"), Some("A\nB"));
        assert_eq!(get(&s, "", "b"), Some("C"));
        assert_eq!(lines("a\n\nb\n").collect::<Vec<_>>(), ["a", "", "b"]);
    }

    #[test]
    fn a_pair_takes_a_colon_spaces_and_unknown_fields() {
        let s = store("[k]\nvalue : spaced  \nstale-it=x\nwhatever=y\ncomment=c\ncontext-it=c\n not a pair\n-x=y");
        assert_eq!(get(&s, "", "k"), Some("spaced  "));
        assert!(s.warnings.contains(&"WP:STALE:`k.stale-it`".to_string()));
        assert!(s.warnings.contains(&"WP:WHO:`k.whatever-`".to_string()));
        assert_eq!(s.warnings.len(), 2);
    }

    #[test]
    fn key_names() {
        for name in ["menu.file-open", "errors.404", "_x", "हिंदी.शब्द", "$unit", "a.b.c", "ab_c", "日本"] {
            assert!(is_key_name(name), "{name}");
        }
        for name in [
            "", ".a", "a.", "a..b", "-a", "a.-b", "$a.b", "a$b", "lang.c#", "a b", "a=b", "a:b", "a;b", "a{b", "a}b", "a>b", "$",
            "\u{301}a",
        ] {
            assert!(!is_key_name(name), "{name}");
        }
    }
}
