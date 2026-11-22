//! One language's words, flattened, and how they render (Core SPEC: One pass for
//! every brace, Plural forms).

use crate::plural::{self, Category};
use crate::template::{self, Kind};
use std::collections::HashMap;
use std::fmt::Display;

/// One language's words, every fallback already flattened in: what an app reads
/// its messages from. Made by [`Dictionary::words`](crate::Dictionary::words),
/// [`for_locale`](crate::Dictionary::for_locale) or
/// [`from_env`](crate::Dictionary::from_env).
///
/// Nothing a value says makes a call fail: a missing key reads `#key#`, a format
/// item with nothing to fill it `#{0}#`, a brace that opens nothing shows as
/// written, and a reference that comes back round to its own key `# ∞ #`.
#[derive(Clone, Debug, Default)]
pub struct Words {
    pub(crate) language: String,
    pub(crate) entries: HashMap<String, String>,
}

/// The arguments a call's format items and selectors read: by number, the
/// positional ones; by name, the named ones.
#[derive(Clone, Copy)]
struct Args<'a> {
    positional: &'a [&'a dyn Display],
    named: &'a [(&'a str, &'a dyn Display)],
}

impl<'a> Args<'a> {
    fn read(&self, argument: &str) -> Option<&'a dyn Display> {
        if argument.starts_with(|c: char| c.is_ascii_digit()) {
            argument.parse::<usize>().ok().and_then(|index| self.positional.get(index).copied())
        } else {
            self.named.iter().find(|(name, _)| *name == argument).map(|&(_, value)| value)
        }
    }
}

impl Words {
    /// The language whose plural rules pick a count's form, as a code (`en-GB`);
    /// empty for the default read on its own, which counts as English.
    pub fn language(&self) -> &str {
        &self.language
    }

    /// Whether `key` has words here, its own or fallen back to.
    pub fn contains(&self, key: &str) -> bool {
        self.entries.contains_key(key)
    }

    /// `key`'s words, references and constants rendered, format items and
    /// selectors left as written: there is nothing to fill them with. A missing
    /// key reads `#key#`.
    pub fn get(&self, key: &str) -> String {
        let mut rendering = Rendering::new(self, None);
        rendering.key(key);
        rendering.out
    }

    /// `key`'s words with each `{0}`, `{1}`… filled from `args` by position, and
    /// each `{0#word}` the form of `word` that argument's count picks.
    pub fn format(&self, key: &str, args: &[&dyn Display]) -> String {
        self.format_with(key, args, &[])
    }

    /// `key`'s words with each `{Name}` filled from `args` by name, and each
    /// `{Name#word}` the form of `word` that argument's count picks.
    pub fn format_named(&self, key: &str, args: &[(&str, &dyn Display)]) -> String {
        self.format_with(key, &[], args)
    }

    /// `key`'s words with `{0}` read from `positional` and `{Name}` from `named`,
    /// in one call. An item there is no argument for reads `#{0}#` or `#{Name}#`,
    /// and a selector there is none for picks `other`.
    pub fn format_with(&self, key: &str, positional: &[&dyn Display], named: &[(&str, &dyn Display)]) -> String {
        let mut rendering = Rendering::new(self, Some(Args { positional, named }));
        rendering.key(key);
        rendering.out
    }

    /// `key`'s own form for the count `n`: `word` at 1 and `words` at 2 in
    /// English. Like [`get`](Words::get), it leaves format items as written. A
    /// count is whatever `n` displays as: digits, a sign and a fraction, which
    /// picks `other`; anything else is no number and picks `other` too.
    pub fn count(&self, key: &str, n: impl Display) -> String {
        let mut rendering = Rendering::new(self, None);
        rendering.key(&self.form_key(key, self.category_of(&n)));
        rendering.out
    }

    /// `key`'s own form for the count `n`, with its format items and selectors
    /// filled as [`format_with`](Words::format_with) fills them.
    pub fn count_with(&self, key: &str, n: impl Display, positional: &[&dyn Display], named: &[(&str, &dyn Display)]) -> String {
        let mut rendering = Rendering::new(self, Some(Args { positional, named }));
        rendering.key(&self.form_key(key, self.category_of(&n)));
        rendering.out
    }

    /// The CLDR category `n` falls in under this language's rules.
    pub fn category(&self, n: impl Display) -> Category {
        self.category_of(&n)
    }

    fn category_of(&self, n: &dyn Display) -> Category {
        match plural::count_of(&n.to_string()) {
            Some(count) => plural::select(&self.language, count),
            None => Category::Other,
        }
    }

