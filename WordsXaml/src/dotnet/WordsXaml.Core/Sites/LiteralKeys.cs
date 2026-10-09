namespace WordsXaml.Sites
{
    /// <summary>
    /// The key in a C# string literal token, from its text alone: the span between the quotes of
    /// <c>"main.title"</c>, <c>@"main.title"</c> or a one-line raw <c>"""main.title"""</c>. An
    /// unterminated literal, as it is while being typed, runs to the end of the token. An
    /// interpolated or UTF-8 (<c>u8</c>) literal is no key.
    /// </summary>
    public static class LiteralKeys
    {
        public static KeySpan Parse(string token)
        {
            if (string.IsNullOrEmpty(token) || token[0] == '$' || token.EndsWith("u8") || token.EndsWith("U8"))
                return null;

            var i = token[0] == '@' ? 1 : 0;
            var quotes = 0;
            while (i + quotes < token.Length && token[i + quotes] == '"')
                quotes++;
            if (quotes == 0)
                return null;

            // "" is an empty regular literal, not the opening of a raw one
            if (quotes == 2)
                return new KeySpan(string.Empty, i + 1);

            // a raw literal opens with three or more quotes, and closes with as many
            var open = quotes >= 3 && i == 0 ? quotes : 1;
            var start = i + open;
            var end = token.Length;
            if (end - start >= open && token.Substring(end - open).Trim('"').Length == 0 && end > start)
                end -= open;
            if (open > 1 && token.IndexOf('\n', start) >= 0)
                return null; // a multi-line raw literal holds no key
            return new KeySpan(token.Substring(start, end - start), start);
        }
    }
}
