using PatTech.Localization;
using PatTech.Localization.Authoring;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>The one door for dirtiness, each file's own, on a document small enough to see through.</summary>
public class ViewModelSaveBaseTests {
	private sealed class Document : ViewModelSaveBase {
		/// <summary>How often the marks were told to have changed.</summary>
		public int Told { get; private set; }

		public override void Save() {
			foreach (WordsFile file in Unsaved.ToList()) {
				MarkClean(file);
			}
		}

		protected override void OnUnsavedChanged() => Told++;
	}

	[Fact]
	public void EachFileIsDirtyOnItsOwn_AndTheTitleStarsWhileAnyIs() {
		var session = new WordsSession();
		WordsFile a = session.New(new StringReader(""), "a"), b = session.New(new StringReader(""), "b");
		var document = new Document { Title = Words.Known["app.name"] };
		Assert.False(document.IsDirty);
		Assert.Equal("Wordsmith", document.TitleMarked);

		document.MarkDirty(a);
		Assert.True(document.IsUnsaved(a));
		Assert.False(document.IsUnsaved(b));
		Assert.Equal("Wordsmith *", document.TitleMarked);
		Assert.Equal(1, document.Told);
		document.MarkDirty(a); //marked already: nothing to tell
		Assert.Equal(1, document.Told);

		document.MarkDirty([a, b]);
		document.MarkClean(a); //one written, the other still dirty
		Assert.False(document.IsUnsaved(a));
		Assert.True(document.IsDirty);
		Assert.Equal("Wordsmith *", document.TitleMarked);

		document.Save();
		Assert.False(document.IsDirty);
		Assert.Equal("Wordsmith", document.TitleMarked);

		document.MarkDirty([a, b]);
		document.MarkClean(); //Reset: every file gone
		Assert.Empty(document.Unsaved);

		//and no other door: the marks are the base's own
		Assert.Null(typeof(ViewModelSaveBase).GetProperty(nameof(ViewModelSaveBase.IsDirty))!.SetMethod);
	}
}
