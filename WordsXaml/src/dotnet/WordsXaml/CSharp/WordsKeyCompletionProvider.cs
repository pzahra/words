using JetBrains.Application.Parts;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems;
using JetBrains.ReSharper.Feature.Services.CSharp.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Tree;
using WordsXaml.Index;
using WordsXaml.Keys;

namespace WordsXaml.CSharp
{
    /// <summary>
    /// Offers words-key completion inside a C# string literal bound to a <c>[WordsKey]</c> target:
    /// <c>Words.Known["|"]</c>, <c>Words.FormatKnown("|", …)</c>, <c>[Words("|")]</c>. The same
    /// hierarchical list as in XAML (<see cref="WordsLookupItems"/>). Registered as JetBrains' own
    /// in-string C# providers are (the I18n <c>CodeCompletionInsideStringItemsProvider</c>).
    /// </summary>
    [Language(typeof(CSharpLanguage), Instantiation.DemandAnyThreadSafe)]
    public sealed class WordsKeyCompletionProvider : ItemsProviderOfSpecificContext<CSharpCodeCompletionContext>
    {
        protected override bool IsAvailable(CSharpCodeCompletionContext context)
        {
            return FindKeyContext(context) != null;
        }

        protected override bool AddLookupItems(CSharpCodeCompletionContext context, IItemsCollector collector)
        {
            var token = FindKeyContext(context);
            if (token == null)
                return false;

            var index = context.BasicContext.Solution.GetComponent<WordsIndexService>().Index;
            return WordsLookupItems.Add(context.BasicContext, index, token, collector);
        }

        /// <summary>
        /// The literal under the caret in the committed file, not the reparsed completion tree, so its
        /// offsets are the document's; a string literal parses while being typed.
        /// </summary>
        private static WordsKeyToken FindKeyContext(CSharpCodeCompletionContext context)
        {
            var basic = context.BasicContext;
            var file = basic.File;
            if (file == null)
                return null;

            var caret = basic.CaretDocumentOffset;
            var node = file.FindNodeAt(file.Translate(new DocumentRange(caret)));
            return WordsKeySites.FindAt(node, caret);
        }
    }
}
