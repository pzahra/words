using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LocalizationAnalyzer
{
    /// <summary>
    /// Extracts the set of declared keys from the <c>*words.ini</c> files supplied to the compilation as
    /// AdditionalFiles, for use by <see cref="WordsKeyAnalyzer"/>.
    /// </summary>
    /// <remarks>
    /// Only section headers are needed (not values), including <c>[.suffix]</c> inheritance where a
    /// dot-prefixed section extends the last fully-qualified header (e.g. <c>[material]</c> then
    /// <c>[.metals]</c> =&gt; <c>material.metals</c>). Headers are read as the runtime's
    /// <c>WordsParser</c> reads them, which this assembly cannot reference, so its grammar is ported
    /// here: a header whose resolved name is no key name (<c>WordsParser.IsKeyName</c>) is no key, as
    /// the runtime skips it with WP:NAME, and the lines a field's continuation runs on through are
    /// never headers. Consumers add the ini(s) as AdditionalFiles, e.g.
    /// <c>&lt;AdditionalFiles Include="Assets\**\*words.ini" /&gt;</c>. If none are present the analyzer
    /// stays silent, so it never produces false positives on projects that don't opt in.
    /// </remarks>
    internal static class WordsIniKeys
    {
        // WordsParser's own patterns, as written there: keep them in step.
        private static readonly Regex Block = new Regex(@"^\[(?<1>[^]]*)\]", RegexOptions.ExplicitCapture);
        private static readonly Regex Pair = new Regex(
            @"^(?<key>\w+)(-(?<lang>\w+(?:-\w+)*))?(?<form>#\w+)?\s*[:=]\s*(?<text>.*)",
            RegexOptions.ExplicitCapture);
        private static readonly Regex IsContinuedLine = new Regex(@"^([\\_].|[^\\_])*[\\_]$", RegexOptions.ExplicitCapture);
        private static readonly Regex Comment = new Regex(@"^\s*;", RegexOptions.ExplicitCapture);

        /// <summary>Loads and merges the keys declared across all <c>*words.ini</c> AdditionalFiles.</summary>
        public static ImmutableHashSet<string> Load(
                ImmutableArray<AdditionalText> additionalFiles,
                CancellationToken cancellationToken)
        {
            ImmutableHashSet<string>.Builder builder = null;

            foreach (var file in additionalFiles)
            {
                if (!IsWordsIni(file.Path))
                {
                    continue;
                }

                var text = file.GetText(cancellationToken);
                if (text is null)
                {
                    continue;
                }

                if (builder is null)
                {
                    builder = ImmutableHashSet.CreateBuilder(StringComparer.Ordinal);
                }
                CollectKeys(text.ToString(), builder);
            }

            return builder?.ToImmutable() ?? ImmutableHashSet<string>.Empty;
        }

        private static bool IsWordsIni(string path) =>
            path != null && path.EndsWith("words.ini", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="key"/> is a key's name, as <c>WordsParser.IsKeyName</c> decides:
        /// dotted segments of UAX #31 identifier characters and <c>-</c>, or <c>$</c> and one, in NFC.
        /// </summary>
        internal static bool IsKeyName(string key)
        {
            if (key == null)
            {
                return false;
            }
            if (key.StartsWith("$", StringComparison.Ordinal))
            {
                return IsSegment(key, 1, key.Length) && InNfc(key);
            }
            var start = 0;
            for (int dot; (dot = key.IndexOf('.', start)) >= 0; start = dot + 1)
            {
                if (!IsSegment(key, start, dot))
                {
                    return false;
                }
            }
            return IsSegment(key, start, key.Length) && InNfc(key);
        }

        /// <summary>
        /// Whether some key name begins with <paramref name="prefix"/> (the fixed start of a built key):
        /// a constant's start, or whole segments and a segment begun.
        /// </summary>
        internal static bool CouldStartKeyName(string prefix)
        {
            if (prefix == null)
            {
                return false;
            }
            if (prefix.StartsWith("$", StringComparison.Ordinal))
            {
                return prefix.Length == 1 || IsSegment(prefix, 1, prefix.Length);
            }
            var start = 0;
            for (int dot; (dot = prefix.IndexOf('.', start)) >= 0; start = dot + 1)
            {
                if (!IsSegment(prefix, start, dot))
                {
                    return false;
                }
            }
            return start == prefix.Length || IsSegment(prefix, start, prefix.Length);
        }

        // UAX #31's identifier in the profile key names use, WordsParser's ported: an XID_Start
        // character, a decimal digit or a connector (_) first, then XID_Continue characters and -,
        // a code point at a time. The categories are the host's .NET's, which may know fewer
        // code points than the app's.
        private static bool IsSegment(string text, int from, int to)
        {
            for (var i = from; i < to;)
            {
                int c;
                if (char.IsHighSurrogate(text[i]) && i + 1 < to && char.IsLowSurrogate(text[i + 1]))
                {
                    c = char.ConvertToUtf32(text[i], text[i + 1]);
                }
                else if (char.IsSurrogate(text[i]))
                {
                    return false;
                }
                else
                {
                    c = text[i];
                }
                var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                var fits = i == from
                    ? XidStart(c, category) || category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.ConnectorPunctuation
                    : c == '-' || XidContinue(c, category);
                if (!fits)
                {
                    return false;
                }
                i += c > 0xFFFF ? 2 : 1;
            }
            return to > from;
        }

        private static bool XidStart(int c, UnicodeCategory category)
        {
            if (c == 0x1885 || c == 0x1886 || c == 0x2118 || c == 0x212E)
            {
                return true;
            }
            if (c == 0x2E2F || c == 0x037A || c == 0x0E33 || c == 0x0EB3 || c == 0x309B || c == 0x309C || c == 0xFF9E || c == 0xFF9F
                || (c >= 0xFC5E && c <= 0xFC63) || c == 0xFDFA || c == 0xFDFB || (c >= 0xFE70 && c <= 0xFE7E && c % 2 == 0))
            {
                return false;
            }
            return category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter
                || category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter
                || category == UnicodeCategory.OtherLetter || category == UnicodeCategory.LetterNumber;
        }

        private static bool XidContinue(int c, UnicodeCategory category)
        {
            if (c == 0x00B7 || c == 0x0387 || (c >= 0x1369 && c <= 0x1371) || c == 0x19DA || c == 0x200C || c == 0x200D
                || c == 0x30FB || c == 0xFF65 || c == 0x0E33 || c == 0x0EB3 || c == 0xFF9E || c == 0xFF9F)
            {
                return true;
            }
            return XidStart(c, category) || category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.ConnectorPunctuation;
        }

        private static bool InNfc(string name)
        {
            try
            {
                return name.IsNormalized(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Walks the lines as <c>WordsParser.Load</c> does, adding each block whose resolved name is a
        /// key name. A <c>[.child]</c> resolves against the last full header, even one that was no key
        /// name, so the children of <c>[a b]</c> and of a constant (<c>[$c]</c>) are no keys either;
        /// before any full header it resolves against nothing, and stays no key.
        /// </summary>
        private static void CollectKeys(string text, ImmutableHashSet<string>.Builder keys)
        {
            var baseKey = "";
            // a field's text ended in a continuation: the next line is more of it, whatever it says
            var continued = false;

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (continued)
                    {
                        continued = IsContinuedLine.IsMatch(line);
                        continue;
                    }
                    if (Comment.IsMatch(line))
                    {
                        continue;
                    }

                    var block = Block.Match(line);
                    if (block.Success)
                    {
                        var name = block.Groups[1].Value;
                        string key;
                        // [] names no key, and leaves none for a [.child] to resolve against
                        if (name.StartsWith(".", StringComparison.Ordinal))
                        {
                            key = baseKey + name;
                        }
                        else
                        {
                            key = baseKey = name;
                        }

                        if (IsKeyName(key))
                        {
                            keys.Add(key);
                        }
                        continue;
                    }

                    // a field, kept or read past for its language alike, runs on through its continuations
                    var pair = Pair.Match(line);
                    if (pair.Success)
                    {
                        continued = IsContinuedLine.IsMatch(pair.Groups["text"].Value);
                    }
                }
            }
        }
    }
}
