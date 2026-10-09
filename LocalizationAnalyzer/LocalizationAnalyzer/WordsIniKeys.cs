using System;
using System.Collections.Immutable;
using System.IO;
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
        private static readonly Regex KeyName = new Regex(@"^(\$\w[\w-]*|\w[\w-]*(\.\w[\w-]*)*)\z");
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

        /// <summary>Whether <paramref name="key"/> is a key's name, as <c>WordsParser.IsKeyName</c> decides.</summary>
        internal static bool IsKeyName(string key) => key != null && KeyName.IsMatch(key);

        //what a key name can begin with: a constant's start, or whole segments and a segment begun
        private static readonly Regex KeyNameStart = new Regex(@"^(\$(\w[\w-]*)?|(\w[\w-]*\.)*(\w[\w-]*)?)\z");

        /// <summary>Whether some key name begins with <paramref name="prefix"/> (the fixed start of a built key).</summary>
        internal static bool CouldStartKeyName(string prefix) => prefix != null && KeyNameStart.IsMatch(prefix);

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
