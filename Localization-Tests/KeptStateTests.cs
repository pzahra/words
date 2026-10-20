using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     What an editor's history keeps of the document and puts back (editor SPEC:
///     Undo): a language commit before and after, a key copied out and restored, a
///     file's settings references, a key's parameters and entries, and a form written
///     as the model holds forms.
/// </summary>
public class KeptStateTests {
	private const string Ini = "value=!en\nvalue-en=English\nvalue-de=Deutsch\nvalue-fr=Français\nparam-de=german.ini\n\n[k]\nvalue=x\nvalue-en=ex\nvalue-de=ix\nvalue-fr=ixe\n\n[k.sub]\nvalue=y\nvalue#other=ys\nparam-0=int:a count\nvalue-de=yy\nstale-de=then\n";

	private static (WordsSession Session, WordsFile File) Load() {
		var session = new WordsSession();
		return (session, session.Load(new StringReader(Ini), "Main"));
	}

	private static string Saved(WordsSession session, WordsFile file) {
		var writer = new StringWriter();
		session.Save(file, KeyTree.Build(file), writer);
		return writer.ToString();
	}

	[Fact]
	public void ALanguageChange_PutsBackWholeEitherWay_TheFileByteForByte() {
		var (session, file) = Load();
		string before = Saved(session, file);
		var change = new LanguageTableChange(session, file);

		//a swap, a removal and an addition, the default's language moved
		var rows = session.Languages.For(file);
		session.Languages.Apply(file, [
			new PlannedLanguage(rows[0], new LanguageEntry("de", "English")),
			new PlannedLanguage(rows[1], new LanguageEntry("en", "Deutsch")),
			new PlannedLanguage(null, new LanguageEntry("it", "Italiano"), IsDefault: true),
		], change);
		Assert.True(change.Changed);
		Assert.False(change.Merged);
		Assert.Throws<InvalidOperationException>(() => change.PutBack(before: false)); //no after until it closes
		change.Close();
		string after = Saved(session, file);
		Assert.NotEqual(before, after);

		change.PutBack(before: true);
		Assert.Equal(before, Saved(session, file));
		Assert.Equal(["en", "de", "fr"], session.Languages.Known.Select(language => language.Code));
		Assert.Equal("german.ini", file.LanguageSettings["de"]);
		change.PutBack(before: false);
		Assert.Equal(after, Saved(session, file));
		Assert.Equal("ix", session.FileNamed("Main").Keys["k"].Entries["en"].Value);
		Assert.Equal("it", file.DefaultLanguage);
	}

	[Fact]
	public void ALanguageChange_SaysWhenItChangedNothing_WhichFilesItChanged_AndWhenItMerged() {
		var (session, file) = Load();

		var nothing = new LanguageTableChange(session, file);
		nothing.Declare(file, "en"); //says so already
		nothing.Reorder(file, 1, 1);
		Assert.False(nothing.Changed);
		Assert.Empty(nothing.Files);

		var merge = new LanguageTableChange(session, file);
		merge.Rename(file, "fr", new LanguageEntry("de", "Deutsch"));
		Assert.True(merge.Merged); //onto a code the file declares: two languages' entries made one
		Assert.Equal([file], merge.Files); //the file whose table it changed (the editor's SPEC: Dirty by file)

		//one file's change: another's table is not its to touch
		WordsFile other = session.Load(new StringReader("value-en=English\n\n[o]\nvalue=y\n"), "Other");
		Assert.Throws<ArgumentException>(() => merge.Add(other, new LanguageEntry("it", "Italiano")));
		Assert.DoesNotContain("it", other.Languages);
	}

	[Fact]
	public void AKeptKey_IsRestoredAsItWas_AndTheCopyStaysKept() {
		var (session, file) = Load();
		List<WordsKey> kept = [.. session.KeysUnder(file, "k").Select(key => new WordsKey(key))];
		Assert.Equal(["k", "k.sub"], kept.Select(key => key.BlockKey));
		Assert.Empty(session.KeysUnder(file, "k.s")); //a segment whole, never a prefix of one

		Assert.Equal(2, session.RemoveKeysUnder(file, "k"));
		foreach (WordsKey key in kept) {
			session.Restore(file, key);
		}

		WordsKey sub = file.Keys["k.sub"];
		Assert.Equal(("y", "ys", "yy", "then"), (sub.DefaultValue, sub.Forms["other"], sub.Entries["de"].Value, sub.Entries["de"].Stale));
		Assert.Equal("a count", Assert.Single(sub.Parameters).Description);
		sub.Entries["de"].Value = "changed";
		sub.Parameters[0].Description = "changed";
		Assert.Equal("yy", kept[1].Entries["de"].Value); //the copy is the copy's
		Assert.Equal("a count", kept[1].Parameters[0].Description);
	}

	[Fact]
	public void AKeysParametersAndEntries_AreCopied_AndPutBack() {
		var (session, _) = Load();
		WordsKey key = session.FileNamed("Main").Keys["k.sub"];
		var parameters = key.CopyParameters();
		var entries = key.CopyEntries();

		key.Parameters[0].Description = "changed";
		Assert.False(WordsParameter.Same(parameters, key.CopyParameters()));
		key.PutBackParameters(parameters);
		Assert.True(WordsParameter.Same(parameters, key.CopyParameters()));
		Assert.NotSame(parameters[0], key.Parameters[0]);

		key.Entries["de"] = new WordsEntry();
		key.PutBackEntries(entries);
		Assert.Equal("yy", key.Entries["de"].Value);
		Assert.NotSame(entries["de"], key.Entries["de"]);
	}

