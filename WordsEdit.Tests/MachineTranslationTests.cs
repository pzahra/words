using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Machine translation in the editor, by hand (SPEC: Machine translation, In Wordsmith
///     and It lands for review): the request asks for the selection's first keys; an answer
///     pasted fills the grid, a flagged key's rows unticked with the reason; an edited row
///     is applied as edited, as one undo entry that marks each key's entry stale; and the
///     next request passes over what was written.
/// </summary>
public sealed class MachineTranslationTests : IDisposable {
	private readonly string folder = Path.Combine(Path.GetTempPath(), $"WordsEditMachine-{Guid.NewGuid():N}");

	public MachineTranslationTests() => Directory.CreateDirectory(folder);

	public void Dispose() => Directory.Delete(folder, recursive: true);

	private const string Text = "value=!en\nvalue-en=English\nvalue-it=Italiano\n\n[greet]\nvalue=Hello\n\n[bye]\nvalue=Bye {0}\n\n[cat]\nvalue=Cat\n";

	//the answer for greet and bye, bye's {0} dropped
	private const string Answer = """
		Here you go:
		```json
		{ "words": "translation/1", "target": { "code": "it" }, "items": [
		  { "key": "greet", "default": { "value": "Hello" }, "translation": { "value": "Ciao" } },
		  { "key": "bye", "default": { "value": "Bye {0}" }, "translation": { "value": "Arrivederci" } },
		] }
		```
		""";

	[Fact]
	public void TheRequest_AsksForTheKeysMissingTheLanguage_UpToABatch() {
		var (vm, dialogs, machine) = Opened();
		Assert.Equal(["greet", "bye", "cat"], machine.Selected().Select(key => key.BlockKey));
		Assert.Equal(Words.Known.Format("machine.selected", 3, 3), machine.Summary);

		machine.Batch = "2";
		machine.Brief = "Informal: tu.";
		machine.CopyCommand.Execute(null);

		string request = Assert.Single(dialogs.Copied);
		Assert.Contains("\"greet\"", request);
		Assert.Contains("\"bye\"", request);
		Assert.DoesNotContain("\"cat\"", request);
		Assert.Contains("Informal: tu.", request);
		Assert.Equal(Words.Known.Format("machine.selected", 3, 2), machine.Summary);
	}

	[Fact]
	public void APastedAnswer_FillsTheGrid_AFlaggedKeyUnticked_SayingWhy() {
		var (_, _, machine) = Opened();
		machine.Answer = Answer;
		machine.ReviewCommand.Execute(null);

		Assert.True(machine.IsReviewing);
		Assert.Equal(["greet", "bye"], machine.Rows.Select(row => row.Key));
		Assert.True(machine.Rows[0].Ticked);
		Assert.Equal("", machine.Rows[0].Notes);
		Assert.False(machine.Rows[1].Ticked);
		Assert.True(machine.Rows[1].Flagged);
		Assert.StartsWith(Words.Known.Format("machine.flagged", "").TrimEnd(), machine.Rows[1].Notes);
		Assert.Equal("Bye {0}", machine.Rows[1].Default);
	}

	[Fact]
	public void ARowEdited_IsCheckedAgain_ItsTickThePersonsCall() {
		var (_, _, machine) = Opened();
		machine.Answer = Answer;
		machine.ReviewCommand.Execute(null);

		machine.Rows[1].Text = "Arrivederci {0}";

		Assert.False(machine.Rows[1].Flagged);
		Assert.Equal("", machine.Rows[1].Notes);
		Assert.False(machine.Rows[1].Ticked);
	}

	[Fact]
	public void Apply_WritesTheTickedRowsAsEdited_AsOneUndoEntry_EachKeyMarkedStale() {
		var (vm, _, machine) = Opened();
		machine.Answer = Answer;
		machine.ReviewCommand.Execute(null);
		machine.Rows[0].Text = "Ciao!";
		int done = vm.UndoStack.DoneCount;

		machine.ApplyCommand.Execute(null);

		WordsKey greet = Key(vm, "T", "greet"), bye = Key(vm, "T", "bye");
		Assert.Equal("Ciao!", greet.Entries["it"].Value);
		Assert.Equal(TranslationViewModel.DefaultMark, greet.Entries["it"].Stale);
		Assert.False(bye.Entries.ContainsKey("it") && bye.Entries["it"].Value != "");
		Assert.Equal(done + 1, vm.UndoStack.DoneCount);
		Assert.True(vm.IsDirty);
		Assert.False(machine.IsReviewing);
		Assert.Equal(Words.Known.Format("machine.applied", 1), machine.Applied);

		vm.UndoCommand.Execute(null);
		if (vm.UndoStack.DoneCount != done) {
			vm.UndoCommand.Execute(null);
		}
		Assert.Equal(done, vm.UndoStack.DoneCount);
		Assert.True(!greet.Entries.TryGetValue("it", out var entry) || entry.Value == "" && entry.Stale is null);
	}

	[Fact]
	public void AnAnswerNamingItself_SaysSoAboveTheGrid_AndItsNameFollowsTheMark() {
		var (vm, _, machine) = Opened();
		machine.Stale = true;
		machine.Answer = Answer.Replace("{ \"words\": \"translation/1\",", "{ \"words\": \"translation/1\", \"translator\": \"ChatGPT\",");
		machine.ReviewCommand.Execute(null);

		Assert.Equal(Words.Known.Format("machine.answered-by", "ChatGPT"), machine.Translator);

		machine.ApplyCommand.Execute(null);

		Assert.Equal($"{TranslationViewModel.DefaultMark}: ChatGPT", Key(vm, "T", "greet").Entries["it"].Stale);
		Assert.Equal("", machine.Translator);
		Assert.DoesNotContain("greet", machine.Selected().Select(key => key.BlockKey));
	}

	[Fact]
	public void TheNextRequest_PassesOverWhatWasWritten_StaleWithTheMark() {
		var (_, _, machine) = Opened();
		machine.Stale = true;
		machine.Answer = Answer;
		machine.ReviewCommand.Execute(null);
		machine.ApplyCommand.Execute(null);

		Assert.Equal(["bye", "cat"], machine.Selected().Select(key => key.BlockKey));
	}

	[Fact]
	public void AnAnswer_InAnotherLanguage_OrNoDocument_IsNoAnswer() {
		var (_, _, machine) = Opened();
		machine.Answer = Answer.Replace("\"it\"", "\"fr\"");
		machine.ReviewCommand.Execute(null);
		Assert.False(machine.IsReviewing);
		Assert.Equal(Words.Known.Format("machine.other-language", "fr", machine.Target!.DisplayName), machine.Problem);

		machine.Answer = "Sorry, I can't help with that.";
		machine.ReviewCommand.Execute(null);
		Assert.False(machine.IsReviewing);
		Assert.StartsWith(Words.Known.Format("machine.no-answer", "").TrimEnd(), machine.Problem);
	}

	[Fact]
	public void AnAnswerThatIsNoJson_TellsTheViewWhereItStops_AndSaysWhy() {
		var (_, _, machine) = Opened();
		int? fault = null;
		machine.AnswerFaulted += at => fault = at;
		machine.Answer = Answer.Replace("\"value\": \"Ciao\"", "\"value\": Ciao\"");

		machine.ReviewCommand.Execute(null);

		Assert.False(machine.IsReviewing);
		Assert.Equal(machine.Answer.IndexOf("Ciao", StringComparison.Ordinal), fault);
		Assert.StartsWith(Words.Known.Format("machine.no-json", "").TrimEnd(), machine.Problem);
		Assert.DoesNotContain("line", machine.Problem);
	}

	[Fact]
	public void TheBrief_IsKeptForItsLanguage() {
		var memory = new TranslationMemory();
		var (vm, _, machine) = Opened(memory);
		machine.Brief = "Informal: tu.";
		machine.Mark = "mt";

		var again = new TranslationViewModel(vm, memory);
		Assert.Equal("Informal: tu.", again.Brief);
		Assert.Equal("mt", again.Mark);
	}

	//T.ini loaded, its root selected and Italian the language, the dialog open on it
	private (MainWindowViewModel Vm, FakeDialogs Dialogs, TranslationViewModel Machine) Opened(ITranslationMemory? memory = null) {
		string path = Path.Combine(folder, "T.ini");
		File.WriteAllText(path, Text);
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(path);
		vm.Tree.Select(Node(vm, "T", ""));
		vm.Tree.SelectedLanguage = vm.Tree.FileLanguages.Single(language => language.Code == "it");
		return (vm, dialogs, new TranslationViewModel(vm, memory ?? new TranslationMemory()));
	}
}
