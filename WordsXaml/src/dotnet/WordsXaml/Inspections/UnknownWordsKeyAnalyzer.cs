using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi.Xaml.Tree.MarkupExtensions;
using WordsXaml.Index;
using WordsXaml.Xaml;

namespace WordsXaml.Inspections
{
    /// <summary>
    /// Raises <see cref="UnknownWordsKeyHighlighting"/> on an <c>l:Words</c> key, in any of its three
    /// spellings, that no *-words.ini of the solution declares. Registered the way JetBrains' own XAML
    /// markup analyzers are (<c>TextAfterMarkupAnalyzer</c> runs on <see cref="IMarkup"/> too). Silent
    /// while the solution has no keys at all, so a project that keeps its words elsewhere is not
    /// covered in squiggles.
    /// </summary>
    [ElementProblemAnalyzer(typeof(IMarkup), HighlightingTypes = new[] { typeof(UnknownWordsKeyHighlighting) })]
    public sealed class UnknownWordsKeyAnalyzer : ElementProblemAnalyzer<IMarkup>
    {
        private readonly WordsIndexService _indexService;

        public UnknownWordsKeyAnalyzer(WordsIndexService indexService)
        {
            _indexService = indexService;
        }

        protected override void Run(IMarkup element, ElementProblemAnalyzerData data, IHighlightingConsumer consumer)
        {
            var token = WordsMarkupContext.TryGetKeyToken(element);
            if (token == null || token.Key.Length == 0)
                return;

            var index = _indexService.Index;
            if (index.Keys.Count == 0 || index.TryGet(token.Key, out _))
                return;

            consumer.AddHighlighting(new UnknownWordsKeyHighlighting(token.Key, token.Range));
        }
    }
}
