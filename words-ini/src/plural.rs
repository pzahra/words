//! Unicode CLDR's plural rules for whole numbers, as Core's `PluralRules` carries
//! them (Core SPEC: Plural forms). A number with a fractional part is `other`.

use std::fmt;

/// A CLDR plural category: which of a key's forms a count reads.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum Category {
    /// `zero`, as Arabic and Welsh count 0, and Latvian 10 to 20.
    Zero,
    /// `one`, the singular: a key's plain value is this form.
    One,
    /// `two`, the dual.
    Two,
    /// `few`, as Russian counts 2 to 4.
    Few,
    /// `many`, as Russian counts 5 to 20, and French an exact million.
    Many,
    /// `other`, every language's "more", and every fraction.
    Other,
}

use Category::*;

impl Category {
    /// CLDR's six, in CLDR's order.
    pub const ALL: [Category; 6] = [Zero, One, Two, Few, Many, Other];

    /// The name a file writes after `#`: `few` in `value-ru#few`.
    pub const fn name(self) -> &'static str {
        match self {
            Zero => "zero",
            One => "one",
            Two => "two",
            Few => "few",
            Many => "many",
            Other => "other",
        }
    }

    /// The category a lowercase name means; none for anything else.
    pub fn from_name(name: &str) -> Option<Category> {
        Category::ALL.into_iter().find(|category| category.name() == name)
    }
}

impl fmt::Display for Category {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.name())
    }
}

const LARGE: u64 = 1_000_000_000_000;

/// A count as a rule reads it: whole, its low digits kept past 10^12, or a
/// fraction, which is always `other`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Count {
    Whole(u64),
    Fraction,
}

/// Reads a count from the text a value displays as: an optional sign, digits,
/// and an optional fractional part (`2.0` is whole, `1.5` is not). Anything
/// else is no number.
pub(crate) fn count_of(text: &str) -> Option<Count> {
    let unsigned = text.strip_prefix(['-', '+']).unwrap_or(text);
    let (whole, fraction) = match unsigned.split_once('.') {
        Some((whole, fraction)) => (whole, Some(fraction)),
        None => (unsigned, None),
    };
    let digits = |s: &str| s.bytes().all(|b| b.is_ascii_digit());
    if whole.is_empty() || !digits(whole) {
        return None;
    }
    if let Some(fraction) = fraction {
        if fraction.is_empty() || !digits(fraction) {
            return None;
        }
        if fraction.bytes().any(|b| b != b'0') {
            return Some(Count::Fraction);
        }
    }
    let whole = whole.trim_start_matches('0');
    //past 10^12 only the low digits matter to any rule, and the count stays large
    Some(Count::Whole(if whole.len() > 12 {
        whole[whole.len() - 12..].parse::<u64>().unwrap_or(0) + LARGE
    } else if whole.is_empty() {
        0
    } else {
        whole.parse().unwrap_or(0)
    }))
}

/// The category `count` falls in for `language`.
pub(crate) fn select(language: &str, count: Count) -> Category {
    match count {
        Count::Fraction => Other,
        Count::Whole(i) => find(language).select(i),
    }
}

/// The category a whole number falls in for `language`. A code the table does
/// not know falls back to a shorter one, a language it does not know has only
/// `other`, and the empty code counts as English.
pub fn category(language: &str, number: u64) -> Category {
    select(language, Count::Whole(if number > LARGE { number % LARGE + LARGE } else { number }))
}

/// The categories a whole number reaches in `language`, in CLDR's order: `one,
/// other` for English, `one, few, many` for Russian, just `other` for Japanese.
pub fn categories(language: &str) -> &'static [Category] {
    find(language).categories()
}

/// The categories a translation into `language` may leave out, each with the
/// category a missing form reads instead: Maltese `two` reads `few`, Hebrew `two`
/// reads `other`, and the exact millions of French, Italian, Spanish and their
/// neighbours read `other`. Words' own table, not CLDR's.
pub fn optional(language: &str) -> &'static [(Category, Category)] {
    lookup(language, optional_entry).unwrap_or(&[])
}

