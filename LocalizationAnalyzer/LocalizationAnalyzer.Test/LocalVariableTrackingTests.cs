using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS = LocalizationAnalyzer.Test.CSharpAnalyzerVerifier<
    LocalizationAnalyzer.LocalizationAnalyzer>;

namespace LocalizationAnalyzer.Test
{
    [TestClass]
    public class LocalVariableTrackingTests
    {
        /// <summary>The [Localized] attribute plus a small API with localized/non-localized members.</summary>
        private const string Boilerplate = @"
namespace PatTech.Localization
{
    [System.AttributeUsage(
        System.AttributeTargets.Parameter
        | System.AttributeTargets.ReturnValue
        | System.AttributeTargets.Property
        | System.AttributeTargets.Field,
        AllowMultiple = false)]
    public class LocalizedAttribute : System.Attribute { }
}

public partial class T
{
    public static void Test([PatTech.Localization.Localized] string x, string y) { }
    [PatTech.Localization.Localized] public static string Loc => """";
    public static string Plain => """";
}
";

        [TestMethod]
        public async Task LocalHoldingLocalizedValue_IsNotFlagged()
        {
            // The gap this closes: a local used as an intermediary for a localized value.
            var source = @"
public partial class T
{
    static void M()
    {
        var s = Loc;
        Test(s, s);
    }
}
" + Boilerplate;
            await VerifyCS.VerifyAnalyzerAsync(source);
        }

        [TestMethod]
        public async Task LocalHoldingNonLocalizedValue_IsFlagged()
        {
            var source = @"
public partial class T
{
    static void M()
    {
        var s = Plain;
        Test({|#0:s|}, s);
    }
}
" + Boilerplate;
            await VerifyCS.VerifyAnalyzerAsync(
                    source,
                    VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic)
                        .WithLocation(0)
                        .WithArguments("x", "Test"));
        }

        [TestMethod]
        public async Task LocalReassignedToNonLocalized_IsFlagged_Conservatively()
        {
            // Any non-localized assignment anywhere in scope makes the local suspect.
            var source = @"
public partial class T
{
    static void M()
    {
        var s = Loc;
        s = Plain;
        Test({|#0:s|}, s);
    }
}
" + Boilerplate;
            await VerifyCS.VerifyAnalyzerAsync(
                    source,
                    VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic)
                        .WithLocation(0)
                        .WithArguments("x", "Test"));
        }

        [TestMethod]
        public async Task LocalReassignedToLocalized_IsNotFlagged()
        {
            var source = @"
public partial class T
{
    static void M()
    {
        var s = Loc;
        s = Loc;
        Test(s, s);
    }
}
" + Boilerplate;
            await VerifyCS.VerifyAnalyzerAsync(source);
        }
    }
}
