using GongSolutions.Wpf.DragDrop;
using PatTech.Localization;
using PatTech.Localization.Authoring;
using PatTech.Text.Ini;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Save as a patch in the editor (SPEC: Save as a patch): Save patches a file where it is,
///     Save As writes it whole; landed it is clean, refused it is told in Wordsmith's words and
///     stays dirty, pending it is told, stays dirty and is where it was written, and the next
///     Save finishes it. A read that finishes a write over a change made since says where that
///     change is kept.
/// </summary>
public sealed class SavePatchTests : IDisposable {
	private readonly string folder = Path.Combine(Path.GetTempPath(), $"WordsEditPatch-{Guid.NewGuid():N}");

	public SavePatchTests() => Directory.CreateDirectory(folder);

	public void Dispose() {
		IniFiles.Step = null;
		Directory.Delete(folder, recursive: true);
	}

	private const string Hand = "; by hand\nvalue-en=English\n\n[greeting]\nvalue=Hello\n\n\n[farewell]\nvalue=Bye\n";

	private string this[string name] => Path.Combine(folder, name);

	[Fact]
	public void Save_PatchesTheFile_AndSaveAsAtItsOwnPathRewritesItWhole() {
		var (vm, dialogs) = Loaded();

		Edit(vm, "farewell", "Goodbye");
		vm.Save();

		Assert.Equal(Hand.Replace("value=Bye", "value=Goodbye"), File.ReadAllText(this["words.ini"]));
		Assert.False(vm.IsDirty);

		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes.Single();
		dialogs.FileToSave = this["words.ini"];
		vm.SaveAsCommand.Execute(null);
		Assert.Equal(Written(vm), File.ReadAllText(this["words.ini"]));
		Assert.NotEqual(Hand.Replace("value=Bye", "value=Goodbye"), Written(vm));
	}

