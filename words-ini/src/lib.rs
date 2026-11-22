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
//! A build script can turn the file into a function per key, embedded and
//! checked when the app compiles: see [`build`].

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
