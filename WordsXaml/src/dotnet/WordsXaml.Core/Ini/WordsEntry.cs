using System;
using System.Collections.Generic;
using System.Linq;

namespace WordsXaml.Ini
{
    /// <summary>
    /// A single resolved words key, e.g. <c>params.focal-law-base.capture-delay</c>.
    /// Backs completion items, quick-doc tooltips and go-to-definition.
    /// </summary>
    public sealed class WordsEntry
    {
        /// <summary>CLDR's plural categories, in CLDR's order: the forms a <c>value#form=</c> may name.</summary>
        public static readonly IReadOnlyList<string> PluralCategories = new[] { "zero", "one", "two", "few", "many", "other" };

        public WordsEntry(string key, string filePath, int lineNumber)
        {
            Key = key;
            FilePath = filePath;
            LineNumber = lineNumber;
            Values = new Dictionary<string, string>();
            Forms = new Dictionary<string, Dictionary<string, string>>();
        }

        /// <summary>Dotted key from the section header, e.g. <c>calibration.help.reset-hint</c>.</summary>
        public string Key { get; }

        /// <summary>Absolute path to the *-words.ini file the key was declared in.</summary>
        public string FilePath { get; }

        /// <summary>1-based line number of the <c>[section]</c> header (for go-to-definition).</summary>
        public int LineNumber { get; }

        /// <summary>
        /// Language -> value, unescaped and joined as the runtime reads it. The invariant value is
        /// stored under the empty-string key (matching <c>value=</c>); <c>value-en-GB=</c> is stored
        /// under "en-GB", the code cased by kind as the runtime cases it.
        /// </summary>
        public Dictionary<string, string> Values { get; }

        /// <summary>
        /// Language -> plural form -> value: <c>value#other=</c> is <c>Forms[""]["other"]</c>,
        /// <c>value-ru#few=</c> is <c>Forms["ru"]["few"]</c>. A form is a variant of the key's value,
        /// never a language of its own.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Forms { get; }

        /// <summary>The default/invariant value (from <c>value=</c>), or null if only variants exist.</summary>
        public string DefaultValue =>
            Values.TryGetValue(string.Empty, out var v) ? v : null;

        /// <summary>Every form the key has in any language, in CLDR's order (<c>few, many, other</c>).</summary>
        public IReadOnlyList<string> FormNames =>
            PluralCategories.Where(c => Forms.Values.Any(f => f.ContainsKey(c))).ToList();

        /// <summary>Sets a value, overwriting an earlier one for the same language, as the runtime does.</summary>
        internal void SetValue(string language, string form, string text)
        {
            if (form == null)
            {
                Values[language] = text;
                return;
            }
            if (!Forms.TryGetValue(language, out var forms))
                Forms[language] = forms = new Dictionary<string, string>(StringComparer.Ordinal);
            forms[form] = text;
        }

        /// <summary>Appends a continuation segment to a value set before.</summary>
        internal void AppendValue(string language, string form, string text)
        {
            if (form == null)
                Values[language] += text;
            else
                Forms[language][form] += text;
        }
    }
}
