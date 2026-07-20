using System.Collections.Generic;

namespace WordsXaml.Ini
{
    /// <summary>
    /// A single resolved words key, e.g. <c>params.focal-law-base.capture-delay</c>.
    /// Backs completion items, quick-doc tooltips and go-to-definition.
    /// </summary>
    public sealed class WordsEntry
    {
        public WordsEntry(string key, string filePath, int lineNumber)
        {
            Key = key;
            FilePath = filePath;
            LineNumber = lineNumber;
            Values = new Dictionary<string, string>();
        }

        /// <summary>Dotted key from the section header, e.g. <c>calibration.help.reset-hint</c>.</summary>
        public string Key { get; }

        /// <summary>Absolute path to the *-words.ini file the key was declared in.</summary>
        public string FilePath { get; }

        /// <summary>1-based line number of the <c>[section]</c> header (for go-to-definition).</summary>
        public int LineNumber { get; }

        /// <summary>
        /// Locale variant -> raw value. The invariant value is stored under the empty-string key
        /// (matching <c>value=</c>); <c>value-en=</c> is stored under "en", etc.
        /// </summary>
        public Dictionary<string, string> Values { get; }

        /// <summary>The default/invariant value (from <c>value=</c>), or null if only variants exist.</summary>
        public string DefaultValue =>
            Values.TryGetValue(string.Empty, out var v) ? v : null;
    }
}
