using System;
using System.Collections.Immutable;
using System.IO;
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
    /// <c>[.metals]</c> =&gt; <c>material.metals</c>). Consumers add the ini(s) as AdditionalFiles, e.g.
    /// <c>&lt;AdditionalFiles Include="Assets\**\*words.ini" /&gt;</c>. If none are present the analyzer
    /// stays silent, so it never produces false positives on projects that don't opt in.
    /// </remarks>
    internal static class WordsIniKeys
    {
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

        private static void CollectKeys(string text, ImmutableHashSet<string>.Builder keys)
        {
            // The most recent header that did NOT start with '.', used to resolve [.suffix] sections.
            string lastFullKey = null;

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.Length == 0 || trimmed[0] != '[')
                    {
                        continue;
                    }

                    var close = trimmed.IndexOf(']');
                    if (close <= 1)
                    {
                        continue;
                    }

                    var header = trimmed.Substring(1, close - 1).Trim();
                    string key;
                    if (header.Length > 0 && header[0] == '.')
                    {
                        key = lastFullKey != null ? lastFullKey + header : header.TrimStart('.');
                    }
                    else
                    {
                        key = header;
                        lastFullKey = header;
                    }

                    if (key.Length > 0)
                    {
                        keys.Add(key);
                    }
                }
            }
        }
    }
}
