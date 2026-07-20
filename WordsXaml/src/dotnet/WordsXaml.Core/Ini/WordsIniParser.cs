using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WordsXaml.Ini
{
    /// <summary>
    /// Parser for the pattech.words *-words.ini grammar. Standalone and side-effect free so it can be
    /// unit-tested without the ReSharper SDK. The SDK cache layer only supplies file contents + paths.
    ///
    /// Grammar (see Helios/Helios/Assets/helios-words.ini and evolution-services/Assets/evo-words.ini):
    ///   [section.dotted.key]        fully-qualified section header -> WordsEntry.Key
    ///   [.suffix]                   inherits: appended to the last fully-qualified header, e.g.
    ///                                 [material] then [.metals] -> "material.metals". Dot-sections do
    ///                                 NOT become the new base; siblings keep hanging off [material].
    ///   value=Some text             invariant value  -> Values[""]
    ///   value-en=English            locale variant   -> Values["en"]
    ///   \  at end of line           line continuation, joined with a newline
    ///   _  at end of line           line continuation, concatenated (no separator)
    ///   repeated value= / value-x=  concatenated onto the same key (no separator)
    /// {>other.key} cross-refs and [icon:name] tokens are left verbatim in the value.
    /// Blank lines and lines starting with ';' or '#' are ignored.
    /// </summary>
    public static class WordsIniParser
    {
        public static IReadOnlyList<WordsEntry> Parse(string text, string filePath)
        {
            var entries = new List<WordsEntry>();
            WordsEntry current = null;

            // The most recent header that did NOT start with '.', used to resolve [.suffix] sections.
            string lastFullKey = null;

            // Pending continuation: which (entry, valueKey) is being extended, and the separator to
            // insert before the next line ("\n" for a trailing '\', "" for a trailing '_').
            WordsEntry contEntry = null;
            string contValueKey = null;
            string contSeparator = null;
            var contBuilder = new StringBuilder();

            // Append a part onto an entry's key so a repeated value= (or a flushed continuation)
            // concatenates rather than overwrites.
            void AddValue(WordsEntry entry, string valueKey, string part)
            {
                entry.Values[valueKey] = entry.Values.TryGetValue(valueKey, out var existing)
                    ? existing + part
                    : part;
            }

            void FlushContinuation()
            {
                if (contEntry != null && contValueKey != null)
                    AddValue(contEntry, contValueKey, contBuilder.ToString());
                contEntry = null;
                contValueKey = null;
                contSeparator = null;
                contBuilder.Clear();
            }

            // Separator this line implies for the NEXT line, or null if it does not continue.
            //   '\' -> newline join   '_' -> concat (no separator)
            static string ContinuationSeparator(string s)
            {
                if (s.EndsWith("\\")) return "\n";
                if (s.EndsWith("_")) return "";
                return null;
            }

            var lineNumber = 0;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;

                    // Mid-continuation: append this line using the pending separator.
                    if (contEntry != null)
                    {
                        var sep = ContinuationSeparator(line);
                        var payload = sep != null ? line.Substring(0, line.Length - 1) : line;
                        contBuilder.Append(contSeparator).Append(payload);
                        if (sep != null)
                            contSeparator = sep;   // separator before the next payload line
                        else
                            FlushContinuation();
                        continue;
                    }

                    var trimmed = line.TrimStart();
                    if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
                        continue;

                    if (trimmed[0] == '[')
                    {
                        var close = trimmed.IndexOf(']');
                        if (close > 1)
                        {
                            var header = trimmed.Substring(1, close - 1).Trim();

                            string key;
                            if (header.StartsWith("."))
                            {
                                // [.suffix] extends the last fully-qualified header (base + ".suffix").
                                // If none has been seen yet, fall back to the bare suffix.
                                key = lastFullKey != null ? lastFullKey + header : header.TrimStart('.');
                            }
                            else
                            {
                                key = header;
                                lastFullKey = header;
                            }

                            current = new WordsEntry(key, filePath, lineNumber);
                            entries.Add(current);
                        }
                        continue;
                    }

                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0 || current == null)
                        continue;

                    var name = trimmed.Substring(0, eq).Trim();
                    var value = trimmed.Substring(eq + 1);

                    // Only "value" and "value-<locale>" keys are meaningful.
                    string valueKey;
                    if (name == "value")
                        valueKey = string.Empty;
                    else if (name.StartsWith("value-"))
                        valueKey = name.Substring("value-".Length);
                    else
                        continue;

                    var startSep = ContinuationSeparator(value);
                    if (startSep != null)
                    {
                        // Starts a continuation; the accumulated text is concatenated onto any
                        // existing value for this key when flushed (repeated value= + continuation).
                        contEntry = current;
                        contValueKey = valueKey;
                        contSeparator = startSep;
                        contBuilder.Clear();
                        contBuilder.Append(value.Substring(0, value.Length - 1));
                    }
                    else
                    {
                        AddValue(current, valueKey, value);
                    }
                }
            }

            FlushContinuation();
            return entries;
        }
    }
}