	//what the history puts back finds its key by file and name, as the history names it
	[Fact]
	public void AField_IsSetByItsKeysFileAndName_AnEmptyFormBeingNone() {
		var (session, file) = Load();
		WordsKey sub = file.Keys["k.sub"];

		Assert.True(session.Set(file, "k.sub", new WordsField("value", "", ""), "z"));
		Assert.True(session.Set(file, "k.sub", new WordsField("value", "", "other"), ""));
		Assert.True(session.Set(file, "k.sub", new WordsField("value", "de", "other"), "zz"));
		Assert.True(session.Set(file, "k.sub", new WordsField("context", "", ""), "where"));
		Assert.True(session.Set(file, "k.sub", new WordsField("comment", "fr", ""), "note"));
		Assert.Equal(("z", "zz", "where", "note"), (sub.DefaultValue, sub.Entries["de"].Forms["other"], sub.Context, sub.Entries["fr"].Comment));
		Assert.Empty(sub.Forms); //empty: none, as the session holds forms

		//the hand and a stale mark, up and down
		Assert.True(session.Set(file, "k.sub", WordsField.Hand, ""));
		Assert.True(session.Set(file, "k.sub", WordsField.StaleIn("fr"), "machine translated"));
		Assert.True(session.Set(file, "k.sub", WordsField.StaleIn("de"), null));
		Assert.Equal((true, "machine translated", null), (sub.NeedsReview, sub.Entries["fr"].Stale, sub.Entries["de"].Stale));
		Assert.True(session.Set(file, "k.sub", WordsField.Hand, null));
		Assert.False(sub.NeedsReview);

		//no key of the name, or nothing written in a language the session lacks: nothing,
		//and no entry made
		Assert.False(session.Set(file, "nope", WordsField.Hand, ""));
		Assert.False(session.Set(file, "k.sub", new WordsField("value", "it", ""), ""));
		Assert.False(sub.Entries.ContainsKey("it"));

		//words in it bring the language into the session, as words in it keep it there,
		//and every key has an entry for it (the editor's SPEC: Dirty by file)
		Assert.True(session.Set(file, "k.sub", new WordsField("value", "it", ""), "ciao"));
		Assert.Equal("ciao", sub.Entries["it"].Value);
		Assert.Contains(session.Languages.Known, language => language.Code == "it");
		Assert.True(file.Keys["k"].Entries.ContainsKey("it"));
	}

	//a default's change marks languages through the session, as typing and the flags
	//do, giving back what an undo needs
	[Fact]
	public void ADefaultsChange_MarksLanguagesStale_ByTheKeysFileAndName() {
		var (session, file) = Load();
		WordsKey k = file.Keys["k"];

		//a mark already there stays, and an empty translation is missing, not stale
		k.Entries["de"].Stale = "then";
		k.Entries["fr"].Value = "";
		Assert.Equal(["en"], session.StaleTranslations(file, "k", "now"));
		Assert.Equal(("now", "then", null), (k.Entries["en"].Stale, k.Entries["de"].Stale, k.Entries["fr"].Stale));

		Assert.Empty(session.StaleTranslations(file, "nope", "now"));
	}

	[Fact]
	public void ADescription_AndAKeysParametersAndEntries_ArePutBackByName() {
		var (session, file) = Load();
		WordsKey sub = file.Keys["k.sub"];
		var parameters = sub.CopyParameters();
		var entries = sub.CopyEntries();

		Assert.True(session.Describe(file, "k.sub", "0", "the files"));
		Assert.Equal((WordsParameterType.Int, "the files"), (sub.Parameters[0].DataType, sub.Parameters[0].Description)); //its type kept
		Assert.False(session.Describe(file, "k.sub", "1", "none such"));

		sub.Parameters.Clear();
		sub.Entries["de"] = new WordsEntry();
		Assert.True(session.PutBackParameters(file, "k.sub", parameters));
		Assert.True(session.PutBackEntries(file, "k.sub", entries));
		Assert.Equal("a count", Assert.Single(sub.Parameters).Description);
		Assert.Equal(("yy", "then"), (sub.Entries["de"].Value, sub.Entries["de"].Stale));
		Assert.False(session.PutBackParameters(file, "nope", parameters));
		Assert.False(session.PutBackEntries(file, "nope", entries));
	}

	[Fact]
	public void AFilesSettingsSlots_AreKept_AndPutBack() {
		var (_, file) = Load();
		var before = SettingsSlots.Of(file);

		file.Settings = "rules.ini";
		file.LanguageSettings["fr"] = "french.ini";
		Assert.False(SettingsSlots.Of(file).Matches(before));
		before.PutBack(file);

		Assert.True(SettingsSlots.Of(file).Matches(before));
		Assert.Equal("", file.Settings);
		Assert.Equal(["de"], file.LanguageSettings.Keys);
	}

	[Fact]
	public void AFormWritten_Empty_IsNone() {
		Dictionary<string, string> forms = new() { ["other"] = "files" };

		Assert.False(forms.Write("other", "files")); //the same text is no change
		Assert.True(forms.Write("few", "fajls"));
		Assert.True(forms.Write("other", ""));
		Assert.Equal(["few"], forms.Keys);
	}
}
