using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using PatTech.Localization.Cli;
using System.Globalization;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     What a language is called (runtime SPEC: Language codes): one grammar, in
///     Core, up to three subtags, falling back by truncation; the parser, Flatten,
///     the plural rules, the authoring reader, the codecs and the command line all
///     read a code through it.
/// </summary>
public class LanguageCodeTests {
	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	[Theory]
	[InlineData("en", "en")]
	[InlineData("EN", "en")]
	[InlineData("ceb", "ceb")]
	[InlineData("en-gb", "en-GB")]
	[InlineData("es-419", "es-419")]
	[InlineData("sr-latn", "sr-Latn")]
	[InlineData("ZH-HANS-CN", "zh-Hans-CN")]
	[InlineData("sr-Latn-RS", "sr-Latn-RS")]
	public void ACodeIsUpToThreeSubtags_CasedByKind(string text, string code) {
		Assert.True(LanguageCode.TryParse(text, out var parsed));
		Assert.Equal(code, parsed.ToString());
		Assert.Equal(code, WordsParser.NormalizeLanguageCasing(text));
	}

	[Theory]
	[InlineData("")]
	[InlineData("e")]
	[InlineData("english")]
	[InlineData("en_GB")]
	[InlineData("en-")]
	[InlineData("-en")]
	[InlineData("en GB")]
	[InlineData("en-1")]
	[InlineData("en-12")]
	[InlineData("en-GBR")]
	[InlineData("en-US-POSIX")]
	[InlineData("zh-Hans-Hant")]
	[InlineData("zh-CN-Hans")]
	[InlineData("en-GB\n")]
	public void AnythingElse_IsNoCode(string text) {
		Assert.False(LanguageCode.TryParse(text, out var code));
		Assert.False(code.IsCode);
		Assert.Equal("", code.ToString());
	}

	[Theory]
	[InlineData("ca-ES-valencia", "ca-ES")]
	[InlineData("en-US-POSIX", "en-US")]
	[InlineData("de-DE_phoneb", "de-DE")]
	[InlineData("zh-CHS", "zh")]
	[InlineData("sr-Latn-RS", "sr-Latn-RS")]
	public void ACulturesName_ReadsAsTheCodeItStartsWith(string name, string code) {
		Assert.True(LanguageCode.TryRead(name, out var read));
		Assert.Equal(code, read.ToString());
	}

	[Theory]
	[InlineData("")]
	[InlineData("english")]
	[InlineData("x-klingon")]
	public void ANameThatStartsWithNoCode_ReadsAsNone(string name) {
		Assert.False(LanguageCode.TryRead(name, out _));
	}

	[Theory]
	[InlineData("zh-Hant-TW", new[] { "zh-Hant-TW", "zh-Hant", "zh" })]
	[InlineData("zh-TW", new[] { "zh-TW", "zh-Hant", "zh" })] //a Chinese region, by the script it writes
	[InlineData("zh-HK", new[] { "zh-HK", "zh-Hant", "zh" })]
	[InlineData("zh-MO", new[] { "zh-MO", "zh-Hant", "zh" })]
	[InlineData("zh-CN", new[] { "zh-CN", "zh-Hans", "zh" })]
	[InlineData("zh-SG", new[] { "zh-SG", "zh-Hans", "zh" })]
	[InlineData("zh-Hans-HK", new[] { "zh-Hans-HK", "zh-Hans", "zh" })] //the script it names first
	[InlineData("zh-US", new[] { "zh-US", "zh" })]
	[InlineData("es-419", new[] { "es-419", "es" })]
	[InlineData("sr-Latn", new[] { "sr-Latn", "sr" })]
	[InlineData("en", new[] { "en" })]
	public void ACodeFallsBackByTruncation(string text, string[] chain) {
		Assert.True(LanguageCode.TryParse(text, out var code));
		Assert.Equal(chain, code.Chain.Select(level => level.ToString()));
		Assert.Empty(default(LanguageCode).Chain);
	}

