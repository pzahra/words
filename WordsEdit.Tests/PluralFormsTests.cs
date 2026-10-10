using System.Globalization;
using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Plural forms in the editor, headless (SPEC: Plural forms): each value pane
///     shows one form at a time, picked from all six of CLDR's rows, greyed where
///     the language does not count by them and marked where they have words; a
///     form types, undoes, badges, searches and previews like its plain value.
/// </summary>
public class PluralFormsTests {
	private const string Ini = """
		value=!en
		value-en=English
		value-mt=Malti
		value-ja=日本語

		[word]
		value=file
		value#other=files
		value-mt=fajl
		value-mt#other=fajls

		[count]
		value={0} {0#word}
		param-0=int:the files

		[plain]
		value=hello
		value-mt=bongu

		""";

	private static MainWindowViewModel Load(string ini = Ini) {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader(ini), "Example");
		return vm;
	}

	private static void Select(MainWindowViewModel vm, string label, string? language = null) {
		vm.Tree.Select(MainWindowViewModelTests.Node(vm, $"Example.{label}"));
		if (language is not null) {
			vm.Tree.SelectedLanguage = vm.Session.Languages.Find(language)!;
		}
	}

	private static ChoiceItem ChoiceOf(MainWindowViewModel vm, string key) => vm.Commands.Choices.Single(choice => choice.Caption == Words.Known[key]);

	private static string Save(MainWindowViewModel vm) {
		var writer = new StringWriter();
		vm.Session.Save(vm.Session.FileOf("Example")!, vm.Tree.NodeOf(vm.Session.FileOf("Example")!), writer);
		return writer.ToString();
	}

	//once to go there when the change is out of view, once more to apply it
	private static void Step(MainWindowViewModel vm, bool undoing) {
		Func<int> depth = undoing ? () => vm.UndoStack.DoneCount : () => vm.UndoStack.UndoneCount;
		var command = undoing ? vm.UndoCommand : vm.RedoCommand;
		int before = depth();
		command.Execute(null);
		if (depth() == before) {
			command.Execute(null);
		}
		Assert.Equal(before - 1, depth());
	}

	[Fact]
	public void TheSelectorsListAllSixRows() {
		var vm = Load();
		Select(vm, "word", "mt");
		//the baseline counts in English: other live, the rest but the plain value greyed and empty
		ChoiceItem baseline = ChoiceOf(vm, "menu.default-form");
		Assert.Equal(PluralRules.Names, baseline.Options.Select(option => (string)option.Value));
		Assert.Equal(["one, the plain value: 1", "other: 0, 2…"], baseline.Options.Where(option => !option.IsGreyed).Select(option => option.Label));
		Assert.Equal(["zero", "two", "few", "many"], baseline.Options.Where(option => option.IsGreyed).Select(option => (string)option.Value));
		Assert.All(baseline.Options.Where(option => option.IsGreyed), option => Assert.False(option.IsEnabled));
		Assert.Equal("zero: no count reads it here", baseline.Options[0].Label);
		//the words it has wear a dot: the plain value and other; English wants nothing more
		Assert.Equal(["one", "other"], baseline.Options.Where(option => option.HasWords).Select(option => (string)option.Value));
		Assert.DoesNotContain(baseline.Options, option => option.IsMissing);
		//Maltese counts by five, two and many optional, each saying what it reads
		ChoiceItem translation = ChoiceOf(vm, "menu.translation-form");
		Assert.Equal(PluralRules.Names, translation.Options.Select(option => (string)option.Value));
		Assert.Equal(["zero"], translation.Options.Where(option => option.IsGreyed).Select(option => (string)option.Value));
		Assert.Equal("two: 2 — reads few", translation.Options[2].Label);
		Assert.StartsWith("few: 0, 3–10, 103–110", translation.Options[3].Label);
		Assert.StartsWith("many: 11–19, 111–119", translation.Options[4].Label);
		Assert.EndsWith("— reads other", translation.Options[4].Label);
		Assert.StartsWith("other: 20–102, 120–202", translation.Options[5].Label);
		Assert.EndsWith("— reads the plain value", translation.Options[5].Label); //from 11 up, the singular
		Assert.All(translation.Options, option => FakeDialogs.Rendered(option.Label));
		Assert.Equal(FormRow.Optional, vm.Tree.TranslationForms.Row("two"));
		Assert.Equal(FormRow.Live, vm.Tree.TranslationForms.Row("few"));
		//what the badge misses is bold, as the tree shows it: Maltese few
		Assert.Equal(["few"], translation.Options.Where(option => option.IsMissing).Select(option => (string)option.Value));
	}

	[Fact]
	public void TheBadgeMissesFewButNotTwoOrMany() {
		var vm = Load();
		Select(vm, "word", "mt");
		KeyNode node = vm.Tree.SelectedKeyNode!;
		Assert.True(node.EmptyValue); //Maltese has other, and wants few
		vm.Tree.PickTranslationForm("few");
		vm.Tree.EntryText = "fajls";
		Assert.False(node.EmptyValue); //two and many read few and other
		Assert.Equal("fajls", vm.Tree.SelectedEntry!.Forms["few"]);
		//nor does Maltese other, which reads the plain word, the singular from 11 up
		vm.Tree.PickTranslationForm("other");
		vm.Tree.EntryText = "";
		Assert.False(node.EmptyValue);
		Assert.DoesNotContain(ChoiceOf(vm, "menu.translation-form").Options, option => option.IsMissing);
		//none where the default speaks the language
		Select(vm, "word", "en");
		Assert.False(node.EmptyValue);
		//the default's own forms count too: a key Maltese made plural misses English other,
		//even where English needs nothing of its own
		Select(vm, "plain", "mt");
		Assert.False(vm.Tree.SelectedKeyNode!.EmptyValue);
		vm.Tree.SelectedEntry!.Forms["other"] = "bongus";
		Select(vm, "plain", "en");
		Assert.True(vm.Tree.SelectedKeyNode!.EmptyValue);
	}

	[Fact]
	public void TypingIntoAFormWritesItAndUndoes() {
		var vm = Load();
		Select(vm, "word", "mt");
		vm.Tree.PickTranslationForm("few");
		vm.Tree.EntryText = "f";
		vm.Tree.EntryText = "fajls";
		Assert.Contains("value-mt#few=fajls", Save(vm));
		Assert.Equal(1, vm.UndoStack.DoneCount); //one run
		//another form is another field
		vm.Tree.PickTranslationForm("other");
		vm.Tree.EntryText = "fajlijiet";
		Assert.Equal(2, vm.UndoStack.DoneCount);
		Step(vm, undoing: true);
		Assert.Equal("fajls", vm.Tree.SelectedEntry!.Forms["other"]);
		//the next undo is in few: the pane goes there first, then takes it back
		vm.UndoCommand.Execute(null);
		Assert.Equal("few", vm.Tree.TranslationForms.Form);
		Assert.Equal(1, vm.UndoStack.DoneCount);
		vm.UndoCommand.Execute(null);
		Assert.False(vm.Tree.SelectedEntry!.Forms.ContainsKey("few"));
		Assert.Equal("", vm.Tree.EntryText);
		Step(vm, undoing: false);
		Assert.Equal("fajls", vm.Tree.EntryText);
		//the next keystroke replaces what the undo put back
		vm.Tree.EntryText = "fajlsx";
		Step(vm, undoing: true);
		Assert.Equal("fajls", vm.Tree.SelectedEntry!.Forms["few"]);
		//clearing a form removes it, and the default's last form takes the key's plural with it
		Select(vm, "plain", "mt");
		vm.Tree.PickDefaultForm("other");
		vm.Tree.DefaultText = "hellos";
		Assert.True(vm.Tree.IsPlural);
		vm.Tree.DefaultText = "";
		Assert.Empty(vm.Tree.SelectedKey!.Forms);
		Assert.False(vm.Tree.IsPlural);
		Assert.Equal("other", vm.Tree.DefaultForms.Form); //an edit keeps the pick
	}

	[Fact]
	public void PickingMalteseFewMovesTheBaselineToOther() {
		var vm = Load();
		Select(vm, "word", "mt");
		vm.Tree.PickTranslationForm("few");
		Assert.Equal("other", vm.Tree.DefaultForms.Form);
		vm.Tree.PickTranslationForm("one");
		Assert.True(vm.Tree.DefaultForms.IsPlain);
		//the baseline's own pick moves nothing else
		vm.Tree.PickDefaultForm("other");
		Assert.True(vm.Tree.TranslationForms.IsPlain);
		//through the choice too
		ChoiceItem translation = ChoiceOf(vm, "menu.translation-form");
		translation.Options.Single(option => (string)option.Value == "many").IsChecked = true;
		Assert.Equal("many", vm.Tree.TranslationForms.Form);
		Assert.Equal("other", vm.Tree.DefaultForms.Form);
	}

	[Fact]
	public void AKeyThatIsNotPluralGreysTheTranslationSelector() {
		var vm = Load();
		Select(vm, "word", "mt");
		vm.Tree.PickTranslationForm("few");
		Assert.True(ChoiceOf(vm, "menu.translation-form").IsEnabled);
		Select(vm, "plain");
		Assert.False(vm.Tree.TranslationFormsEnabled);
		Assert.False(ChoiceOf(vm, "menu.translation-form").IsEnabled);
		Assert.True(vm.Tree.TranslationForms.IsPlain);
		Assert.Equal("bongu", vm.Tree.EntryText);
		Assert.True(vm.Tree.DefaultFormsEnabled); //the baseline is how a key becomes plural
		//a greyed selector takes no pick
		ChoiceOf(vm, "menu.translation-form").Options[3].IsChecked = true;
		Assert.True(vm.Tree.TranslationForms.IsPlain);
	}

	[Fact]
	public void EachPaneKeepsItsPickWhereTheKeyOffersIt() {
		var vm = Load("""
			value=!en
			value-mt=Malti

			[a]
			value=file
			value#other=files
			value-mt=fajl

			[b]
			value=dog
			value#other=dogs
			value-mt=kelb

			[c]
			value=hello

			""");
		Select(vm, "a", "mt");
		vm.Tree.PickTranslationForm("few");
		Select(vm, "b");
		Assert.Equal("few", vm.Tree.TranslationForms.Form); //a run through every few
		Assert.Equal("other", vm.Tree.DefaultForms.Form);
		Select(vm, "c");
		Assert.True(vm.Tree.TranslationForms.IsPlain);
		Assert.True(vm.Tree.DefaultForms.IsPlain); //a new key's default is not typed into other
		Select(vm, "b");
		Assert.True(vm.Tree.TranslationForms.IsPlain);
		//picking other on a key that is not plural, then leaving untyped, goes back too
		Select(vm, "c");
		vm.Tree.PickDefaultForm("other");
		Select(vm, "a");
		Select(vm, "c");
		Assert.True(vm.Tree.DefaultForms.IsPlain);
	}

	[Fact]
	public void AFormTheLanguageDoesNotUseStaysReachable() {
		var vm = Load("""
			value=!en

			[stray]
			value=file
			value#few=stray

			""");
		Assert.NotEmpty(vm.Session.FileOf("Example")!.Errors); //griped on load
		Select(vm, "stray");
		ChoiceItem baseline = ChoiceOf(vm, "menu.default-form");
		Choice few = baseline.Options.Single(option => (string)option.Value == "few");
		Assert.True(few.IsGreyed);
		Assert.True(few.HasWords);
		Assert.False(few.IsMissing);
		Assert.True(few.IsEnabled);
		few.IsChecked = true;
		Assert.Equal("stray", vm.Tree.DefaultText);
		//an empty greyed row cannot be picked
		Choice zero = baseline.Options[0];
		Assert.False(zero.IsEnabled);
		zero.IsChecked = true;
		Assert.Equal("few", vm.Tree.DefaultForms.Form);
		//cleared, it stays picked while the key does
		vm.Tree.DefaultText = "";
		Assert.Equal("few", vm.Tree.DefaultForms.Form);
		Assert.False(vm.Tree.DefaultForms.CanPick("few"));
	}

	[Fact]
	public void ALanguageWithOneCategoryHasNothingToPick() {
		var vm = Load();
		Select(vm, "word", "ja");
		Assert.False(vm.Tree.TranslationForms.AnyToPick);
		Assert.False(vm.Tree.TranslationFormsEnabled);
		Assert.Equal("The plain value: every count", ChoiceOf(vm, "menu.translation-form").Options[1].Label);
		Assert.All(ChoiceOf(vm, "menu.translation-form").Options.Where(option => (string)option.Value != "one"), option => Assert.True(option.IsGreyed));
	}

	[Fact]
	public void TheTitlesNameTheFormAndAnEmptyFormHintsWhatItReads() {
		var vm = Load();
		Select(vm, "word", "mt");
		Assert.Equal(Words.Known["main.default"], vm.Tree.DefaultTitle);
		vm.Tree.PickTranslationForm("few");
		Assert.Equal("Translation · few", vm.Tree.TranslationTitle);
		Assert.Equal("Default · other", vm.Tree.DefaultTitle);
		//Maltese few, empty, reads Maltese other; two would read few once it has words
		Assert.Equal("", vm.Tree.EntryText);
		Assert.Equal("fajls", vm.Tree.EntryHint);
		vm.Tree.EntryText = "fajliet";
		vm.Tree.PickTranslationForm("two");
		Assert.Equal("fajliet", vm.Tree.EntryHint);
		//a key without the default's other hints its plain value there
		Select(vm, "plain");
		vm.Tree.PickDefaultForm("other");
		Assert.Equal("hello", vm.Tree.DefaultFormHint);
		Assert.Equal("Default · other", vm.Tree.DefaultTitle);
	}

	[Fact]
	public void ALanguageWithNoWordsOfItsOwnHintsOnlyWhereTheDefaultSpeaksIt() {
		var vm = Load("""
			value=!en
			value-en-GB=English (UK)
			value-mt=Malti

			[word]
			value=file
			value#other=files

			""");
		Select(vm, "word", "mt");
		vm.Tree.PickTranslationForm("other");
		Assert.Null(vm.Tree.EntryHint);
		Select(vm, "word", "en-GB");
		vm.Tree.PickTranslationForm("other");
		Assert.Equal("files", vm.Tree.EntryHint);
	}

	[Fact]
	public void TheSearchReadsEveryForm() {
		var vm = Load();
		vm.Tree.SelectedLanguage = vm.Session.Languages.Find("mt")!;
		vm.Tree.SearchFilterText = "fajls";
		Assert.True(MainWindowViewModelTests.Node(vm, "Example.word").IsVisible);
		Assert.False(MainWindowViewModelTests.Node(vm, "Example.plain").IsVisible);
		vm.Tree.SearchFilterText = "files";
		Assert.True(MainWindowViewModelTests.Node(vm, "Example.word").IsVisible);
	}

	[Fact]
	public void ThePreviewsRenderTheFormPickedAndSelectWithTheSamples() {
		var vm = Load();
		vm.ShowDefaultPreview = true;
		vm.ShowLocalizationPreview = true;
		Select(vm, "word", "mt");
		Assert.Equal("file", vm.DefaultPreview.Text);
		vm.Tree.PickDefaultForm("other");
		Assert.Equal("files", vm.DefaultPreview.Text);
		vm.Tree.PickTranslationForm("few");
		Assert.Equal("fajls", vm.TranslationPreview.Text); //an empty few reads other
		//a value that selects previews with the inputs typed for it
		vm.Inputs.Of(vm.Session.Keys["Example.count"])["0"] = "4";
		Select(vm, "count");
		Assert.Equal("4 files", vm.DefaultPreview.Text);
		Assert.Equal("4 fajls", vm.TranslationPreview.Text);
		vm.Inputs.Of(vm.Tree.SelectedKey!)["0"] = "1";
		Select(vm, "word");
		Select(vm, "count");
		Assert.Equal("1 file", vm.DefaultPreview.Text);
	}

	[Fact]
	public void APreviewCountsByTheLanguageDotNetDoesNotKnow() {
		//Cebuano is the invariant culture to .NET; its translation still selects by Cebuano's rule
		var vm = Load("""
			value=!en
			value-ceb=Cebuano

			[word]
			value=file
			value#other=files
			value-ceb=file
			value-ceb#other=mga file

			[count]
			value={0} {0#word}
			value-ceb={0} {0#word}
			param-0=int:the files

			""");
		vm.Inputs.Of(vm.Session.Keys["Example.count"])["0"] = "5";
		vm.ShowDefaultPreview = true;
		vm.ShowLocalizationPreview = true;
		Select(vm, "count", "ceb");
		Assert.Equal("5 files", vm.DefaultPreview.Text);
		Assert.Equal("5 file", vm.TranslationPreview.Text); //Cebuano's one takes 5
	}

	[Fact]
	public void TestParametersSelectAsThePreviewDoes() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(new StringReader(Ini), "Example");
		Select(vm, "count");
		TestParametersViewModel? dialog = null;
		dialogs.OnShow = shown => {
			dialog = (TestParametersViewModel)shown;
			dialog.Rows[0].Sample = "4";
			Assert.Equal("4 files", dialog.Result);
			Assert.False(dialog.IsError);
			dialog.Rows[0].Sample = "1"; //the sample picks the form
			Assert.Equal("1 file", dialog.Result);
		};
		vm.TestParametersCommand.Execute(null);
		Assert.NotNull(dialog);
	}

	[Fact]
	public void TheNumbersACategoryTakes() {
		Assert.Equal("0, 2…", FormNumbers.Describe("en", "other"));
		Assert.Equal("1", FormNumbers.Describe("en", "one"));
		Assert.Equal("1, 21, 31, 41…", FormNumbers.Describe("ru", "one"));
		string million = 1_000_000.ToString("N0", CultureInfo.CurrentCulture);
		Assert.StartsWith(million, FormNumbers.Describe("fr", "many"));
		Assert.EndsWith("…", FormNumbers.Describe("fr", "many"));
		Assert.Equal(3, FormNumbers.Sample("mt", "few"));
		Assert.Equal(1_000_000, FormNumbers.Sample("fr", "many"));
		Assert.Null(FormNumbers.Sample("ru", "other")); //fractions only
		Assert.True(MissingWords.Misses("mt", new Dictionary<string, string> { ["other"] = "x" }));
		Assert.False(MissingWords.Misses("mt", new Dictionary<string, string> { ["few"] = "x", ["many"] = "x" })); //the sample's Maltese: other reads the plain word
		Assert.False(MissingWords.Misses("ja", new Dictionary<string, string>()));
		Assert.False(MissingWords.Misses("fr", new Dictionary<string, string> { ["other"] = "x" })); //exact millions read other
	}
}