	[Fact]
	public void AFileChangedOnDisk_IsRefused_InWordsmithsWords_AndStaysDirty() {
		var (vm, dialogs) = Loaded();
		File.WriteAllText(this["words.ini"], Hand + "\n[theirs]\nvalue=Theirs\n");

		Edit(vm, "farewell", "Goodbye");
		vm.Save();

		Assert.Equal(Words.Known.Format("file.save-failed", this["words.ini"], Words.Known["ini.changed"]), Assert.Single(dialogs.Notices));
		Assert.Contains("[theirs]", File.ReadAllText(this["words.ini"]));
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void APendingSave_IsTold_AndStaysDirty_TheNextSaveFinishingIt() {
		var (vm, dialogs) = Loaded();
		Edit(vm, "farewell", "Goodbye");

		Failing(() => vm.Save());

		Assert.Equal(Words.Known.Format("file.write-pending", this["words.ini"], this[".words.ini.tmp"]), Assert.Single(dialogs.Notices));
		Assert.Equal(Hand, File.ReadAllText(this["words.ini"]));
		Assert.True(vm.IsDirty);

		vm.Save();

		Assert.Equal(Hand.Replace("value=Bye", "value=Goodbye"), File.ReadAllText(this["words.ini"]));
		Assert.Single(dialogs.Notices); //its own commit, not refused as a change on disk
		Assert.False(vm.IsDirty);
		Assert.Equal([this["words.ini"]], Directory.GetFiles(folder));
	}

	[Fact]
	public void APendingSaveAs_HasMoved_AndStaysDirty() {
		var (vm, dialogs) = Loaded();
		Edit(vm, "farewell", "Goodbye");
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes.Single();
		dialogs.FileToSave = this["moved.ini"];

		Failing(() => vm.SaveAsCommand.Execute(null));

		Assert.Equal(Words.Known.Format("file.write-pending", this["moved.ini"], this[".moved.ini.tmp"]), Assert.Single(dialogs.Notices));
		Assert.Equal(this["moved.ini"], vm.Session.Files.Single().Path);
		Assert.Equal("moved", vm.Tree.KeyNodes.Single().Label);
		Assert.True(vm.IsDirty);

		vm.Save();
		Assert.Equal(Written(vm), File.ReadAllText(this["moved.ini"]));
		Assert.Equal(Hand, File.ReadAllText(this["words.ini"]));
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void APendingSave_Undone_IsDirty_AndTheNextSaveWritesTheUndo() {
		var (vm, _) = Loaded();
		Edit(vm, "farewell", "Goodbye");
		Failing(() => vm.Save());

		int done = vm.UndoStack.DoneCount;
		vm.UndoCommand.Execute(null);
		if (vm.UndoStack.DoneCount == done) {
			vm.UndoCommand.Execute(null);
		}

		//the log lands Goodbye, so Bye is unsaved work
		Assert.Equal("Bye", Key(vm, "words", "farewell").DefaultValue);
		Assert.True(vm.IsDirty);
		vm.Save();
		Assert.Equal(Hand, File.ReadAllText(this["words.ini"]));
		Assert.False(vm.IsDirty);
		Assert.Equal([this["words.ini"]], Directory.GetFiles(folder));
	}

	[Fact]
	public void APendingSaveAs_OfACleanFile_IsDirty_TheNextSaveFinishingIt() {
		var (vm, dialogs) = Loaded();
		vm.Tree.SelectedKeyNode = vm.Tree.KeyNodes.Single();
		dialogs.FileToSave = this["moved.ini"];

		Failing(() => vm.SaveAsCommand.Execute(null));

		Assert.False(File.Exists(this["moved.ini"]));
		Assert.True(vm.IsDirty);
		vm.Save();
		Assert.Equal(Written(vm), File.ReadAllText(this["moved.ini"]));
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void AReadFinishingAPendingWrite_OverAChangeMadeSince_SaysWhereThatChangeIsKept() {
		var (vm, _) = Loaded();
		Edit(vm, "farewell", "Goodbye");
		Failing(() => vm.Save());
		File.WriteAllText(this["words.ini"], Hand + "\n[theirs]\nvalue=Theirs\n");

		var dialogs = new FakeDialogs();
		var again = new MainWindowViewModel(dialogs);
		again.LoadFile(this["words.ini"]);

		string kept = this[".words.ini.recovered"];
		Assert.Equal(Words.Known.Format("file.recovered-kept", this["words.ini"], kept), Assert.Single(dialogs.Notices));
		Assert.Contains("[theirs]", File.ReadAllText(kept));
		Assert.Equal("Goodbye", Key(again, "words", "farewell").DefaultValue);
	}

	//each edit saved patches the file to read as the writer writes it, and undone and saved,
	//gives back the bytes it had
	[Fact]
	public void EachEditSaved_ReadsAsWritten_AndUndoneAndSaved_GivesBackTheFile() {
		string path = this["Example.ini"];
		using (TextReader example = MainWindowViewModelTests.GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini")) {
			File.WriteAllText(path, example.ReadToEnd());
		}
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(path);

		Saved("typing the default", () => { vm.Tree.Select(Node(vm, "Example", "main.circle-1")); vm.Tree.SelectedKey!.DefaultValue = "words"; });
		Saved("typing a translation", () => { vm.Tree.Select(Node(vm, "Example", "main.circle-1")); vm.Tree.SelectedEntry!.Value = "words too"; });
		Saved("needs review", () => { vm.Tree.Select(Node(vm, "Example", "main.circle-1")); vm.ToggleNeedsReviewCommand.Execute(null); });
		Saved("rename", () => { vm.Tree.Select(Node(vm, "Example", "main.circle-1")); vm.RenameNode("renamed"); });
		Saved("typing a comment", () => { vm.Tree.Select(Node(vm, "Example", "main").Children.OfType<OrganizerNode>().First()); vm.Tree.SelectedOrganizer!.Text = " edited"; });
		Saved("add a comment", () => { vm.Tree.Select(Node(vm, "Example", "main.circle-1")); vm.AddOrganizerCommand.Execute(null); vm.Tree.SelectedOrganizer!.Text = " fresh note"; }, steps: 2);
		Saved("remove the preamble", () => { vm.Tree.Select(vm.Tree.KeyNodes[0].Children[0]); vm.RemoveNodeCommand.Execute(null); });
		Saved("remove a key", () => { vm.Tree.Select(Node(vm, "Example", "enum.none")); vm.RemoveKeyCommand.Execute(null); });
		Saved("drag among siblings", () => vm.KeyDrag.Drop(new FakeDropInfo(Node(vm, "Example", "format.named"), Node(vm, "Example", "format.object"), RelativeInsertPosition.BeforeTargetItem)));
		Saved("drag under another parent", () => vm.KeyDrag.Drop(new FakeDropInfo(Node(vm, "Example", "enum.two"), Node(vm, "Example", "format"), RelativeInsertPosition.TargetItemCenter)));
		Saved("drag a comment", () => vm.KeyDrag.Drop(new FakeDropInfo(Node(vm, "Example", "main").Children.OfType<CommentNode>().First(), Node(vm, "Example", "enum.none"), RelativeInsertPosition.AfterTargetItem)));
		Saved("settings slots", () => {
			vm.Tree.Select(Node(vm, "Example", "enum"));
			dialogs.OnShow = shown => {
				var settings = (SettingsViewModel)shown;
				settings.SettingsFile = "wordsmith.ini";
				settings.OkayCommand.Execute(null);
			};
			vm.SettingsCommand.Execute(null);
			dialogs.OnShow = null;
		});
		Saved("add a language", () => {
			var manager = new LanguageManagerViewModel(vm);
			manager.AddCommand.Execute(null);
			manager.Selected!.Code = "fr";
			manager.Selected.NativeName = "Français";
			manager.Selected.EnglishName = "French";
			manager.OkCommand.Execute(null);
		});
		Saved("relabel a language", () => {
			var manager = new LanguageManagerViewModel(vm);
			manager.Rows.Single(row => row.Code == "en-GB").NativeName = "English (British)";
			manager.OkCommand.Execute(null);
		});

		void Saved(string what, Action edit, int steps = 1) {
			byte[] before = File.ReadAllBytes(path);
			edit();
			vm.Save();
			Assert.True(Reread(path) == Written(vm), $"{what}, saved, should read as the writer writes it");
			//an undo shows where the change is first, where the selection is elsewhere
			for (int step = 0; step < steps; step++) {
				int done = vm.UndoStack.DoneCount;
				vm.UndoCommand.Execute(null);
				if (vm.UndoStack.DoneCount == done) {
					vm.UndoCommand.Execute(null);
				}
			}
			vm.Save();
			Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"{what}, undone and saved, should give back the file: {Apart(before, File.ReadAllBytes(path))}");
		}
	}

	//where two texts part: the first line on, a few lines of each
	private static string Apart(byte[] was, byte[] now) {
		string[] a = System.Text.Encoding.UTF8.GetString(was).Split('\n'), b = System.Text.Encoding.UTF8.GetString(now).Split('\n');
		int at = a.Zip(b).TakeWhile(pair => pair.First == pair.Second).Count();
		return $"line {at + 1}: was [{string.Join(" / ", a.Skip(at).Take(6))}] now [{string.Join(" / ", b.Skip(at).Take(6))}]";
	}

	//the file read again in a session of its own, as the writer writes it
	private static string Reread(string path) {
		var session = new WordsSession();
		WordsFile file = session.Load(path);
		var text = new StringWriter();
		session.Save(file, KeyTree.Build(file), text);
		return text.ToString();
	}

	private (MainWindowViewModel Vm, FakeDialogs Dialogs) Loaded() {
		File.WriteAllText(this["words.ini"], Hand);
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(this["words.ini"]);
		return (vm, dialogs);
	}

	//a key's default typed over, as the editor records it
	private static void Edit(MainWindowViewModel vm, string key, string value) {
		vm.Tree.Select(Node(vm, "words", key));
		vm.Tree.SelectedKey!.DefaultValue = value;
	}

	//the file's commit fails at its write, twice, so it stands in its log
	private static void Failing(Action save) {
		IniFiles.Step = step => {
			if (step == "writing") {
				throw new IOException("the file went away");
			}
		};
		try {
			save();
		}
		finally {
			IniFiles.Step = null;
		}
	}

	//the file as Save As writes it, whole
	private static string Written(MainWindowViewModel vm) {
		WordsFile file = vm.Session.Files.Single();
		var text = new StringWriter();
		vm.Session.Save(file, vm.Tree.NodeOf(file), text);
		return text.ToString();
	}
}
