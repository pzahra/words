using PatTech.Localization.Authoring;
using PatTech.Localization.Authoring.Codecs;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     What a key may be called (runtime SPEC: Key names): one grammar, in Core,
///     which the runtime skips a block by, the authoring reader gripes by, the
///     command line refuses by, and a foreign name is made to fit.
/// </summary>
public class KeyNameTests {
	private sealed class CaptureLogger : ITakeException {
		public readonly List<string> Messages = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	[Theory]
	[InlineData("a")]
	[InlineData("menu.file-open")]
	[InlineData("x_1.y2.z")]
	[InlineData("_lead.é-ü")]
	[InlineData("$unit")]
	[InlineData("$si-unit_2")]
	public void AKeyIsDottedSegments_OrAConstant(string key) {
		Assert.True(WordsParser.IsKeyName(key));
	}

	[Theory]
	[InlineData("")]
	[InlineData("a b")]
	[InlineData("lang.c#")]
	[InlineData("x=y")]
	[InlineData(";z")]
	[InlineData("a:b")]
	[InlineData("{a}")]
	[InlineData(">a")]
	[InlineData(".a")]
	[InlineData("a.")]
	[InlineData("a..b")]
	[InlineData("-a")]
	[InlineData("a.-b")]
	[InlineData("$")]
	[InlineData("$a.b")] //a constant has no children
	[InlineData("a.$b")]
	[InlineData("a\n")]
	public void AnythingElse_IsNoKey(string key) {
		Assert.False(WordsParser.IsKeyName(key));
	}

	[Fact]
	public void ASegment_IsOneOfThem() {
		Assert.True(WordsParser.IsKeySegment("file-open"));
		Assert.False(WordsParser.IsKeySegment("file.open"));
		Assert.False(WordsParser.IsKeySegment("$unit"));
		Assert.False(WordsParser.IsKeySegment(""));
	}

	[Fact]
	public void TheRuntime_SkipsABlockNamedOtherwise_WithItsFields() {
		var logger = new CaptureLogger();
		var builder = WordsBuilder.Create(logger).LoadString(
			"value-de=Deutsch\n\n[lang.c]\nvalue=C\nvalue-de=C (de)\n\n[lang.c#]\nvalue=C#\\\nsharp\nvalue-de=C# (de)\n\n[after]\nvalue=after\n");

		var words = builder.Flatten("de");

		//1.4.0 read C#; plural forms made '#' a form's mark, so the block is no key's
		Assert.Equal("C (de)", words["lang.c"]);
		Assert.False(words.ContainsKey("lang.c#"));
		Assert.False(words.ContainsKey("lang.c#sharp"));
		Assert.Equal("after", words["after"]);
		Assert.Equal("WP:NAME:`lang.c#`", Assert.Single(logger.Messages));
	}

	[Fact]
	public void TheRuntime_SkipsTheChildrenOfAConstant_AndOfABlockNamedOtherwise() {
		var logger = new CaptureLogger();
		var builder = WordsBuilder.Create(logger).LoadString(
			"[$unit]\nvalue=kg\n\n[.child]\nvalue=x\n\n[a b]\nvalue=y\n\n[.c]\nvalue=z\n");

		var words = builder.Flatten("");

		Assert.Equal("kg", words["$unit"]);
		Assert.False(words.ContainsKey("$unit.child"));
		Assert.False(words.ContainsKey("a b"));
		Assert.False(words.ContainsKey("a b.c"));
		Assert.Equal(["WP:NAME:`$unit.child`", "WP:NAME:`a b`", "WP:NAME:`a b.c`"], logger.Messages);
	}

	[Fact]
	public void TheNextFilesLabels_AreReadThoughTheLastFileEndedInASkippedBlock() {
		var builder = WordsBuilder.Create()
			.LoadString("value-en=English\n\n[bad key]\nvalue=x\n")
			.LoadString("value-fr=Français\n\n[good]\nvalue=y\n");

		Assert.Equal(["en", "fr"], builder.GetLanguages().Select(language => language.Key));
		Assert.Equal("y", builder.Flatten("fr")["good"]);
	}

	[Fact]
	public void TheAuthoringReader_KeepsTheBlock_AndGripesThatARuntimeSkipsIt() {
		var session = new WordsSession();
		string ini = "value=!en\n\n[lang.c#]\nvalue=C#\n\n[ok]\nvalue=ok\n\n";

		WordsFile file = session.Load(new StringReader(ini), "Odd");

		Assert.Equal("C#", session.Keys["Odd.lang.c#"].DefaultValue);
		Assert.StartsWith("[lang.c#]: 'lang.c#' is no key name, so a runtime skips it", Assert.Single(file.Errors));
		var output = new StringWriter { NewLine = "\n" };
		session.Save(file, KeyTree.Build(session, file), output);
		Assert.Equal(ini, output.ToString()); //written back unchanged, for a rename to fix
	}

	[Fact]
	public void TheCommandLine_WritesNoKeyNamedOtherwise_ButRemovesOne() {
		var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes("[a b]\nvalue=x\n\n[ok]\nvalue=y\n"));
		Assert.True(WordsField.TryParse("value", out var value, out _));

		var refused = Assert.Throws<ArgumentException>(() => patcher.Set("a b", value, "z"));
		Assert.Contains("is no key name", refused.Message);
		Assert.Throws<ArgumentException>(() => patcher.Set("lang.c#", value, "z"));

		patcher.Remove("a b");
		Assert.Equal("[ok]\nvalue=y\n", patcher.Text);
	}

	[Fact]
	public void TheCommandLine_RefusesToRemoveAConstantThatBasesChildren_SayingWhy() {
		var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes("[$a]\nvalue=x\n\n[.child]\nvalue=y\n"));

		var refused = Assert.Throws<IniPatchException>(() => patcher.Remove("$a"));

		Assert.Contains("no constant can", refused.Message);
	}

	[Fact]
	public void AForeignName_IsMadeAKey_AndTwoNeverBecomeOne() {
		string folder = Path.Combine(Path.GetTempPath(), $"KeyNames-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string path = Path.Combine(folder, "Strings.resx");
			File.WriteAllText(path, """
				<?xml version="1.0" encoding="utf-8"?>
				<root>
				  <data name="a b"><value>1</value></data>
				  <data name="$this.Text"><value>2</value></data>
				  <data name="a]"><value>3</value></data>
				  <data name="a_"><value>4</value></data>
				  <data name="$unit"><value>5</value></data>
				  <data name=".lead"><value>6</value></data>
				</root>
				""");

			var loaded = new ResxCodec().Read([path]);

			Assert.Equal(["a_b", "_this.Text", "a_", "a_-2", "$unit", "_.lead"], loaded.WordKeys.Keys);
			Assert.True(loaded.WordKeys["$unit"].IsConstant);
			Assert.False(loaded.WordKeys["_this.Text"].IsConstant);
			Assert.Equal("3", loaded.WordKeys["a_"].DefaultValue);
			Assert.Equal("4", loaded.WordKeys["a_-2"].DefaultValue); //not overwritten
			Assert.Contains(loaded.Errors, error => error.Contains("'a_' would load as 'a_', which 'a]' already is, so it loads as 'a_-2'"));
			Assert.All(loaded.WordKeys.Keys, key => Assert.True(WordsParser.IsKeyName(key), key));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
