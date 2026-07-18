using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS = LocalizationAnalyzer.Test.CSharpAnalyzerVerifier<
    LocalizationAnalyzer.LocalizationAnalyzer>;

namespace LocalizationAnalyzer.Test
{
    [TestClass]
    public class LocalizationAnalyzerUnitTest
    {
        /// <summary>
        /// The attribute normally supplied by the PatTech.Localization.Analyzer
        /// package, appended to each test compilation.
        /// </summary>
        private const string AttributeSource = @"
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
";

        [TestMethod]
        public async Task EmptySource_ReportsNothing()
        {
            await VerifyCS.VerifyAnalyzerAsync(string.Empty);
        }

        [TestMethod]
        public async Task LocalizedParameter_GivenLiteral_Warns()
        {
            var test = @"
using PatTech.Localization;

public static class Program
{
    public static void Main()
    {
        WriteLocal({|#0:""welcome""|});
    }

    static void WriteLocal([Localized] string message)
    {
    }
}
" + AttributeSource;

            var expected = VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic)
                .WithLocation(0)
                .WithArguments("message", "WriteLocal");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task LocalizedParameter_GivenLocalizedReturnValue_ReportsNothing()
        {
            var test = @"
using PatTech.Localization;

public static class Program
{
    public static void Main()
    {
        WriteLocal(GetLocalizedMessage());
    }

    static void WriteLocal([Localized] string message)
    {
    }

    [return: Localized]
    static string GetLocalizedMessage()
    {
        return ""hello"";
    }
}
" + AttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task LocalizedParameter_GivenLocalizedProperty_ReportsNothing()
        {
            var test = @"
using PatTech.Localization;

public static class Program
{
    [Localized]
    static string Bye
    {
        get { return ""goodbye""; }
    }

    public static void Main()
    {
        WriteLocal(Bye);
    }

    static void WriteLocal([Localized] string message)
    {
    }
}
" + AttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task LocalizedProperty_AssignedLiteral_Warns()
        {
            var test = @"
using PatTech.Localization;

public class ViewModel
{
    [Localized]
    public string Message { get; set; }

    public void Update()
    {
        Message = {|#0:""raw text""|};
    }
}
" + AttributeSource;

            var expected = VerifyCS.Diagnostic(LocalizationAnalyzer.PropertyAssignmentDiagnostic)
                .WithLocation(0)
                .WithArguments("ViewModel.Message");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task LocalizedField_AssignedLiteral_Warns()
        {
            var test = @"
using PatTech.Localization;

public class ViewModel
{
    [Localized]
    public string message;

    public void Update()
    {
        message = {|#0:""raw text""|};
    }
}
" + AttributeSource;

            var expected = VerifyCS.Diagnostic(LocalizationAnalyzer.FieldAssignmentDiagnostic)
                .WithLocation(0)
                .WithArguments("ViewModel.message");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task LocalizedField_AssignedLocalizedField_ReportsNothing()
        {
            var test = @"
using PatTech.Localization;

public class ViewModel
{
    [Localized]
    public string message;

    [Localized]
    public string backup;

    public void Update()
    {
        backup = message;
    }
}
" + AttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task ConditionalExpression_FlagsOnlyTheUnlocalizedArm_Warns()
        {
            var test = @"
using PatTech.Localization;

public static class Program
{
    [Localized]
    static string Bye
    {
        get { return ""goodbye""; }
    }

    public static void Main(string[] args)
    {
        WriteLocal(args.Length > 0 ? Bye : {|#0:""fallback""|});
    }

    static void WriteLocal([Localized] string message)
    {
    }
}
" + AttributeSource;

            var expected = VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic)
                .WithLocation(0)
                .WithArguments("message", "WriteLocal");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}
