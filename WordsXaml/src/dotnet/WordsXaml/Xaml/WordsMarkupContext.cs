using JetBrains.DocumentModel;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Xaml.Tree.MarkupExtensions;

namespace WordsXaml.Xaml
{
    /// <summary>
    /// Recognises the <c>{l:Words &lt;key&gt;}</c> context inside XAML PSI and pulls the key argument out.
    /// Centralised so completion, quick-doc and the inspection all agree on what "we are on a Words key"
    /// means.
    ///
    /// In ReSharper 2025.3, a markup-extension usage is an <see cref="IMarkup"/> node: <c>Name</c>/
    /// <c>NameNode</c> identify the extension and <c>Value</c> is its (single positional) argument. For
    /// <c>{l:Words params.focal-law-base.capture-delay}</c> the value is an <see cref="IPathValue"/> whose
    /// text is the dotted key. We match on the extension's short name (<c>NameNode.Id == "Words"</c>) so
    /// the per-file alias ("l") is irrelevant.
    /// </summary>
    public static class WordsMarkupContext
    {
        public const string MarkupExtensionShortName = "Words";

        /// <summary>The xmlns the extension is registered under; kept for reference/diagnostics.</summary>
        public const string MarkupExtensionXmlns = "https://github.com/pzahra/words";
        /// <summary>The name the extension was registered under before the project URL became the namespace; still an alias.</summary>
        public const string LegacyMarkupExtensionXmlns = "pattech.words";

        /// <summary>
        /// Returns the key text and its document range if <paramref name="node"/> sits inside an
        /// <c>l:Words</c> markup extension; otherwise null. When the key is still empty (caret right after
        /// <c>{l:Words }</c>) the key is "" and the range collapses to the caret's containing node, so the
        /// completion list still opens.
        /// </summary>
        public static WordsKeyToken TryGetKeyToken(ITreeNode node)
        {
            if (node == null)
                return null;

            var markup = node.GetContainingNode<IMarkup>(returnThis: true);
            if (markup == null || !IsWordsExtension(markup))
                return null;

            // Value is the positional argument node (IPathValue for a dotted key). Null while empty.
            var valueNode = markup.Value;
            if (valueNode == null)
                return new WordsKeyToken(string.Empty, markup, markup.GetDocumentRange());

            return new WordsKeyToken(valueNode.GetText(), valueNode, valueNode.GetDocumentRange());
        }

        private static bool IsWordsExtension(IMarkup markup)
        {
            // NameNode.Id is the extension's local name without the alias qualifier.
            var id = markup.NameNode?.Id;
            return id == MarkupExtensionShortName;
        }
    }

    /// <summary>The key argument of an <c>l:Words</c> extension, plus where it lives for range-based edits.</summary>
    public sealed class WordsKeyToken
    {
        public WordsKeyToken(string key, ITreeNode argumentNode, DocumentRange range)
        {
            Key = key;
            ArgumentNode = argumentNode;
            Range = range;
        }

        public string Key { get; }
        public ITreeNode ArgumentNode { get; }
        public DocumentRange Range { get; }
    }
}
