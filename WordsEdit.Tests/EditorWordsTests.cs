using System.Globalization;
using System.Text.RegularExpressions;
using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Wordsmith's own words (SPEC: Wordsmith's own words): the embedded file is
///     wired in — it loads clean and a key resolves — survives the editor's round
///     trip byte for byte, and names every key the source asks for, and no more.
///     How the library resolves, falls back and sets cultures is its own tests' business.
/// </summary>
public class EditorWordsTests {
	[Fact]
	public void TheFileIsWiredIn() {
		var collector = new GripeCollector();
		var gripes = new List<string>();
		using (collector.Listen(gripes)) {
			EditorWords.Builder(collector).ToWords(EditorWords.Fallback);
		}
		Assert.Empty(gripes);

		Assert.Contains(EditorWords.Languages, language => language.Key == EditorWords.Fallback);
		Assert.Equal(EditorWords.Fallback, EditorWords.Current); //as the module initializer left it
		Assert.Equal("Wordsmith", Words.Known["app.name"]);
	}

	[Fact]
	public void TheMenuEntryIsTheLanguageOrItsFamily() {
		Assert.Equal("en", EditorWords.MenuCode("en"));
		Assert.Equal("en", EditorWords.MenuCode("en-GB"));
		Assert.Null(EditorWords.MenuCode("eo"));
	}

	[Fact]
	public void TheCommandLineNamesTheLanguageElseTheOsDoes() {
		Assert.Equal("it", EditorWords.LanguageFrom(["file.ini", "--lang=it"]));
		Assert.Equal("de", EditorWords.LanguageFrom(["--lang=it", "--lang=de"])); //the last one wins
		Assert.Equal(CultureInfo.CurrentUICulture.Name, EditorWords.LanguageFrom(["file.ini", "--lang="]));
		Assert.Equal(CultureInfo.CurrentUICulture.Name, EditorWords.LanguageFrom([]));
	}

	[Fact]
	public void PickingALanguageReloadsTheWordsAndSaysSo() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\n"), "Main");
		int changes = 0;
		vm.UiLanguageChanged += () => changes++;
		try {
			Assert.Equal("en", vm.UiLanguage);
			Assert.Contains(vm.UiLanguages, language => language.Key == "it");

			vm.UiLanguage = "it";
			Assert.Equal(1, changes);
			Assert.Equal("it", EditorWords.Current);
			Assert.Equal("Lingue", Words.Known["languages.title"]); //the screen that is translated
			Assert.Equal("Wordsmith Editor — Main", vm.Title); //re-rendered; Italian has no title yet

			vm.UiLanguage = "it"; //the same again is no change
			Assert.Equal(1, changes);
		}
		finally {
			vm.UiLanguage = "en";
		}
		Assert.Equal(2, changes);
	}

	[Fact]
	public void RoundTripsThroughTheEditorByteForByte() {
		string text = EditorWords.Text();
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader(text), "words");

		var writer = new StringWriter();
		vm.Session.Save(vm.Session.FileOf("words")!, vm.Tree.KeyNodes.Single(), writer);

		Assert.Equal(text, writer.ToString());
		Assert.Empty(vm.Session.FileOf("words")!.Errors);
	}

	//{l:Words key}, <l:WordsInline Key="key"/>, Words.Known["key"], Words.Known.Format("key", …)
	private static readonly Regex rxSourceKeys = new(
		@"\{l:Words\s+(?<key>[\w.$-]+)\s*\}|WordsInline\s+Key=""(?<key>[\w.$-]+)""|Words\.Known\[""(?<key>[\w.$-]+)""\]|Words\.Known\.Format(?:ByName)?\(""(?<key>[\w.$-]+)""",
		RegexOptions.Compiled);

	[Fact]
	public void TheSourceAndTheFileNameTheSameKeys() {
		string source = SourceRoot();
		var named = new Dictionary<string, string>(); //key → where it was seen first
		foreach (string file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)) {
			if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
					|| Path.GetExtension(file) is not (".cs" or ".xaml")) {
				continue;
			}
			foreach (Match match in rxSourceKeys.Matches(File.ReadAllText(file))) {
				named.TryAdd(match.Groups["key"].Value, Path.GetRelativePath(source, file));
			}
		}
		Assert.NotEmpty(named);

		//the keys as the editor reads them, the file label stripped
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader(EditorWords.Text()), "words");
		var declared = vm.Session.Keys.Keys.Select(key => key["words.".Length..]).ToHashSet();
		var missing = named.Where(pair => !declared.Contains(pair.Key)).Select(pair => $"{pair.Key} ({pair.Value})").Order().ToList();
		Assert.True(missing.Count == 0, "named in the source, not in words.ini: " + string.Join(", ", missing));
		var unused = declared.Where(key => !named.ContainsKey(key)).Order().ToList();
		Assert.True(unused.Count == 0, "in words.ini, named nowhere: " + string.Join(", ", unused));
	}

	//the tests run from their bin folder; the editor's source is a sibling of theirs
	private static string SourceRoot() {
		for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent) {
			string candidate = Path.Combine(folder.FullName, "WordsEdit");
			if (File.Exists(Path.Combine(candidate, "WordsEdit.csproj"))) {
				return candidate;
			}
		}
		throw new InvalidOperationException("WordsEdit source not found above " + AppContext.BaseDirectory);
	}
}
