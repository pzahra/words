using System.Reflection;
using PatTech.Localization.Authoring;
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

	//every dialog answered by the fake: the tests never see a window
	private static MainWindowViewModel NewVm() => new(new FakeDialogs());

	private static MainWindowViewModel LoadExample() {
		var vm = NewVm();
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		return vm;
	}

	public static IEnumerable<KeyNode> GetAllKeyNodes(IEnumerable<KeyNode> rootNodes)
		=> rootNodes.SelectMany(node => node.SelfAndDescendants());

	public static KeyNode Node(MainWindowViewModel viewModel, string fullLabel)
		=> GetAllKeyNodes(viewModel.Tree.KeyNodes).First(node => node.FullLabel == fullLabel);

	private static string Save(MainWindowViewModel vm, string label) {
		var writer = new StringWriter();
		vm.Session.Save(vm.Session.FileOf(label)!, vm.Tree.KeyNodes.First(k => k.FullLabel == label), writer);
		return writer.ToString();
	}

	[Fact]
	public void MainWindowViewModel_LoadTest() {
		var vm = LoadExample();

		Assert.NotEmpty(vm.Tree.KeyNodes);
		Assert.NotEmpty(vm.Session.Keys);
		Assert.True(vm.Tree.KnownLanguages.Count > 1);
		Assert.Single(vm.Session.Files);
	}

	[Fact]
	public void MainWindowViewModel_IdempotencyTest_FileContents() {
		var reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
		string originalFileContents = reader.ReadToEnd();
		reader.BaseStream.Position = 0;
		var vm = NewVm();

		vm.LoadFile(reader, "Example");

		Assert.Equal(originalFileContents, Save(vm, "Example"));
	}

	[Fact]
	public void MainWindowViewModel_SettingsReferences_CapturedOnLoadAndWrittenBack() {
		// the top-of-file param slots name the settings files: captured per file
		// on load, threaded back through the writer on save, captured again on reload
		var vm1 = NewVm();
		vm1.LoadFile(new StringReader(@"
value-en=English
value-de=Deutsch
param=wordsmith.ini
param-de=../de/wordsmith.ini

[k]
value=x
"), "Example");

		WordsFile file = vm1.Session.FileOf("Example")!;
		Assert.Equal("wordsmith.ini", file.Settings);
		Assert.Equal("../de/wordsmith.ini", file.LanguageSettings["de"]);

		var vm2 = NewVm();
		vm2.LoadFile(new StringReader(Save(vm1, "Example")), "Example");

		Assert.Equal("wordsmith.ini", vm2.Session.FileOf("Example")!.Settings);
		Assert.Equal("../de/wordsmith.ini", vm2.Session.FileOf("Example")!.LanguageSettings["de"]);
	}

	[Fact]
	public void MainWindowViewModel_PreviewSettingsFollowTheSelectedFileAndLanguage() {
		// the previews take their rules from the selected key's file: the default
		// pane the dictionary's settings alone, the translation pane the selected
		// language's layered over them; links go by the same rules
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditSettings-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			File.WriteAllText(Path.Combine(folder, "wordsmith.ini"), "[images]\nshot=shots\nshot-decode=/^shot:(\\w+)$//$1.png\n[hyperlinks]\nhelp=shellexec\nhelp-decode=/^help:(\\w+)$//https://example.com/help/$1.html\n");
			File.WriteAllText(Path.Combine(folder, "wordsmith-de.ini"), "[images]\nshot=shots-de\n");
			var dialogs = new FakeDialogs { ConfirmAnswer = false };
			var vm = new MainWindowViewModel(dialogs);
			vm.LoadFile(new StringReader("value-en=English\nvalue-de=Deutsch\nparam=wordsmith.ini\nparam-de=wordsmith-de.ini\n\n[k]\nvalue=x\n"), Path.Combine(folder, "strings.ini"));
			vm.LoadFile(new StringReader("value-en=English\n\n[j]\nvalue=y\n"), "Other");
			vm.ShowDefaultPreview = true;
			vm.ShowLocalizationPreview = true;

			vm.Tree.SelectedKeyNode = Node(vm, "strings.k");
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots")), Assert.Single(vm.DefaultPreview.Settings.Images).Root);
			Assert.Same(vm.DefaultPreview.Settings, vm.TranslationPreview.Settings); //en has no file of its own

			vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de");
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots")), Assert.Single(vm.DefaultPreview.Settings.Images).Root);
			Assert.True(vm.TranslationPreview.Settings.TryLocate(new Uri("shot:Login"), out string root, out string path));
			Assert.Equal(Path.GetFullPath(Path.Combine(folder, "shots-de")), root);
			Assert.Equal("Login.png", path);

			//a decoded shellexec link confirms with what it will launch
			vm.FollowLink(new Uri("help:merge"));
			Assert.Contains("https://example.com/help/merge.html", Assert.Single(dialogs.Confirmations));
			Assert.Empty(dialogs.Notices);

			vm.Tree.SelectedKeyNode = Node(vm, "Other.j");
			Assert.Same(ProjectSettings.Empty, vm.DefaultPreview.Settings);
			Assert.Same(ProjectSettings.Empty, vm.TranslationPreview.Settings);
			vm.FollowLink(new Uri("help:merge")); //no rules here: reported as it is
			Assert.Contains("help:merge", Assert.Single(dialogs.Notices));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void MainWindowViewModel_PreviewRendersLiveWhileShown() {
		// nothing is rendered until the preview is shown; from then on it follows
		// every edit, and a sample that will not format keeps the raw text and says why
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		Assert.Equal("", vm.DefaultPreview.Text);

		vm.ShowDefaultPreview = true;
		Assert.Equal("Base", vm.DefaultPreview.Text);

		vm.Tree.SelectedKey!.DefaultValue = "Base {0:N1} {1}";
		Assert.Equal("Base 22.0 one", vm.DefaultPreview.Text);
		Assert.Empty(vm.DefaultPreview.Gripes);

		//the parameter dialog closes: the samples are re-applied
		dialogs.OnShow = _ => vm.Tree.SelectedKey.Parameters[0].Value = "twenty-two";
		vm.TestParametersCommand.Execute(vm.Tree.SelectedKey.Parameters);
		Assert.Equal("Base {0:N1} {1}", vm.DefaultPreview.Text);
		Assert.NotEmpty(vm.DefaultPreview.Gripes);

		vm.Tree.SelectedKeyNode = Node(vm, "Example.main.single-line");
		Assert.Equal("line 1 still line 1", vm.DefaultPreview.Text);
		Assert.Empty(vm.DefaultPreview.Gripes);
	}

	[Fact]
	public void MainWindowViewModel_TranslationPreviewFormatsInTheLanguagesCulture() {
		var vm = NewVm();
		vm.LoadFile(new StringReader(@"
value-en=English
value-de=Deutsch

[k]
value=n={0:N1}
param-0=Double:22.5
value-de=n={0:N1}
"), "T");
		vm.ShowDefaultPreview = true;
		vm.ShowLocalizationPreview = true;
		vm.Tree.SelectedKeyNode = Node(vm, "T.k");

		Assert.Equal("n=22.5", vm.DefaultPreview.Text);
		Assert.Equal("n=22.5", vm.TranslationPreview.Text); //en

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de");
		Assert.Equal("n=22,5", vm.TranslationPreview.Text);

		vm.Tree.SelectedEntry!.Value = "N={0:N2}";
		Assert.Equal("N=22,50", vm.TranslationPreview.Text);
	}

	[Fact]
	public void MainWindowViewModel_PreviewResolvesReferencesAcrossFiles() {
		// SPEC (baseline pane): {>reference} works across files, simulating a host
		// app loading several dictionaries
		var vm = NewVm();
		vm.LoadFile(new StringReader("value-en=English\n\n[a]\nvalue=hello\n"), "A");
		vm.LoadFile(new StringReader("value-en=English\n\n[b]\nvalue={>a} world\n"), "B");
		vm.ShowDefaultPreview = true;

		vm.Tree.SelectedKeyNode = Node(vm, "B.b");

		Assert.Equal("hello world", vm.DefaultPreview.Text);
	}

	[Fact]
	public void MainWindowViewModel_FollowLinkAsksBeforeTheShellAndReportsTheRest() {
		var dialogs = new FakeDialogs { ConfirmAnswer = false };
		var vm = new MainWindowViewModel(dialogs);

		vm.FollowLink(new Uri("https://example.com/page"));
		Assert.Contains("https://example.com/page", Assert.Single(dialogs.Confirmations));
		Assert.Empty(dialogs.Notices);

		vm.FollowLink(new Uri("appcmd:do-something"));
		Assert.Contains("appcmd:do-something", Assert.Single(dialogs.Notices));
	}

	[Fact]
	public void MainWindowViewModel_PreviewGripesCollectWhatWordsComplainsAbout() {
		// SPEC (Markdown previews → Gripes): a circular or missing reference and a
		// sample that will not format are listed per pane, opened in a dialog from
		// the tool button, and cleared when the preview hides
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.main.circle-1");
		Assert.Empty(vm.DefaultPreview.Gripes);
		Assert.False(vm.ShowGripesCommand.CanExecute(vm.DefaultPreview));

		vm.ShowDefaultPreview = true;
		Assert.Contains("# ∞ #", vm.DefaultPreview.Text);
		Assert.Contains(vm.DefaultPreview.Gripes, gripe => gripe.StartsWith("WORDS:CIRC"));
		Assert.Equal(vm.DefaultPreview.Gripes.Count, vm.DefaultPreview.GripeCount);

		vm.Tree.SelectedKey!.DefaultValue = "see {>nowhere}";
		Assert.Equal("see #nowhere#", vm.DefaultPreview.Text);
		Assert.Contains(vm.DefaultPreview.Gripes, gripe => gripe.Contains("WORDS:KEY") && gripe.Contains("nowhere"));
		Assert.DoesNotContain(vm.DefaultPreview.Gripes, gripe => gripe.StartsWith("WORDS:CIRC")); //each render starts afresh

		Assert.True(vm.ShowGripesCommand.CanExecute(vm.DefaultPreview));
		vm.ShowGripesCommand.Execute(vm.DefaultPreview);
		var shown = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
		Assert.Contains("nowhere", shown.Text);

		//a sample that will not format heads the list and the text stays raw
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		vm.Tree.SelectedKey!.Parameters[0].Value = "twenty-two";
		vm.Tree.SelectedKey.DefaultValue = "Base {0:N1}";
		Assert.Equal("Base {0:N1}", vm.DefaultPreview.Text);
		Assert.Contains("twenty-two", vm.DefaultPreview.Gripes[0]);

		//the translation pane keeps a list of its own
		vm.ShowLocalizationPreview = true;
		vm.Tree.SelectedEntry!.Value = "Basis {0:N1}";
		Assert.Equal("Basis {0:N1}", vm.TranslationPreview.Text);
		Assert.Contains("twenty-two", vm.TranslationPreview.Gripes[0]);

		//a hidden preview holds nothing
		vm.ShowDefaultPreview = false;
		Assert.Equal("", vm.DefaultPreview.Text);
		Assert.Empty(vm.DefaultPreview.Gripes);
		Assert.NotEmpty(vm.TranslationPreview.Gripes);
	}

	[Fact]
	public void MainWindowViewModel_IdempotencyTest_Data() {
		var vm1 = LoadExample();
		var vm2 = NewVm();
		vm2.LoadFile(new StringReader(Save(vm1, "Example")), "Example");

		var keys1 = vm1.Session.Keys.Values.ToList();
		var keys2 = vm2.Session.Keys.Values.ToList();
		Assert.Equal(keys1.Count, keys2.Count);
		Assert.Equal(vm1.Tree.KnownLanguages.Count, vm2.Tree.KnownLanguages.Count);
		foreach (var (key1, key2) in keys1.Zip(keys2)) {
			Assert.Equal(key1.BlockKey, key2.BlockKey);
			Assert.Equal(key1.Comment, key2.Comment);
			Assert.Equal(key1.Context, key2.Context);
			Assert.Equal(key1.DefaultValue, key2.DefaultValue);
			Assert.Equal(key1.IsConstant, key2.IsConstant);
			Assert.Equal(key1.NeedsReview, key2.NeedsReview);
			Assert.Equal(key1.Entries.Keys, key2.Entries.Keys);
			foreach (var (parameter1, parameter2) in key1.Parameters.Zip(key2.Parameters)) {
				Assert.Equal(parameter1.Key, parameter2.Key);
				Assert.Equal(parameter1.Value, parameter2.Value);
				Assert.Equal(parameter1.DataType, parameter2.DataType);
			}
			foreach (string languageCode in key1.Entries.Keys) {
				WordsEntry entry1 = key1.Entries[languageCode];
				WordsEntry entry2 = key2.Entries[languageCode];
				Assert.Equal(entry1.Value, entry2.Value);
				Assert.Equal(entry1.Stale, entry2.Stale);
				Assert.Equal(entry1.Context, entry2.Context);
				Assert.Equal(entry1.Comment, entry2.Comment);
			}
		}
		foreach (var (language1, language2) in vm1.Tree.KnownLanguages.Zip(vm2.Tree.KnownLanguages)) {
			Assert.Equal(language1.Code, language2.Code);
			Assert.Equal(language1.NativeName, language2.NativeName);
			Assert.Equal(language1.EnglishName, language2.EnglishName);
		}
	}

	[Fact]
	public void MainWindowViewModel_ToggleStaleTest() {
		// the badge is per selected language (SPEC: badges), so toggling en-CA
		// shows on the node while en-CA is the language shown
		var vm = LoadExample();
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "en-CA");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");

		vm.ToggleStaleLanguageCommand.Execute("en-CA");

		Assert.NotNull(vm.Session.Keys["Example.view.section-name.key"].Entries["en-CA"].Stale);
		Assert.True(vm.Tree.SelectedKeyNode?.IsStale);
		Assert.True(vm.IsDirty);

		vm.ToggleStaleLanguageCommand.Execute("en-CA");
		Assert.Null(vm.Session.Keys["Example.view.section-name.key"].Entries["en-CA"].Stale);
		Assert.False(vm.Tree.SelectedKeyNode?.IsStale);
	}

	[Fact]
	public void MainWindowViewModel_ToggleConstantTest() {
		var vm = NewVm();
		vm.Session.AddKey("fullLabel");
		vm.Tree.KeyNodes.Add(new KeyNode("label", "fullLabel"));
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];

		vm.ToggleLocalizationKeyIsConstantCommand.Execute(null);

		Assert.True(vm.Tree.SelectedKey?.IsConstant);
		Assert.True(vm.Tree.SelectedKeyNode?.IsConstant);
		Assert.Null(vm.Tree.SelectedEntry);
	}

	[Fact]
	public void MainWindowViewModel_ToggleConstantMarksOnlyLastSegment() {
		// the $ marker belongs on the last segment alone:
		// Example.view.section-name.key <-> Example.view.section-name.$key
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");

		vm.ToggleLocalizationKeyIsConstantCommand.Execute(null);

		Assert.Equal("Example.view.section-name.$key", vm.Tree.SelectedKey?.BlockKey);
		Assert.Equal("Example.view.section-name.$key", vm.Tree.SelectedKeyNode.FullLabel);
		Assert.Equal("Example.view.section-name.$key.tooltip", vm.Tree.SelectedKeyNode.Children[0].FullLabel); //descendants follow
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.$key"));
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.$key.tooltip"));
		Assert.False(vm.Session.Keys.ContainsKey("Example.view.section-name.key"));

		vm.ToggleLocalizationKeyIsConstantCommand.Execute(null);

		Assert.Equal("Example.view.section-name.key", vm.Tree.SelectedKey?.BlockKey);
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.key"));
	}

	[Fact]
	public void MainWindowViewModel_ToggleConstantAsksBeforeDiscardingTranslations() {
		// SPEC: constants carry no translations — they are removed, after asking
		var dialogs = new FakeDialogs { ConfirmAnswer = false };
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");

		vm.ToggleLocalizationKeyIsConstantCommand.Execute(null);

		Assert.Single(dialogs.Confirmations);
		Assert.False(vm.Tree.SelectedKey!.IsConstant);
		Assert.Equal("Base", vm.Tree.SelectedKey.Entries["en"].Value);

		dialogs.ConfirmAnswer = true;
		vm.ToggleLocalizationKeyIsConstantCommand.Execute(null);

		Assert.True(vm.Tree.SelectedKey.IsConstant);
		Assert.All(vm.Tree.SelectedKey.Entries.Values, entry => Assert.True(entry.IsEmpty()));
	}

	[Fact]
	public void MainWindowViewModel_RemoveKeyAndNodeLeavesSimilarSiblings() {
		// removing `view` must not catch `viewer`, and every removed
		// descendant must leave the store too
		var vm = NewVm();
		vm.LoadFile(new StringReader(@"
value-en=English

[view]
value=v
[view.a]
value=va
[viewer]
value=w
"), "T");
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0].Children[0]; //T.view

		vm.RemoveLocalizationKeyAndNodeCommand.Execute(null);

		Assert.False(vm.Session.Keys.ContainsKey("T.view"));
		Assert.False(vm.Session.Keys.ContainsKey("T.view.a"));
		Assert.True(vm.Session.Keys.ContainsKey("T.viewer"));
		Assert.Equal(["viewer"], vm.Tree.KeyNodes[0].Children.Select(n => n.Label));
		Assert.Same(vm.Tree.KeyNodes[0], vm.Tree.SelectedKeyNode);
	}

	[Fact]
	public void MainWindowViewModel_SaveWritesFilesLoadedByPath() {
		// the session keys files by path; Save finds each file's node by label
		// and writes back canonically
		var path = Path.Combine(Path.GetTempPath(), $"WordsEditSaveTest-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		var filePath = Path.Combine(path, "Example.ini");
		try {
			using (var resource = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini")) {
				File.WriteAllText(filePath, resource.ReadToEnd());
			}
			var vm = NewVm();
			vm.LoadFile(filePath);
			vm.IsDirty = true;

			vm.Save();

			using var reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini");
			Assert.Equal(reader.ReadToEnd(), File.ReadAllText(filePath));
			Assert.False(vm.IsDirty);
		}
		finally {
			Directory.Delete(path, recursive: true);
		}
	}

	[Fact]
	public void MainWindowViewModel_ReloadReplacesTheNodeInPlace() {
		var path = Path.Combine(Path.GetTempPath(), $"WordsEditReload-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		var filePath = Path.Combine(path, "A.ini");
		try {
			File.WriteAllText(filePath, "value-en=English\n\n[one]\nvalue=1\n\n[two]\nvalue=2\n");
			var vm = NewVm();
			vm.LoadFile(filePath);
			vm.LoadFile(new StringReader("value-en=English\n\n[b]\nvalue=B\n"), "B");
			vm.Tree.SelectedKeyNode = Node(vm, "A.two");

			File.WriteAllText(filePath, "value-en=English\n\n[one]\nvalue=uno\n");
			vm.LoadFile(filePath);

			Assert.Equal(["A", "B"], vm.Tree.KeyNodes.Select(n => n.FullLabel));
			Assert.Equal(["one"], vm.Tree.KeyNodes[0].Children.Select(n => n.Label)); //the key deleted on disk is gone
			Assert.False(vm.Session.Keys.ContainsKey("A.two"));
			Assert.Equal("uno", vm.Session.Keys["A.one"].DefaultValue);
			Assert.Null(vm.Tree.SelectedKeyNode); //it pointed into the replaced tree
		}
		finally {
			Directory.Delete(path, recursive: true);
		}
	}

	[Fact]
	public void MainWindowViewModel_OrganizerNodesPresentTheComments() {
		// preamble pins to the file's start, trailer to its end, and a banner
		// shows as an organizer node in front of the key it precedes
		var vm = LoadExample();
		KeyNode file = vm.Tree.KeyNodes[0];

		var preamble = Assert.IsType<OrganizerNode>(file.Children[0]);
		Assert.Equal(" ExampleFile preamble — kept above the language labels", preamble.Text);
		var trailer = Assert.IsType<CommentNode>(file.Children[^1]);
		Assert.Equal(" trailer — comments after the last block close the file", trailer.Text);

		KeyNode main = file.Children.First(n => n.Label == "main");
		int titleIndex = main.Children.IndexOf(main.Children.First(n => n.Label == "title"));
		var banner = Assert.IsType<CommentNode>(main.Children[titleIndex - 1]);
		Assert.Equal(" a banner: freeform comments above a header ride the block", banner.Text);
		Assert.StartsWith("a banner:", banner.Caption);
		Assert.All(GetAllKeyNodes(vm.Tree.KeyNodes).Where(n => n != file), n => Assert.NotNull(n.Parent));
	}

	[Fact]
	public void MainWindowViewModel_OrganizerEditsFlowToTheDocument() {
		var vm = LoadExample();
		KeyNode file = vm.Tree.KeyNodes[0];
		KeyNode main = file.Children.First(n => n.Label == "main");
		var banner = main.Children.OfType<OrganizerNode>().First();

		vm.Tree.SelectedKeyNode = banner;
		Assert.Same(banner, vm.Tree.SelectedOrganizer);
		Assert.Null(vm.Tree.SelectedKey);

		banner.Text = " rewritten";
		Assert.True(vm.IsDirty);
		string saved = Save(vm, "Example");
		Assert.Contains("; rewritten", saved);
		Assert.DoesNotContain("; a banner:", saved);

		var preamble = (OrganizerNode)file.Children[0];
		preamble.Text = " new preamble";
		Assert.Equal(" new preamble", vm.Session.FileOf("Example")!.Preamble);
		Assert.StartsWith("; new preamble", Save(vm, "Example"));
	}

	[Fact]
	public void MainWindowViewModel_RemovingAnOrganizerDeletesTheComment() {
		var vm = LoadExample();
		KeyNode main = vm.Tree.KeyNodes[0].Children.First(n => n.Label == "main");
		var banner = main.Children.OfType<OrganizerNode>().First();

		vm.Tree.SelectedKeyNode = banner;
		vm.RemoveLocalizationKeyAndNodeCommand.Execute(null);

		Assert.DoesNotContain(banner, main.Children);
		Assert.True(vm.Session.Keys.ContainsKey("Example.main.title"));
		Assert.DoesNotContain("; a banner:", Save(vm, "Example"));
	}

	[Fact]
	public void MainWindowViewModel_RemovingAKeyLeavesTheCommentStanding() {
		// the organizer is standalone: deleting the key beneath it leaves the
		// comment in place, riding above whatever block comes next
		var vm = LoadExample();
		KeyNode main = vm.Tree.KeyNodes[0].Children.First(n => n.Label == "main");
		KeyNode title = main.Children.First(n => n.Label == "title");
		var banner = main.Children.OfType<OrganizerNode>().First();

		vm.Tree.SelectedKeyNode = title;
		vm.RemoveLocalizationKeyAndNodeCommand.Execute(null);

		Assert.DoesNotContain(title, main.Children);
		Assert.Contains(banner, main.Children);

		var output = Save(vm, "Example");
		Assert.True(output.IndexOf("; a banner:") < output.IndexOf("[.circle-1]"));
		Assert.DoesNotContain("[.title]", output);
	}

	[Fact]
	public void MainWindowViewModel_AddOrganizerInsertsAheadOfTheKey() {
		var vm = LoadExample();
		KeyNode main = vm.Tree.KeyNodes[0].Children.First(n => n.Label == "main");
		KeyNode circle = main.Children.First(n => n.Label == "circle-1");
		vm.Tree.SelectedKeyNode = circle;

		vm.AddOrganizerCommand.Execute(null);

		var organizer = Assert.IsType<CommentNode>(vm.Tree.SelectedKeyNode);
		Assert.Same(organizer, main.Children[main.Children.IndexOf(circle) - 1]);
		Assert.Same(main, organizer.Parent);
		organizer.Text = " note to self";

		// running the command again on the key reuses the existing organizer
		vm.Tree.SelectedKeyNode = circle;
		vm.AddOrganizerCommand.Execute(null);
		Assert.Same(organizer, vm.Tree.SelectedKeyNode);

		Assert.Contains("; note to self", Save(vm, "Example"));
	}

	[Fact]
	public void MainWindowViewModel_FilesKeepTheirOwnLanguageTables() {
		// the session dropdown is the union, but each file writes back only the
		// languages it declared — a main file never absorbs a library's extras
		var vm = NewVm();
		vm.LoadFile(new StringReader(@"value-en=English

[a]
value=A
"), "Main");
		vm.LoadFile(new StringReader(@"value-en=English
value-fr=Français

[b]
value=B
value-fr=Bé
"), "Extra");

		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "fr");
		Assert.Equal(["en"], vm.Session.FileOf("Main")!.Languages);
		Assert.Equal(["en", "fr"], vm.Session.FileOf("Extra")!.Languages);

		var mainOutput = Save(vm, "Main");
		Assert.Contains("value-en=English", mainOutput);
		Assert.DoesNotContain("value-fr=Français", mainOutput);
	}

	[Fact]
	public void MainWindowViewModel_EditLanguageRecodeShiftsEntries() {
		// re-coding a language onto an existing one shifts the entries: the
		// target's values win, displaced source values park in context, and
		// the two dropdown entries collapse into one
		var vm = NewVm();
		vm.LoadFile(new StringReader(@"value-en=English
value-en-GB=British

[k]
value=x
value-en=family
value-en-GB=regional

[j]
value=y
value-en-GB=only regional
"), "Main");
		LanguageManagerViewModel manager = new(vm) {
			SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "en-GB"),
		};

		manager.EditLanguage(new LanguageEntry("en", "English"));

		Assert.DoesNotContain(manager.KnownLanguages, l => l.Code == "en-GB");
		Assert.Equal(1, manager.KnownLanguages.Count(l => l.Code == "en"));
		Assert.Equal("en", manager.SelectedLanguage.Code);
		Assert.Equal(["en"], vm.Session.FileOf("Main")!.Languages);

		WordsKey collided = vm.Session.Keys["Main.k"];
		Assert.Equal("family", collided.Entries["en"].Value);
		Assert.Equal("regional", collided.Entries["en"].Context);
		Assert.NotNull(collided.Entries["en"].Stale);
		Assert.False(collided.Entries.ContainsKey("en-GB"));

		WordsKey moved = vm.Session.Keys["Main.j"];
		Assert.Equal("only regional", moved.Entries["en"].Value);
		Assert.Null(moved.Entries["en"].Stale);
		Assert.False(moved.Entries.ContainsKey("en-GB"));
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void MainWindowViewModel_LibraryFileKeepsItsBangLabels() {
		// a library file (only !Labels) is flagged, still populates the language
		// list when opened solo, and writes its own !Labels back
		var vm = NewVm();
		vm.LoadFile(new StringReader(@"value-en=!English
value-eo=!Esperanto

[k]
value=x
"), "Lib");

		Assert.True(vm.Tree.KeyNodes[0].IsLibraryFile);
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == "eo");

		string output = Save(vm, "Lib");
		Assert.Contains("value-en=!English", output);
		Assert.Contains("value-eo=!Esperanto", output);
	}

	[Fact]
	public void MainWindowViewModel_TreeIsVisibleAfterLoad() {
		// the tree styles Visibility from IsVisible; a fresh load with no
		// filters must show every node
		var vm = LoadExample();

		Assert.All(GetAllKeyNodes(vm.Tree.KeyNodes), node => Assert.True(node.IsVisible));
	}

	[Fact]
	public void MainWindowViewModel_CanBeConstantTest() {
		// only a leaf directly under a file may become a constant
		var vm = LoadExample();

		Assert.All(GetAllKeyNodes(vm.Tree.KeyNodes).Where(node => node is not OrganizerNode), node =>
			Assert.Equal(node.Parent is { IsFile: true } && node.Children.Count == 0, node.CanBeConstant));
		Assert.True(Node(vm, "Example.$rsi-unit").CanBeConstant);
		Assert.False(Node(vm, "Example.view").CanBeConstant);
		Assert.False(Node(vm, "Example.view.section-name.key").CanBeConstant);
	}

	[Fact]
	public void MainWindowViewModel_StaleAllLanguagesTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");

		vm.StaleAllLanguagesCommand.Execute(null);

		Assert.NotNull(vm.Tree.SelectedKey);
		Assert.All(vm.Tree.SelectedKey.Entries.Values, entry => Assert.NotNull(entry.Stale));
		Assert.True(vm.Tree.SelectedKeyNode.IsStale);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void MainWindowViewModel_ResetCoreTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name");
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages[1];

		vm.ResetCore();

		Assert.Null(vm.Tree.SelectedKeyNode);
		Assert.Null(vm.Tree.SelectedKey);
		Assert.Null(vm.Tree.SelectedEntry);
		Assert.Single(vm.Tree.KnownLanguages);
		Assert.Empty(vm.Session.Keys);
		Assert.Empty(vm.Session.Files);
		Assert.Empty(vm.Tree.KeyNodes);
		Assert.Equal("", vm.Tree.SearchFilterText);
		Assert.False(vm.Tree.IsStaleFilter);
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code);
	}

	[Fact]
	public void MainWindowViewModel_RemoveLocalizationKeyTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		WordsKey? selectedKey = vm.Tree.SelectedKey;
		Assert.NotNull(selectedKey);

		vm.RemoveLocalizationKeyCommand.Execute(null);

		Assert.Null(vm.Tree.SelectedKey);
		Assert.Null(vm.Tree.SelectedEntry);
		Assert.DoesNotContain(selectedKey, vm.Session.Keys.Values);
		Assert.False(vm.Session.Keys.ContainsKey("Example.view.section-name.key"));
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.key.tooltip")); //descendants stay
		Assert.False(vm.Tree.SelectedKeyNode!.IsStale); //the node still stands, badgeless
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void MainWindowViewModel_RemoveLocalizationKeyAndNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		string blockKey = vm.Tree.SelectedKey?.BlockKey ?? throw new InvalidOperationException();

		vm.RemoveLocalizationKeyAndNodeCommand.Execute(null);

		Assert.DoesNotContain(vm.Session.Keys.Keys, k => k.StartsWith(blockKey));
		Assert.DoesNotContain(GetAllKeyNodes(vm.Tree.KeyNodes), k => k.FullLabel.StartsWith(blockKey));
	}

	[Fact]
	public void MainWindowViewModel_RenameLocalizationKeyNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		string blockKey = vm.Tree.SelectedKey?.BlockKey ?? throw new InvalidOperationException();

		vm.RenameLocalizationKeyAndNode("test");

		Assert.Equal("test", vm.Tree.SelectedKeyNode.Label);
		Assert.Equal("Example.view.section-name.test", vm.Tree.SelectedKeyNode.FullLabel);
		Assert.Equal("Example.view.section-name.test", vm.Tree.SelectedKey.BlockKey);
		Assert.Equal("Example.view.section-name.test.tooltip", vm.Tree.SelectedKeyNode.Children[0].FullLabel);
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.test.tooltip"));
		Assert.DoesNotContain(vm.Session.Keys.Keys, k => k.StartsWith(blockKey));
		Assert.DoesNotContain(GetAllKeyNodes(vm.Tree.KeyNodes), k => k.FullLabel.StartsWith(blockKey));
	}

	[Fact]
	public void MainWindowViewModel_RenameOntoASiblingIsRefused() {
		// nothing is overwritten to make room: the rename is reported and refused
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.enum.none");

		vm.RenameLocalizationKeyAndNode("two");

		Assert.Single(dialogs.Notices);
		Assert.Equal("Example.enum.none", vm.Tree.SelectedKeyNode.FullLabel);
		Assert.True(vm.Session.Keys.ContainsKey("Example.enum.none"));
		Assert.Equal("Two Selection", vm.Session.Keys["Example.enum.two"].DefaultValue);
	}

	[Fact]
	public void MainWindowViewModel_AddLocalizationKeyTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view"); //a group: it has no key yet

		vm.AddLocalizationKeyCommand.Execute(null);

		WordsKey newKey = vm.Session.Keys["Example.view"];
		Assert.Same(newKey, vm.Tree.SelectedKey);
		Assert.Same(newKey.Entries[vm.Tree.SelectedLanguage.Code], vm.Tree.SelectedEntry);
		Assert.All(vm.Tree.KnownLanguages, language => Assert.True(newKey.Entries.ContainsKey(language.Code)));
		Assert.True(vm.Tree.SelectedKeyNode.EmptyValue);
		Assert.True(vm.IsDirty);

		//SPEC (The tree): a key can exist on any node except a file
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];
		vm.AddLocalizationKeyCommand.Execute(null);
		Assert.False(vm.Session.Keys.ContainsKey("Example"));
		Assert.Null(vm.Tree.SelectedKey);
	}

	[Fact]
	public void MainWindowViewModel_AddLocalizationKeyNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view");

		vm.AddLocalizationKeyNode("test");

		KeyNode view = Node(vm, "Example.view");
		KeyNode newKeyNode = view.Children[1]; //Should be added, FullLabel = Example.view.test
		Assert.Equal("test", newKeyNode.Label);
		Assert.Equal("Example.view.test", newKeyNode.FullLabel);
		Assert.Same(view, newKeyNode.Parent);
		Assert.False(newKeyNode.CanBeConstant);
		Assert.True(newKeyNode.IsSelected);
		Assert.False(view.IsSelected);
		Assert.True(view.IsExpanded);
		Assert.Same(newKeyNode, vm.Tree.SelectedKeyNode);
		Assert.Null(vm.Tree.SelectedKey);
		Assert.Null(vm.Tree.SelectedEntry);

		//a leaf added under the file may become a constant; its parent no longer can
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];
		vm.AddLocalizationKeyNode("leaf");
		Assert.True(vm.Tree.SelectedKeyNode.CanBeConstant);
	}

	[Fact]
	public void MainWindowViewModel_MergeTest() {
		var vm = LoadExample();
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile.ini"), "MergeTestFile");
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile2.ini"), "MergeTestFile2");
		var sources = new Dictionary<string, string> {
			["en"] = "Example",
			["zh"] = "MergeTestFile",
			["en-CA"] = "MergeTestFile2",
		};

		var rewrite = WordsOperations.Merge(vm.Session.Keys, "Example", sources, "Example", out var conflicts);
		var newFile = WordsOperations.Merge(vm.Session.Keys, "Example", sources, "MergedFile", out _);

		Assert.Empty(conflicts);
		Assert.NotNull(rewrite);
		Assert.NotNull(newFile);
		AssertMerged(rewrite, "Example");
		AssertMerged(newFile, "MergedFile");

		//each language's fields come from the file mapped to it
		static void AssertMerged(Dictionary<string, WordsKey> merged, string prefix) {
			foreach (string suffix in new[] { "view.section-name.key", "view.section-name.key.tooltip" }) {
				WordsKey key = merged[$"{prefix}.{suffix}"];
				Assert.Equal("Base", key.DefaultValue);
				Assert.Equal("Base", key.Context);
				Assert.Equal("Base", key.Comment);
				foreach (var (code, expected) in new[] { ("en", "Base"), ("zh", "2"), ("en-CA", "3") }) {
					Assert.Equal(expected, key.Entries[code].Value);
					Assert.Equal(expected, key.Entries[code].Context);
					Assert.Equal(expected, key.Entries[code].Comment);
				}
			}
		}
	}

	[Fact]
	public void MainWindowViewModel_FilesTest() {
		var vm = LoadExample();
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile.ini"), "MergeTestFile");
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile2.ini"), "MergeTestFile2");

		Assert.Equal(["Example", "MergeTestFile", "MergeTestFile2"], vm.Session.Files.Select(f => f.Path));
		Assert.Equal(["Example", "MergeTestFile", "MergeTestFile2"], vm.Tree.KeyNodes.Select(n => n.FullLabel));
	}

	[Fact]
	public void MainWindowViewModel_RemoveFileNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];

		vm.RemoveFileNodeCore(vm.Tree.SelectedKeyNode);

		Assert.Null(vm.Tree.SelectedKeyNode);
		Assert.Null(vm.Tree.SelectedKey);
		Assert.Null(vm.Tree.SelectedEntry);
		Assert.Empty(vm.Tree.KeyNodes);
		Assert.Empty(vm.Session.Files);
		Assert.Empty(vm.Session.Keys);
		Assert.Equal("en", vm.Tree.SelectedLanguage.Code); //the pruned dropdown still has a selection
	}

	[Fact]
	public void MainWindowViewModel_RemoveFileNodeWithTwoFilesKeepsTheSurvivor() {
		var vm = LoadExample();
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.MergeTestFile.ini"), "MergeTestFile");
		int survivorKeys = vm.Session.KeysOf(vm.Session.FileOf("MergeTestFile")!).Count();
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];

		vm.RemoveFileNodeCore(vm.Tree.KeyNodes[0]);

		Assert.Equal(["MergeTestFile"], vm.Tree.KeyNodes.Select(n => n.FullLabel));
		Assert.Same(vm.Tree.KeyNodes[0], vm.Tree.SelectedKeyNode);
		Assert.Equal(survivorKeys, vm.Session.Keys.Count);
		Assert.Contains(vm.Tree.KnownLanguages, l => l.Code == vm.Tree.SelectedLanguage.Code);
	}

	[Fact]
	public void MainWindowViewModel_StaleViewTest() {
		var vm = LoadExample();
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages[4]; //zh - Chinese (Simplified)

		vm.Tree.IsStaleFilter = true;

		Assert.True(vm.Tree.KeyNodes[0].IsVisible); //Example
		Assert.True(Node(vm, "Example.view").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name.key").IsVisible);
		Assert.False(Node(vm, "Example.view.section-name.key.tooltip").IsVisible);
		Assert.False(Node(vm, "Example.$rsi-unit").IsVisible);
		Assert.True(Node(vm, "Example.main").IsVisible);
		Assert.False(Node(vm, "Example.format").IsVisible);
		Assert.False(Node(vm, "Example.enum").IsVisible);
	}

	[Fact]
	public void MainWindowViewModel_SearchTest() {
		var vm = LoadExample();
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages[4]; //zh - Chinese (Simplified)

		vm.Tree.SearchFilterText = "tooltip";

		Assert.True(vm.Tree.KeyNodes[0].IsVisible); //Example
		Assert.True(Node(vm, "Example.view").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name.key").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name.key.tooltip").IsVisible);
		Assert.False(Node(vm, "Example.$rsi-unit").IsVisible);
		Assert.False(Node(vm, "Example.main").IsVisible);
		Assert.False(Node(vm, "Example.format").IsVisible);
		Assert.True(Node(vm, "Example.enum").IsVisible);
		Assert.False(Node(vm, "Example.enum.none").IsVisible);
		Assert.True(Node(vm, "Example.enum.two").IsVisible);
		Assert.True(Node(vm, "Example.enum.two.tooltip").IsVisible);
		Assert.False(Node(vm, "Example.enum.two.desc").IsVisible);
	}

	[Fact]
	public void MainWindowViewModel_StaleAndSearchTest() {
		var vm = LoadExample();
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages[4]; //zh - Chinese (Simplified)

		vm.Tree.IsStaleFilter = true;
		vm.Tree.SearchFilterText = "tooltip";

		Assert.False(vm.Tree.KeyNodes[0].IsVisible); //Example
	}

	[Fact]
	public void MainWindowViewModel_BadgesFollowTheSelectedLanguage() {
		// SPEC (translation pane): changing the dropdown re-contextualizes the
		// tree — stale and empty-value emphasis refresh to the new language
		var vm = LoadExample();
		KeyNode key = Node(vm, "Example.view.section-name.key");
		KeyNode tooltip = Node(vm, "Example.view.section-name.key.tooltip");
		KeyNode title = Node(vm, "Example.main.title");

		Assert.False(key.IsStale); //stale-zh only
		Assert.True(key.IsOverwritten); //en-CA overrides en
		Assert.False(title.EmptyValue); //value-en present

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "zh");

		Assert.True(key.IsStale);
		Assert.False(key.IsOverwritten); //no zh-HK value on the key itself…
		Assert.True(tooltip.IsOverwritten); //…but its tooltip has one
		Assert.True(title.EmptyValue); //no value-zh
	}
}
