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
		var change = new LanguageTableChange(session);

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
	public void ALanguageChange_SaysWhenItChangedNothing_AndWhenItMerged() {
		var (session, file) = Load();

		var nothing = new LanguageTableChange(session);
		nothing.Declare(file, "en"); //says so already
		nothing.Reorder(file, 1, 1);
		Assert.False(nothing.Changed);

		var merge = new LanguageTableChange(session);
		merge.Rename(file, "fr", new LanguageEntry("de", "Deutsch"));
		Assert.True(merge.Merged); //onto a code the file declares: two languages' entries made one
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
