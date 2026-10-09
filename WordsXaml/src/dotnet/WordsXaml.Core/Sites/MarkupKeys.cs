using System;

namespace WordsXaml.Sites
{
    /// <summary>
    /// Finds the key in a <c>{l:Words …}</c> markup extension, from its text alone, so completion,
    /// quick-doc and the unknown-key inspection agree on it however the XAML PSI shapes the
    /// arguments. All three spellings name the key:
    /// <code>
    /// {l:Words main.title}          positional (the constructor's [WordsKey] object key)
    /// {l:Words Key=main.title}      named (the [WordsKey] Key property)
    /// {l:Words 'main.title'}        quoted, either quote
    /// </code>
    /// A named <c>Key=</c> wins over a positional argument, as the property is set after the
    /// constructor runs. <c>{l:Words {Binding …}}</c> binds its key at run time: no key. The alias
    /// is irrelevant, and the class's own name, <c>WordsExtension</c>, is the same extension.
    /// </summary>
    public static class MarkupKeys
    {
        /// <summary>The extension's names as XAML may write them, the alias aside.</summary>
        public static readonly string[] ExtensionNames = { "Words", "WordsExtension" };

        /// <summary>The named argument that carries the key.</summary>
        public const string KeyArgument = "Key";

        /// <summary>
        /// Reads the markup extension opening at <paramref name="open"/> (a '{') and returns its
        /// key's span; null if it is no Words extension, or names no key. A key not typed yet is ""
        /// where it is due: after <c>{l:Words </c> or <c>Key=</c>. An unterminated extension, as it
        /// is while being typed (<c>{l:Words main.ti</c>), reads up to the end of the text.
        /// </summary>
        public static KeySpan Parse(string text, int open = 0)
        {
            if (text == null || open < 0 || open >= text.Length || text[open] != '{')
                return null;

            var i = SkipSpace(text, open + 1);
            var nameStart = i;
            while (i < text.Length && (IsNameChar(text[i]) || text[i] == ':'))
                i++;
            var name = text.Substring(nameStart, i - nameStart);
            var local = name.Substring(name.LastIndexOf(':') + 1);
            if (Array.IndexOf(ExtensionNames, local) < 0)
                return null;

            // the name must be followed by a space before any argument: {l:Words} has none
            var nameEnd = i;
            i = SkipSpace(text, i);
            if (i == nameEnd)
                return null;

            KeySpan positional = null;
            KeySpan named = null;
            var first = true;
            while (true)
            {
                i = SkipSpace(text, i);
                if (i >= text.Length || text[i] == '}')
                {
                    if (first)
                        positional = new KeySpan(string.Empty, i);
                    break;
                }

                // Name= ?
                string argument = null;
                var identStart = i;
                var j = i;
                while (j < text.Length && IsNameChar(text[j]))
                    j++;
                var afterIdent = SkipSpace(text, j);
                if (j > identStart && afterIdent < text.Length && text[afterIdent] == '=')
                {
                    argument = text.Substring(identStart, j - identStart);
                    i = SkipSpace(text, afterIdent + 1);
                }

                var value = ReadValue(text, ref i);
                if (argument == null)
                {
                    if (first)
                        positional = value;
                }
                else if (argument == KeyArgument)
                {
                    named = value;
                }
                first = false;

                i = SkipSpace(text, i);
                if (i < text.Length && text[i] == ',')
                {
                    i++;
                    continue;
                }
                break;
            }

            return named ?? positional;
        }

        /// <summary>
        /// The key a caret at <paramref name="caret"/> is on, looking back through the text for the
        /// Words extension that holds it; null if the caret is on no Words key. The text can be any
        /// window of the document around the caret, a line or more.
        /// </summary>
        public static KeySpan FindAt(string text, int caret)
        {
            if (text == null)
                return null;
            caret = Math.Min(Math.Max(caret, 0), text.Length);

            // A key holds no brace, so the extension holding the caret opens at one of the last few
            // '{' before it; a '<' is outside any attribute value, where no extension reaches.
            var tries = 0;
            for (var i = caret - 1; i >= 0 && tries < 8; i--)
            {
                if (text[i] == '<')
                    break;
                if (text[i] != '{')
                    continue;
                tries++;
                var span = Parse(text, i);
                if (span != null && span.Contains(caret))
                    return span;
            }
            return null;
        }

        // A value: a nested extension (no key), a quoted string or bare text up to ',' or '}'.
        private static KeySpan ReadValue(string text, ref int i)
        {
            if (i >= text.Length)
                return new KeySpan(string.Empty, i);

            var c = text[i];
            if (c == '{')
            {
                i = SkipNested(text, i);
                return null;
            }

            if (c == '\'' || c == '"')
            {
                var start = i + 1;
                var end = start;
                // unterminated while being typed: stop where a key could not go on
                while (end < text.Length && text[end] != c && !EndsQuoted(text[end], c))
                    end++;
                i = end < text.Length && text[end] == c ? end + 1 : end;
                return new KeySpan(text.Substring(start, end - start), start);
            }

            var bareStart = i;
            while (i < text.Length && text[i] != ',' && text[i] != '}' && !EndsBare(text[i]))
                i++;
            var bareEnd = i;
            while (bareEnd > bareStart && char.IsWhiteSpace(text[bareEnd - 1]))
                bareEnd--;
            return new KeySpan(text.Substring(bareStart, bareEnd - bareStart), bareStart);
        }

        // Past the '}' matching the '{' at i, quotes respected; the end of the text if unmatched.
        private static int SkipNested(string text, int i)
        {
            var depth = 0;
            char quote = '\0';
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                }
                else if (c == '\'' || c == '"')
                    quote = c;
                else if (c == '{')
                    depth++;
                else if (c == '}' && --depth == 0)
                    return i + 1;
            }
            return i;
        }

        private static int SkipSpace(string text, int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            return i;
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-';

        private static bool EndsBare(char c) => c == '"' || c == '<' || c == '>' || c == '{' || c == '\r' || c == '\n';

        private static bool EndsQuoted(char c, char quote) =>
            c == '}' || c == '<' || c == '>' || c == '\r' || c == '\n' || (quote == '\'' && c == '"');
    }
}
