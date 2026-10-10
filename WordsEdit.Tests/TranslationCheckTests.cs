using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.ViewModels;
using Xunit;
using static WordsEdit.Tests.MainWindowViewModelTests;

namespace WordsEdit.Tests;

/// <summary>
///     The translation check in the editor (SPEC: Parameters → Translation check):
///     a translation that drops or adds a parameter beside its default's badges its
///     key for the selected language, filters, and is named beside the translation,
///     the definitions widening what it may use.
/// </summary>
public class TranslationCheckTests {
	private const string Ini = """
		value=!en
		value-en=English
		value-it=Italiano
		value-mt=Malti

		[word]
		value=file
		value#other=files
		value-it=file
		value-mt=fajl
		value-mt#other=fajls

		[drop]
		value={0} files in {1}
		value-it={0} file
		value-mt={0} fajls f'{1}

		[extra]
		value=Saved
		value-it=Salvato {2}
		value-mt=Issejvjat {Name}

		[counted]
		value={0} {0#word}
		value-it={0} file
		value-mt={0#word}

		[defined]
		value=Saved
		param-0=int:the files saved
		value-it=Salvati {0}

		[target]
		value={0} left
		value-it=ne restano {0}

		[follower]
		value=Note: {>target}
		value-it=Nota: {>target}

		""";

	private static MainWindowViewModel Load() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader(Ini), "T");
		return vm;
	}

	private static void Speak(MainWindowViewModel vm, string code) => vm.Tree.SelectedLanguage = vm.Tree.KnownLanguages.Single(language => language.Code == code);

	private static string Dropped(string names) => Words.Known.Format("parameters.dropped", names);
	private static string Extra(string names) => Words.Known.Format("parameters.extra", names);

	[Fact]
	public void AMismatch_BadgesTheKeyForTheSelectedLanguage_AndNamesWhatItDropsOrAdds() {
		var vm = Load();
		Speak(vm, "it");

		Assert.Equal(Dropped("{1}"), Node(vm, "T.drop").Mismatch);
		Assert.Equal(Extra("{2}"), Node(vm, "T.extra").Mismatch);
		Assert.True(Node(vm, "T.extra").HasMismatch);
		//printing where the default counts, and a definition the default never uses, are fine
		Assert.False(Node(vm, "T.counted").HasMismatch);
		Assert.False(Node(vm, "T.defined").HasMismatch);
		Assert.False(Node(vm, "T.word").HasMismatch);

		Speak(vm, "mt");
		Assert.False(Node(vm, "T.drop").HasMismatch);
		Assert.Equal(Extra("{Name}"), Node(vm, "T.extra").Mismatch);
		Assert.False(Node(vm, "T.counted").HasMismatch); //counting where the default prints too
		Assert.False(Node(vm, "T.defined").HasMismatch); //no words: missing, not mismatched

		//both at once, joined
		vm.Tree.Select(Node(vm, "T.drop"));
		vm.Tree.SelectedEntry!.Value = "{0} fajls {2}";
		Assert.Equal($"{Dropped("{1}")} · {Extra("{2}")}", vm.Tree.SelectedKeyNode!.Mismatch);
	}

	[Fact]
	public void TheMismatchView_ShowsTheKeysThatDropOrAdd_InTheSelectedLanguage() {
		var vm = Load();
		Speak(vm, "it");
		vm.Tree.MismatchFilter = true;
		Assert.True(vm.Tree.IsFiltering);

		Assert.True(Node(vm, "T.drop").IsVisible);
		Assert.True(Node(vm, "T.extra").IsVisible);
		Assert.False(Node(vm, "T.counted").IsVisible);
		Assert.False(Node(vm, "T.target").IsVisible);

		Speak(vm, "mt");
		Assert.False(Node(vm, "T.drop").IsVisible);
		Assert.True(Node(vm, "T.extra").IsVisible);

		vm.Tree.ClearFilters();
		Assert.True(Node(vm, "T.drop").IsVisible);
	}

	[Fact]
	public void AnEdit_ChecksItsKeyAgain_AndEveryKeyFollowingItsWords() {
		var vm = Load();
		Speak(vm, "it");
		Assert.False(Node(vm, "T.follower").HasMismatch);

		//the target's Italian drops {0}, and so does the follower's, whose words bring it in
		vm.Tree.Select(Node(vm, "T.target"));
		vm.Tree.SelectedEntry!.Value = "ne restano";
		Assert.Equal(Dropped("{0}"), Node(vm, "T.target").Mismatch);
		Assert.Equal(Dropped("{0}"), Node(vm, "T.follower").Mismatch);
		vm.Tree.SelectedEntry!.Value = "ne restano {0}";
		Assert.False(Node(vm, "T.target").HasMismatch);
		Assert.False(Node(vm, "T.follower").HasMismatch);

		//the default, typed: what a translation must keep follows it
		vm.Tree.SelectedKey!.DefaultValue = "left";
		Assert.Equal(Extra("{0}"), Node(vm, "T.target").Mismatch);
		Assert.Equal(Extra("{0}"), Node(vm, "T.follower").Mismatch);
	}

	[Fact]
	public void ADefinition_WidensWhatATranslationMayUse_AndAnUndoTakesItBack() {
		var vm = Load();
		Speak(vm, "it");
		KeyNode extra = Node(vm, "T.extra");
		vm.Tree.Select(extra);

		vm.ParametersPane.AddCommand.Execute(null); //{0}, which the Italian does not use
		Assert.Equal(Extra("{2}"), extra.Mismatch);
		vm.ParametersPane.Definitions[0].NameText = "2";
		Assert.False(extra.HasMismatch);
		Assert.Equal(["2"], vm.Tree.SelectedKey!.Parameters.Select(parameter => parameter.Key));

		vm.UndoCommand.Execute(null); //the rename
		Assert.Equal(Extra("{2}"), extra.Mismatch);
		vm.RedoCommand.Execute(null);
		Assert.False(extra.HasMismatch);

		vm.ParametersPane.Definitions[0].RemoveCommand.Execute(null);
		Assert.Equal(Extra("{2}"), extra.Mismatch);
	}

	[Fact]
	public void TheNote_IsTheSelectedKeysMismatch_ForTheTranslationBesideIt() {
		var vm = Load();
		Speak(vm, "it");
		vm.Tree.Select(Node(vm, "T.drop"));
		Assert.Equal(Dropped("{1}"), vm.Tree.SelectedKeyNode!.Mismatch);
		Assert.Equal(Dropped("{1}"), TreeViewModel.MismatchNote(new ParameterMismatch(["1"], [])));
		Assert.Equal("", TreeViewModel.MismatchNote(ParameterMismatch.None));
		Assert.Equal($"{Dropped("{0}, {Count}")} · {Extra("{3}")}", TreeViewModel.MismatchNote(new ParameterMismatch(["0", "Count"], ["3"])));
	}
}