	[Theory]
	[InlineData("en", "en", true)]
	[InlineData("en", "en-AU", true)]
	[InlineData("EN", "en-gb", true)]
	[InlineData("en-AU", "en", false)]
	[InlineData("en-AU", "en-US", false)]
	[InlineData("zh", "zh-Hant-TW", true)]
	[InlineData("zh-Hant", "zh-Hant-TW", true)]
	[InlineData("zh-Hans", "zh-Hant-TW", false)] //Traditional never reads Simplified
	[InlineData("zh-TW", "zh-Hant-TW", false)]
	[InlineData("zh-Hant", "zh-TW", true)]
	[InlineData("zh-Hans", "zh-TW", false)]
	[InlineData("zh-Hans", "zh-CN", true)]
	[InlineData("sr", "sr-Latn-RS", true)]
	[InlineData(null, "en", false)]
	public void TheDefaultSpeaks_WhereItsCodeIsOneTheLanguageFallsBackTo(string? spoken, string asked, bool speaks) {
		Assert.Equal(speaks, WordsParser.DefaultSpeaks(spoken, asked));
	}

	private const string Chinese = """
		value=!en
		value-zh=中文
		value-zh-Hans=简体中文
		value-zh-Hant=繁體中文
		value-zh-Hant-TW=繁體中文（台灣）

		[own]
		value=own
		value-zh-Hant-TW=臺灣

		[script]
		value=script
		value-zh-Hant=繁體
		value-zh-Hans=简体

		[family]
		value=family
		value-zh=中

		[none]
		value=none

		""";

	[Fact]
	public void Flatten_FallsBackThroughEachShorterCode_ThenTheDefault() {
		var builder = WordsBuilder.Create().LoadString(Chinese);

		var words = builder.Flatten("zh-hant-tw");

		Assert.Equal("臺灣", words["own"]);
		Assert.Equal("繁體", words["script"]); //never the Simplified one
		Assert.Equal("中", words["family"]);
		Assert.Equal("none", words["none"]);
		Assert.Equal(["zh", "zh-Hans", "zh-Hant", "zh-Hant-TW"], builder.GetLanguages().Select(language => language.Key));
	}

	[Fact]
	public void Flatten_BrandsEachShorterCodesWords_AndTheDefaults() {
		var words = WordsBuilder.Create().LoadString(Chinese).Debug().Flatten("zh-Hant-TW");

		Assert.Equal("臺灣", words["own"]);
		Assert.Equal("🕮繁體", words["script"]);
		Assert.Equal("🕮中", words["family"]);
		Assert.Equal("📚none", words["none"]);
	}

	[Fact]
	public void Flatten_ReadsAChineseRegion_InTheScriptItWrites_UnlessItNamesOne() {
		var builder = WordsBuilder.Create().LoadString(Chinese).Debug();

		//1.5.0's first cut read zh-TW straight to zh: a Windows Traditional Chinese user lost 繁體
		Assert.Equal("🕮繁體", builder.Flatten("zh-TW")["script"]); //zh-Hant's words, a shorter code's
		Assert.Equal("🕮繁體", builder.Flatten("zh-HK")["script"]);
		Assert.Equal("🕮简体", builder.Flatten("zh-CN")["script"]);
		Assert.Equal("🕮简体", builder.Flatten("zh-Hans-HK")["script"]);
		Assert.Equal("繁體", WordsBuilder.Create().LoadString(Chinese).Flatten("zh-TW")["script"]);
		Assert.Equal("🕮中", builder.Flatten("zh-TW")["family"]);
	}

	//a POSIX name reads its code's words, and has its code's culture: 1.5.0's first cut
	//gave en_US the invariant one, and threw for sr_Latn_RS, a name .NET refuses
	[Fact]
	public void ToWords_GivesAPosixNameItsCodesCulture() {
		var builder = WordsBuilder.Create().LoadString("value-en-US=American\nvalue-sr-Latn-RS=Srpski\n\n[k]\nvalue=x\nvalue-en-US=us\nvalue-sr-Latn-RS=rs\n");

		var american = builder.ToWords("en_US");
		Assert.Equal(("us", "en-US", "en-US"), (american["k"], american.UICulture.Name, american.Language));
		var serbian = builder.ToWords("sr_Latn_RS");
		Assert.Equal(("rs", "sr-Latn-RS"), (serbian["k"], serbian.UICulture.Name));
		Assert.Equal("ca-ES-valencia", builder.ToWords("ca-ES-valencia").UICulture.Name, ignoreCase: true); //a name .NET knows keeps what it says
		Assert.Equal(CultureInfo.InvariantCulture, builder.ToWords("").UICulture);
		Assert.Throws<ArgumentException>(() => builder.ToWords("english"));
	}

