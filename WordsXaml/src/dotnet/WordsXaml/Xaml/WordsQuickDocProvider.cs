using JetBrains.Application.Parts;
using JetBrains.ProjectModel;
using WordsXaml.Index;

namespace WordsXaml.Xaml
{
    /// <summary>
    /// Supplies the hover / Ctrl+Q tooltip for an <c>l:Words</c> key: the resolved value text, collapsed
    /// to one line and truncated (see <see cref="WordsIndex.RenderPreview"/>).
    ///
    /// TODO(SDK): implement the IQuickDocProvider contract for the current SDK — CanNavigate(context) to
    /// gate on WordsMarkupContext, and Resolve(...) to return an IQuickDocPresenter whose HTML body is the
    /// rendered preview. The interesting logic is already in WordsIndex; this class is pure plumbing.
    /// </summary>
    [SolutionComponent(Instantiation.DemandAnyThreadSafe)]
    public sealed class WordsQuickDocProvider
    {
        private readonly WordsIndexService _indexService;

        public WordsQuickDocProvider(WordsIndexService indexService)
        {
            _indexService = indexService;
        }

        /// <summary>Returns the HTML-ready preview for a key, or null if unknown.</summary>
        public string BuildTooltip(string key)
        {
            var preview = _indexService.Index.RenderPreview(key);
            if (preview == null)
                return null;

            _indexService.Index.TryGet(key, out var entry);
            var source = entry != null ? System.IO.Path.GetFileName(entry.FilePath) : "";
            return $"<b>{key}</b><br/>{System.Net.WebUtility.HtmlEncode(preview)}<br/><i>{source}</i>";
        }
    }
}
