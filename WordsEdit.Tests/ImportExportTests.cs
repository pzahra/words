using PatTech.Localization;
using PatTech.Localization.Authoring;
using PatTech.Utils;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Import and export through the editor (SPEC: Import and export), driven
///     headless with <see cref="FakeDialogs"/>: the picker, the native path, the
///     ask, dirtiness, the plan, the loss and the overwrite confirmation. What a
///     format reads and writes is its codec's tests' business.
/// </summary>
public class ImportExportTests {
	private const string Neutral = """
		<?xml version="1.0" encoding="utf-8"?>
		<root>
		  <data name="greeting" xml:space="preserve">
		    <value>Hello</value>
		    <comment>Said on arrival</comment>
		  </data>
		</root>
		""";

	private const string French = """
		<?xml version="1.0" encoding="utf-8"?>
		<root>
		  <data name="greeting" xml:space="preserve">
		    <value>Bonjour</value>
		  </data>
		</root>
		""";

	private const string Ini = @"
value-en=English
value-fr=Français

[greeting]
context=Said on arrival
comment=Keep it short
value=Hello
value-fr=Bonjour
";

	private static string Folder() {
		string folder = Path.Combine(Path.GetTempPath(), $"WordsEditImportExport-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		return folder;
	}

	private static string ResxSet(string folder) {
		File.WriteAllText(Path.Combine(folder, "Strings.resx"), Neutral);
		string french = Path.Combine(folder, "Strings.fr.resx");
		File.WriteAllText(french, French);
		return french;
	}

	[Fact]
	public void Import_PresentsTheSetAtItsNativePath() {
		string folder = Folder();
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);

			//cancelled: nothing happens
			vm.ImportCommand.Execute(null);
			Assert.Empty(vm.Tree.KeyNodes);

			//one pick gathers the set; what loads is the words.ini it becomes
			dialogs.FilesToOpen = [ResxSet(folder)];
			vm.ImportCommand.Execute(null);

			KeyNode node = Assert.Single(vm.Tree.KeyNodes);
			Assert.Equal("Strings", node.FullLabel);
			WordsFile file = vm.Session.FileOf("Strings")!;
			Assert.Equal(Path.Combine(folder, "Strings.ini"), file.Path);
			Assert.Contains("fr", file.Languages);
			WordsKey key = vm.Session.Keys["Strings.greeting"];
			Assert.Equal("Hello", key.DefaultValue);
			Assert.Equal("Said on arrival", key.Context);
			Assert.Equal("Bonjour", key.Entries["fr"].Value);
			Assert.True(vm.IsDirty); //the ini is not on disk yet: unsaved work
			Assert.Empty(dialogs.Confirmations);
			Assert.Empty(dialogs.Notices);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_AsksWhenTheNativeIniIsOnDisk() {
		string folder = Folder();
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			File.WriteAllText(Path.Combine(folder, "Strings.ini"), "value-en=English\n");
			//the set picked twice over: asked once, imported once
			dialogs.FilesToOpen = [Path.Combine(folder, "Strings.resx"), ResxSet(folder)];

			dialogs.ConfirmAnswer = false;
			vm.ImportCommand.Execute(null);
			Assert.Single(dialogs.Confirmations);
			Assert.Empty(vm.Tree.KeyNodes);

			dialogs.ConfirmAnswer = true;
			vm.ImportCommand.Execute(null);
			Assert.Equal(2, dialogs.Confirmations.Count);
			Assert.Single(vm.Tree.KeyNodes);
			Assert.True(vm.IsDirty);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_OfAnIniIsALoad() {
		string folder = Folder();
		try {
			var dialogs = new FakeDialogs();
			var vm = new MainWindowViewModel(dialogs);
			string path = Path.Combine(folder, "Strings.ini");
			File.WriteAllText(path, Ini);
			dialogs.FilesToOpen = [path];

			vm.ImportCommand.Execute(null);

			Assert.Equal(path, Assert.Single(vm.Session.Files).Path);
			Assert.False(vm.IsDirty);
			Assert.Empty(dialogs.Confirmations); //the file on disk is the pick itself
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Import_TellsWhatItCannotRead() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);

		//an extension no format claims
		dialogs.FilesToOpen = [Path.Combine(Path.GetTempPath(), "Strings.docx")];
		vm.ImportCommand.Execute(null);
		Assert.Single(dialogs.Notices);

		//a claimed one that isn't there
		dialogs.FilesToOpen = [Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.resx")];
		vm.ImportCommand.Execute(null);
		Assert.Equal(2, dialogs.Notices.Count);
		Assert.Empty(vm.Tree.KeyNodes);
	}

	private static (MainWindowViewModel vm, FakeDialogs dialogs, ExportViewModel export) Open(string folder) {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader(Ini), Path.Combine(folder, "Strings.ini"));
		Assert.True(vm.ExportCommand.CanExecute(null));
		vm.ExportCommand.Execute(null);
		var export = Assert.IsType<ExportViewModel>(Assert.Single(dialogs.Shown));
		dialogs.Shown.Clear();
		return (vm, dialogs, export);
	}

	[Fact]
	public void Export_PlansAndSaysWhatIsLost() {
		string folder = Folder();
		try {
			var (vm, _, export) = Open(folder);
			Assert.False(vm.ExportCommand.CanExecute(null) && vm.Tree.KeyNodes.Count == 0);
			Assert.Same(vm.Tree.KeyNodes[0], export.FileNode);
			Assert.Equal(["ini", "resx", "xliff"], export.Formats.Select(row => row.Format.Info.Id));
			FakeDialogs.Rendered(export.Formats[1].Label);
			Assert.False(export.CanExport); //no target yet
			Assert.Empty(export.Plan);

			//resx: the neutral file and one per language, and the comment channel goes
			export.Format = export.Formats.Single(row => row.Format.Info.Id == "resx");
			export.Target = Path.Combine(folder, "Out.resx");
			Assert.True(export.CanExport);
			Assert.Equal(["Out.resx", "Out.en.resx", "Out.fr.resx"], export.Plan.Select(row => Path.GetFileName(row.Path)));
			Assert.All(export.Plan, row => Assert.False(row.Exists));
			Assert.Contains(WordsFeatures.Comment.Describe("S"), export.Loss);
			Assert.DoesNotContain(WordsFeatures.Context.Describe("S"), export.Loss);

			//xliff: one file per declared language, and it keeps everything this file uses
			export.Format = export.Formats.Single(row => row.Format.Info.Id == "xliff");
			export.Target = Path.Combine(folder, "Out.xlf");
			Assert.Equal(["Out.en.xlf", "Out.fr.xlf"], export.Plan.Select(row => Path.GetFileName(row.Path)));
			Assert.Equal(Words.Known["export.loses-nothing"], export.Loss);

			export.Target = "";
			Assert.False(export.CanExport);
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_WritesThePlanAndShowsTheGripes() {
		string folder = Folder();
		try {
			var (_, dialogs, export) = Open(folder);
			export.Format = export.Formats.Single(row => row.Format.Info.Id == "resx");
			export.Target = Path.Combine(folder, "Out.resx");
			bool closed = false;
			export.CloseRequested += () => closed = true;

			export.ExportCommand.Execute(null);

			Assert.True(closed);
			Assert.All(export.Plan, row => Assert.True(File.Exists(row.Path)));
			Assert.Contains("Bonjour", File.ReadAllText(Path.Combine(folder, "Out.fr.resx")));
			Assert.Empty(dialogs.Confirmations); //nothing was there to overwrite
			//resx dropped the comment, and said so
			var gripes = Assert.IsType<GripesViewModel>(Assert.Single(dialogs.Shown));
			Assert.Contains(gripes.Gripes, gripe => gripe.Contains("comment"));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_AsksBeforeOverwriting() {
		string folder = Folder();
		try {
			var (_, dialogs, export) = Open(folder);
			string existing = Path.Combine(folder, "Out.fr.xlf");
			File.WriteAllText(existing, "stale");
			export.Format = export.Formats.Single(row => row.Format.Info.Id == "xliff");
			export.Target = Path.Combine(folder, "Out.xlf");
			Assert.Equal([false, true], export.Plan.Select(row => row.Exists)); //en is new, fr is there

			dialogs.ConfirmAnswer = false;
			export.ExportCommand.Execute(null);
			Assert.Single(dialogs.Confirmations);
			Assert.Equal("stale", File.ReadAllText(existing));

			dialogs.ConfirmAnswer = true;
			export.ExportCommand.Execute(null);
			Assert.Equal(2, dialogs.Confirmations.Count);
			Assert.Contains("Bonjour", File.ReadAllText(existing));
		}
		finally {
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Export_NeedsAFileLoaded() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		Assert.False(vm.ExportCommand.CanExecute(null));
	}
}
