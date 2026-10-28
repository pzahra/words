using PatTech.Localization;
using PatTech.Text.Ini;
using WordsEdit.Utils;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     The driver's failures in Wordsmith's words (SPEC: Ini files through the driver): a log that
///     will not settle says what the system said, not a lock it may not be, and where any bytes were kept.
/// </summary>
public class IniWordsTests {
	[Fact]
	public void ALogThatWontSettle_SaysTheSystemsReason_AndWhereTheFileWasKept() {
		var full = new IniCommitException(IniCommitFailure.Unsettled, "words.ini", ".words.ini.tmp", new IOException("the disk is full"));
		Assert.Equal(Words.Known.Format("ini.unsettled", ".words.ini.tmp", "the disk is full"), IniWords.Say(full));
		Assert.DoesNotContain(Words.Known["ini.held"], IniWords.Say(full));

		var kept = new IniCommitException(IniCommitFailure.Unsettled, "words.ini", ".words.ini.tmp", new IOException("the disk is full"), ".words.ini.recovered");
		Assert.Equal(Words.Known.Format("ini.unsettled-kept", ".words.ini.tmp", "the disk is full", ".words.ini.recovered"), IniWords.Say(kept));
	}
}
