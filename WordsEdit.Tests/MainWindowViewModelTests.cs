using System.Collections.ObjectModel;
using System.Reflection;
using PatTech.Localization.Authoring;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;
public class MainWindowViewModelTests {
	public static StreamReader GetExampleFileReader(string filePath) {
		var stream = Assembly.GetExecutingAssembly()
			.GetManifestResourceStream(filePath)
			?? throw new InvalidOperationException("embedded resource not found");
		var reader = new StreamReader(stream);
		return reader;
	}

	public static HashSet<KeyNode> GetAllKeyNodes(IEnumerable<KeyNode> rootNodes) {
		return getAllKeyNodesCore(rootNodes, new());

		static HashSet<KeyNode> getAllKeyNodesCore(IEnumerable<KeyNode> keyNodes, HashSet<KeyNode> allKeyNodes) {
			foreach (KeyNode keyNode in keyNodes) {
				allKeyNodes.Add(keyNode);
				foreach (KeyNode childKeyNode in keyNode.Children) {
					allKeyNodes.Add(childKeyNode);
					getAllKeyNodesCore(childKeyNode.Children, allKeyNodes);
				}
			}
			return allKeyNodes;
		}
	}

	[Fact]
	public void MainWindowViewModel_LoadTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");

		//Act
		mainWindowViewModel.LoadFile(reader, "Example");