fn optional_entry(code: &str) -> Option<&'static [(Category, Category)]> {
    match code {
        "mt" => Some(&[(Two, Few), (Many, Other), (Other, One)]),
        "he" | "iw" => Some(&[(Two, Other)]),
        "br" | "ca" | "es" | "fr" | "it" | "lld" | "pt" | "scn" | "vec" => Some(&[(Many, Other)]),
        _ => None,
    }
}

//the language's entry, else that of each shorter code it falls back to
fn lookup<T>(language: &str, entry: fn(&str) -> Option<T>) -> Option<T> {
    let lowered = language.replace('_', "-").to_ascii_lowercase();
    let mut code = lowered.as_str();
    while !code.is_empty() {
        if let Some(found) = entry(code) {
            return Some(found);
        }
        match code.rfind('-') {
            Some(separator) if separator > 0 => code = &code[..separator],
            _ => return None,
        }
    }
    None
}

fn find(language: &str) -> Rule {
    if language.is_empty() { Rule::OneOnly } else { lookup(language, rule_entry).unwrap_or(Rule::OtherOnly) }
}

//each rule is named for the languages that use it, or for what it picks
#[derive(Clone, Copy)]
enum Rule {
    OtherOnly,
    ZeroOne,
    OneOnly,
    OneMillions,
    ZeroOneMillions,
    Icelandic,
    Filipino,
    Tamazight,
    Latvian,
    ZeroOneOther,
    OneTwo,
    Tachelhit,
    Romanian,
    Bosnian,
    Gaelic,
    Slovenian,
    Czech,
    Polish,
    Russian,
    Lithuanian,
    Samogitian,
    Breton,
    Maltese,
    Irish,
    Manx,
    Cornish,
    Arabic,
    Welsh,
}

fn rule_entry(code: &str) -> Option<Rule> {
    Some(match code {
        "bm" | "bo" | "dz" | "hnj" | "id" | "ig" | "ii" | "in" | "ja" | "jbo" | "jv" | "jw" | "kde" | "kea" | "km" | "ko" | "lkt"
        | "lo" | "ms" | "my" | "nqo" | "osa" | "sah" | "ses" | "sg" | "su" | "th" | "to" | "tpi" | "vi" | "wo" | "yo" | "yue" | "zh" => {
            Rule::OtherOnly
        }
        "ak" | "am" | "as" | "bho" | "bn" | "csw" | "doi" | "fa" | "ff" | "gu" | "guw" | "hi" | "hy" | "kab" | "kn" | "kok" | "ln"
        | "mg" | "nso" | "pa" | "pcm" | "si" | "ti" | "wa" | "zu" => Rule::ZeroOne,
        "af" | "an" | "asa" | "ast" | "az" | "bal" | "bem" | "bez" | "bg" | "brx" | "ce" | "cgg" | "chr" | "ckb" | "da" | "de" | "dv"
        | "ee" | "el" | "en" | "eo" | "et" | "eu" | "fi" | "fo" | "fur" | "fy" | "gl" | "gsw" | "ha" | "haw" | "hu" | "ia" | "ie"
        | "io" | "jgo" | "ji" | "jmc" | "ka" | "kaj" | "kcg" | "kk" | "kkj" | "kl" | "ks" | "ksb" | "ku" | "ky" | "lb" | "lg" | "lij"
        | "mas" | "mgo" | "ml" | "mn" | "mr" | "nah" | "nb" | "nd" | "ne" | "nl" | "nn" | "nnh" | "no" | "nr" | "ny" | "nyn" | "om"
        | "or" | "os" | "pap" | "ps" | "rm" | "rof" | "rwk" | "saq" | "sc" | "sd" | "sdh" | "seh" | "sn" | "so" | "sq" | "ss" | "ssy"
        | "st" | "sv" | "sw" | "syr" | "ta" | "te" | "teo" | "tig" | "tk" | "tn" | "tr" | "ts" | "ug" | "ur" | "uz" | "ve" | "vo"
        | "vun" | "wae" | "xh" | "xog" | "yi" => Rule::OneOnly,
        "ca" | "es" | "it" | "lld" | "pt-pt" | "scn" | "vec" => Rule::OneMillions,
        "fr" | "pt" => Rule::ZeroOneMillions,
        "is" | "mk" => Rule::Icelandic,
        "ceb" | "fil" | "tl" => Rule::Filipino,
        "tzm" => Rule::Tamazight,
        "lv" | "prg" => Rule::Latvian,
        "blo" | "cv" | "ksh" | "lag" => Rule::ZeroOneOther,
        "he" | "iu" | "iw" | "naq" | "sat" | "se" | "sma" | "smi" | "smj" | "smn" | "sms" => Rule::OneTwo,
        "shi" => Rule::Tachelhit,
        "mo" | "ro" => Rule::Romanian,
        "bs" | "hr" | "sh" | "sr" => Rule::Bosnian,
        "gd" => Rule::Gaelic,
        "dsb" | "hsb" | "sl" => Rule::Slovenian,
        "cs" | "sk" => Rule::Czech,
        "pl" => Rule::Polish,
        "be" | "ru" | "uk" => Rule::Russian,
        "lt" => Rule::Lithuanian,
        "sgs" => Rule::Samogitian,
        "br" => Rule::Breton,
        "mt" => Rule::Maltese,
        "ga" => Rule::Irish,
        "gv" => Rule::Manx,
        "kw" => Rule::Cornish,
        "ar" | "ars" => Rule::Arabic,
        "cy" => Rule::Welsh,
        _ => return None,
    })
}

