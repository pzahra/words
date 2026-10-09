using JetBrains.Application.Parts;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems;
using JetBrains.ReSharper.Features.Intellisense.CodeCompletion.Xaml;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Xaml;
using WordsXaml.Index;
using WordsXaml.Keys;

namespace WordsXaml.Xaml
{
    /// <summary>
    /// Offers words-key completion inside <c>{l:Words |}</c>, <c>{l:Words Key=|}</c> and
    /// <c>{l:Words '|'}</c>. The list itself comes from <see cref="WordsLookupItems"/>; this class only
    /// says where the key is. The registration (<c>Instantiation.DemandAnyThreadSafe</c>) matches
    /// JetBrains' own XAML items providers.
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
            return WordsLookupItems.Add(context.BasicContext, index, token, collector);
        }

        /// <summary>
        /// Locates the key from the document text around the caret. The text, not the tree, because
        /// while typing (<c>{l:Words fo|</c>) the extension may not parse yet, and the reparsed
        /// completion tree's offsets are not the document's.
        /// </summary>
        private static WordsKeyToken FindKeyContext(XamlCodeCompletionContext context)
        {
            return WordsMarkupContext.FindAtCaret(context.BasicContext.CaretDocumentOffset);
        }
    }
}
