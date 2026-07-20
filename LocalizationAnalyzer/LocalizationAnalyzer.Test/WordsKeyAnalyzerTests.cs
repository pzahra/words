using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;
using VerifyCS = LocalizationAnalyzer.Test.CSharpAnalyzerVerifier<
    LocalizationAnalyzer.WordsKeyAnalyzer>;

namespace LocalizationAnalyzer.Test
{
    [TestClass]
    public class WordsKeyAnalyzerTests
    {
        /// <summary>The attribute normally supplied by the PatTech.Localization package.</summary>
        private const string AttributeSource = @"
namespace PatTech.Localization
{
    [System.AttributeUsage(
        System.AttributeTargets.Parameter
        | System.AttributeTargets.ReturnValue
        | System.AttributeTargets.Property
        | System.AttributeTargets.Field,
        AllowMultiple = false)]
    public class WordsKeyAttribute : System.Attribute { }
}
";

        /// <summary>A small API surface that takes keys, mirroring things like WordsInline.Key.</summary>
        private const string ApiSource = @"
using PatTech.Localization;

public partial class T
{
    public T([WordsKey] string key) { }
    public static void Use([WordsKey] string key) { }
    public static void Plain(string notAKey) { }
    [WordsKey] public static string Key { get; set; }
    public static string SomeRuntime() => """";
}
";

        // Note the [.metals] inheritance -> the valid key is "material.metals".
        private const string WordsIni = @"
[calibration.help.reset-hint]
value=x
[material]
[.metals]
value=METALS
";

        private static async Task VerifyWithIni(string testSource, params DiagnosticResult[] expected)
        {
            var test = new VerifyCS.Test();
            test.TestState.Sources.Add(AttributeSource);
            test.TestState.Sources.Add(ApiSource);
            test.TestState.Sources.Add(testSource);
            test.TestState.AdditionalFiles.Add(("helios-words.ini", WordsIni));
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        [TestMethod]
        public async Task ValidKeys_NoDiagnostic()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""calibration.help.reset-hint"");
        Use(""material.metals"");            // via [.metals] inheritance
        Key = ""calibration.help.reset-hint"";
        var t = new T(""material.metals"");
        Plain(""anything-goes-here"");        // not a [WordsKey] target
    }
}
";
            await VerifyWithIni(source);
        }

        [TestMethod]
        public async Task UnknownKeys_ReportedOnEachTarget()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use({|#0:""nope.not.here""|});
        Key = {|#1:""also.bad""|};
        var t = new T({|#2:""ctor.bad""|});
    }
}
";
            await VerifyWithIni(
                    source,
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyDiagnostic).WithLocation(0).WithArguments("nope.not.here"),
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyDiagnostic).WithLocation(1).WithArguments("also.bad"),
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyDiagnostic).WithLocation(2).WithArguments("ctor.bad"));
        }

        [TestMethod]
        public async Task NonConstantExpression_Ignored()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(SomeRuntime());
    }
}
";
            await VerifyWithIni(source);
        }

        [TestMethod]
        public async Task NoWordsIni_StaysSilent()
        {
            var test = new VerifyCS.Test();
            test.TestState.Sources.Add(AttributeSource);
            test.TestState.Sources.Add(ApiSource);
            test.TestState.Sources.Add(@"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""definitely.unknown"");
    }
}
");
            await test.RunAsync(CancellationToken.None);
        }

        [TestMethod]
        public async Task Concatenation_WithKnownPrefix_NoDiagnostic()
        {
            // "calibration." matches existing keys, so the dynamic tail is assumed fine.
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""calibration."" + SomeRuntime());
    }
}
";
            await VerifyWithIni(source);
        }

        [TestMethod]
        public async Task Concatenation_WithUnknownPrefix_Flagged()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use({|#0:""bogus."" + SomeRuntime()|});
    }
}
";
            await VerifyWithIni(
                    source,
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyPrefixDiagnostic).WithLocation(0).WithArguments("bogus."));
        }

        [TestMethod]
        public async Task Concatenation_FullyDynamic_NoDiagnostic()
        {
            // No leading literal to judge -> left alone.
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(SomeRuntime() + "".suffix"");
    }
}
";
            await VerifyWithIni(source);
        }

        [TestMethod]
        public async Task Interpolation_WithKnownPrefix_NoDiagnostic()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use($""calibration.{SomeRuntime()}"");
    }
}
";
            await VerifyWithIni(source);
        }

        [TestMethod]
        public async Task Interpolation_WithUnknownPrefix_Flagged()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use({|#0:$""bogus.{SomeRuntime()}""|});
    }
}
";
            await VerifyWithIni(
                    source,
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyPrefixDiagnostic).WithLocation(0).WithArguments("bogus."));
        }

        [TestMethod]
        public async Task ConstantConcatenation_IsCheckedExactly()
        {
            // Both operands constant -> folded to "a.b" and checked as an exact (unknown) key.
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use({|#0:""a."" + ""b""|});
    }
}
";
            await VerifyWithIni(
                    source,
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyDiagnostic).WithLocation(0).WithArguments("a.b"));
        }
    }
}
