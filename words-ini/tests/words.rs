//! What the crate decides for itself: which language a locale reads, what a
//! .NET format spec does here, and the checks a test can lean on.

use words_ini::{Category, Dictionary};

const ENGLISH_ONLY: &str = "value=!en\n\n[file]\nvalue=file\nvalue#other=files\n\n[files]\nvalue={0} {0#file}\n";

const TRANSLATED: &str = "\
value=!en
value-de=Deutsch
value-ru=!Русский

[hello]
value=Hello
value-de=Hallo
value-ru=Привет
";

#[test]
fn a_locale_reads_its_language_where_the_file_has_words_for_it() {
    let dictionary = Dictionary::parse(TRANSLATED);
    assert_eq!(dictionary.for_locale("de_DE.UTF-8").get("hello"), "Hallo");
    assert_eq!(dictionary.for_locale("de_DE.UTF-8").language(), "de-DE");
    assert_eq!(dictionary.for_locale("de_CH@euro").get("hello"), "Hallo");
    assert_eq!(dictionary.for_locale("ru").get("hello"), "Привет");
    assert_eq!(dictionary.for_locale("fr_FR.UTF-8").get("hello"), "Hello");
    assert_eq!(dictionary.for_locale("C").get("hello"), "Hello");
    assert_eq!(dictionary.for_locale("POSIX").language(), "en");
    assert_eq!(dictionary.for_locale("").language(), "en");
}

#[test]
fn a_locale_the_file_has_no_words_for_counts_by_the_defaults_rules() {
    let dictionary = Dictionary::parse(ENGLISH_ONLY);
    //Russian would read 21 as one, the plain value: "21 file"
    assert_eq!(dictionary.for_locale("ru_RU.UTF-8").format("files", &[&21]), "21 files");
    assert_eq!(dictionary.for_locale("ru_RU.UTF-8").language(), "en");
    //asked for by code, a language counts by its own rules, as Core's ToWords does
    assert_eq!(dictionary.words("ru").format("files", &[&21]), "21 file");
    //with no declaration the default counts as English
    let undeclared = Dictionary::parse("[file]\nvalue=file\nvalue#other=files\n");
    assert_eq!(undeclared.for_locale("ja_JP").language(), "");
    assert_eq!(undeclared.for_locale("ja_JP").count("file", 2), "files");
}

#[test]
fn a_code_that_is_none_reads_the_default() {
    let dictionary = Dictionary::parse(TRANSLATED);
    assert_eq!(dictionary.words("").get("hello"), "Hello");
    assert_eq!(dictionary.words("english").get("hello"), "Hello");
    assert_eq!(dictionary.words("english").language(), "");
    assert_eq!(dictionary.words("ca-ES-valencia").language(), "ca-ES");
}

#[test]
fn a_file_lists_its_languages_and_names_the_defaults() {
    let dictionary = Dictionary::parse(TRANSLATED);
    assert_eq!(dictionary.languages(), [("de", "Deutsch")]);
    assert_eq!(dictionary.default_language().as_deref(), Some("en"));
    assert_eq!(Dictionary::parse("value=! EN-gb \n").default_language().as_deref(), Some("en-GB"));
    assert_eq!(Dictionary::parse("value=English\n").default_language(), None);
    assert_eq!(Dictionary::parse("value=English\n").languages(), [("", "English")]);
}

#[test]
fn a_dotnet_format_spec_goes_unread_and_an_alignment_pads() {
    let dictionary = Dictionary::parse("[n]\nvalue={0:N2}|{0,8:C}|{Name,-4}|\n");
    assert_eq!(dictionary.words("en").format_with("n", &[&1234.5], &[("Name", &"ab")]), "1234.5|  1234.5|ab  |");
}

#[test]
fn a_named_call_reads_no_numbered_item() {
    let dictionary = Dictionary::parse("[n]\nvalue={0} {Name}\n");
    assert_eq!(dictionary.words("en").format_named("n", &[("Name", &"x")]), "#{0}# x");
}

#[test]
fn a_count_is_whatever_its_argument_shows() {
    let dictionary = Dictionary::parse(ENGLISH_ONLY);
    let english = dictionary.words("en");
    assert_eq!(english.count("file", 1u8), "file");
    assert_eq!(english.count("file", -1i64), "file");
    assert_eq!(english.count("file", 1.0f64), "file");
    assert_eq!(english.count("file", 1.5f32), "files");
    assert_eq!(english.count("file", u128::MAX), "files");
    assert_eq!(english.count("file", "one"), "files");
    assert_eq!(english.category(1), Category::One);
    assert_eq!(dictionary.words("ru").category(22), Category::Few);
}

#[test]
fn check_names_what_a_reader_would_trip_over() {
    let dictionary = Dictionary::parse(
        "value=!en\n[a]\nvalue={>nowhere} {$none} {0#nothing} {0,x}\nvalue-english=no\n[b]\nvalue-de=only German\n[bad name]\nvalue=x\n[c]\nvalue={>a} {0#a} {>.d}\n[.d]\nvalue=d\n",
    );
    let problems = dictionary.check();
    let expected = [
        "WP:LANG:`a.value-english`",
        "WP:NAME:`bad name`",
        "`b` has no default value",
        "`a`: value refers to `nowhere`, which has no words",
        "`a`: value reads `$none`, which has no value",
        "`a`: value selects from `nothing`, which has no words",
        "`a`: value holds `{0,x}`, a brace that opens nothing",
    ];
    for problem in expected {
        assert!(problems.iter().any(|p| p == problem), "{problem} in {problems:#?}");
    }
    assert_eq!(problems.len(), expected.len(), "{problems:#?}");
}

#[test]
fn missing_names_the_keys_the_default_lacks() {
    let dictionary = Dictionary::parse(TRANSLATED);
    assert_eq!(dictionary.missing(&["hello", "goodbye"]), ["goodbye"]);
    assert_eq!(dictionary.keys(), ["hello"]);
}

#[test]
fn a_later_file_stacks_over_an_earlier_one() {
    let mut dictionary = Dictionary::parse(TRANSLATED);
    dictionary.load("[hello]\nvalue-de=Servus\n");
    assert_eq!(dictionary.words("de").get("hello"), "Servus");
    assert_eq!(dictionary.warnings(), ["WB:KOVR:`hello`-`de` = Servus"]);
}
