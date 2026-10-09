using System;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems.Impl;
using WordsXaml.Ini;

namespace WordsXaml.Keys
{
    /// <summary>
    /// Fills a completion list with words keys for a key site, XAML or C#. All the "which keys / what
    /// preview" logic lives in <see cref="WordsIndex"/>; this only bridges the SDK.
    ///
    /// The ranges are the key's own, not the ones the language's completion context computed: the
    /// typed prefix runs from the key's start to the caret, and accepting an item replaces the whole
    /// key, so the quotes of <c>'main.title'</c> or <c>"main.title"</c> stay where they are.
    /// </summary>
    public static class WordsLookupItems
    {
        public static bool Add(CodeCompletionContext context, WordsIndex index, WordsKeyToken token, IItemsCollector collector)
        {
            var keyStart = token.Range.StartOffset;
            var caret = context.CaretDocumentOffset;
            var typedLength = Math.Min(Math.Max(caret.Offset - keyStart.Offset, 0), token.Key.Length);
            var insertEnd = keyStart.Shift(typedLength);
            var ranges = new TextLookupRanges(new DocumentRange(keyStart, insertEnd), token.Range, isGreedyToLeft: false);

            // Show only the current tree level (next segment), not every fully-qualified key: at the root
            // that's ~a dozen branches instead of thousands of keys. The committed prefix is the typed text
            // up to its last '.'; ReSharper's matcher then filters this level by the typed text. As the
            // user accepts a branch (which ends in '.'), the next completion shows the level below.
            var committed = WordsIndex.CommittedPrefix(token.Key.Substring(0, typedLength));

            foreach (var segment in index.CompleteSegments(committed))
            {
                // typeText (right-aligned): child count for a branch, resolved value for a leaf.
                var typeText = segment.IsBranch
                    ? $"{segment.ChildCount} key{(segment.ChildCount == 1 ? "" : "s")}"
                    : segment.LeafPreview ?? string.Empty;

                var item = new TextLookupItem(segment.InsertText, typeText, isDynamic: false);
                item.InitializeRanges(ranges, context);
                collector.Add(item);
            }

            return true;
        }
    }
}
