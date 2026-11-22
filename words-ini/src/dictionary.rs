//! Every language a words.ini holds, and the words one language reads.

use crate::code::LanguageCode;
use crate::parse::Store;
use crate::template::{self, Kind};
use crate::words::{Words, key_of, relative};
use std::collections::{BTreeSet, HashMap, HashSet};

/// Every language one or more words.ini files hold, as loaded. A later file's
/// value replaces an earlier one's, as `WordsBuilder.Load` stacks them.
#[derive(Clone, Debug, Default)]
pub struct Dictionary {
    store: Store,
}

impl Dictionary {
    /// Reads a words.ini's text. Nothing in it makes this fail: a line that is
    /// none of the format's is read past, and a field the format refuses is
    /// noted in [`warnings`](Dictionary::warnings).
    pub fn parse(text: &str) -> Dictionary {
        let mut dictionary = Dictionary::default();
        dictionary.load(text);
        dictionary
    }

    /// Reads another words.ini over this one: its values replace the ones
    /// already read for the same key and language.
    pub fn load(&mut self, text: &str) -> &mut Dictionary {
        self.store.load(text);
        self
    }

    /// What the reading refused or replaced, in Core's warning codes:
    /// `WP:NAME` for a block whose name is no key's, `WP:LANG` for a field
    /// whose language is no code, `WP:FORM` for a form that is none,
    /// `WP:STALE`, `WP:WHO` for an unknown field, and `WB:KOVR` for a value
    /// replaced.
    pub fn warnings(&self) -> &[String] {
        &self.store.warnings
    }

    /// The languages a file lists, each with its label (`value-it=Italiano`), in
    /// the order met; a label written `!Label` keeps its language off the list.
    pub fn languages(&self) -> Vec<(&str, &str)> {
        self.store
            .codes
            .iter()
            .filter_map(|code| {
                let label = self.store.languages.get(code)?.get("")?;
                (!label.is_empty() && !label.starts_with('!')).then_some((code.as_str(), label.as_str()))
            })
            .collect()
    }

    /// The language the default's words are written in, as a top-of-file
    /// `value=!en` declares it, cased as a code; none where no file declares one.
    pub fn default_language(&self) -> Option<String> {
        let label = self.store.languages.get("")?.get("")?;
        let code = label.strip_prefix('!')?.trim();
        if code.is_empty() {
            return None;
        }
        Some(LanguageCode::parse(code).map_or_else(|| code.to_string(), |c| c.to_string()))
    }

    /// The words for `code`, read as given: each key from the language itself
    /// (`zh-Hant-TW`), then each shorter code it falls back to (`zh-Hant`,
    /// `zh`), then the default; a key's plural forms come whole from the first
    /// of those with any of its words. A culture's name that says more than a
    /// code reads as the code it starts with (`ca-ES-valencia` is `ca-ES`). The
    /// empty string, or a name that starts with no code, reads the default
    /// alone, counted as English.
    pub fn words(&self, code: &str) -> Words {
        let Some(code) = LanguageCode::read(code) else {
            return Words { language: String::new(), entries: self.store.languages.get("").cloned().unwrap_or_default() };
        };
        let mut entries = HashMap::new();
        for level in code.chain() {
            if let Some(source) = self.store.languages.get(level.as_str()) {
                patch(&mut entries, source);
            }
        }
        if let Some(default) = self.store.languages.get("") {
            patch(&mut entries, default);
        }
        Words { language: code.to_string(), entries }
    }

    /// The words for a locale as an environment names it (`ru_RU.UTF-8`,
    /// `de_CH@euro`, `zh_TW`, or a plain code), so a count is never read by the
    /// rules of a language the file has no words in: the locale's language where
    /// the file has words for it or a code it falls back to, else the default,
    /// counted by the rules of the language `value=!xx` declares it is written in
    /// (English where none is declared). `C`, `POSIX` and the empty string read
    /// the default.
    pub fn for_locale(&self, locale: &str) -> Words {
        let name = locale.split(['.', '@']).next().unwrap_or_default();
        match LanguageCode::read(name) {
            Some(code) if code.chain().iter().any(|level| self.has_words(level.as_str())) => self.words(code.as_str()),
            _ => self.words(&self.default_language().unwrap_or_default()),
        }
    }

    /// The words for the locale the environment names: the first of `LC_ALL`,
    /// `LC_MESSAGES` and `LANG` that is set and not empty, read as
    /// [`for_locale`](Dictionary::for_locale) reads it. Windows usually sets
    /// none of them, so there it reads the default, unless one is set.
    pub fn from_env(&self) -> Words {
        self.for_locale(&locale_from_env().unwrap_or_default())
    }

