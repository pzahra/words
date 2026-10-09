using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Psi.CSharp;

namespace WordsXaml.CSharp
{
    /// <summary>
    /// The C# key sites need C# PSI on top of what the root <see cref="WordsXaml.ZoneMarker"/> asks
    /// for; a nested marker keeps that requirement to this namespace.
    /// </summary>
    [ZoneMarker]
    public class ZoneMarker : IRequire<ILanguageCSharpZone>
    {
    }
}
