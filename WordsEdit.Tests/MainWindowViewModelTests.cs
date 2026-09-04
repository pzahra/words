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
		vm.TestParametersCommand.Execute(vm.Tree.SelectedKey);
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

		vm.ToggleConstantCommand.Execute(null);

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

		vm.ToggleConstantCommand.Execute(null);

		Assert.Equal("Example.view.section-name.$key", vm.Tree.SelectedKey?.BlockKey);
		Assert.Equal("Example.view.section-name.$key", vm.Tree.SelectedKeyNode.FullLabel);
		Assert.Equal("Example.view.section-name.$key.tooltip", vm.Tree.SelectedKeyNode.Children[0].FullLabel); //descendants follow
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.$key"));
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.$key.tooltip"));
		Assert.False(vm.Session.Keys.ContainsKey("Example.view.section-name.key"));

		vm.ToggleConstantCommand.Execute(null);

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

		vm.ToggleConstantCommand.Execute(null);

		Assert.Single(dialogs.Confirmations);
		Assert.False(vm.Tree.SelectedKey!.IsConstant);
		Assert.Equal("Base", vm.Tree.SelectedKey.Entries["en"].Value);

		dialogs.ConfirmAnswer = true;
		vm.ToggleConstantCommand.Execute(null);

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

		vm.RemoveNodeCommand.Execute(null);

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
		vm.RemoveNodeCommand.Execute(null);

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
		vm.RemoveNodeCommand.Execute(null);

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
	public void MainWindowViewModel_RemoveKeyTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		WordsKey? selectedKey = vm.Tree.SelectedKey;
		Assert.NotNull(selectedKey);

		vm.RemoveKeyCommand.Execute(null);

		Assert.Null(vm.Tree.SelectedKey);
		Assert.Null(vm.Tree.SelectedEntry);
		Assert.DoesNotContain(selectedKey, vm.Session.Keys.Values);
		Assert.False(vm.Session.Keys.ContainsKey("Example.view.section-name.key"));
		Assert.True(vm.Session.Keys.ContainsKey("Example.view.section-name.key.tooltip")); //descendants stay
		Assert.False(vm.Tree.SelectedKeyNode!.IsStale); //the node still stands, badgeless
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void MainWindowViewModel_RemoveNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		string blockKey = vm.Tree.SelectedKey?.BlockKey ?? throw new InvalidOperationException();

		vm.RemoveNodeCommand.Execute(null);

		Assert.DoesNotContain(vm.Session.Keys.Keys, k => k.StartsWith(blockKey));
		Assert.DoesNotContain(GetAllKeyNodes(vm.Tree.KeyNodes), k => k.FullLabel.StartsWith(blockKey));
	}

	[Fact]
	public void MainWindowViewModel_RenameNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		string blockKey = vm.Tree.SelectedKey?.BlockKey ?? throw new InvalidOperationException();

		vm.RenameNode("test");

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

		vm.RenameNode("two");

		Assert.Single(dialogs.Notices);
		Assert.Equal("Example.enum.none", vm.Tree.SelectedKeyNode.FullLabel);
		Assert.True(vm.Session.Keys.ContainsKey("Example.enum.none"));
		Assert.Equal("Two Selection", vm.Session.Keys["Example.enum.two"].DefaultValue);
	}

	[Fact]
	public void MainWindowViewModel_AddKeyTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view"); //a group: it has no key yet

		vm.AddKeyCommand.Execute(null);

		WordsKey newKey = vm.Session.Keys["Example.view"];
		Assert.Same(newKey, vm.Tree.SelectedKey);
		Assert.Same(newKey.Entries[vm.Tree.SelectedLanguage.Code], vm.Tree.SelectedEntry);
		Assert.All(vm.Tree.KnownLanguages, language => Assert.True(newKey.Entries.ContainsKey(language.Code)));
		Assert.True(vm.Tree.SelectedKeyNode.EmptyValue);
		Assert.True(vm.IsDirty);

		//SPEC (The tree): a key can exist on any node except a file
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes[0];
		vm.AddKeyCommand.Execute(null);
		Assert.False(vm.Session.Keys.ContainsKey("Example"));
		Assert.Null(vm.Tree.SelectedKey);
	}

	[Fact]
	public void MainWindowViewModel_AddNodeTest() {
		var vm = LoadExample();
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view");

		vm.AddNode("test");

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
		vm.AddNode("leaf");
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

		//stale means stale: a key merely without words is the missing filter's
		Assert.True(vm.Tree.KeyNodes[0].IsVisible); //Example
		Assert.True(Node(vm, "Example.view").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name").IsVisible);
		Assert.True(Node(vm, "Example.view.section-name.key").IsVisible);
		Assert.False(Node(vm, "Example.view.section-name.key.tooltip").IsVisible);
		Assert.False(Node(vm, "Example.$rsi-unit").IsVisible);
		Assert.False(Node(vm, "Example.main").IsVisible);
		Assert.False(Node(vm, "Example.format").IsVisible);
		Assert.False(Node(vm, "Example.enum").IsVisible);

		vm.Tree.IsStaleFilter = false;
		vm.Tree.MissingFilter = true;

		Assert.True(Node(vm, "Example.main").IsVisible);
		Assert.True(Node(vm, "Example.main.title").IsVisible); //no value-zh
		Assert.True(Node(vm, "Example.main.single-line").IsVisible); //no value-zh either
		Assert.False(Node(vm, "Example.view.section-name.key").IsVisible); //has value-zh
		Assert.False(Node(vm, "Example.$rsi-unit").IsVisible); //constants want no translation
	}

	[Fact]
	public void MainWindowViewModel_TitleNamesTheFilesAndStarsWhenDirty() {
		var vm = NewVm();
		Assert.Equal("Wordsmith Editor", vm.TitleMarked);

		vm.LoadFile(new StringReader("value-en=English\n\n[a]\nvalue=A\n"), "Main");
		vm.LoadFile(new StringReader("value-en=English\n\n[b]\nvalue=B\n"), "Lib");
		Assert.Equal("Wordsmith Editor — Main, Lib", vm.Title);
		vm.IsDirty = true;
		Assert.Equal("Wordsmith Editor — Main, Lib *", vm.TitleMarked);

		vm.RemoveFileNodeCore(vm.Tree.KeyNodes[0]);
		Assert.Equal("Wordsmith Editor — Lib", vm.Title);
		vm.ResetCore();
		Assert.Equal("Wordsmith Editor", vm.TitleMarked);
	}

	[Fact]
	public void MainWindowViewModel_SearchMatchesWordsAndNotesAndCountsTheHidden() {
		// what a translator searches for is rarely a key name
		var vm = LoadExample();
		Assert.False(vm.Tree.IsFiltering);
		Assert.Equal(0, vm.Tree.HiddenCount);

		vm.Tree.SearchFilterText = "Locale"; //main.title's default value; no name has it
		Assert.True(vm.Tree.IsFiltering);
		Assert.True(Node(vm, "Example.main.title").IsVisible);
		Assert.True(Node(vm, "Example.main").IsVisible); //the path stays readable
		Assert.False(Node(vm, "Example.enum").IsVisible);
		Assert.Equal(GetAllKeyNodes(vm.Tree.KeyNodes).Count(node => !node.IsVisible), vm.Tree.HiddenCount);
		Assert.True(vm.Tree.HiddenCount > 0);

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "zh");
		vm.Tree.SearchFilterText = "ZH:make"; //enum's words in the selected language
		Assert.True(Node(vm, "Example.enum").IsVisible);
		Assert.False(Node(vm, "Example.main.title").IsVisible);

		vm.Tree.SearchFilterText = "debug logs"; //main.title's comment
		Assert.True(Node(vm, "Example.main.title").IsVisible);

		vm.ClearFiltersCommand.Execute(null);
		Assert.False(vm.Tree.IsFiltering);
		Assert.Equal(0, vm.Tree.HiddenCount);
		Assert.All(GetAllKeyNodes(vm.Tree.KeyNodes), node => Assert.True(node.IsVisible));
	}

	[Fact]
	public void MainWindowViewModel_SelectionFollowsTheFilter() {
		var vm = LoadExample();
		KeyNode desc = Node(vm, "Example.enum.two.desc");
		vm.Tree.Select(desc);
		Assert.True(desc.IsSelected);
		Assert.Same(desc, vm.Tree.SelectedKeyNode);

		vm.Tree.SearchFilterText = "tooltip"; //desc hides; its parent stays for enum.two.tooltip
		Assert.False(desc.IsVisible);
		Assert.False(desc.IsSelected);
		Assert.Same(Node(vm, "Example.enum.two"), vm.Tree.SelectedKeyNode);
		Assert.True(vm.Tree.SelectedKeyNode!.IsSelected);

		vm.Tree.SearchFilterText = "nothing-like-this";
		Assert.Null(vm.Tree.SelectedKeyNode);
	}

	[Fact]
	public void MainWindowViewModel_LanguageDropdownIsFedByTheSelectedKeysFile() {
		// the dropdown offers what the selected key's file speaks: its table, the
		// codes found on its fields, and whatever is selected; the union otherwise
		var vm = NewVm();
		vm.LoadFile(new StringReader("value-en=English\nvalue-de=Deutsch\n\n[a]\nvalue=A\n"), "Main");
		vm.LoadFile(new StringReader("value-en=English\n\n[c]\nvalue=C\nvalue-fr=C en français\n"), "Lib");
		Assert.Equal(["en", "de", "fr"], vm.Tree.KnownLanguages.Select(l => l.Code));
		Assert.Equal(["en", "de", "fr"], vm.Tree.FileLanguages.Select(l => l.Code));

		vm.Tree.SelectedKeyNode = Node(vm, "Main.a");
		Assert.Equal(["en", "de"], vm.Tree.FileLanguages.Select(l => l.Code));
		vm.Tree.SelectedKeyNode = Node(vm, "Lib.c");
		Assert.Equal(["en", "fr"], vm.Tree.FileLanguages.Select(l => l.Code)); //fr through the stray field

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de");
		Assert.Equal(["en", "de", "fr"], vm.Tree.FileLanguages.Select(l => l.Code)); //the selection always shows
		vm.Tree.SelectedKeyNode = Node(vm, "Main.a");
		Assert.Equal(["en", "de"], vm.Tree.FileLanguages.Select(l => l.Code));
		vm.Tree.SelectedKeyNode = null;
		Assert.Equal(["en", "de", "fr"], vm.Tree.FileLanguages.Select(l => l.Code));
	}

	[Fact]
	public void MainWindowViewModel_TestParametersShowTheFormattedResult() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
		vm.Tree.SelectedKey!.DefaultValue = "Base {0:N1} {1}";
		vm.IsDirty = false;

		TestParametersViewModel? dialog = null;
		dialogs.OnShow = shown => {
			dialog = (TestParametersViewModel)shown;
			Assert.Equal("Base 22.0 one", dialog.Result);
			Assert.False(dialog.IsError);

			dialog.Parameters[0].Value = "twenty-two"; //no double: the result says why
			Assert.True(dialog.IsError);
			Assert.NotEqual("", dialog.Result);

			dialog.Parameters[0].Value = "7";
			Assert.Equal("Base 7.0 one", dialog.Result);
			Assert.False(dialog.IsError);
			dialog.CloseCommand.Execute(null);
		};
		vm.TestParametersCommand.Execute(vm.Tree.SelectedKey);
		Assert.NotNull(dialog);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void MainWindowViewModel_CommentsAreTheirOwnIdentity() {
		// a comment's label is a synthetic marker the writer ignores; after the
		// parent is renamed every comment under it carries the same one, and
		// nothing minds: the rows stay apart, search reads their text, and both
		// are written where they stand
		var vm = LoadExample();
		KeyNode main = vm.Tree.KeyNodes[0].Children.First(n => n.Label == "main");
		vm.Tree.SelectedKeyNode = main.Children.First(n => n.Label == "circle-1");
		vm.AddOrganizerCommand.Execute(null);
		var first = Assert.IsType<CommentNode>(vm.Tree.SelectedKeyNode);
		first.Text = " first note";
		vm.Tree.SelectedKeyNode = main.Children.First(n => n.Label == "single-line");
		vm.AddOrganizerCommand.Execute(null);
		var second = Assert.IsType<CommentNode>(vm.Tree.SelectedKeyNode);
		second.Text = " second note";
		Assert.NotSame(first, second);

		vm.Tree.SelectedKeyNode = main;
		vm.RenameNode("renamed");
		Assert.Equal("Example.renamed.;comment", first.FullLabel);
		Assert.Equal(first.FullLabel, second.FullLabel); //shared by design
		Assert.Contains(first, main.Children);
		Assert.Contains(second, main.Children);

		vm.Tree.SearchFilterText = "second note";
		Assert.False(first.IsVisible);
		Assert.True(second.IsVisible);
		vm.Tree.SearchFilterText = ";comment"; //the marker is not text anyone typed
		Assert.False(first.IsVisible);
		Assert.False(second.IsVisible);
		vm.Tree.SearchFilterText = "";

		vm.Tree.Select(first);
		first.Text = " first note, edited";
		Assert.Equal(" second note", second.Text);

		string saved = Save(vm, "Example");
		Assert.True(saved.IndexOf("; first note, edited", StringComparison.Ordinal) < saved.IndexOf("circle-1]", StringComparison.Ordinal));
		Assert.True(saved.IndexOf("; second note", StringComparison.Ordinal) < saved.IndexOf("single-line]", StringComparison.Ordinal));
	}

	[Fact]
	public void MainWindowViewModel_EveryMutationDirtiesAndSaveOrResetCleans() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditDirty-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			string path = Path.Combine(folder, "Example.ini");
			using (StreamReader reader = GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini")) {
				File.WriteAllText(path, reader.ReadToEnd());
			}
			vm.LoadFile(path);
			Assert.False(vm.IsDirty);

			void Dirties(string what, Action edit) {
				vm.IsDirty = false;
				edit();
				Assert.True(vm.IsDirty, $"{what} should dirty the session");
			}

			Dirties("add node", () => { vm.Tree.SelectedKeyNode = Node(vm, "Example.main"); vm.AddNode("fresh"); });
			Dirties("add key", () => { vm.Tree.SelectedKeyNode = Node(vm, "Example.main.fresh"); vm.AddKeyCommand.Execute(null); });
			Dirties("rename", () => vm.RenameNode("renamed"));
			Dirties("needs review", () => vm.ToggleNeedsReviewCommand.Execute(null));
			Dirties("stale", () => vm.ToggleStaleLanguageCommand.Execute("en"));
			Dirties("stale all", () => vm.StaleAllLanguagesCommand.Execute(null));
			Dirties("default edit", () => vm.Tree.SelectedKey!.DefaultValue = "words");
			Dirties("translation edit", () => vm.Tree.SelectedEntry!.Value = "words too");
			Dirties("constant", () => { vm.Tree.SelectedKeyNode = Node(vm, "Example.prefix-whitespace"); vm.ToggleConstantCommand.Execute(null); });
			Dirties("organizer edit", () => { vm.Tree.SelectedKeyNode = Node(vm, "Example.main").Children.OfType<OrganizerNode>().First(); vm.Tree.SelectedOrganizer!.Text = " edited"; });
			Dirties("remove key", () => { vm.Tree.SelectedKeyNode = Node(vm, "Example.enum.none"); vm.RemoveKeyCommand.Execute(null); });
			Dirties("remove node", () => { vm.Tree.SelectedKeyNode = Node(vm, "Example.enum.two"); vm.RemoveNodeCommand.Execute(null); });
			Dirties("parameters", () => {
				vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key");
				dialogs.OnShow = shown => ((TestParametersViewModel)shown).Parameters[0].Value = "1";
				vm.TestParametersCommand.Execute(vm.Tree.SelectedKey);
				dialogs.OnShow = null;
			});

			vm.Save();
			Assert.False(vm.IsDirty);
			Assert.Empty(dialogs.Notices);
			Assert.Contains("renamed", File.ReadAllText(path));

			vm.IsDirty = true;
			vm.ResetCore();
			Assert.False(vm.IsDirty);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void MainWindowViewModel_ReviewFilterAloneAndComposed() {
		var vm = LoadExample();
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "zh");
		vm.Tree.SelectedKeyNode = Node(vm, "Example.enum.none");
		vm.ToggleNeedsReviewCommand.Execute(null); //the translator raises a hand

		vm.Tree.NeedsReviewFilter = true;
		Assert.True(Node(vm, "Example.enum.none").IsVisible);
		Assert.True(Node(vm, "Example.enum").IsVisible); //the path
		Assert.True(vm.Tree.KeyNodes[0].IsVisible);
		Assert.False(Node(vm, "Example.enum.two").IsVisible);
		Assert.False(Node(vm, "Example.view").IsVisible);
		Assert.Equal(GetAllKeyNodes(vm.Tree.KeyNodes).Count() - 3, vm.Tree.HiddenCount);

		//needs-review and search: both must hold
		vm.Tree.SearchFilterText = "none";
		Assert.True(Node(vm, "Example.enum.none").IsVisible);
		vm.Tree.SearchFilterText = "tooltip";
		Assert.False(Node(vm, "Example.enum.none").IsVisible);
		Assert.False(vm.Tree.KeyNodes[0].IsVisible);
		vm.Tree.SearchFilterText = "";

		//stale and needs-review: the key stale in zh is not the one raised
		vm.Tree.IsStaleFilter = true;
		Assert.False(vm.Tree.KeyNodes[0].IsVisible);
		vm.Tree.SelectedKeyNode = Node(vm, "Example.view.section-name.key"); //stale-zh
		vm.ToggleNeedsReviewCommand.Execute(null);
		vm.Tree.ApplyFilters(); //a badge change does not yank rows; the next pass reads it
		Assert.True(Node(vm, "Example.view.section-name.key").IsVisible);
		Assert.False(Node(vm, "Example.enum.none").IsVisible); //raised, not stale

		vm.Tree.ClearFilters();
		Assert.All(GetAllKeyNodes(vm.Tree.KeyNodes), node => Assert.True(node.IsVisible));
		Assert.Equal(0, vm.Tree.HiddenCount);
		Assert.False(vm.Tree.IsFiltering);
	}

	[Fact]
	public void MainWindowViewModel_SearchReadsCommentText() {
		var vm = LoadExample();
		KeyNode main = Node(vm, "Example.main");
		var banner = Assert.IsType<CommentNode>(main.Children[0]);

		vm.Tree.SearchFilterText = "freeform"; //a word in the banner only

		Assert.True(banner.IsVisible);
		Assert.True(main.IsVisible);
		Assert.False(Node(vm, "Example.main.title").IsVisible);
		Assert.False(vm.Tree.KeyNodes[0].Children[0].IsVisible); //the preamble says nothing of the kind
		Assert.False(Node(vm, "Example.enum").IsVisible);
	}

	[Fact]
	public void MainWindowViewModel_MissingTranslationEmphasisFollowsTheFilesTable() {
		// SPEC (Badges): a key reads as missing the selected language only when its
		// file registers that language — !-hidden counts, a stray code does not,
		// and a dictionary that never declared it shows no gap
		var vm = NewVm();
		vm.LoadFile(new StringReader("value-en=English\nvalue-de=!Deutsch\n\n[a]\nvalue=A\n\n[b]\nvalue=B\nvalue-de=Bee\n\n[e]\ncontext=no default\n"), "Main");
		vm.LoadFile(new StringReader("value-en=English\n\n[c]\nvalue=C\nvalue-fr=stray\n\n[d]\nvalue=D\n"), "Lib");

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de");
		Assert.True(Node(vm, "Main.a").EmptyValue); //hidden, but declared: a promise
		Assert.False(Node(vm, "Main.b").EmptyValue);
		Assert.True(Node(vm, "Main.e").EmptyValue); //no default: wanting regardless
		Assert.False(Node(vm, "Lib.c").EmptyValue); //Lib never registered de
		Assert.False(Node(vm, "Lib.d").EmptyValue);

		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "fr"); //known through the stray field only
		Assert.False(Node(vm, "Lib.d").EmptyValue); //a gripe, not a registration
		Assert.False(Node(vm, "Main.a").EmptyValue);
		Assert.True(Node(vm, "Main.e").EmptyValue);

		//the missing filter follows the same rule
		vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.First(l => l.Code == "de");
		vm.Tree.MissingFilter = true;
		Assert.True(Node(vm, "Main.a").IsVisible);
		Assert.False(Node(vm, "Main.b").IsVisible);
		Assert.True(Node(vm, "Main.e").IsVisible);
		Assert.False(Node(vm, "Lib.c").IsVisible);
		Assert.False(vm.Tree.KeyNodes[1].IsVisible); //nothing in Lib wants words in de
	}

	[Fact]
	public void MainWindowViewModel_FileGripesShowOnTheNodeAndOpenInADialog() {
		// SPEC (Out of scope → now in): what the parser griped about loading a file
		// is counted on its node and listed on demand
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\nvalue-fr=stray\n"), "Stray");
		vm.LoadFile(new StringReader("value-en=English\n\n[k]\nvalue=x\n"), "Clean");
		KeyNode stray = vm.Tree.KeyNodes[0], clean = vm.Tree.KeyNodes[1];

		Assert.Equal(1, stray.GripeCount);
		Assert.Equal(0, clean.GripeCount);
		Assert.True(vm.ShowFileGripesCommand.CanExecute(stray));
		Assert.False(vm.ShowFileGripesCommand.CanExecute(clean));
		Assert.False(vm.ShowFileGripesCommand.CanExecute(Node(vm, "Stray.k")));

		vm.ShowFileGripesCommand.Execute(stray);

		var shown = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
		Assert.Contains("'fr'", shown.Text);
		Assert.StartsWith("Stray", shown.Title);
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