    /// Every key with words in any language, sorted: no labels, no constants.
    pub fn keys(&self) -> Vec<&str> {
        let keys: BTreeSet<&str> = self
            .store
            .languages
            .values()
            .flat_map(|words| words.keys())
            .map(|entry| key_of(entry))
            .filter(|key| !key.is_empty() && !key.starts_with('$'))
            .collect();
        keys.into_iter().collect()
    }

    /// The `keys` the default has no words for: a key in code that reads
    /// `#key#` wherever no translation has it. A test that lists the keys an
    /// app reads by name asserts this is empty.
    pub fn missing<'k>(&self, keys: &[&'k str]) -> Vec<&'k str> {
        let default = self.store.languages.get("");
        keys.iter().copied().filter(|key| !default.is_some_and(|words| words.contains_key(*key))).collect()
    }

    /// What a reader of this file would trip over, one line each: what the
    /// reading refused ([`warnings`](Dictionary::warnings)), a key with no
    /// default value, a reference, constant or selector naming words that the
    /// value's language reaches nowhere along its fallbacks, and a brace that
    /// opens nothing. Empty for a sound file, so a test can assert it is.
    pub fn check(&self) -> Vec<String> {
        let mut problems: Vec<String> = self.store.warnings.clone();
        let empty = HashMap::new();
        let default = self.store.languages.get("").unwrap_or(&empty);
        for key in self.keys() {
            if !default.contains_key(key) {
                problems.push(format!("`{key}` has no default value"));
            }
        }
        //the levels a language's words are flattened from, as words() flattens them
        let levels = |language: &str| -> Vec<&HashMap<String, String>> {
            let chain = LanguageCode::parse(language).map(|code| code.chain()).unwrap_or_default();
            chain.iter().filter_map(|level| self.store.languages.get(level.as_str())).chain([default]).collect()
        };
        let holds = |language: &str, entry: &str| levels(language).iter().any(|words| words.contains_key(entry));
        let has_forms = |language: &str, key: &str| {
            let prefix = format!("{key}#");
            levels(language).iter().any(|words| words.contains_key(key) || words.keys().any(|entry| entry.starts_with(&prefix)))
        };
        for code in &self.store.codes {
            let words = &self.store.languages[code];
            let mut entries: Vec<&String> = words.keys().filter(|entry| !entry.is_empty() && !entry.starts_with('$')).collect();
            entries.sort();
            for entry in entries {
                let field = field_name(code, entry);
                let key = key_of(entry);
                for piece in template::parse(&words[entry]) {
                    match piece.kind {
                        Kind::Reference(target) if !holds(code, &relative(target, key)) => {
                            problems.push(format!("`{key}`: {field} refers to `{}`, which has no words", relative(target, key)));
                        }
                        Kind::Constant(constant) if !holds(code, constant) => {
                            problems.push(format!("`{key}`: {field} reads `{constant}`, which has no value"));
                        }
                        Kind::Selector { key: target, .. } if !has_forms(code, &relative(target, key)) => {
                            problems.push(format!("`{key}`: {field} selects from `{}`, which has no words", relative(target, key)));
                        }
                        Kind::Malformed => {
                            problems.push(format!("`{key}`: {field} holds `{}`, a brace that opens nothing", piece.written));
                        }
                        _ => {}
                    }
                }
            }
        }
        problems
    }

    //whether the file has words, not just a label, in a language
    fn has_words(&self, code: &str) -> bool {
        self.store.languages.get(code).is_some_and(|words| words.keys().any(|entry| !entry.is_empty()))
    }

    pub(crate) fn store(&self) -> &Store {
        &self.store
    }
}

//a level's words under the ones already flattened: a key's forms come from the
//first level with any of its words, so beside a translation's own, none flattens in
fn patch(target: &mut HashMap<String, String>, source: &HashMap<String, String>) {
    let owned: HashSet<String> = target.keys().map(|entry| key_of(entry).to_string()).collect();
    for (entry, value) in source {
        let owner = key_of(entry);
        if target.contains_key(entry) || (owner != entry && owned.contains(owner)) {
            continue;
        }
        target.insert(entry.clone(), value.clone());
    }
}

//the field an entry was written as: value, value-it, value-it#few
fn field_name(code: &str, entry: &str) -> String {
    let form = entry.find('#').map_or("", |mark| &entry[mark..]);
    if code.is_empty() { format!("value{form}") } else { format!("value-{code}{form}") }
}

/// The locale the environment names: the first of `LC_ALL`, `LC_MESSAGES` and
/// `LANG` that is set and not empty.
pub fn locale_from_env() -> Option<String> {
    ["LC_ALL", "LC_MESSAGES", "LANG"].iter().find_map(|name| std::env::var(name).ok().filter(|value| !value.is_empty()))
}
