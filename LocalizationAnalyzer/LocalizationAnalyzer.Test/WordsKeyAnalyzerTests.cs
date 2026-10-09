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

/// <summary>Indexers that take keys, mirroring IWords: the key alone, and the key and a count.</summary>
public class Lookup
{
    public string this[[WordsKey] string key] => key;
    public string this[[WordsKey] string key, decimal count] => key;
    public string this[int index] => """";
}

/// <summary>An interface that marks its key, and an implementation that doesn't repeat it.</summary>
public interface IKeyed
{
    string this[[WordsKey] string key] { get; }
    void Show([WordsKey] string key);
}

public class Keyed : IKeyed
{
    public string this[string key] => key;
    public void Show(string key) { }
}

[System.AttributeUsage(System.AttributeTargets.All)]
public class KeyedAttribute : System.Attribute
{
    public KeyedAttribute([WordsKey] string key) { }
    [WordsKey] public string Other { get; set; }
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

        private static Task VerifyWithIni(string testSource, params DiagnosticResult[] expected)
            => VerifyWithIni(WordsIni, testSource, expected);

        private static async Task VerifyWithIni(string ini, string testSource, params DiagnosticResult[] expected)
        {
            var test = new VerifyCS.Test();
            test.TestState.Sources.Add(AttributeSource);
            test.TestState.Sources.Add(ApiSource);
            test.TestState.Sources.Add(testSource);
            test.TestState.AdditionalFiles.Add(("helios-words.ini", ini));
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        private static DiagnosticResult Unknown(int location, string key)
            => VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyDiagnostic).WithLocation(location).WithArguments(key);

        private static DiagnosticResult Invalid(int location, string key)
            => VerifyCS.Diagnostic(WordsKeyAnalyzer.InvalidKeyDiagnostic).WithLocation(location).WithArguments(key);

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
        public async Task NoKeyName_ReportedAsInvalid()
        {
            // code names keys language-neutrally: a form is picked by a count, never named, so
            // "key#other" is no key even where the ini has the key and its forms
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M(string form)
    {
        Use({|#0:""material.metals#other""|});
        var l = new Lookup();
        _ = l[{|#1:""calibration.help.reset-hint#few""|}];
        Use({|#2:""a b""|});
        Use({|#3:""material.metals#"" + form|});
        Use({|#4:$""material..{form}""|});
        Use(""material."" + form);           // a key can start so: no diagnostic
    }
}
";
            await VerifyWithIni(
                    source,
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.InvalidKeyDiagnostic).WithLocation(0).WithArguments("material.metals#other"),
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.InvalidKeyDiagnostic).WithLocation(1).WithArguments("calibration.help.reset-hint#few"),
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.InvalidKeyDiagnostic).WithLocation(2).WithArguments("a b"),
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.InvalidKeyDiagnostic).WithLocation(3).WithArguments("material.metals#"),
                    VerifyCS.Diagnostic(WordsKeyAnalyzer.InvalidKeyDiagnostic).WithLocation(4).WithArguments("material.."));
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

        [TestMethod]
        public async Task IndexerKey_CheckedLikeAnArgument()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M(Lookup lookup)
    {
        _ = lookup[""calibration.help.reset-hint""];
        _ = lookup[{|#0:""nope.not.here""|}];
        _ = lookup?[""material.metals""];
        _ = lookup?[{|#1:""also.bad""|}];
        _ = lookup[key: {|#2:""named.bad""|}];
        _ = lookup[3];
    }
}
";
            await VerifyWithIni(source, Unknown(0, "nope.not.here"), Unknown(1, "also.bad"), Unknown(2, "named.bad"));
        }

        [TestMethod]
        public async Task CountIndexer_ChecksTheKey_NotTheCount()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M(Lookup lookup)
    {
        _ = lookup[""material.metals"", 2];
        _ = lookup[{|#0:""materials""|}, 2];
        _ = lookup[count: 1, key: {|#1:""metals""|}];
    }
}
";
            await VerifyWithIni(source, Unknown(0, "materials"), Unknown(1, "metals"));
        }

        [TestMethod]
        public async Task ImplementingMember_IsMarkedLikeTheInterfaceMember()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M(Keyed keyed)
    {
        _ = keyed[""material.metals""];
        _ = keyed[{|#0:""nope""|}];
        keyed.Show({|#1:""nada""|});
    }
}
";
            await VerifyWithIni(source, Unknown(0, "nope"), Unknown(1, "nada"));
        }

        [TestMethod]
        public async Task GenericImplementation_IsMarkedLikeTheInterfaceMember()
        {
            const string source = @"
using PatTech.Localization;

public interface IGeneric
{
    void Show<TValue>([WordsKey] string key);
}

public class Generic : IGeneric
{
    public void Show<TValue>(string key) { }
}

public partial class T
{
    static void M(Generic generic)
    {
        generic.Show<int>(""material.metals"");
        generic.Show<int>({|#0:""nope""|});
    }
}
";
            await VerifyWithIni(source, Unknown(0, "nope"));
        }

        [TestMethod]
        public async Task InheritedImplementation_IsMarkedOnTheTypeThatImplements()
        {
            const string source = @"
using PatTech.Localization;

public class Base
{
    public void Show(string key) { }
}

/// <summary>Base.Show implements IKeyed.Show here, though Base knows nothing of IKeyed.</summary>
public class Derived : Base, IKeyed
{
    public string this[string key] => key;
}

public partial class T
{
    static void M(Derived derived, Base plain)
    {
        derived.Show({|#0:""nope""|});
        plain.Show(""nope"");
    }
}
";
            await VerifyWithIni(source, Unknown(0, "nope"));
        }

        [TestMethod]
        public async Task EmptyKey_NamesNothing_NoDiagnostic()
        {
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M(Lookup lookup)
    {
        Use("""");
        Key = """";
        _ = lookup[""""];
    }
}
";
            await VerifyWithIni(source);
        }

        [TestMethod]
        public async Task ObjectKey_OnlyAStringIsChecked()
        {
            const string source = @"
using PatTech.Localization;

/// <summary>Mirrors WordsExtension(object): a key, or a binding to localize instead.</summary>
public class Extension
{
    public Extension([WordsKey] object key) { }
}

public class Binding
{
    public Binding(string path) { }
}

public partial class T
{
    static void M(object runtime)
    {
        _ = new Extension(new Binding(""Some.Path""));
        _ = new Extension(runtime);
        _ = new Extension(null);
        _ = new Extension(42);
        _ = new Extension(""material.metals"");
        _ = new Extension({|#0:""nope""|});
        _ = new Extension({|#1:""nope."" + runtime|});
    }
}
";
            await VerifyWithIni(source,
                Unknown(0, "nope"),
                VerifyCS.Diagnostic(WordsKeyAnalyzer.UnknownKeyPrefixDiagnostic).WithLocation(1).WithArguments("nope."));
        }

        [TestMethod]
        public async Task AttributeArguments_AreChecked()
        {
            const string source = @"
using PatTech.Localization;

[Keyed(""material.metals"", Other = ""calibration.help.reset-hint"")]
public class Good { }

[Keyed({|#0:""nope""|}, Other = {|#1:""nada""|})]
public class Bad { }
";
            await VerifyWithIni(source, Unknown(0, "nope"), Unknown(1, "nada"));
        }

        [TestMethod]
        public async Task HeaderNamedOtherwise_IsNoKey_NorAreItsChildren()
        {
            // as the runtime skips them with WP:NAME; a header is read where the line starts, untrimmed
            const string ini = @"
[a b]
value=x
[.c]
value=y
[x#y]
value=z
[ spaced ]
value=s
  [indented]
value=i
[good]
[.child]
";
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""good"");
        Use(""good.child"");
        Use({|#0:""a b""|});
        Use({|#1:""a b.c""|});
        Use({|#2:""x#y""|});
        Use({|#3:""x""|});
        Use({|#4:"" spaced ""|});
        Use({|#5:""spaced""|});
        Use({|#6:""indented""|});
    }
}
";
            await VerifyWithIni(
                    ini,
                    source,
                    Invalid(0, "a b"),
                    Invalid(1, "a b.c"),
                    Invalid(2, "x#y"),
                    Unknown(3, "x"),
                    Invalid(4, " spaced "),
                    Unknown(5, "spaced"),
                    Unknown(6, "indented"));
        }

        [TestMethod]
        public async Task ChildBeforeAnyFullHeader_IsNoKey()
        {
            // it resolves against nothing, to ".early", which is no key name
            const string ini = @"
[.early]
value=x
[late]
[.child]
";
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""late"");
        Use(""late.child"");
        Use({|#0:""early""|});
        Use({|#1:"".early""|});
    }
}
";
            await VerifyWithIni(ini, source, Unknown(0, "early"), Invalid(1, ".early"));
        }

        [TestMethod]
        public async Task ConstantsChild_IsNoKey()
        {
            // a constant has no children
            const string ini = @"
[$unit]
value=kg
[.child]
value=x
";
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""$unit"");
        Use({|#0:""$unit.child""|});
    }
}
";
            await VerifyWithIni(ini, source, Invalid(0, "$unit.child"));
        }

        [TestMethod]
        public async Task ContinuedValue_NextLineIsNoHeader()
        {
            // a trailing \ or _ runs the field on through the next line, whatever it says;
            // a doubled \\ or __ is an escaped character, and continues nothing
            const string ini = @"
[real]
value=first line\
[fake]
comment=and on_
[fake2]
value-de=weiter\
und weiter\
[fake3]
[after]
value=a\\
[counted]
value=b__
[counted2]
";
            const string source = @"
using PatTech.Localization;

public partial class T
{
    static void M()
    {
        Use(""real"");
        Use(""after"");
        Use(""counted"");
        Use(""counted2"");
        Use({|#0:""fake""|});
        Use({|#1:""fake2""|});
        Use({|#2:""fake3""|});
    }
}
";
            await VerifyWithIni(ini, source, Unknown(0, "fake"), Unknown(1, "fake2"), Unknown(2, "fake3"));
        }
    }
}
