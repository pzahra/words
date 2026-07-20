namespace WordsXaml.Ini
{
    /// <summary>
    /// One entry in a single level of the dotted-key tree, produced by
    /// <see cref="WordsIndex.CompleteSegments"/>. Either a branch (has children — insert text ends with
    /// '.') or a leaf (a concrete key). Backs hierarchical completion so the list shows only the next
    /// segment instead of every fully-qualified key.
    /// </summary>
    public sealed class WordsSegment
    {
        public WordsSegment(string insertText, bool isBranch, int childCount, string leafPreview)
        {
            InsertText = insertText;
            IsBranch = isBranch;
            ChildCount = childCount;
            LeafPreview = leafPreview;
        }

        /// <summary>Canonical text to insert: the committed prefix + this segment, plus a trailing '.' for a branch.</summary>
        public string InsertText { get; }

        /// <summary>True if this segment has descendants (drill-down); false if it is a concrete key.</summary>
        public bool IsBranch { get; }

        /// <summary>For a branch, how many keys live under it; 0 for a leaf.</summary>
        public int ChildCount { get; }

        /// <summary>For a leaf, the resolved value preview; null for a branch.</summary>
        public string LeafPreview { get; }
    }
}
