using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;

namespace WordsXaml.Inspections
{
    /// <summary>
    /// Warning shown on an <c>l:Words</c> key that has no matching <c>[section]</c> in any *-words.ini.
    /// This is the highest-value feature for a codebase with 30+ consuming .axaml files: it turns silent
    /// missing-string bugs into an editor squiggle.
    ///
    /// Register the config id in a matching <c>WordsXaml.dotSettings</c> so severity is user-tweakable.
    /// </summary>
    [ConfigurableSeverityHighlighting(
        SeverityId,
        JetBrains.ReSharper.Psi.Xaml.XamlLanguage.Name,
        OverlapResolve = OverlapResolveKind.WARNING,
        ToolTipFormatString = "Unknown words key '{0}'")]
    public sealed class UnknownWordsKeyHighlighting : IHighlighting
    {
        public const string SeverityId = "WordsXaml.UnknownKey";

        private readonly DocumentRange _range;

        public UnknownWordsKeyHighlighting(string key, DocumentRange range)
        {
            Key = key;
            _range = range;
            ToolTip = $"Unknown words key '{key}'";
        }

        public string Key { get; }
        public string ToolTip { get; }
        public string ErrorStripeToolTip => ToolTip;

        public bool IsValid() => _range.IsValid();
        public DocumentRange CalculateRange() => _range;
    }
}
