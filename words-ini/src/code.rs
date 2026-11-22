//! Language codes as a words.ini writes them (Core SPEC: Language codes).

use std::fmt;

/// A language code: the BCP 47 subset `language(-Script)?(-REGION)?`, where the
/// language is 2 or 3 letters, the script 4 letters and the region 2 letters or
/// 3 digits: `en`, `ceb`, `es-419`, `zh-Hans-CN`, `sr-Latn-RS`. Each subtag is
/// cased by its kind, so `sr-latn-rs` reads `sr-Latn-RS`, and two codes compare
/// in that form.
#[derive(Clone, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct LanguageCode {
    code: String,
    language: String,
    script: String,
    region: String,
}

impl LanguageCode {
    fn new(language: String, script: String, region: String) -> LanguageCode {
        let mut code = language.clone();
        for subtag in [&script, &region] {
            if !subtag.is_empty() {
                code.push('-');
                code.push_str(subtag);
            }
        }
        LanguageCode { code, language, script, region }
    }

    /// Reads `text` as a code, in any casing: `EN-gb` is `en-GB`. `_` is no
    /// separator here, and the empty string is no code (it names the default).
    pub fn parse(text: &str) -> Option<LanguageCode> {
        let mut parts = text.split('-');
        let language = parts.next()?;
        if !(2..=3).contains(&language.len()) || !language.bytes().all(|b| b.is_ascii_alphabetic()) {
            return None;
        }
        let mut script = "";
        let mut region = "";
        let mut next = parts.next();
        if let Some(part) = next {
            if part.len() == 4 && part.bytes().all(|b| b.is_ascii_alphabetic()) {
                script = part;
                next = parts.next();
            }
        }
        if let Some(part) = next {
            let letters = part.len() == 2 && part.bytes().all(|b| b.is_ascii_alphabetic());
            let digits = part.len() == 3 && part.bytes().all(|b| b.is_ascii_digit());
            if !letters && !digits {
                return None;
            }
            region = part;
            next = parts.next();
        }
        if next.is_some() {
            return None;
        }
        let mut cased = script.to_ascii_lowercase();
        if let Some(first) = cased.get_mut(..1) {
            first.make_ascii_uppercase();
        }
        Some(LanguageCode::new(language.to_ascii_lowercase(), cased, region.to_ascii_uppercase()))
    }

    /// Reads a culture's name, which can say more than a code does
    /// (`ca-ES-valencia`, `en_US`): the longest run of its leading subtags that is
    /// a code, `_` reading as `-`. A name whose first subtag is no language reads
    /// as none.
    pub fn read(name: &str) -> Option<LanguageCode> {
        let name = name.replace('_', "-");
        let mut text = name.as_str();
        while !text.is_empty() {
            if let Some(code) = LanguageCode::parse(text) {
                return Some(code);
            }
            match text.rfind('-') {
                Some(separator) if separator > 0 => text = &text[..separator],
                _ => return None,
            }
        }
        None
    }

    /// The language subtag, lowercase: `zh` in `zh-Hant-TW`.
    pub fn language(&self) -> &str {
        &self.language
    }

    /// The script subtag, titlecase, or empty: `Hant` in `zh-Hant-TW`.
    pub fn script(&self) -> &str {
        &self.script
    }

    /// The region subtag, uppercase or 3 digits, or empty: `TW` in `zh-Hant-TW`.
    pub fn region(&self) -> &str {
        &self.region
    }

    /// The code as a file writes it: `zh-Hant-TW`.
    pub fn as_str(&self) -> &str {
        &self.code
    }

    /// This code and each shorter one it falls back to, longest first:
    /// `zh-Hant-TW`, `zh-Hant`, `zh`. A region falls back past its script, never
    /// to a sibling. A Chinese region that names no script falls back past the
    /// one it is written in: `zh-TW`, `zh-HK` and `zh-MO` read `zh-Hant`, and
    /// `zh-CN` and `zh-SG` read `zh-Hans`.
    pub fn chain(&self) -> Vec<LanguageCode> {
        let mut chain = vec![self.clone()];
        let script = if !self.script.is_empty() || self.region.is_empty() {
            self.script.as_str()
        } else {
            likely_script(&self.language, &self.region)
        };
        if !self.region.is_empty() && !script.is_empty() {
            chain.push(LanguageCode::new(self.language.clone(), script.to_string(), String::new()));
        }
        if !self.region.is_empty() || !self.script.is_empty() {
            chain.push(LanguageCode::new(self.language.clone(), String::new(), String::new()));
        }
        chain
    }
}

//the script a region writes a language in where its name says none (CLDR's
//likely subtags): Windows names Chinese cultures zh-TW and zh-CN
fn likely_script(language: &str, region: &str) -> &'static str {
    match (language, region) {
        ("zh", "TW" | "HK" | "MO") => "Hant",
        ("zh", "CN" | "SG") => "Hans",
        _ => "",
    }
}

impl fmt::Display for LanguageCode {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.code)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn code(text: &str) -> Option<String> {
        LanguageCode::parse(text).map(|c| c.to_string())
    }

    fn chain(text: &str) -> Vec<String> {
        LanguageCode::parse(text).unwrap().chain().iter().map(|c| c.to_string()).collect()
    }

    #[test]
    fn each_subtag_is_cased_by_its_kind() {
        assert_eq!(code("en").as_deref(), Some("en"));
        assert_eq!(code("EN-gb").as_deref(), Some("en-GB"));
        assert_eq!(code("sr-latn-rs").as_deref(), Some("sr-Latn-RS"));
        assert_eq!(code("ceb").as_deref(), Some("ceb"));
        assert_eq!(code("es-419").as_deref(), Some("es-419"));
        assert_eq!(code("zh-HANS").as_deref(), Some("zh-Hans"));
    }

    #[test]
    fn a_subtag_too_long_too_short_or_out_of_order_is_none() {
        for text in ["", "e", "engl", "en-", "en-G", "en-GBR", "en-1234", "en-GB-Latn", "en_GB", "zh-Hans-CN-x", "e1", "en-41"] {
            assert_eq!(code(text), None, "{text}");
        }
    }

    #[test]
    fn a_culture_name_reads_its_leading_code() {
        let read = |name: &str| LanguageCode::read(name).map(|c| c.to_string());
        assert_eq!(read("ca-ES-valencia").as_deref(), Some("ca-ES"));
        assert_eq!(read("en_US").as_deref(), Some("en-US"));
        assert_eq!(read("sr_Latn_RS").as_deref(), Some("sr-Latn-RS"));
        assert_eq!(read("en-US-POSIX").as_deref(), Some("en-US"));
        assert_eq!(read("C"), None);
        assert_eq!(read("POSIX"), None);
        assert_eq!(read(""), None);
    }

    #[test]
    fn a_code_falls_back_by_truncation_and_a_chinese_region_through_its_script() {
        assert_eq!(chain("en"), ["en"]);
        assert_eq!(chain("en-GB"), ["en-GB", "en"]);
        assert_eq!(chain("zh-Hant-TW"), ["zh-Hant-TW", "zh-Hant", "zh"]);
        assert_eq!(chain("zh-TW"), ["zh-TW", "zh-Hant", "zh"]);
        assert_eq!(chain("zh-HK"), ["zh-HK", "zh-Hant", "zh"]);
        assert_eq!(chain("zh-CN"), ["zh-CN", "zh-Hans", "zh"]);
        assert_eq!(chain("zh-Hans-HK"), ["zh-Hans-HK", "zh-Hans", "zh"]);
        assert_eq!(chain("pa-PK"), ["pa-PK", "pa"]);
    }
}