fn million(i: u64) -> bool {
    i != 0 && i % 1_000_000 == 0
}

impl Rule {
    fn select(self, i: u64) -> Category {
        let (m10, m100) = (i % 10, i % 100);
        match self {
            Rule::OtherOnly => Other,
            Rule::ZeroOne => {
                if i <= 1 {
                    One
                } else {
                    Other
                }
            }
            Rule::OneOnly => {
                if i == 1 {
                    One
                } else {
                    Other
                }
            }
            Rule::OneMillions => {
                if i == 1 {
                    One
                } else if million(i) {
                    Many
                } else {
                    Other
                }
            }
            Rule::ZeroOneMillions => {
                if i <= 1 {
                    One
                } else if million(i) {
                    Many
                } else {
                    Other
                }
            }
            Rule::Icelandic => {
                if m10 == 1 && m100 != 11 {
                    One
                } else {
                    Other
                }
            }
            Rule::Filipino => {
                if matches!(i, 1..=3) || !matches!(m10, 4 | 6 | 9) {
                    One
                } else {
                    Other
                }
            }
            Rule::Tamazight => {
                if i <= 1 || (11..=99).contains(&i) {
                    One
                } else {
                    Other
                }
            }
            Rule::Latvian => {
                if m10 == 0 || (11..=19).contains(&m100) {
                    Zero
                } else if m10 == 1 {
                    One
                } else {
                    Other
                }
            }
            Rule::ZeroOneOther => match i {
                0 => Zero,
                1 => One,
                _ => Other,
            },
            Rule::OneTwo => match i {
                1 => One,
                2 => Two,
                _ => Other,
            },
            Rule::Tachelhit => {
                if i <= 1 {
                    One
                } else if (2..=10).contains(&i) {
                    Few
                } else {
                    Other
                }
            }
            Rule::Romanian => {
                if i == 1 {
                    One
                } else if i == 0 || (1..=19).contains(&m100) {
                    Few
                } else {
                    Other
                }
            }
            Rule::Bosnian => {
                if m10 == 1 && m100 != 11 {
                    One
                } else if (2..=4).contains(&m10) && !(12..=14).contains(&m100) {
                    Few
                } else {
                    Other
                }
            }
            Rule::Gaelic => match i {
                1 | 11 => One,
                2 | 12 => Two,
                3..=10 | 13..=19 => Few,
                _ => Other,
            },
            Rule::Slovenian => match m100 {
                1 => One,
                2 => Two,
                3 | 4 => Few,
                _ => Other,
            },
            Rule::Czech => match i {
                1 => One,
                2..=4 => Few,
                _ => Other,
            },
            Rule::Polish => {
                if i == 1 {
                    One
                } else if (2..=4).contains(&m10) && !(12..=14).contains(&m100) {
                    Few
                } else {
                    Many
                }
            }
            Rule::Russian => {
                if m10 == 1 && m100 != 11 {
                    One
                } else if (2..=4).contains(&m10) && !(12..=14).contains(&m100) {
                    Few
                } else {
                    Many
                }
            }
            Rule::Lithuanian => {
                if (11..=19).contains(&m100) {
                    Other
                } else if m10 == 1 {
                    One
                } else if m10 >= 2 {
                    Few
                } else {
                    Other
                }
            }
            Rule::Samogitian => {
                if m10 == 1 && m100 != 11 {
                    One
                } else if i == 2 {
                    Two
                } else if m10 >= 2 && !(11..=19).contains(&m100) {
                    Few
                } else {
                    Other
                }
            }
            Rule::Breton => {
                if m10 == 1 && !matches!(m100, 11 | 71 | 91) {
                    One
                } else if m10 == 2 && !matches!(m100, 12 | 72 | 92) {
                    Two
                } else if matches!(m10, 3 | 4 | 9) && !matches!(m100, 10..=19 | 70..=79 | 90..=99) {
                    Few
                } else if million(i) {
                    Many
                } else {
                    Other
                }
            }
            Rule::Maltese => {
                if i == 1 {
                    One
                } else if i == 2 {
                    Two
                } else if i == 0 || (3..=10).contains(&m100) {
                    Few
                } else if (11..=19).contains(&m100) {
                    Many
                } else {
                    Other
                }
            }
            Rule::Irish => match i {
                1 => One,
                2 => Two,
                3..=6 => Few,
                7..=10 => Many,
                _ => Other,
            },
            Rule::Manx => {
                if m10 == 1 {
                    One
                } else if m10 == 2 {
                    Two
                } else if matches!(m100, 0 | 20 | 40 | 60 | 80) {
                    Few
                } else {
                    Other
                }
            }
            Rule::Cornish => {
                if i == 0 {
                    Zero
                } else if i == 1 {
                    One
                } else if matches!(m100, 2 | 22 | 42 | 62 | 82)
                    || (i % 1000 == 0 && matches!(i % 100_000, 1000..=20_000 | 40_000 | 60_000 | 80_000))
                    || i % 1_000_000 == 100_000
                {
                    Two
                } else if matches!(m100, 3 | 23 | 43 | 63 | 83) {
                    Few
                } else if matches!(m100, 1 | 21 | 41 | 61 | 81) {
                    Many
                } else {
                    Other
                }
            }
            Rule::Arabic => {
                if i == 0 {
                    Zero
                } else if i == 1 {
                    One
                } else if i == 2 {
                    Two
                } else if (3..=10).contains(&m100) {
                    Few
                } else if m100 >= 11 {
                    Many
                } else {
                    Other
                }
            }
            Rule::Welsh => match i {
                0 => Zero,
                1 => One,
                2 => Two,
                3 => Few,
                6 => Many,
                _ => Other,
            },
        }
    }

