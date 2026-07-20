using JetBrains.Application.Parts;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems.Impl;
using JetBrains.ReSharper.Features.Intellisense.CodeCompletion.Xaml;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Xaml;
using WordsXaml.Index;
using WordsXaml.Ini;

namespace WordsXaml.Xaml
{
    /// <summary>
    /// Offers words-key completion inside <c>{l:Words |}</c>. All the "which keys / what preview" logic
    /// lives in <see cref="Ini.WordsIndex"/>; this class only bridges the SDK. The registration
    /// (<c>Instantiation.DemandAnyThreadSafe</c>) matches JetBrains' own XAML items providers.
    /// </summary>
    [Language(typeof(XamlLanguage), Instantiation.DemandAnyThreadSafe)]
    public sealed class WordsCompletionProvider : ItemsProviderOfSpecificContext<XamlCodeCompletionContext>
    {
        protected override bool IsAvailable(XamlCodeCompletionContext context)
        {
            return FindKeyContext(context) != null;
        }

        protected override bool AddLookupItems(XamlCodeCompletionContext context, IItemsCollector collector)
        {
            var token = FindKeyContext(context);
            if (token == null)
                return false;

            var index = context.BasicContext.Solution.GetComponent<WordsIndexService>().Index;

            // Show only the current tree level (next segment), not every fully-qualified key: at the root
            // that's ~a dozen branches instead of thousands of keys. The committed prefix is the typed text
            // up to its last '.'; ReSharper's matcher then filters this level by the partial segment. As
            // the user accepts a branch (which ends in '.'), the next completion shows the level below.
            var committed = WordsIndex.CommittedPrefix(token.Key);

            foreach (var segment in index.CompleteSegments(committed))
            {
                // typeText (right-aligned): child count for a branch, resolved value for a leaf.
                var typeText = segment.IsBranch
                    ? $"{segment.ChildCount} key{(segment.ChildCount == 1 ? "" : "s")}"
                    : segment.LeafPreview ?? string.Empty;

                var item = new TextLookupItem(segment.InsertText, typeText, isDynamic: false);
                item.InitializeRanges(context.Ranges, context.BasicContext);
                collector.Add(item);
            }

            return true;
        }

        /// <summary>
        /// Locates the <c>{l:Words …}</c> context from the node under the caret. The reparsed
        /// "unterminated" tree is preferred because while typing (<c>{l:Words fo|</c>) the original tree
        /// may not parse; we fall back to the committed tree node.
        /// </summary>
        private static WordsKeyToken FindKeyContext(XamlCodeCompletionContext context)
        {
            ITreeNode node = context.UnterminatedContext?.TreeNode ?? context.TreeNode;
            return WordsMarkupContext.TryGetKeyToken(node);
        }
    }
}
