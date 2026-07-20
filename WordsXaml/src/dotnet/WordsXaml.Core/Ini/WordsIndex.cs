using System;
using System.Collections.Generic;
using System.Linq;

namespace WordsXaml.Ini
{
    /// <summary>
    /// Solution-wide, case-sensitive key -> entry map aggregated from every parsed *-words.ini file.
    /// In the real plugin this is rebuilt when an .ini file changes; here it is a plain immutable
    /// snapshot so it can be tested and reasoned about on its own.
    ///
    /// Later keys win on duplicate: matches typical last-loaded-overrides ini behaviour. If Helios
    /// instead errors on duplicates, swap the assignment for a diagnostic.
    /// </summary>
    public sealed class WordsIndex
    {
        /// <summary>Default cap for tooltip preview text; long help bodies get an ellipsis.</summary>
        public const int DefaultPreviewLength = 100;

        private readonly Dictionary<string, WordsEntry> _byKey;

        public WordsIndex(IEnumerable<WordsEntry> entries)
        {
            _byKey = new Dictionary<string, WordsEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
                _byKey[e.Key] = e;
        }

        public IReadOnlyCollection<string> Keys => _byKey.Keys;

        public bool TryGet(string key, out WordsEntry entry) => _byKey.TryGetValue(key, out entry);

        /// <summary>Prefix/substring matches for a completion query, ordered prefix-first then alphabetical.</summary>
        public IEnumerable<WordsEntry> Match(string query)
        {
            query ??= string.Empty;
            return _byKey.Values
                .Where(e => e.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(e => e.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(e => e.Key, StringComparer.Ordinal);
        }

        /// <summary>
        /// Returns the entries directly under <paramref name="committedPrefix"/> collapsed to a single
        /// tree level: one branch per distinct next segment (drill-down, insert text ends with '.') plus
        /// any leaf keys at this level. This is what keeps the completion list short — at the root you see
        /// ~a dozen top-level segments instead of thousands of keys, and each accepted branch reveals the
        /// next level.
        ///
        /// <paramref name="committedPrefix"/> is the text up to and including the last '.', e.g. "params."
        /// (or "" at the root). Matching is case-insensitive but insert text always uses the key's own
        /// casing. A trailing partial segment the user is still typing must NOT be included here — strip it
        /// to the last '.' first (see <c>WordsCompletionProvider</c>); ReSharper's matcher filters the
        /// returned level by that partial text.
        /// </summary>
        public IReadOnlyList<WordsSegment> CompleteSegments(string committedPrefix, int maxLeafPreview = DefaultPreviewLength)
        {
            committedPrefix ??= string.Empty;

            var branchChildCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var leaves = new List<WordsSegment>();

            foreach (var key in _byKey.Keys)
            {
                if (!key.StartsWith(committedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var restStart = committedPrefix.Length;
                if (restStart >= key.Length)
                    continue; // key == prefix exactly (prefix had no trailing dot); nothing to add

                var dot = key.IndexOf('.', restStart);
                if (dot >= 0)
                {
                    // Branch: take up to and including the next dot, using the key's canonical casing.
                    var branchText = key.Substring(0, dot + 1);
                    branchChildCounts.TryGetValue(branchText, out var count);
                    branchChildCounts[branchText] = count + 1;
                }
                else
                {
                    leaves.Add(new WordsSegment(key, isBranch: false, childCount: 0,
                        leafPreview: RenderPreview(key, maxLeafPreview)));
                }
            }

            var result = new List<WordsSegment>(branchChildCounts.Count + leaves.Count);
            foreach (var b in branchChildCounts)
                result.Add(new WordsSegment(b.Key, isBranch: true, childCount: b.Value, leafPreview: null));
            result.AddRange(leaves);
            result.Sort((a, b) => string.CompareOrdinal(a.InsertText, b.InsertText));
            return result;
        }

        /// <summary>The committed part of a typed key: everything up to and including the last '.', else "".</summary>
        public static string CommittedPrefix(string typed)
        {
            if (string.IsNullOrEmpty(typed))
                return string.Empty;
            var lastDot = typed.LastIndexOf('.');
            return lastDot >= 0 ? typed.Substring(0, lastDot + 1) : string.Empty;
        }

        /// <summary>
        /// Single-line preview for a key's invariant value: continuations collapsed to spaces and
        /// truncated with an ellipsis. Cross-refs ({&gt;key}) and [icon:x] tokens are shown verbatim.
        /// Returns null for an unknown key.
        /// </summary>
        public string RenderPreview(string key, int maxLength = DefaultPreviewLength)
        {
            if (!TryGet(key, out var entry))
                return null;

            var value = entry.DefaultValue ?? entry.Values.Values.FirstOrDefault();
            if (string.IsNullOrEmpty(value))
                return value;

            var oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length > maxLength
                ? oneLine.Substring(0, maxLength).TrimEnd() + "…"
                : oneLine;
        }
    }
}
