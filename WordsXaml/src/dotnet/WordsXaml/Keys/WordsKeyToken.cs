using JetBrains.DocumentModel;

namespace WordsXaml.Keys
{
    /// <summary>A words key at a use site (XAML or C#), plus where it lives for range-based edits.</summary>
    public sealed class WordsKeyToken
    {
        public WordsKeyToken(string key, DocumentRange range)
        {
            Key = key;
            Range = range;
        }

        /// <summary>The key as written, without quotes; "" where one is due but not typed yet.</summary>
        public string Key { get; }

        /// <summary>The key's own range: inside any quotes.</summary>
        public DocumentRange Range { get; }
    }
}
