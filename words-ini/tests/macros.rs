//! embed! and words!, the macros a crate uses in place of a build script: each
//! call reads what the generated function would, and the module beside it is
//! the generated module's, less the functions.

words_ini::embed!("tests/fixtures/app.ini");

mod nested {
    //words! reaches modules declared after embed!
    pub fn status(n: u32) -> String {
        words!("pool.status", n, Name = "Main")
    }
}

//the one test that reads words: the language is chosen once a process
#[test]
fn each_call_reads_its_key_in_the_language_chosen() {
    assert!(words::use_locale("ru_RU.UTF-8"));
    assert_eq!(words::words().language(), "ru-RU");
    assert_eq!(words!("app"), "Pool Manager");
    assert_eq!(words!("app.title"), "Pool Manager 0.1");
    assert_eq!(nested::status(5), "Main: 5 файлов");
    assert_eq!(words!("pool.status", 22, Name = "Main",), "Main: 22 файла");
    assert_eq!(words!("pool.removed", 1, "Main"), "Removed 1 файл from Main.");
    assert_eq!(words!("file", 2), "файла");
    assert_eq!(words!("files", 3, 3, Pool = "Main"), "3 files in Main");
    assert_eq!(words!("errors.404", Path = "/x"), "Not found: /x");
    assert_eq!(words!("errors.type"), "Wrong type");
    assert_eq!(words!("use"), "Use words-ini.");
    let count = 4u64;
    assert_eq!(words!(r"file", count), "файла");
    assert_eq!(words!("pool.status", count + 1, Name = format!("{}{}", "Ma", "in")), "Main: 5 файлов");
}

#[test]
fn the_module_is_the_generated_ones_less_the_functions() {
    assert!(words::KEYS.contains(&"pool.status"));
    assert_eq!(words::dictionary().check(), Vec::<String>::new());
    assert!(words::TEXT.contains("[pool]"));
}