    fn categories(self) -> &'static [Category] {
        match self {
            Rule::OtherOnly => &[Other],
            Rule::ZeroOne | Rule::OneOnly | Rule::Icelandic | Rule::Filipino | Rule::Tamazight => &[One, Other],
            Rule::OneMillions | Rule::ZeroOneMillions => &[One, Many, Other],
            Rule::Latvian | Rule::ZeroOneOther => &[Zero, One, Other],
            Rule::OneTwo => &[One, Two, Other],
            Rule::Tachelhit | Rule::Romanian | Rule::Bosnian | Rule::Czech | Rule::Lithuanian => &[One, Few, Other],
            Rule::Gaelic | Rule::Slovenian | Rule::Samogitian | Rule::Manx => &[One, Two, Few, Other],
            Rule::Polish | Rule::Russian => &[One, Few, Many],
            Rule::Breton | Rule::Maltese | Rule::Irish => &[One, Two, Few, Many, Other],
            Rule::Cornish | Rule::Arabic | Rule::Welsh => &[Zero, One, Two, Few, Many, Other],
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn picks(language: &str, numbers: &[u64]) -> Vec<Category> {
        numbers.iter().map(|&n| category(language, n)).collect()
    }

    #[test]
    fn english_and_russian_count_as_cldr_has_them() {
        let numbers = [1, 2, 5, 11, 21, 22, 25, 101];
        assert_eq!(picks("en", &numbers), [One, Other, Other, Other, Other, Other, Other, Other]);
        assert_eq!(picks("ru", &numbers), [One, Few, Many, Many, One, Few, Many, One]);
        assert_eq!(picks("", &[0, 1, 2]), [Other, One, Other]);
    }

    #[test]
    fn spot_checks_follow_current_cldr() {
        assert_eq!(picks("fr", &[0, 1, 2, 1_000_000, 2_000_000]), [One, One, Other, Many, Many]);
        assert_eq!(picks("it", &[0, 1, 1_000_000]), [Other, One, Many]);
        assert_eq!(picks("pt", &[0, 1]), [One, One]);
        assert_eq!(picks("pt-PT", &[0, 1]), [Other, One]);
        assert_eq!(picks("pt-BR", &[0]), [One]);
        assert_eq!(picks("ja", &[1, 2]), [Other, Other]);
        assert_eq!(picks("mt", &[1, 2, 3, 11, 20]), [One, Two, Few, Many, Other]);
        assert_eq!(picks("ar", &[0, 1, 2, 3, 11, 100]), [Zero, One, Two, Few, Many, Other]);
        assert_eq!(picks("cy", &[0, 1, 2, 3, 6, 7]), [Zero, One, Two, Few, Many, Other]);
        assert_eq!(picks("pl", &[1, 2, 5, 22]), [One, Few, Many, Few]);
        assert_eq!(picks("lv", &[0, 1, 11, 21]), [Zero, One, Zero, One]);
        assert_eq!(picks("kw", &[2, 1000, 100_000]), [Two, Two, Two]);
        for language in ["blo", "cv", "kok", "sgs"] {
            assert_ne!(categories(language), [Other], "{language}");
        }
        assert_eq!(picks("tlh", &[1]), [Other]);
        assert_eq!(picks("zh-Hant-TW", &[1]), [Other]);
    }

    #[test]
    fn each_rule_reaches_its_categories() {
        for language in ["en", "ru", "fr", "it", "ja", "mt", "ar", "cy", "kw", "br", "ga", "gv", "lt", "sgs", "lv", "pl"] {
            let mut reached: Vec<Category> = (0..200).chain([1000, 100_000, 1_000_000, 2_000_000]).map(|n| category(language, n)).collect();
            reached.sort_unstable();
            reached.dedup();
            assert_eq!(reached, categories(language), "{language}");
        }
    }

    #[test]
    fn an_optional_category_and_the_one_it_reads_are_its_languages() {
        for language in ["mt", "he", "fr", "it", "es", "pt", "ca", "br"] {
            for (category, reads) in optional(language) {
                assert!(categories(language).contains(category), "{language} {category}");
                assert!(categories(language).contains(reads) || *reads == Other, "{language} {reads}");
            }
        }
        assert!(optional("en").is_empty());
        assert_eq!(optional("fr-CA"), [(Many, Other)]);
    }

    #[test]
    fn a_category_is_named_as_a_file_writes_it() {
        assert_eq!(Category::ALL.map(Category::name), ["zero", "one", "two", "few", "many", "other"]);
        for category in Category::ALL {
            assert_eq!(Category::from_name(category.name()), Some(category));
        }
        assert_eq!(Category::from_name("FEW"), None);
        assert_eq!(Category::from_name("plural"), None);
        assert_eq!(Few.to_string(), "few");
    }

    #[test]
    fn a_count_reads_from_its_text() {
        assert_eq!(count_of("5"), Some(Count::Whole(5)));
        assert_eq!(count_of("-21"), Some(Count::Whole(21)));
        assert_eq!(count_of("2.0"), Some(Count::Whole(2)));
        assert_eq!(count_of("1.5"), Some(Count::Fraction));
        assert_eq!(count_of("007"), Some(Count::Whole(7)));
        assert_eq!(count_of("0"), Some(Count::Whole(0)));
        assert_eq!(count_of("1000000000000"), Some(Count::Whole(LARGE)));
        assert_eq!(count_of("100000000000000000001"), Some(Count::Whole(LARGE + 1)));
        assert_eq!(count_of("0000000000000005"), Some(Count::Whole(5)));
        for text in ["", "x", "1x", "1.", ".5", "1e3", "inf", "NaN", "1 000", "--1"] {
            assert_eq!(count_of(text), None, "{text}");
        }
    }

    #[test]
    fn a_huge_count_keeps_the_digits_a_rule_reads() {
        assert_eq!(select("ru", count_of("100000000000000000001").unwrap()), One);
        assert_eq!(select("ru", count_of("1.5").unwrap()), Other);
    }
}
