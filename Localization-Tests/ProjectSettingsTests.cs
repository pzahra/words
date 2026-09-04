using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The project settings file (SPEC: Markdown previews): the two tables, the
///     built-in image shapes, decode rules, the language layer and the hyperlink
///     modes — all string work, no UI.
/// </summary>
public class ProjectSettingsTests {
	private const string Example = @"; the project's preview rules
; scheme=folder: the URI's path is looked up under that folder
[images]
pack=../Images
shot=../Captures
shot-decode=/^shot:(\w+)$/i/$1.png
; launch what the link resolves to, or show what the command would be
[hyperlinks]
https=shellexec
appcmd=popup
";

	private static readonly string Here = Path.Combine(Path.GetTempPath(), "WordsSettings", "Project");
	private static readonly string SettingsPath = Path.Combine(Here, "wordsmith.ini");

	static ProjectSettingsTests() {
		//WPF registers the pack scheme at startup; without it System.Uri rejects
		//the ",,," authority. The settings only ever see the string
		try {
			if (!UriParser.IsKnownScheme("pack")) {
				UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), "pack", -1);
			}
		}
		catch (InvalidOperationException) {
			//another test class got there first
		}
	}

	private static ProjectSettings Load(string ini, string path) => ProjectSettings.Load(new StringReader(ini), path);

	private static string Relative(string root, string relativePath) => Path.GetRelativePath(Here, Path.Combine(root, relativePath));

	[Fact]
	public void Load_ReadsBothTables() {
		var settings = Load(Example, SettingsPath);

		Assert.Empty(settings.Errors);
		Assert.Equal(["pack", "shot"], settings.Images.Select(rule => rule.Scheme));
		Assert.Equal("../Captures", settings.Images[1].Folder);
		Assert.Equal(Path.GetFullPath(Path.Combine(Here, "..", "Captures")), settings.Images[1].Root);
		Assert.Equal(@"/^shot:(\w+)$/i/$1.png", settings.Images[1].Decode);
		Assert.Null(settings.Images[0].Decode);
		Assert.Equal(LinkMode.ShellExec, settings.Links.Single(rule => rule.Scheme == "https").Mode);
		Assert.Equal(LinkMode.Popup, settings.Links.Single(rule => rule.Scheme == "appcmd").Mode);
	}

	[Fact]
	public void TryResolveImage_ClampsToTheRootAndProbesExtensions() {
		// a decode rule is just another way to arrive at a relative path: wherever
		// it came from it stays under the scheme's folder, and a stem finds its file
		string root = Path.Combine(Path.GetTempPath(), $"WordsResolve-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(root, "pics", "sub"));
		try {
			File.WriteAllBytes(Path.Combine(root, "pics", "sub", "ok.png"), [1, 2, 3]);
			File.WriteAllBytes(Path.Combine(root, "pics", "Logo.png"), [1, 2, 3]);
			File.WriteAllBytes(Path.Combine(root, "secret.png"), [1, 2, 3]); //exists, and must stay out of reach
			var settings = Load("[images]\nshot=pics\nshot-decode=/^shot:(.*)$//$1\nstaticres=pics\n", Path.Combine(root, "wordsmith.ini"));
			Assert.Empty(settings.Errors);

			Assert.True(settings.TryResolveImage(new Uri("shot:sub/ok.png"), out string filePath));
			Assert.Equal(Path.Combine(root, "pics", "sub", "ok.png"), filePath);
			Assert.True(settings.TryResolveImage(new Uri("staticres:Logo"), out filePath));
			Assert.Equal(Path.Combine(root, "pics", "Logo.png"), filePath);
			Assert.True(settings.TryResolveImage(new Uri("staticres:Logo.png"), out _));

			Assert.False(settings.TryResolveImage(new Uri("shot:../secret.png"), out _));
			Assert.False(settings.TryResolveImage(new Uri("shot:sub/../../secret.png"), out _));
			Assert.False(settings.TryResolveImage(new Uri("shot:..%5Csecret.png"), out _)); //a decode passes the text through as written
			Assert.False(settings.TryResolveImage(new Uri("shot:" + Path.Combine(root, "secret.png")), out _)); //rooted
			Assert.False(settings.TryResolveImage(new Uri("shot://server/share/secret.png"), out _)); //decodes to a UNC path
			Assert.False(settings.TryResolveImage(new Uri("shot:sub/missing.png"), out _));
			Assert.False(settings.TryResolveImage(new Uri("staticres:Missing"), out _));
			Assert.False(settings.TryResolveImage(new Uri("nobody:home"), out _));
		}
		finally {
			Directory.Delete(root, recursive: true);
		}
	}

	[Theory]
	[InlineData("assets:icons/save.png", "assets", "icons/save.png")]
	[InlineData("assets:/icons/save.png", "assets", "icons/save.png")]
	[InlineData("avares://My.App/Assets/logo.png", "avares", "Assets/logo.png")]
	[InlineData("pack://application:,,,/My.App;component/Images/logo.png", "pack", "Images/logo.png")]
	[InlineData("pack://application:,,,/Images/logo.png", "pack", "Images/logo.png")]
	[InlineData("resx:Logo", "resx", "Logo")]
	[InlineData("staticres:Save%20Icon", "staticres", "Save Icon")]
	[InlineData("staticres:Icon?height=16", "staticres", "Icon")]
	public void TryLocate_BuiltInShapesNeedOnlyAFolder(string uri, string scheme, string expected) {
		var settings = Load($"[images]\n{scheme}=pics\n", SettingsPath);

		Assert.True(settings.TryLocate(new Uri(uri), out string root, out string relativePath));
		Assert.Equal(Path.Combine(Here, "pics"), root);
		Assert.Equal(expected, relativePath);
	}

	[Fact]
	public void TryLocate_DecodeRuleMakesThePath_OrNothing() {
		var settings = Load(Example, SettingsPath);

		Assert.True(settings.TryLocate(new Uri("shot:Login"), out string root, out string relativePath));
		Assert.Equal(Path.GetFullPath(Path.Combine(Here, "..", "Captures")), root);
		Assert.Equal("Login.png", relativePath);
		Assert.True(settings.TryLocate(new Uri("SHOT:login"), out _, out _)); //the i option
		Assert.False(settings.TryLocate(new Uri("shot:not-a-word"), out _, out _)); //pattern misses: no path
		Assert.False(settings.TryLocate(new Uri("nobody:home"), out _, out _)); //no rule at all
	}

	[Fact]
	public void TryLocate_DecodeOverridesABuiltInShape() {
		//the replacement is over the whole URI, so the rule anchors both ends
		var settings = Load("[images]\npack=pics\npack-decode=/^.*;component\\/(.*)$//flat/$1\n", SettingsPath);

		Assert.Empty(settings.Errors);
		Assert.True(settings.TryLocate(new Uri("pack://application:,,,/App;component/deep/logo.png"), out _, out string relativePath));
		Assert.Equal("flat/deep/logo.png", relativePath);
	}

	[Fact]
	public void TryLocate_UnknownSchemeWithoutDecode_GripesAndYieldsNothing() {
		var settings = Load("[images]\nshot=pics\n", SettingsPath);

		Assert.Contains(settings.Errors, error => error.Contains("shot-decode"));
		Assert.False(settings.TryLocate(new Uri("shot:Login"), out _, out _));
	}

	[Fact]
	public void Load_MalformedRulesAreErrorsNotThrows() {
		var settings = Load(@"stray=1
[images]
shot=pics
shot-decode=no slashes
pack=pics
pack-decode=/(unclosed/i/x
loose-decode=/a//b
[hyperlinks]
appcmd=launch
appcmd-decode=/a/q/b
[other]
x=y
", SettingsPath);

		Assert.Contains(settings.Errors, error => error.StartsWith("stray"));
		Assert.Contains(settings.Errors, error => error.Contains("shot-decode") && error.Contains("/pattern/options/replacement"));
		Assert.Contains(settings.Errors, error => error.Contains("pack-decode") && error.Contains("bad pattern"));
		Assert.Contains(settings.Errors, error => error.Contains("loose-decode") && error.Contains("no loose= folder"));
		Assert.Contains(settings.Errors, error => error.Contains("appcmd") && error.Contains("neither popup nor shellexec"));
		Assert.Contains(settings.Errors, error => error.Contains("appcmd-decode") && error.Contains("unknown regex option 'q'"));
		Assert.Contains(settings.Errors, error => error.Contains("[other]"));
		//the broken decode does not fall back to the built-in shape
		Assert.False(settings.TryLocate(new Uri("pack://application:,,,/App;component/logo.png"), out _, out _));
		Assert.Equal(["shot", "pack"], settings.Images.Select(rule => rule.Scheme));
	}

	[Fact]
	public void Load_MissingFileIsAnError() {
		var settings = ProjectSettings.Load(Path.Combine(Here, $"missing-{Guid.NewGuid():N}.ini"));

		Assert.Single(settings.Errors);
		Assert.Empty(settings.Images);
		Assert.False(settings.TryLocate(new Uri("assets:x.png"), out _, out _));
	}

	[Fact]
	public void Over_LanguageRulesWinKeyByKey() {
		var project = Load(Example, SettingsPath);
		var german = Load("[images]\nshot=../Captures-de\n[hyperlinks]\nhttps=popup\n", Path.Combine(Here, "wordsmith-de.ini"));

		var layered = german.Over(project);

		//the folder comes from the language file, the decode falls through to the project's
		Assert.True(layered.TryLocate(new Uri("shot:Login"), out string root, out string relativePath));
		Assert.Equal(Path.GetFullPath(Path.Combine(Here, "..", "Captures-de")), root);
		Assert.Equal("Login.png", relativePath);
		//a scheme the language file does not mention is the project's
		Assert.True(layered.TryLocate(new Uri("pack://application:,,,/App;component/a.png"), out root, out _));
		Assert.Equal(Path.GetFullPath(Path.Combine(Here, "..", "Images")), root);
		//and so for links
		layered.Link(new Uri("https://example.com"), out LinkMode mode);
		Assert.Equal(LinkMode.Popup, mode);
		layered.Link(new Uri("appcmd:x"), out mode);
		Assert.Equal(LinkMode.Popup, mode);
		Assert.Empty(layered.Errors);
	}

	[Fact]
	public void Link_ModesDefaultByScheme_DecodeRewritesFirst() {
		var empty = ProjectSettings.Empty;
		Assert.Equal("https://example.com/", empty.Link(new Uri("https://example.com/"), out LinkMode mode));
		Assert.Equal(LinkMode.ShellExec, mode);
		empty.Link(new Uri("mailto:someone@example.com"), out mode);
		Assert.Equal(LinkMode.ShellExec, mode);
		empty.Link(new Uri("appcmd:do-something"), out mode);
		Assert.Equal(LinkMode.Popup, mode);

		var settings = Load(@"[hyperlinks]
https=popup
appcmd=shellexec
appcmd-decode=/^appcmd:(.*)$//https://example.com/commands/$1
help-decode=/^help:(\w+)$//https://example.com/help/$1.html
", SettingsPath);

		Assert.Empty(settings.Errors);
		settings.Link(new Uri("https://example.com/"), out mode);
		Assert.Equal(LinkMode.Popup, mode);
		Assert.Equal("https://example.com/commands/do-something", settings.Link(new Uri("appcmd:do-something"), out mode));
		Assert.Equal(LinkMode.ShellExec, mode);
		//a decode without a mode keeps the scheme's default
		Assert.Equal("https://example.com/help/merge.html", settings.Link(new Uri("help:merge"), out mode));
		Assert.Equal(LinkMode.Popup, mode);
		//a decode that does not match leaves the target alone
		Assert.Equal("help:not-a-word", settings.Link(new Uri("help:not-a-word"), out _));
	}

	[Fact]
	public void Write_RoundTripsTheRules() {
		//comments in a hand-written file are read past; the tables come back whole
		var settings = Load(Example, SettingsPath);
		var output = new StringWriter();

		settings.Write(output);
		var reloaded = Load(output.ToString(), SettingsPath);
		var again = new StringWriter();
		reloaded.Write(again);

		Assert.Equal(output.ToString(), again.ToString());
		Assert.DoesNotContain(";", output.ToString());
		Assert.Equal(settings.Images.Select(rule => (rule.Scheme, rule.Folder, rule.Decode)), reloaded.Images.Select(rule => (rule.Scheme, rule.Folder, rule.Decode)));
		Assert.Equal(settings.Links.Select(rule => (rule.Scheme, rule.Mode, rule.Decode)), reloaded.Links.Select(rule => (rule.Scheme, rule.Mode, rule.Decode)));
		Assert.Empty(reloaded.Errors);
	}

	[Fact]
	public void Write_EscapesLikeAnyIni() {
		//a rule with the characters the ini escapes (an underscore, a quote) and
		//one long enough to wrap comes back exactly, decode and folder alike
		string longFolder = "../" + string.Join('/', Enumerable.Repeat("a-rather-long-folder-name", 4));
		var settings = new ProjectSettings(SettingsPath,
			[new ImageRule("shot", longFolder, @"/^shot:(\w+)_(\d+)'$//$1/$2.png")],
			[new LinkRule("appcmd", LinkMode.ShellExec, null)]);
		var output = new StringWriter();

		settings.Write(output);
		var reloaded = Load(output.ToString(), SettingsPath);

		Assert.Empty(reloaded.Errors);
		Assert.Equal(longFolder, reloaded.Images[0].Folder);
		Assert.Equal(@"/^shot:(\w+)_(\d+)'$//$1/$2.png", reloaded.Images[0].Decode);
		Assert.True(reloaded.TryLocate(new Uri("shot:Login_3'"), out _, out string relativePath));
		Assert.Equal("Login/3.png", relativePath);
	}

	[Fact]
	public void DecodeRule_TryParseCoversTheShapes() {
		Assert.NotNull(DecodeRule.TryParse("/a//c", out string? error));
		Assert.Null(error);
		Assert.Equal("C", DecodeRule.TryParse("/a/i/C", out _)!.Apply("A"));
		var slashInPattern = DecodeRule.TryParse(@"/a\/b//c", out error);
		Assert.NotNull(slashInPattern);
		Assert.Equal("c", slashInPattern.Apply("a/b"));
		Assert.Null(slashInPattern.Apply("ab"));
		Assert.Null(DecodeRule.TryParse("no-slash", out error));
		Assert.NotNull(error);
		Assert.Null(DecodeRule.TryParse("/only-two/", out error));
		Assert.NotNull(error);
		Assert.Null(DecodeRule.TryParse("/(/i/x", out error));
		Assert.Contains("bad pattern", error);
	}
}
