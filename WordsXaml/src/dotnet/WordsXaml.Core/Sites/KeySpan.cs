namespace WordsXaml.Sites
{
    /// <summary>
    /// Where a words key sits in some text: the key as written (without quotes) and its span. The
    /// SDK glue adds <see cref="Start"/> to the document offset of the text it handed in to get the
    /// key's document range, for completion's replace range, the quick-doc hit test and the squiggle.
    /// </summary>
    public sealed class KeySpan
    {
        public KeySpan(string key, int start)
        {
            Key = key;
            Start = start;
        }

        /// <summary>The key's text, e.g. <c>main.title</c>; "" where a key is due but none is typed yet.</summary>
        public string Key { get; }

        /// <summary>Offset of the key's first character (after any opening quote) in the text parsed.</summary>
        public int Start { get; }

        public int Length => Key.Length;

        /// <summary>Offset just past the key's last character.</summary>
        public int End => Start + Key.Length;

        /// <summary>Whether a caret at <paramref name="offset"/> is on the key, its end included.</summary>
        public bool Contains(int offset) => offset >= Start && offset <= End;
    }
}
