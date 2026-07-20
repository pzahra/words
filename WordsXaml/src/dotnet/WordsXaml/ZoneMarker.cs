using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Feature.Services;
using JetBrains.ReSharper.Psi.Xaml;

namespace WordsXaml
{
    /// <summary>
    /// Declares the product zones this extension requires. Without a ZoneMarker the components below are
    /// never activated. We need XAML PSI (for markup-extension parsing) and the code-editing feature zone
    /// (completion, quick-doc, daemon inspections).
    /// </summary>
    [ZoneMarker]
    public class ZoneMarker : IRequire<ILanguageXamlZone>, IRequire<ICodeEditingZone>
    {
    }
}