    /// The entry a count in `form` reads: `key#form` when there are words for
    /// it, else the form an optional category reads instead, else `key#other`,
    /// else `key`, the plain value, which is the `one` form. A language with one
    /// category reads only the plain value.
    pub(crate) fn form_key(&self, key: &str, form: Category) -> String {
        if form != Category::One && plural::categories(&self.language).len() > 1 {
            let entry = format!("{key}#{form}");
            if self.entries.contains_key(&entry) {
                return entry;
            }
            if let Some((_, stand_in)) = plural::optional(&self.language).iter().find(|(category, _)| *category == form) {
                let entry = format!("{key}#{stand_in}");
                if self.entries.contains_key(&entry) {
                    return entry;
                }
            }
            let entry = format!("{key}#{}", Category::Other);
            if self.entries.contains_key(&entry) {
                return entry;
            }
        }
        key.to_string()
    }
}

/// The key an entry is words of: a form's key (`word#other` is `word`'s), or the
/// entry itself.
pub(crate) fn key_of(entry: &str) -> &str {
    match entry.find('#') {
        Some(mark) if mark > 0 => &entry[..mark],
        _ => entry,
    }
}

/// `.sub` under `base`; with no base the dot is dropped.
pub(crate) fn relative(key: &str, base: &str) -> String {
    match key.strip_prefix('.') {
        Some(sub) if base.is_empty() => sub.to_string(),
        Some(_) => format!("{base}{key}"),
        None => key.to_string(),
    }
}

//one rendering, written as it goes: each value read into its pieces once, and
//what a reference or a selector brings in rendered as pieces of its own
struct Rendering<'w> {
    words: &'w Words,
    args: Option<Args<'w>>,
    out: String,
    //the entries being rendered, for the circular cut
    path: Vec<String>,
}

const CIRCULAR: &str = "# ∞ #";

impl<'w> Rendering<'w> {
    fn new(words: &'w Words, args: Option<Args<'w>>) -> Rendering<'w> {
        Rendering { words, args, out: String::new(), path: Vec::new() }
    }

    //entry's words, rendered against the key they are words of
    fn key(&mut self, entry: &str) {
        if self.path.iter().any(|on| on == entry) {
            self.out.push_str(CIRCULAR);
            return;
        }
        let words = self.words;
        let Some(value) = words.entries.get(entry) else {
            self.out.push('#');
            self.out.push_str(entry);
            self.out.push('#');
            return;
        };
        //a constant is text, never looked into
        if entry.starts_with('$') {
            self.out.push_str(value);
            return;
        }
        self.path.push(entry.to_string());
        self.pieces(value, key_of(entry));
        self.path.pop();
    }

    fn pieces(&mut self, text: &str, key: &str) {
        for piece in template::parse(text) {
            match piece.kind {
                Kind::Text(run) => self.out.push_str(&run),
                Kind::Reference(target) => self.key(&relative(target, key)),
                Kind::Constant(constant) => self.key(constant),
                Kind::Selector { argument, key: target } if self.args.is_some() => self.select(&argument, &relative(target, key)),
                Kind::Item { argument, alignment, .. } if self.args.is_some() => self.fill(&argument, alignment),
                _ => self.out.push_str(piece.written),
            }
        }
    }

    //the form of key its argument selects: a key whose words are being rendered,
    //plain or a form, is out of reach of its own selectors
    fn select(&mut self, argument: &str, key: &str) {
        let form_of_key = |entry: &String| entry == key || (entry.starts_with(key) && entry[key.len()..].starts_with('#'));
        if self.path.iter().any(form_of_key) {
            self.out.push_str(CIRCULAR);
            return;
        }
        let category = match self.args.and_then(|args| args.read(argument)) {
            Some(value) => self.words.category_of(value),
            None => Category::Other,
        };
        let entry = self.words.form_key(key, category);
        self.key(&entry);
    }

    //an item filled from its argument and padded to its alignment; the spec is
    //.NET's and goes unread. An item with nothing to read is marked
    fn fill(&mut self, argument: &str, alignment: Option<i64>) {
        let Some(value) = self.args.and_then(|args| args.read(argument)) else {
            self.out.push_str("#{");
            self.out.push_str(argument);
            self.out.push_str("}#");
            return;
        };
        let text = value.to_string();
        let width = alignment.map_or(0, |a| a.unsigned_abs() as usize);
        let padding = width.saturating_sub(text.chars().count());
        let right = alignment.is_some_and(|a| a > 0);
        if right {
            self.out.extend(std::iter::repeat_n(' ', padding));
        }
        self.out.push_str(&text);
        if !right {
            self.out.extend(std::iter::repeat_n(' ', padding));
        }
    }
}
