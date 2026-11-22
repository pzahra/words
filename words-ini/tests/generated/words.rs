// Made by words-ini from a words.ini: change the words.ini, not this.

/// The words of the embedded words.ini, a function per key: `[pool.status]` is
/// `pool::status`, a placeholder is a parameter, and a key with plural forms
/// takes its count first.
pub mod words {
    #![allow(dead_code, missing_docs, non_snake_case, non_upper_case_globals, unreachable_pub, clippy::all)]

    /// The words.ini this module was made from, embedded.
    pub static TEXT: &str = include_str!(concat!(env!("CARGO_MANIFEST_DIR"), "/tests/fixtures/app.ini"));

    /// Every key the file has words for.
    pub const KEYS: &[&str] = &["app", "app.title", "errors.404", "errors.type", "file", "files", "pool.removed", "pool.status", "use"];

    static DICTIONARY: ::std::sync::OnceLock<::words_ini::Dictionary> = ::std::sync::OnceLock::new();
    static WORDS: ::std::sync::OnceLock<::words_ini::Words> = ::std::sync::OnceLock::new();

    /// Every language the embedded file holds.
    pub fn dictionary() -> &'static ::words_ini::Dictionary {
        DICTIONARY.get_or_init(|| ::words_ini::Dictionary::parse(TEXT))
    }

    /// The words every function here reads: the environment's language
    /// (`LC_ALL`, `LC_MESSAGES`, `LANG`), unless [`use_locale`] chose first.
    pub fn words() -> &'static ::words_ini::Words {
        WORDS.get_or_init(|| dictionary().from_env())
    }

    /// Chooses the language before anything reads words, as a `--lang` option
    /// does; `false` once words have been read, or chosen.
    pub fn use_locale(locale: &str) -> bool {
        WORDS.get().is_none() && WORDS.set(dictionary().for_locale(locale)).is_ok()
    }

    /// `app`
    ///
    /// ```text
    /// Pool Manager
    /// ```
    pub fn app() -> ::std::string::String {
        words().get("app")
    }

    /// `file`
    ///
    /// ```text
    /// file
    /// ```
    pub fn file(count: impl ::core::fmt::Display) -> ::std::string::String {
        words().count("file", count)
    }

    /// `files`
    ///
    /// ```text
    /// one file in {Pool}
    /// ```
    pub fn files(count: impl ::core::fmt::Display, arg0: impl ::core::fmt::Display, Pool: impl ::core::fmt::Display) -> ::std::string::String {
        words().count_with("files", count, &[&arg0 as &dyn ::core::fmt::Display], &[("Pool", &Pool as &dyn ::core::fmt::Display)])
    }

    /// `use`
    ///
    /// ```text
    /// Use words-ini.
    /// ```
    pub fn r#use() -> ::std::string::String {
        words().get("use")
    }

    /// The keys under `app`.
    pub mod app {
        /// `app.title`
        ///
        /// ```text
        /// {>app} {$version}
        /// ```
        pub fn title() -> ::std::string::String {
            super::words().get("app.title")
        }
    }

    /// The keys under `errors`.
    pub mod errors {
        /// `errors.404`
        ///
        /// ```text
        /// Not found: {Path}
        /// ```
        pub fn _404(Path: impl ::core::fmt::Display) -> ::std::string::String {
            super::words().format_with("errors.404", &[], &[("Path", &Path as &dyn ::core::fmt::Display)])
        }

        /// `errors.type`
        ///
        /// ```text
        /// Wrong type
        /// ```
        pub fn r#type() -> ::std::string::String {
            super::words().get("errors.type")
        }
    }

    /// The keys under `pool`.
    pub mod pool {
        /// `pool.removed`
        ///
        /// ```text
        /// Removed {0} {0#file} from {1}.
        /// ```
        pub fn removed(arg0: impl ::core::fmt::Display, arg1: impl ::core::fmt::Display) -> ::std::string::String {
            super::words().format_with("pool.removed", &[&arg0 as &dyn ::core::fmt::Display, &arg1 as &dyn ::core::fmt::Display], &[])
        }

        /// `pool.status`
        ///
        /// ```text
        /// {Name}: {0} {0#file}
        /// ```
        pub fn status(arg0: impl ::core::fmt::Display, Name: impl ::core::fmt::Display) -> ::std::string::String {
            super::words().format_with("pool.status", &[&arg0 as &dyn ::core::fmt::Display], &[("Name", &Name as &dyn ::core::fmt::Display)])
        }
    }
}
