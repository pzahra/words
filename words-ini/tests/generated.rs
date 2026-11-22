//! The module a build script makes, checked in as tests/generated/words.rs and
//! compiled here: each key a function, embedded and read by the language chosen.

include!("generated/words.rs");

//the one test that reads words: the language is chosen once a process
#[test]
fn each_key_is_a_function_reading_the_language_chosen() {
    assert!(words::use_locale("ru_RU.UTF-8"));
    assert!(!words::use_locale("en"));
    assert_eq!(words::words().language(), "ru-RU");
    assert_eq!(words::app(), "Pool Manager");
    assert_eq!(words::app::title(), "Pool Manager 0.1");
    assert_eq!(words::pool::status(5, "Main"), "Main: 5 файлов");
    assert_eq!(words::pool::status(22, "Main"), "Main: 22 файла");
    assert_eq!(words::pool::removed(1, "Main"), "Removed 1 файл from Main.");
    assert_eq!(words::file(2), "файла");
    assert_eq!(words::files(1, 1, "Main"), "one file in Main");
    assert_eq!(words::files(3, 3, "Main"), "3 files in Main");
    assert_eq!(words::errors::_404("/x"), "Not found: /x");
    assert_eq!(words::errors::r#type(), "Wrong type");
    assert_eq!(words::r#use(), "Use words-ini.");
}

#[test]
fn the_module_lists_its_keys_and_its_file_is_sound() {
    assert!(words::KEYS.contains(&"pool.status"));
    assert_eq!(words::dictionary().check(), Vec::<String>::new());
    assert_eq!(words::dictionary().missing(words::KEYS), Vec::<&str>::new());
    assert_eq!(words::dictionary().default_language().as_deref(), Some("en"));
}

const INCLUDE: &str = r#"include_str!(concat!(env!("CARGO_MANIFEST_DIR"), "/tests/fixtures/app.ini"))"#;
const GOLDEN: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/tests/generated/words.rs");

#[test]
fn the_checked_in_module_is_what_the_build_makes() {
    let text = std::fs::read_to_string(concat!(env!("CARGO_MANIFEST_DIR"), "/tests/fixtures/app.ini")).unwrap();
    let source = words_ini::build::source("words", INCLUDE, &text).unwrap();
    let golden = std::fs::read_to_string(GOLDEN).unwrap_or_default().replace("\r\n", "\n");
    if golden != source {
        if std::env::var_os("WORDS_INI_BLESS").is_some() {
            std::fs::write(GOLDEN, &source).unwrap();
        } else {
            panic!("tests/generated/words.rs is not what the build makes: run the tests with WORDS_INI_BLESS=1 and look at the diff");
        }
    }
}