	[Fact]
	public void Flatten_ReadsACulturesName_AndRefusesWhatStartsWithNoCode() {
		var builder = WordsBuilder.Create().LoadString("value-ca=Català\n\n[k]\nvalue=x\nvalue-ca=ca\n");

		Assert.Equal("ca", builder.Flatten("ca-ES-valencia")["k"]);
		Assert.Throws<ArgumentException>(() => builder.Flatten("english"));
	}

	[Fact]
	public void AWordsFor_ACultureWithAScript_Builds() {
		//1.4.0 threw on sr-Latn-RS, so an app on such a Windows could not start
		var words = WordsBuilder.Create().LoadString("value-sr=Srpski\n\n[k]\nvalue=x\nvalue-sr=y\n").ToWords("sr-latn-rs");

		Assert.Equal("y", words["k"]);
		Assert.Equal("sr-Latn-RS", words.Language); //as a file writes it
	}

	[Theory]
	[InlineData("sr-Latn-RS", 2, "few")] //sr
	[InlineData("zh-Hant-TW", 2, "other")]
	[InlineData("es-419", 1_000_000, "many")] //es
	[InlineData("pt-PT", 1_000_000, "many")]
	[InlineData("ceb", 4, "other")]
	[InlineData("ceb", 5, "one")]
	public void ThePluralRules_LookUpTheSameChain(string code, int count, string category) {
		Assert.Equal(category, PluralRules.Select(code, count));
	}

	[Fact]
	public void TheRuntime_ReadsAThreePartCode_AndSkipsOneThatIsNone() {
		var logger = new CaptureLogger();
		var builder = WordsBuilder.Create(logger).LoadString(
			"value-zh-Hans-CN=简体中文\nvalue-english=English\n\n[k]\nvalue=x\nvalue-zh-hans-cn=y\nvalue-english=a\\\nvalue-ceb=continued\nvalue-ceb=z\n");

		Assert.Equal("y", builder.Flatten("zh-Hans-CN")["k"]);
		Assert.Equal("z", builder.Flatten("ceb")["k"]); //the skipped field's continuation went with it, unread
		Assert.Equal(["zh-Hans-CN"], builder.GetLanguages().Select(language => language.Key));
		Assert.Equal(["WP:LANG:`.value-english`", "WP:LANG:`k.value-english`"], logger.Messages);
	}

	[Fact]
	public void TheAuthoringReader_KeepsAThreePartCode_AndGripesAboutOneThatIsNone() {
		var session = new WordsSession();
		string ini = "value=!en\nvalue-zh-Hans-CN=简体中文\n\n[k]\nvalue=x\nvalue-zh-Hans-CN=y\nvalue-english=z\n\n";

		WordsFile file = session.Load(new StringReader(ini), "Odd");

		Assert.Equal(["zh-Hans-CN"], file.Languages);
		Assert.Equal("y", session.Keys["Odd.k"].Entries["zh-Hans-CN"].Value);
		Assert.StartsWith("value-english in [k]: no language code, so a runtime skips it and so does Save", Assert.Single(file.Errors));
	}

	[Fact]
	public void AKeysParam_NamesAParameterAsWritten_ButAtTheTopItIsALanguage() {
		var session = new WordsSession();
		string ini = "value=!en\nvalue-sr-Latn=Srpski\nparam-sr-latn=rules-sr.ini\n\n[k]\nvalue={P1} {Count}\nparam-P1=String:a\nparam-Count=Integer:2\n\n";

		WordsFile file = session.Load(new StringReader(ini), "Params");

		Assert.Empty(file.Errors);
		Assert.Equal(["P1", "Count"], session.Keys["Params.k"].Parameters.Select(parameter => parameter.Key));
		var output = new StringWriter { NewLine = "\n" };
		session.Save(file, KeyTree.Build(session, file), output);
		Assert.Contains("param-sr-Latn=rules-sr.ini\n", output.ToString());
		Assert.Contains("param-P1=String:a\nparam-Count=Integer:2\n", output.ToString()); //1.4.0 reloaded them as p1 and count
	}

