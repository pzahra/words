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

        // an assignment through a method's own ref or out parameter names the method, as
        // an argument's does; it named the type that holds the method
        [TestMethod]
        public async Task RefOrOutParameter_GivenLiteral_NamesTheMethod()
        {
            var test = @"
using PatTech.Localization;

public static class Program
{
    static void Fill([Localized] out string message, [Localized] ref string other)
    {
        message = {|#0:""filled""|};
        other = {|#1:""again""|};
        Local(out _);

        void Local([Localized] out string inner)
        {
            inner = {|#2:""inside""|};
        }
    }
}
" + AttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(
                    test,
                    VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic).WithLocation(0).WithArguments("message", "Fill"),
                    VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic).WithLocation(1).WithArguments("other", "Fill"),
                    VerifyCS.Diagnostic(LocalizationAnalyzer.MethodParameterDiagnostic).WithLocation(2).WithArguments("inner", "Local"));
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

        // null and default are no text, not text left unlocalized: a marked member may hold
        // none, whether assigned, passed, an arm of a ?: or a switch, cast, through ! or traced
        [TestMethod]
        public async Task NullAndDefault_AreNoText_ReportsNothing()
        {
            var test = @"
#nullable enable
using PatTech.Localization;

public class ViewModel
{
    [Localized]
    public string? Failure { get; set; }

    [Localized]
    string? note;

    [Localized]
    static string Known
    {
        get { return ""known""; }
    }

    public void Update(int kind, bool flag)
    {
        Failure = null;
        Failure = default;
        Failure = default(string);
        Failure = (string?)null;
        Failure = null!;
        Failure = """";
        Failure = @"""";
        Failure = string.Empty;
        Failure = System.String.Empty;
        Show("""");
        Show(string.Empty);
        note = null;
        Show(null);
        Show(default);
        Failure = kind switch { 0 => Known, _ => null };
        Failure = flag ? Known : null;
        string? held = null;
        held = Known;
        Show(held);
    }

    static void Show([Localized] string? message)
    {
    }
}
" + AttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        // a cast or a ! hides nothing: the text under it is what counts
        [TestMethod]
        public async Task ALiteral_UnderACastOrSuppression_Warns()
        {
            var test = @"
#nullable enable
using PatTech.Localization;

public class ViewModel
{
    [Localized]
    public string? Failure { get; set; }

    [Localized]
    static string Known
    {
        get { return ""known""; }
    }

    public void Update(bool flag)
    {
        Failure = (string){|#0:""raw""|};
        Failure = {|#1:""raw""|}!;
        Failure = (flag ? {|#2:""raw""|} : Known)!;
        Failure = (string?)(flag ? Known : {|#3:""raw""|});
    }
}
" + AttributeSource;

            await VerifyCS.VerifyAnalyzerAsync(test,
                VerifyCS.Diagnostic(LocalizationAnalyzer.PropertyAssignmentDiagnostic).WithLocation(0).WithArguments("ViewModel.Failure"),
                VerifyCS.Diagnostic(LocalizationAnalyzer.PropertyAssignmentDiagnostic).WithLocation(1).WithArguments("ViewModel.Failure"),
                VerifyCS.Diagnostic(LocalizationAnalyzer.PropertyAssignmentDiagnostic).WithLocation(2).WithArguments("ViewModel.Failure"),
                VerifyCS.Diagnostic(LocalizationAnalyzer.PropertyAssignmentDiagnostic).WithLocation(3).WithArguments("ViewModel.Failure"));
        }
    }
}
