using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PatTech.Localization {
	/// <summary>
	/// The standard <see cref="IWordsParserConsumer"/>: collects <c>value</c> fields
	/// into one dictionary per language, ready for <see cref="WordsBuilder"/> to
	/// flatten. A plural form is an entry beside its key's value, keyed
	/// <c>key#form</c> (<c>value-ru#few</c> under <c>[word]</c> is <c>word#few</c> in
	/// <c>ru</c>), so it flattens like one. <c>comment</c>, <c>context</c> and
	/// <c>param</c> fields are ignored; <c>stale</c> fields, unknown field types and
	/// forms that are none are reported to the logger.
	/// </summary>
	/// <param name="logger">Receives warnings about overwritten keys, stale values and unknown fields; <see langword="null"/> discards them.</param>
	public class WordsParserToWordsProvider(ITakeException? logger = null) : IWordsParserConsumer {
		private readonly ITakeException logger = logger ?? ITakeException.Dummy;

		/// <summary>
		/// The words collected so far, one dictionary per language code. The empty
		/// string keys the language-less default.
		/// </summary>
		public IReadOnlyDictionary<string, DictionaryWordsProvider> Languages => languages;
		/// <summary>
		/// The language codes in the order they were first encountered, which drives
		/// the ordering of <see cref="WordsBuilder.GetLanguages"/>.
		/// </summary>
		public IReadOnlyList<string> LanguageCodes => languageCodes;

		private readonly Dictionary<string, DictionaryWordsProvider> languages = [];
		private readonly List<string> languageCodes = [];
		//the entry a form's continuation lines append to; null while the form was refused
		private string? formEntry;

		/// <summary>
		/// Stores a <c>value</c> field, or a plural form, in its language's dictionary,
		/// overwriting (and warning about) any earlier value for the same key — that is
		/// what makes later <see cref="WordsBuilder.Load(string)"/> calls win. Other field
		/// types are metadata: ignored, or logged in the case of <c>stale</c> and unknowns.
		/// </summary>
		public void VisitFieldDeclaration(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			switch (fieldType) {
				case "value":
					Store(blockKey, languageCode, value);
					break;
				case var form when form.StartsWith("value#", StringComparison.Ordinal):
					formEntry = FormEntry(blockKey, form[6..], languageCode);
					if (formEntry is not null) {
						Store(formEntry, languageCode, value);
					}
					break;
				case "comment":
				case "context":
				case "param":
					// safely ignored
					break;
				case "stale":
					logger.Warn(string.Format(
						"WP:STALE:`{0}.{1}-{2}`",
						blockKey,
						fieldType,
						languageCode));
					break;
				default:
					logger.Warn(string.Format(
						"WP:WHO:`{0}.{1}-{2}`",
						blockKey,
						fieldType,
						languageCode));
					break;
			}
		}
		/// <summary>
		/// Appends <paramref name="value"/> to the <c>value</c> field, or plural form,
		/// declared just before it; continuations of any other field type are discarded.
		/// </summary>
		public void VisitFieldContinuation(FieldKey key, string value) {
			var (blockKey, fieldType, languageCode) = key;

			if (fieldType is "value") {
				Debug.Assert(languageCodes.Contains(languageCode));

				var language = languages[languageCode];
				language[blockKey] += value;
			}
			else if (fieldType.StartsWith("value#", StringComparison.Ordinal) && formEntry is not null) {
				languages[languageCode][formEntry] += value;
			}
		}

		/// <summary>
		/// A block whose name holds a <c>#</c> is warned about: the mark separates a key
		/// from its plural form, so <c>[word#few]</c> would read as <c>word</c>'s form.
		/// </summary>
		public void VisitBlock(string baseKey, string name) {
			if (name.Contains('#')) {
				logger.Warn(string.Format("WP:HASH:`{0}`", name));
			}
		}

		private void Store(string entry, string languageCode, string value) {
			if (!languageCodes.Contains(languageCode)) {
				languageCodes.Add(languageCode);
			}

			if (!languages.TryGetValue(languageCode, out var language)) {
				language = [];
				languages.Add(languageCode, language);
			}

			if (!language.TryAdd(entry, value)) {
				// a later file relabelling a language (a host listing what a
				// subordinate file declared with a !label) is the stacking
				// pattern at work, not a key clobbered: no warning for labels
				if (entry != "") {
					logger.Warn(string.Format(
						"WB:KOVR:`{0}`-`{1}` = {2}",
						entry,
						languageCode,
						value));
				}
				language[entry] = value;
			}
		}

		//a form's entry, key#form, or null with a warning: a label has no forms, the
		//plain value is the one form, and only CLDR's categories are forms
		private string? FormEntry(string blockKey, string form, string languageCode) {
			if (blockKey == "" || form == "one" || !PluralRules.Names.Contains(form)) {
				logger.Warn(string.Format(
					"WP:FORM:`{0}.value-{1}#{2}`",
					blockKey,
					languageCode,
					form));
				return null;
			}
			return $"{blockKey}#{form}";
		}
	}
}
