using System;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Xaml.Tree.MarkupExtensions;
using JetBrains.Util;
using WordsXaml.Keys;
using WordsXaml.Sites;

namespace WordsXaml.Xaml
{
    /// <summary>
    /// Recognises the <c>{l:Words &lt;key&gt;}</c> context in XAML and pulls the key out. Centralised so
    /// completion, quick-doc and the inspection all agree on what "we are on a Words key" means.
    ///
    /// The key itself is read from the text by <see cref="MarkupKeys"/> (SDK-free, tested), which
    /// knows the three spellings: positional <c>{l:Words main.title}</c>, named
    /// <c>{l:Words Key=main.title}</c> and quoted <c>{l:Words 'main.title'}</c>. The PSI only says
    /// where an extension starts: in ReSharper 2025.3 a markup-extension usage is an
    /// <see cref="IMarkup"/> node, whose <c>Value</c> is either the positional argument or an
    /// attribute list, so reading the node's text sidesteps how each spelling is shaped. Completion
    /// and quick-doc read the document around the caret instead, which also works while the
    /// extension is half-typed and does not parse.
    /// </summary>
    public static class WordsMarkupContext
    {
        /// <summary>The xmlns the extension is registered under; kept for reference/diagnostics.</summary>
        public const string MarkupExtensionXmlns = "https://github.com/pzahra/words";
        /// <summary>The name the extension was registered under before the project URL became the namespace; still an alias.</summary>
        public const string LegacyMarkupExtensionXmlns = "pattech.words";

        // How much of the document around the caret to read: an extension sits in one attribute value.
        private const int WindowBefore = 2048;
        private const int WindowAfter = 512;

        /// <summary>
        /// The key of <paramref name="markup"/> and its document range if it is an <c>l:Words</c>
        /// extension that names one; otherwise null. For the inspection.
        /// </summary>
        public static WordsKeyToken TryGetKeyToken(IMarkup markup)
        {
            if (markup == null)
                return null;

            var text = markup.GetText();
            var span = MarkupKeys.Parse(text, text.IndexOf('{'));
            if (span == null)
                return null;

            var start = markup.GetDocumentStartOffset();
            return new WordsKeyToken(span.Key, new DocumentRange(start.Shift(span.Start), start.Shift(span.End)));
        }

        /// <summary>
        /// The key a caret at <paramref name="caret"/> is on, if it is on one in an <c>l:Words</c>
        /// extension; otherwise null. When the key is still empty (caret right after
        /// <c>{l:Words </c>) the key is "" and the range collapses to the caret, so the completion
        /// list still opens.
        /// </summary>
        public static WordsKeyToken FindAtCaret(DocumentOffset caret)
        {
            var document = caret.Document;
            if (document == null)
                return null;

            var from = Math.Max(0, caret.Offset - WindowBefore);
            var to = Math.Min(document.GetTextLength(), caret.Offset + WindowAfter);
            var span = MarkupKeys.FindAt(document.GetText(new TextRange(from, to)), caret.Offset - from);
            if (span == null)
                return null;

            return new WordsKeyToken(span.Key, new DocumentRange(document, new TextRange(from + span.Start, from + span.End)));
        }
    }
}
