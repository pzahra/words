using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>Lists (SPEC: Lists): parted by commas and trimmed, a quoted item taking commas, spaces and doubled quotes as its own, and quoted back only where it needs to be.</summary>
public class ListTests {
	[Fact]
	public void AList_SplitsByCommas_EachItemTrimmed() {
		Assert.Equal(["a", "b", "c"], IniList.Split(" a, b ,c "));
		Assert.Equal(["a", "", "b"], IniList.Split("a,,b"));
		Assert.Empty(IniList.Split("  "));
	}

	[Fact]
	public void AQuotedItem_TakesCommasSpacesAndDoubledQuotes_AsItsOwn() {
		Assert.Equal(["x, y", " say \"hi\" ", "z"], IniList.Split("\"x, y\", \" say \"\"hi\"\" \", z"));
	}

	[Fact]
	public void AListJoined_QuotesAnItemOnlyWhereItNeedsToBe_AndSplitsBackAsItWas() {
		string[] items = ["plain", "x, y", " padded ", "say \"hi\"", ""];
		string joined = IniList.Join(items);
		Assert.Equal("plain, \"x, y\", \" padded \", \"say \"\"hi\"\"\", ", joined);
		Assert.Equal(items, IniList.Split(joined));
		Assert.Equal("\"\"", IniList.Join([""]));
		Assert.Equal([""], IniList.Split(IniList.Join([""])));
		Assert.Equal("a,b", IniList.Join(["a", "b"], ","));
	}
}
