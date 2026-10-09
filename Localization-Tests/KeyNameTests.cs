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
	[InlineData("errors.404")]
	[InlineData("हिंदी.शब्द")]           //spacing marks, which 1.4.0's \w lacked
	[InlineData("می‌خواهم")]        //a non-joiner inside a Persian word
	[InlineData("𝒜𝒷𝒸.𐐷")]               //letters past the 16-bit plane
	[InlineData("١٢٣")]                  //a digit of any script starts one
	[InlineData("a·b")]                  //Other_ID_Continue
	public void AKeyIsDottedSegments_OrAConstant(string key) {
		Assert.True(WordsParser.IsKeyName(key));
	}

	[Theory]
	[InlineData("́a")]  //a mark starts nothing
	[InlineData("‌a")]  //nor a joiner
	[InlineData("a😀")]      //a symbol is no identifier's
	[InlineData("a\uD800")]  //nor a lone surrogate
	[InlineData("abͺ")] //nor what NFKC would change: XID, not ID
	[InlineData("café")] //café, decomposed: a name is in NFC
	public void WhatNoIdentifierHolds_IsNoKey(string key) {
		Assert.False(WordsParser.IsKeyName(key));
	}

	[Fact]
	public void ANameIsInNfc_SoItComparesByCodePoints() {
		Assert.True(WordsParser.IsKeyName("café".Normalize()));
		Assert.False(WordsParser.IsKeySegment("café"));
		Assert.True(WordsParser.IsKeySegment("café"));
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
	public void TheRuntime_SkipsAnEmptyHeader_WithItsFields_AndPoursNothingIntoTheKeyAbove() {
		var logger = new CaptureLogger();
		var builder = WordsBuilder.Create(logger).LoadString(
			"value-de=Deutsch\n\n[a]\nvalue=keep\n\n[]\nvalue=lost\nvalue-de=lost\\\nstill lost\n\n[.c]\nvalue=z\n\n[b]\nvalue=b\n");

		var words = builder.Flatten("de");

		//[] once matched no header, so its fields read as a's
		Assert.Equal("keep", words["a"]);
		Assert.False(words.ContainsKey(".c"));
		Assert.Equal("b", words["b"]);
		Assert.Equal(["WP:NAME:``", "WP:NAME:`.c`"], logger.Messages);
	}

	[Fact]
	public void ALeadingEmptyHeader_HoldsNoTopOfFileLabel() {
		var builder = WordsBuilder.Create().LoadString("[]\nvalue-fr=Français\n\n[k]\nvalue=v\n");

		Assert.DoesNotContain("fr", builder.GetLanguages().Select(language => language.Key));
	}

	[Fact]
	public void TheAuthoringReader_GripesAboutAnEmptyHeader_AndKeepsTheKeysAroundIt() {
		var session = new WordsSession();
		string ini = "[]\nvalue=keep\n\n[a]\nvalue=a\n\n[]\nvalue=lost\n\n[b]\nvalue=b\n";

		WordsFile file = session.Load(new StringReader(ini), "Odd");

		Assert.Equal("a", session.Keys["Odd.a"].DefaultValue);
		Assert.Equal("b", session.Keys["Odd.b"].DefaultValue);
		Assert.All(file.Errors, error => Assert.StartsWith("[]: names no key", error));
		Assert.Equal(2, file.Errors.Count);
		var output = new StringWriter { NewLine = "\n" };
		session.Save(file, KeyTree.Build(session, file), output);
		//a leading [] once read its value as the file's default label, saved as value=!keep
		Assert.DoesNotContain("keep", output.ToString());
		Assert.DoesNotContain("lost", output.ToString());
	}

	[Fact]
	public void TheCommandLine_EndsAKeyAtAnEmptyHeader() {
		var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes("[a]\nvalue=x\n\n[]\nvalue=junk\n"));
		Assert.True(WordsField.TryParse("value", out var value, out _));
		Assert.True(WordsField.TryParse("value-de", out var german, out _));

		patcher.Set("a", value, "y");
		patcher.Set("a", german, "z");

		string text = patcher.Text;
		Assert.Contains("value=junk", text);
		Assert.True(text.IndexOf("value=y") < text.IndexOf("[]"));
		Assert.True(text.IndexOf("value-de=z") < text.IndexOf("[]"));
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
				  <data name="हिंदी 😀"><value>7</value></data>
				</root>
				""");

			var loaded = new ResxCodec().Read([path]);

			//\w made हिंदी ह_ंद_: its vowel signs are spacing marks, which UAX #31 keeps
			Assert.Equal(["a_b", "_this.Text", "a_", "a_-2", "$unit", "_.lead", "हिंदी__"], loaded.WordKeys.Keys);
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

	[Fact]
	public void ANameToldApartByANumber_FindsItsKeyInEveryFileOfTheSet() {
		string folder = Path.Combine(Path.GetTempPath(), $"KeyNames-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			string Resx(string first, string second) => $"""
				<?xml version="1.0" encoding="utf-8"?>
				<root>
				  <data name="a]"><value>{first}</value></data>
				  <data name="a_"><value>{second}</value></data>
				</root>
				""";
			File.WriteAllText(Path.Combine(folder, "Strings.resx"), Resx("3", "4"));
			File.WriteAllText(Path.Combine(folder, "Strings.fr.resx"), Resx("trois", "quatre"));
			var codec = new ResxCodec();

			var loaded = codec.Read(codec.Discover(Path.Combine(folder, "Strings.resx")));

			Assert.Equal(["a_", "a_-2"], loaded.WordKeys.Keys);
			Assert.Equal(("3", "trois"), (loaded.WordKeys["a_"].DefaultValue, loaded.WordKeys["a_"].Entries["fr"].Value));
			Assert.Equal(("4", "quatre"), (loaded.WordKeys["a_-2"].DefaultValue, loaded.WordKeys["a_-2"].Entries["fr"].Value));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}
}
