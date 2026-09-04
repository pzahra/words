using PatTech.Localization;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>The one door for dirtiness, on a document small enough to see through.</summary>
public class ViewModelSaveBaseTests {
	private sealed class Document : ViewModelSaveBase {
		public int Saves { get; private set; }
		/// <summary>Document state: editing it dirties.</summary>
		public string Body { get; set => ChangeProperty(ref field, value, dirty: true); } = "";
		/// <summary>View state: editing it does not.</summary>
		public bool IsExpanded { get; set => ChangeProperty(ref field, value); }

		public override void Save() {
			Saves++;
			IsDirty = false;
		}
	}

	[Fact]
	public void DocumentStateDirtiesViewStateDoesNot() {
		var document = new Document { Title = Words.Known["app.name"] };
		Assert.False(document.IsDirty);
		Assert.Equal("Wordsmith", document.TitleMarked);

		document.IsExpanded = true;
		Assert.False(document.IsDirty);

		document.Body = "words";
		Assert.True(document.IsDirty);
		Assert.Equal("Wordsmith *", document.TitleMarked);

		document.Save();
		Assert.False(document.IsDirty);
		Assert.Equal(1, document.Saves);

		document.Body = "words"; //the same value is no edit
		Assert.False(document.IsDirty);

		document.MarkDirty(); //a command's edit takes the same door
		Assert.True(document.IsDirty);
		Assert.Equal("Wordsmith *", document.TitleMarked);
	}
}
