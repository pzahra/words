using System;
using JetBrains.Application.DataContext;
using JetBrains.Application.UI.Components.Theming;
using JetBrains.ReSharper.Feature.Services.QuickDoc;
using JetBrains.ReSharper.Feature.Services.QuickDoc.Render;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.DataContext;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Tree;
using WordsXaml.Index;
using WordsXaml.Ini;
using WordsXaml.Keys;

namespace WordsXaml.CSharp
{
    /// <summary>
    /// The hover / Ctrl+Q tooltip for a words key in a C# string literal bound to a <c>[WordsKey]</c>
    /// target: the same body as in XAML (<see cref="WordsQuickDoc.Html"/>). The node under the caret is
    /// found as JetBrains' JSON schema tooltip finds it: the editor view's range, translated into the
    /// primary PSI file.
    /// </summary>
    [QuickDocProvider(-10)]
    public sealed class WordsKeyQuickDocProvider : IQuickDocProvider
    {
        private readonly WordsIndexService _indexService;
        private readonly ITheming _theming;
        private readonly IXmlDocHtmlRenderer _renderer;

        public WordsKeyQuickDocProvider(WordsIndexService indexService, ITheming theming, IXmlDocHtmlRenderer renderer)
        {
            _indexService = indexService;
            _theming = theming;
            _renderer = renderer;
        }

        public bool CanNavigate(IDataContext context) => Find(context) != null;

        public void Resolve(IDataContext context, Action<IQuickDocPresenter, PsiLanguageType> resolved)
        {
            var found = Find(context);
            if (found == null)
                return;
            resolved(new WordsQuickDocPresenter(found.Item1, found.Item2, _theming, _renderer), CSharpLanguage.Instance);
        }

        /// <summary>The key under the caret and its tooltip body, or null.</summary>
        private Tuple<string, string> Find(IDataContext context)
        {
            var file = context.GetData(PsiDataConstants.SOURCE_FILE)?.GetPrimaryPsiFile();
            if (file == null || !file.Language.Is<CSharpLanguage>())
                return null;

            var view = context.GetData(PsiDataConstants.PSI_EDITOR_VIEW)?.DefaultSourceFile;
            if (view == null)
                return null;

            var range = view.DocumentRangeFromMainDocument;
            var token = WordsKeySites.FindAt(file.FindNodeAt(file.Translate(range)), range.StartOffset);
            if (token == null || token.Key.Length == 0)
                return null;

            var html = WordsQuickDoc.Html(_indexService.Index, token.Key);
            return html == null ? null : Tuple.Create(token.Key, html);
        }
    }
}