	[Fact]
	public void ASettingsFile_KeepsItsOwnSuffixes() {
		var settings = ProjectSettings.Load(new StringReader("[images]\nshot=../Captures\nshot-Decode=/^shot:(\\w+)$/i/$1.png\n"), "");

		Assert.Empty(settings.Errors);
		Assert.Equal(@"/^shot:(\w+)$/i/$1.png", Assert.Single(settings.Images).Decode);
	}

	[Fact]
	public void AnXliffTargetWithThreeParts_SurvivesTheSaveAndTheReload() {
		string folder = Path.Combine(Path.GetTempPath(), $"LanguageCodes-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Strings.zh-Hans-CN.xlf");
			File.WriteAllText(path, Xliff("zh-hans-cn"));
			var codec = new XliffCodec();
			var session = new WordsSession();

			Assert.Equal(Path.Combine(folder, "Strings.ini"), codec.NativePath([path])); //the tail is a culture
			WordsFile file = session.Import(codec, codec.Discover(path));

			Assert.Empty(file.Errors);
			Assert.Equal(["zh-Hans-CN"], file.Languages);
			var output = new StringWriter { NewLine = "\n" };
			session.Save(file, KeyTree.Build(session, file), output);
			var again = new WordsSession();
			again.Load(new StringReader(output.ToString()), "Again");
			Assert.Equal("你好", again.Keys["Again.hello"].Entries["zh-Hans-CN"].Value); //1.4.0 dropped it here
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Theory]
	[InlineData("de-CH-1996", "de-CH", "read as de-CH")]
	[InlineData("english", null, "'english' ignored")]
	public void AnXliffTargetThatIsNoCode_ReadsAsTheCodeItStartsWith_OrAsNone(string target, string? code, string gripe) {
		string folder = Path.Combine(Path.GetTempPath(), $"LanguageCodes-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Strings.xlf");
			File.WriteAllText(path, Xliff(target));
			var codec = new XliffCodec();
			var session = new WordsSession();

			WordsFile file = session.Import(codec, codec.Discover(path));

			string[] languages = code is null ? [] : [code];
			Assert.Equal(languages, file.Languages);
			Assert.Contains(file.Errors, error => error.Contains(gripe));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	private static string Xliff(string target) => $"""
		<?xml version="1.0" encoding="utf-8"?>
		<xliff version="1.2" xmlns="urn:oasis:names:tc:xliff:document:1.2">
		  <file original="Strings" source-language="en" target-language="{target}" datatype="plaintext">
		    <body>
		      <trans-unit id="hello"><source>Hello</source><target>你好</target></trans-unit>
		    </body>
		  </file>
		</xliff>
		""";

	[Theory]
	[InlineData("value-zh-Hans-CN")]
	[InlineData("value-es-419")]
	[InlineData("value-ceb")]
	public void TheCommandLine_SetsAFieldInAnyCode(string field) {
		const string declared = "value-zh-Hans-CN=简体中文\nvalue-es-419=Español\nvalue-ceb=Sinugboanon\n\n";
		var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes(declared + "[k]\nvalue=x\n"));
		Assert.True(WordsField.TryParse(field, out var name, out _));

		patcher.Set("k", name, "y");

		Assert.Equal($"{declared}[k]\nvalue=x\n{field}=y\n", patcher.Text);
	}

	[Fact]
	public void TheCommandLine_ListsMissingWordsInAnyCode_AndRefusesWhatIsNone() {
		string path = Path.Combine(Path.GetTempPath(), $"LanguageCodes-{Guid.NewGuid():N}.ini");
		File.WriteAllText(path, "value=!en\nvalue-zh-Hans-CN=简体中文\n\n[a]\nvalue=a\nvalue-zh-Hans-CN=甲\n\n[b]\nvalue=b\n");
		try {
			var output = new StringWriter();
			var error = new StringWriter();

			Assert.Equal(WordsCommand.Done, WordsCommand.Run(["list", path, "--missing", "zh-hans-cn"], TextReader.Null, output, error));
			Assert.Equal("b", output.ToString().Trim());
			Assert.Equal(WordsCommand.BadCall, WordsCommand.Run(["list", path, "--missing", "english"], TextReader.Null, output, error));
			Assert.Contains("a language code is", error.ToString());
		}
		finally {
			File.Delete(path);
		}
	}
}