		//Assert
		Assert.NotEmpty(mainWindowViewModel.KeyNodes);
		Assert.NotEmpty(mainWindowViewModel.Keys);
		Assert.True(mainWindowViewModel.KnownLanguages.Count > 1);
	}

	[Fact]
	public void MainWindowViewModel_IdempotencyTest_FileContents() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		var reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		string originalFileContents = reader.ReadToEnd();
		reader.BaseStream.Position = 0;
		var fileName = "Example";

		//Act
		mainWindowViewModel.LoadFile(reader, "Example");
		var writer = new StringWriter();
		var fileNode = mainWindowViewModel.KeyNodes.First(k => k.FullLabel == fileName);
		IniWriter.WriteFile(fileNode, writer, mainWindowViewModel.allKeys, mainWindowViewModel.KnownLanguages);
		var modifiedFileContents = writer.ToString();

		//Assert
		Assert.Equal(originalFileContents, modifiedFileContents);
	}

	[Fact]
	public void MainWindowViewModel_IdempotencyTest_Data() {
		// Arrange
		MainWindowViewModel mainWindowViewModel1 = new MainWindowViewModel();
		var reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel1.LoadFile(reader, "Example");
		ObservableCollection<WordsKey> localizationKeys1 = mainWindowViewModel1.Keys;
		ObservableCollection<LanguageEntry> localizationLanguages1 = mainWindowViewModel1.KnownLanguages;
		string fileName = "Example";

		// Act
		var writer = new StringWriter();
		KeyNode fileNode = mainWindowViewModel1.KeyNodes.First(k => k.FullLabel == fileName);
		IniWriter.WriteFile(fileNode, writer, mainWindowViewModel1.allKeys, mainWindowViewModel1.KnownLanguages);
		string generatedFileContents = writer.ToString();

		MainWindowViewModel mainWindowViewModel2 = new MainWindowViewModel();
		var generatedFileReader = new StringReader(generatedFileContents);
		mainWindowViewModel2.LoadFile(generatedFileReader, fileName);

		ObservableCollection<WordsKey> localizationKeys2 = mainWindowViewModel2.Keys;
		ObservableCollection<LanguageEntry> localizationLanguages2 = mainWindowViewModel2.KnownLanguages;

		// Assert
		Assert.Equal(localizationKeys1.Count, localizationKeys2.Count);
		Assert.Equal(localizationLanguages1.Count, localizationLanguages2.Count);
		foreach(WordsKey localizationKey1 in localizationKeys1) {
			WordsKey localizationKey2 = localizationKeys2[localizationKeys1.IndexOf(localizationKey1)];
			Assert.Equal(localizationKey2.BlockKey, localizationKey1.BlockKey);
			Assert.Equal(localizationKey2.Comment, localizationKey1.Comment);
			Assert.Equal(localizationKey2.Context, localizationKey1.Context);
			Assert.Equal(localizationKey2.DefaultValue, localizationKey1.DefaultValue);
			Assert.Equal(localizationKey2.IsConstant, localizationKey1.IsConstant);
			Assert.Equal(localizationKey2.NeedsReview, localizationKey1.NeedsReview);
			Assert.Equal(localizationKey2.Entries.Keys, localizationKey1.Entries.Keys);
			foreach (WordsParameter localizationParameter1 in localizationKey1.Parameters) {
				WordsParameter localizationParameter2 
					= localizationKey2.Parameters[localizationKey1.Parameters.IndexOf(localizationParameter1)];
				Assert.Equal(localizationParameter2.Key, localizationParameter1.Key);
				Assert.Equal(localizationParameter2.Value, localizationParameter1.Value);
				Assert.Equal(localizationParameter2.DataType, localizationParameter1.DataType);
			}
			foreach (string languageCode in localizationKey1.Entries.Keys) {
				WordsEntry localizationKeyLanguageData1 = localizationKey1.Entries[languageCode];
				WordsEntry localizationKeyLanguageData2 = localizationKey2.Entries[languageCode];
				Assert.Equal(localizationKeyLanguageData2.Value, localizationKeyLanguageData1.Value);
				Assert.Equal(localizationKeyLanguageData2.Stale, localizationKeyLanguageData1.Stale);
				Assert.Equal(localizationKeyLanguageData2.Context, localizationKeyLanguageData1.Context);
				Assert.Equal(localizationKeyLanguageData2.Comment, localizationKeyLanguageData1.Comment);
			}
		}
		foreach (LanguageEntry localizationLanguage1 in localizationLanguages1) {
			LanguageEntry localizationLanguage2 = localizationLanguages2[localizationLanguages1.IndexOf(localizationLanguage1)];
			Assert.Equal(localizationLanguage2.Code, localizationLanguage1.Code);
			Assert.Equal(localizationLanguage2.NativeName, localizationLanguage1.NativeName);
			Assert.Equal(localizationLanguage2.EnglishName, localizationLanguage1.EnglishName);
		}
	}


	[Fact]
	public void MainWindowViewModel_ToggleStaleTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKey = mainWindowViewModel.Keys[0]; //BlockKey = Example.view.section-name.key
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0]; //FullLabel = Example.view.section-name.key

		//Act
		mainWindowViewModel.ToggleStaleLanguageCommand.Execute("en-CA");

		//Assert
		Assert.NotNull(mainWindowViewModel.Keys[0].Entries["en-CA"].Stale);
		Assert.True(mainWindowViewModel.SelectedKeyNode?.IsStale);
	}

	[Fact]
	public void MainWindowViewModel_ToggleConstantTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		mainWindowViewModel.Keys.Add(new WordsKey("fullLabel") {
			Entries = { { "en", new WordsEntry() } } 
		});
		mainWindowViewModel.KeyNodes.Add(new KeyNode("label", "fullLabel"));
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0];
		mainWindowViewModel.SelectedKey = mainWindowViewModel.Keys[0];
		mainWindowViewModel.SelectedEntry = mainWindowViewModel.SelectedKey.Entries["en"];
		
		//Act
		mainWindowViewModel.ToggleLocalizationKeyIsConstantCommand.Execute(null);

		//Assert
		Assert.True(mainWindowViewModel.SelectedKey.IsConstant);
		Assert.True(mainWindowViewModel.SelectedKeyNode?.IsConstant);
	}

	[Fact]
	public void MainWindowViewModel_CanBeConstantTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");

		//Act
		mainWindowViewModel.LoadFile(reader, "Example");

		//Assert
		var allKeyNodes = GetAllKeyNodes(mainWindowViewModel.KeyNodes);
		bool correctlyAssignsCanBeConstant = true;
		foreach (KeyNode nodeToCheck in allKeyNodes) {
			foreach (KeyNode file in mainWindowViewModel.KeyNodes) {
				if (file.Children.Contains(nodeToCheck) && nodeToCheck.Children.Count == 0) {
					if (!nodeToCheck.CanBeConstant) {
						correctlyAssignsCanBeConstant = false;
					}
				}
				else {
					if (nodeToCheck.CanBeConstant) {
						correctlyAssignsCanBeConstant = false;
					}
				}
			}
		}
		Assert.True(correctlyAssignsCanBeConstant);
	}

	[Fact]
	public void MainWindowViewModel_StaleAllLanguagesTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0]; //FullLabel = Example.view.section-name.key

		//Act
		mainWindowViewModel.StaleAllLanguagesCommand.Execute(null);

		//Assert
		bool correctlyStalesAllLanguages = true;
		if (mainWindowViewModel.SelectedKey is null) {
			throw new InvalidOperationException();
		}
		foreach (WordsEntry languageData in mainWindowViewModel.SelectedKey.Entries.Values) {
			if (languageData.Stale is null) {
				correctlyStalesAllLanguages = false;
			}
		}
		Assert.True(correctlyStalesAllLanguages);
	}

	[Fact]
	public void MainWindowViewModel_ResetCoreTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[0]; //FullLabel = view.section-name.key
		mainWindowViewModel.SelectedLanguage = mainWindowViewModel.KnownLanguages[1];

		//Act
		mainWindowViewModel.ResetCore();

		//Assert
		Assert.Null(mainWindowViewModel.SelectedKeyNode);
		Assert.Null(mainWindowViewModel.SelectedKey);
		Assert.Null(mainWindowViewModel.SelectedEntry);
		Assert.Single(mainWindowViewModel.KnownLanguages);
		Assert.Empty(mainWindowViewModel.Keys);
		Assert.Empty(mainWindowViewModel.KeyNodes);
		Assert.Equal("", mainWindowViewModel.SearchFilterText);
		Assert.False(mainWindowViewModel.IsStaleFilter);
		Assert.Equal("en", mainWindowViewModel.SelectedLanguage.Code);
		Assert.Empty(mainWindowViewModel.FileNames);
	}


	[Fact]
	public void MainWindowViewModel_RemoveLocalizationKeyTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[0]; //FullLabel = view.section-name.key
		WordsKey? selectedLocalizationKey = mainWindowViewModel.SelectedKey;

		//Act
		mainWindowViewModel.RemoveLocalizationKeyCommand.Execute(null);

		//Assert
		Assert.Null(mainWindowViewModel.SelectedKey);
		Assert.Null(mainWindowViewModel.SelectedEntry);
		Assert.DoesNotContain(selectedLocalizationKey, mainWindowViewModel.Keys);
	}

	[Fact]
	public void MainWindowViewModel_RemoveLocalizationKeyAndNodeTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0]; //FullLabel = Example.view.section-name.key
		string selectedLocalizationKeyBlockKey = mainWindowViewModel.SelectedKey?.BlockKey
			?? throw new InvalidOperationException();

		//Act
		mainWindowViewModel.RemoveLocalizationKeyAndNodeCommand.Execute(null);

		//Assert
		var allKeyNodes = GetAllKeyNodes(mainWindowViewModel.KeyNodes);
		bool allLocalizationKeysRemoved = !mainWindowViewModel.Keys.Any(k => k.BlockKey.Contains(selectedLocalizationKeyBlockKey));
		bool allKeyNodesRemoved = !allKeyNodes.Any(k => k.FullLabel.Contains(selectedLocalizationKeyBlockKey));
		Assert.True(allLocalizationKeysRemoved);
		Assert.True(allKeyNodesRemoved);
	}

	[Fact]
	public void MainWindowViewModel_RenameLocalizationKeyNodeTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0]; //FullLabel = Example.view.section-name.key
		string selectedKeyNodeLabel = mainWindowViewModel.SelectedKeyNode.Label;
		string selectedLocalizationKeyBlockKey = mainWindowViewModel.SelectedKey?.BlockKey
			?? throw new InvalidOperationException();

		//Act
		mainWindowViewModel.RenameLocalizationKeyAndNode("test");

		//Assert
		var allKeyNodes = GetAllKeyNodes(mainWindowViewModel.KeyNodes);
		bool allLocalizationKeysRenamed = !mainWindowViewModel.Keys.Any(k => k.BlockKey.Contains(selectedLocalizationKeyBlockKey));
		bool allKeyNodesRenamed = !allKeyNodes.Any(k => k.FullLabel.Contains(selectedLocalizationKeyBlockKey));
		Assert.Equal("test", mainWindowViewModel.SelectedKeyNode.Label);
		Assert.Equal("Example.view.section-name.test", mainWindowViewModel.SelectedKeyNode.FullLabel);
		Assert.Equal("Example.view.section-name.test", mainWindowViewModel.SelectedKey.BlockKey);
		Assert.True(allLocalizationKeysRenamed);
		Assert.True(allKeyNodesRenamed);
	}

	[Fact]
	public void MainWindowViewModel_AddLocalizationKeyTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0]; //FullLabel = view

		//Act
		mainWindowViewModel.AddLocalizationKeyCommand.Execute(null);

		//Assert
		WordsKey newKey = mainWindowViewModel.Keys.Last();
		bool newKeyHasAllLanguages = true;
		foreach (LanguageEntry language in mainWindowViewModel.KnownLanguages) {
			if (!newKey.Entries.ContainsKey(language.Code)) {
				newKeyHasAllLanguages = false;
			}
		}
		Assert.Equal(newKey, mainWindowViewModel.SelectedKey);
		Assert.Equal(newKey.Entries[mainWindowViewModel.SelectedLanguage.Code], mainWindowViewModel.SelectedEntry);
		Assert.True(newKeyHasAllLanguages);
		Assert.Equal(mainWindowViewModel.SelectedKeyNode.FullLabel, newKey.BlockKey);
	}

	[Fact]
	public void MainWindowViewModel_AddLocalizationKeyNodeTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0].Children[0]; //FullLabel = Example.view

		//Act
		mainWindowViewModel.AddLocalizationKeyNode("test");

		//Assert
		KeyNode newKeyNode = mainWindowViewModel.KeyNodes[0].Children[0].Children[1]; //Should be added, FullLabel = Example.view.test
		Assert.Equal("test", newKeyNode.Label);
		Assert.Equal("Example.view.test", newKeyNode.FullLabel);
		Assert.False(newKeyNode.CanBeConstant);
		Assert.True(newKeyNode.IsSelected);
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[0].IsSelected);
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].IsExpanded);
		Assert.Equal(newKeyNode, mainWindowViewModel.SelectedKeyNode);
		Assert.Null(mainWindowViewModel.SelectedKey);
		Assert.Null(mainWindowViewModel.SelectedEntry);
	}

	[Fact]
	public void MainWindowViewModel_DictionariesHaveTheSameLocalizationKeysTest() {
		//Arrange
		List<Dictionary<string, WordsKey>> localizationKeyDictionaries = new();
		Dictionary<string, WordsKey> dictionary1 = new();
		Dictionary<string, WordsKey> dictionary2 = new();
		Dictionary<string, WordsKey> dictionary3 = new();
		dictionary1.Add("dictionary1.test1", new WordsKey("dictionary1.test1"));
		dictionary2.Add("dictionary2.test1", new WordsKey("dictionary2.test1"));
		dictionary3.Add("dictionary3.test1", new WordsKey("dictionary3.test1"));
		dictionary1.Add("dictionary1.test2", new WordsKey("dictionary1.test2"));
		dictionary2.Add("dictionary2.test2", new WordsKey("dictionary2.test2"));
		dictionary3.Add("dictionary3.test2", new WordsKey("dictionary3.test2"));
		dictionary1.Add("dictionary1.test3", new WordsKey("dictionary1.test3"));
		dictionary2.Add("dictionary2.test3", new WordsKey("dictionary2.test3"));
		dictionary3.Add("dictionary3.test3", new WordsKey("dictionary3.test3"));
		localizationKeyDictionaries.Add(dictionary1);
		localizationKeyDictionaries.Add(dictionary2);
		localizationKeyDictionaries.Add(dictionary3);

		//Act
		bool dictionariesHaveTheSameLocalizationKeys = MainWindowViewModel.DictionariesHaveTheSameLocalizationKeys(localizationKeyDictionaries);

		//Assert
		Assert.True(dictionariesHaveTheSameLocalizationKeys);
	}

	[Fact]
	public void MainWindowViewModel_MergeTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		reader = GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile.ini");
		mainWindowViewModel.LoadFile(reader, "MergeTestFile");
		reader = GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile2.ini");
		mainWindowViewModel.LoadFile(reader, "MergeTestFile2");
		KeyNode baseFile = mainWindowViewModel.KeyNodes[0];
		Dictionary<string, KeyNode> languageCodeFilePairDictionary = new() {
			{ "en", baseFile },
			{ "zh", mainWindowViewModel.KeyNodes[1] },
			{ "en-CA", mainWindowViewModel.KeyNodes[2] }
		};

		//Act
		mainWindowViewModel.GetMergedKeyNode(baseFile, languageCodeFilePairDictionary, "Example", out var mergedKeysRewrite);
		mainWindowViewModel.GetMergedKeyNode(baseFile, languageCodeFilePairDictionary, "MergedFile", out var mergedKeysNewFile);

		//Assert
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].DefaultValue);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Entries["en"].Value);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Entries["en"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Entries["en"].Comment);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key"].Entries["zh"].Value);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key"].Entries["zh"].Context);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key"].Entries["zh"].Comment);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key"].Entries["en-CA"].Value);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key"].Entries["en-CA"].Context);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key"].Entries["en-CA"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].DefaultValue);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en"].Value);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en"].Comment);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["zh"].Value);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["zh"].Context);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["zh"].Comment);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en-CA"].Value);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en-CA"].Context);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en-CA"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].DefaultValue);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Entries["en"].Value);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Entries["en"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key"].Entries["en"].Comment);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key"].Entries["zh"].Value);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key"].Entries["zh"].Context);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key"].Entries["zh"].Comment);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key"].Entries["en-CA"].Value);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key"].Entries["en-CA"].Context);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key"].Entries["en-CA"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].DefaultValue);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Comment);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en"].Value);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en"].Context);
		Assert.Equal("Base", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en"].Comment);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["zh"].Value);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["zh"].Context);
		Assert.Equal("2", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["zh"].Comment);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en-CA"].Value);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en-CA"].Context);
		Assert.Equal("3", mergedKeysRewrite["Example.view.section-name.key.tooltip"].Entries["en-CA"].Comment);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key"].DefaultValue);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key"].Context);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key"].Comment);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["en"].Value);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["en"].Context);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["en"].Comment);
		Assert.Equal("2", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["zh"].Value);
		Assert.Equal("2", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["zh"].Context);
		Assert.Equal("2", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["zh"].Comment);
		Assert.Equal("3", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["en-CA"].Value);
		Assert.Equal("3", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["en-CA"].Context);
		Assert.Equal("3", mergedKeysNewFile["MergedFile.view.section-name.key"].Entries["en-CA"].Comment);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].DefaultValue);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Context);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Comment);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["en"].Value);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["en"].Context);
		Assert.Equal("Base", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["en"].Comment);
		Assert.Equal("2", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["zh"].Value);
		Assert.Equal("2", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["zh"].Context);
		Assert.Equal("2", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["zh"].Comment);
		Assert.Equal("3", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["en-CA"].Value);
		Assert.Equal("3", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["en-CA"].Context);
		Assert.Equal("3", mergedKeysNewFile["MergedFile.view.section-name.key.tooltip"].Entries["en-CA"].Comment);
	}

	[Fact]
	public void MainWindowViewModel_FileNamesTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");

		//Act
		mainWindowViewModel.LoadFile(reader, "Example");
		reader = GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile.ini");
		mainWindowViewModel.LoadFile(reader, "MergeTestFile");
		reader = GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile2.ini");
		mainWindowViewModel.LoadFile(reader, "MergeTestFile2");

		//Assert
		Assert.Contains("Example", mainWindowViewModel.FileNames);
		Assert.Contains("MergeTestFile", mainWindowViewModel.FileNames);
		Assert.Contains("MergeTestFile2", mainWindowViewModel.FileNames);
		Assert.Equal(3, mainWindowViewModel.FileNames.Count);
	}

	[Fact]
	public void MainWindowViewModel_RemoveFileNodeTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedKeyNode = mainWindowViewModel.KeyNodes[0];

		//Act
		mainWindowViewModel.RemoveFileNodeCore(mainWindowViewModel.SelectedKeyNode);

		//Assert
		Assert.Null(mainWindowViewModel.SelectedKeyNode);
		Assert.Null(mainWindowViewModel.SelectedKey);
		Assert.Null(mainWindowViewModel.SelectedEntry);
		Assert.Empty(mainWindowViewModel.KeyNodes);
		Assert.Empty(mainWindowViewModel.FileNames);
		Assert.Empty(mainWindowViewModel.Keys);
	}

	[Fact]
	public void MainWindowViewModel_StaleViewTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedLanguage = mainWindowViewModel.KnownLanguages[4]; //zh - Chinese (Simplified)

		//Act
		mainWindowViewModel.IsStaleFilter = true;

		//Assert
		Assert.True(mainWindowViewModel.KeyNodes[0].IsVisible); //Example
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].IsVisible); //Example.view
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].Children[0].IsVisible); //Example.view.section-name
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0].IsVisible); //Example.view.section-name.key
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0].Children[0].IsVisible); //Example.view.section-name.key.tooltip
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[1].IsVisible); //Example.$rsi-unit
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[2].IsVisible); //Example.main
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[3].IsVisible); //Example.format
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[4].IsVisible); //Example.enum
	}

	[Fact]
	public void MainWindowViewModel_SearchTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedLanguage = mainWindowViewModel.KnownLanguages[4]; //zh - Chinese (Simplified)

		//Act
		mainWindowViewModel.SearchFilterText = "tooltip";

		//Assert
		Assert.True(mainWindowViewModel.KeyNodes[0].IsVisible); //Example
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].IsVisible); //Example.view
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].Children[0].IsVisible); //Example.view.section-name
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0].IsVisible); //Example.view.section-name.key
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[0].Children[0].Children[0].Children[0].IsVisible); //Example.view.section-name.key.tooltip
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[1].IsVisible); //Example.$rsi-unit
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[2].IsVisible); //Example.main
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[3].IsVisible); //Example.format
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[4].IsVisible); //Example.enum
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[4].Children[0].IsVisible); //Example.enum.none
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[4].Children[1].IsVisible); //Example.enum.two
		Assert.True(mainWindowViewModel.KeyNodes[0].Children[4].Children[1].Children[0].IsVisible); //Example.enum.two.tooltip
		Assert.False(mainWindowViewModel.KeyNodes[0].Children[4].Children[1].Children[1].IsVisible); //Example.enum.two.desc
	}

	[Fact]
	public void MainWindowViewModel_StaleAndSearchTest() {
		//Arrange
		MainWindowViewModel mainWindowViewModel = new MainWindowViewModel();
		StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		mainWindowViewModel.LoadFile(reader, "Example");
		mainWindowViewModel.SelectedLanguage = mainWindowViewModel.KnownLanguages[4]; //zh - Chinese (Simplified)

		//Act
		mainWindowViewModel.IsStaleFilter = true;
		mainWindowViewModel.SearchFilterText = "tooltip";

		//Assert
		Assert.False(mainWindowViewModel.KeyNodes[0].IsVisible); //Example
	}
}
