using System.IO;
using System.Net;

namespace WordsXaml.Ini
{
    /// <summary>
    /// The body of the hover / Ctrl+Q tooltip for a words key: the key, its one-line preview, the
    /// plural forms it has, and the file that declares it. Kept here, SDK-free, so the XAML and C#
    /// quick-doc providers show the same thing and it can be tested.
    /// </summary>
    public static class WordsQuickDoc
    {
        /// <summary>HTML for <paramref name="key"/>'s tooltip, or null if the index does not know it.</summary>
        public static string Html(WordsIndex index, string key)
        {
            var preview = index?.RenderPreview(key);
            if (preview == null || !index.TryGet(key, out var entry))
                return null;

            var html = $"<b>{WebUtility.HtmlEncode(key)}</b><br/>{WebUtility.HtmlEncode(preview)}";
            var forms = index.RenderForms(key);
            if (forms != null)
                html += $"<br/>forms: {WebUtility.HtmlEncode(forms)}";
            return html + $"<br/><i>{WebUtility.HtmlEncode(Path.GetFileName(entry.FilePath))}</i>";
        }
    }
}
