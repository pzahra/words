using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WordsXaml.Ini
{
    /// <summary>
    /// Parser for the *-words.ini grammar. Standalone and side-effect free so it can be unit-tested
    /// without the ReSharper SDK. The SDK cache layer only supplies file contents + paths.
    ///
    /// The runtime's reader is the contract: <c>WordsParser.Load</c> walking the lines and
    /// <c>WordsParserToWordsProvider</c> keeping the values (Localization-Core). This plugin cannot
    /// reference it, so its patterns are copied here as written there; keep them in step.
    ///   [section.dotted.key]        a full header: the key, and the base for [.child] headers
    ///   [.suffix]                   appended to the last full header: [material] then [.metals] is
    ///                                 "material.metals"; dot-headers never become the base
    ///   value=Some text             invariant value  -> Values[""]
    ///   value-zh-Hans-CN=...        language variant -> Values["zh-Hans-CN"], the code cased by kind
    ///   value#other= / value-ru#few=  plural forms of the key -> Forms[language][form]
    ///   \  at end of line           line continuation, joined with a newline
    ///   _  at end of line           line continuation, concatenated (no separator)
    ///   \\  __  ''                  doubled escapes: one character each, and never a continuation
    ///   repeated value= / value-x=  the later one wins, as in the runtime
    /// A header or field starts at the line's start: an indented one is no header or field. A header
    /// whose resolved name is no key's name (<see cref="IsKeyName"/>) is no key, its fields with it;
    /// so are the [.child] headers under it or under a constant ([$c]), and a [.x] before any full
    /// header. A field whose language is no language code is read past. A line a field's continuation
    /// runs on through is text, never a header or field. Lines starting with ';' are comments; any
    /// other line (one starting '#', say) is skipped as unrecognised, as the runtime skips it.
    /// {>other.key} cross-refs and [icon:name] tokens are left verbatim in the value.
    /// </summary>
    public static class WordsIniParser
    {
        // WordsParser's own patterns, as written there: keep them in step.
        private static readonly Regex KeyName = new Regex(@"^(\$\w[\w-]*|\w[\w-]*(\.\w[\w-]*)*)\z");
        private static readonly Regex Block = new Regex(@"^\[(?<1>[^]]*)\]", RegexOptions.ExplicitCapture);
        private static readonly Regex Pair = new Regex(
            @"^(?<key>\w+)(-(?<lang>\w+(?:-\w+)*))?(?<form>#\w+)?\s*[:=]\s*(?<text>.*)",
            RegexOptions.ExplicitCapture);
        private static readonly Regex IsContinuedLine = new Regex(@"^([\\_].|[^\\_])*[\\_]$", RegexOptions.ExplicitCapture);
        private static readonly Regex Comment = new Regex(@"^\s*;", RegexOptions.ExplicitCapture);
        private static readonly Regex BlankLine = new Regex(@"^\s*$");
        private static readonly Regex Unescape = new Regex(@"([\\_'])\1");

        // LanguageCode's grammar (runtime SPEC: Language codes): language(-Script)?(-REGION)?
        private static readonly Regex LanguageCode = new Regex(
            @"^(?<lang>[a-zA-Z]{2,3})(-(?<script>[a-zA-Z]{4}))?(-(?<region>[a-zA-Z]{2}|[0-9]{3}))?\z",
            RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether <paramref name="key"/> is a key's name, as <c>WordsParser.IsKeyName</c> decides:
        /// dotted segments of letters, digits, '_' and '-', each starting with one of the first three
        /// (<c>menu.file-open</c>), or a constant, '$' and one segment (<c>$unit</c>).
        /// </summary>
        public static bool IsKeyName(string key) => key != null && KeyName.IsMatch(key);

        /// <summary>
        /// Reads <paramref name="text"/> as a language code, as <c>LanguageCode.TryParse</c> does, and
        /// cases it by kind: <c>zh-hans-cn</c> is <c>zh-Hans-CN</c>. The empty string is none.
        /// </summary>
        public static bool TryNormalizeLanguage(string text, out string code)
        {
            var match = text == null ? null : LanguageCode.Match(text);
            if (match == null || !match.Success)
            {
                code = null;
                return false;
            }
            var script = match.Groups["script"].Value;
            var region = match.Groups["region"].Value;
            code = match.Groups["lang"].Value.ToLowerInvariant()
                + (script.Length == 0 ? "" : "-" + char.ToUpperInvariant(script[0]) + script.Substring(1).ToLowerInvariant())
                + (region.Length == 0 ? "" : "-" + region.ToUpperInvariant());
            return true;
        }

        public static IReadOnlyList<WordsEntry> Parse(string text, string filePath)
        {
            var entries = new List<WordsEntry>();
            // a block named twice in a file is one key, its fields merged as the runtime merges them
            var byKey = new Dictionary<string, WordsEntry>(StringComparer.Ordinal);

            // the last full header, which [.child] headers resolve against: even one that was no key
            var baseKey = string.Empty;
            // the key whose fields are being read; null before the first block and in a skipped one
            WordsEntry current = null;
            // the field the next line continues, while the last line ended in a continuation
            Target target = null;

            var lineNumber = 0;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;

                    // a continued field runs on through this line, whatever it says
                    if (target != null)
                    {
                        var segment = Segment(line, out var continued);
                        target.Entry?.AppendValue(target.Language, target.Form, segment);
                        if (!continued)
                            target = null;
                        continue;
                    }

                    if (Comment.IsMatch(line) || BlankLine.IsMatch(line))
                        continue;

                    var block = Block.Match(line);
                    if (block.Success)
                    {
                        var name = block.Groups[1].Value;
                        string key;
                        // [] names no key, and leaves none for a [.child] to resolve against
                        if (name.StartsWith(".", StringComparison.Ordinal))
                            key = baseKey + name;
                        else
                            key = baseKey = name;

                        current = null;
                        if (IsKeyName(key) && !byKey.TryGetValue(key, out current))
                        {
                            current = new WordsEntry(key, filePath, lineNumber);
                            byKey.Add(key, current);
                            entries.Add(current);
                        }
                        continue;
                    }

                    var pair = Pair.Match(line);
                    if (!pair.Success)
                        continue; // unrecognised: skipped, as the runtime skips it

                    var field = pair.Groups["key"].Value;
                    var language = pair.Groups["lang"].Value;
                    var form = pair.Groups["form"].Value.ToLowerInvariant();

                    // Only a block's value= and value-<code>= fields, and their valid forms, are kept;
                    // everything else (labels before the first block, comment=, context=, param-x=, a
                    // language that is no code, a form that is none) is read past, continuation and all.
                    var entry = field == "value" ? current : null;
                    if (entry != null && language.Length != 0 && !TryNormalizeLanguage(language, out language))
                        entry = null;
                    string formName = null;
                    if (entry != null && form.Length != 0)
                    {
                        formName = form.Substring(1);
                        // the plain value is the 'one' form, and only CLDR's categories are forms
                        if (formName == "one" || !WordsEntry.PluralCategories.Contains(formName))
                            entry = null;
                    }

                    var first = Segment(pair.Groups["text"].Value, out var startsContinuation);
                    entry?.SetValue(language, formName, first);
                    if (startsContinuation)
                        target = new Target(entry, language, formName);
                }
            }

            return entries;
        }

        // One line's worth of a field's text, as WordsParser.TryReadLine reads it: a trailing '\'
        // becomes a newline and a trailing '_' nothing, then each doubled escape becomes one.
        private static string Segment(string text, out bool continued)
        {
            continued = IsContinuedLine.IsMatch(text);
            if (continued)
            {
                var newlineKept = text[text.Length - 1] == '\\';
                text = text.Substring(0, text.Length - 1);
                if (newlineKept)
                    text += "\n";
            }
            return Unescape.Replace(text, "$1");
        }

        private sealed class Target
        {
            public Target(WordsEntry entry, string language, string form)
            {
                Entry = entry;
                Language = language;
                Form = form;
            }

            /// <summary>Where the continuation goes; null when the field is read past.</summary>
            public WordsEntry Entry { get; }
            public string Language { get; }
            public string Form { get; }
        }
    }
}
