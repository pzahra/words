//! This is Words. It gives you Words, in Rust.
//!
//! `words-ini` reads `words.ini`, the dictionary format of PatTech.Localization,
//! for a command line: the same sections, fallbacks, plural forms, references
//! and escapes, read the way the .NET runtime reads them, and rendered as plain
//! text. Its rules are the Core SPEC's; the crate's own `SPEC.md` lists what it
//! leaves out.
//!
//! ```
//! use words_ini::Dictionary;
//!
//! let dictionary = Dictionary::parse("\
//! value=!en
//!
//! [file]
//! value=file
//! value#other=files
//! value-ru=файл
//! value-ru#few=файла
//! value-ru#many=файлов
//!
//! [removed]
//! value=Removed {0} {0#file}.
//! value-ru=Удалено: {0} {0#file}.
//! ");
//! let english = dictionary.words("en");
//! assert_eq!(english.format("removed", &[&1]), "Removed 1 file.");
//! assert_eq!(english.format("removed", &[&3]), "Removed 3 files.");
//! let russian = dictionary.words("ru");
//! assert_eq!(russian.format("removed", &[&5]), "Удалено: 5 файлов.");
//! assert_eq!(russian.get("nope"), "#nope#");
//! ```
//!
//! Two ways check each key while the app compiles. A build script can turn the
//! file into a function per key: see [`build`]. Or [`embed!`] names the file
//! once and defines `words!`, which reads a key from it:
//!
//! ```
//! words_ini::embed!("tests/fixtures/app.ini");
//!
//! fn main() {
//!     words::use_locale("en");
//!     assert_eq!(words!("pool.status", 3, Name = "Main"), "Main: 3 files");
//!     assert_eq!(words!("file", 2), "files");
//!     assert_eq!(words!("app.title"), "Pool Manager 0.1");
//! }
//! ```
//!
//! A key the file lacks fails to compile:
//!
//! ```compile_fail
//! words_ini::embed!("tests/fixtures/app.ini");
//!
//! fn main() {
//!     let _ = words!("pool.statsu", 3, Name = "Main");
//! }
//! ```
//!
//! So does an argument too many, or too few:
//!
//! ```compile_fail
//! words_ini::embed!("tests/fixtures/app.ini");
//!
//! fn main() {
//!     let _ = words!("pool.status", 3);
//! }
//! ```
//!
//! And a name the words don't read:
//!
//! ```compile_fail
//! words_ini::embed!("tests/fixtures/app.ini");
//!
//! fn main() {
//!     let _ = words!("pool.status", 3, Nmae = "Main");
//! }
//! ```

#![forbid(unsafe_code)]
#![warn(missing_docs)]

pub mod build;
mod code;
mod dictionary;
mod parse;
pub mod plural;
mod template;
mod words;

pub use code::LanguageCode;
pub use dictionary::{Dictionary, locale_from_env};
pub use plural::Category;
pub use words::Words;
#[cfg(feature = "macros")]
#[doc(hidden)]
pub use words_ini_macros::__words;
#[cfg(feature = "macros")]
pub use words_ini_macros::embed;
