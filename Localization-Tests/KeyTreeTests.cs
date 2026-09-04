using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

public class KeyTreeTests {
	private static WordsKey Key(string blockKey) => new(blockKey) { DefaultValue = blockKey };

	private static IEnumerable<string> Labels(KeyTreeNode node) => node.Children.Select(child => child.Label);

	[Fact]
	public void Build_NestsDottedKeysInOrderOfFirstAppearance() {
		var tree = KeyTree.Build("F",
			[Key("F.view.section.key"), Key("F.view.section.key.tip"), Key("F.$unit"), Key("F.main"), Key("F.main.title"), Key("F.view.other")],
			new Dictionary<string, string>());

		Assert.True(tree.IsFile);
		Assert.Equal("F", tree.Label);
		Assert.Equal(["view", "unit", "main"], Labels(tree));
		KeyTreeNode view = tree.Children[0];
		Assert.Equal("F.view", view.FullLabel); //a group: made for its descendants, has no key
		Assert.Equal(["section", "other"], Labels(view));
		Assert.Equal(["key"], Labels(view.Children[0]));
		Assert.Equal(["tip"], Labels(view.Children[0].Children[0]));
		Assert.Equal("F.$unit", tree.Children[1].FullLabel); //the marker stays in the key, not the label
	}

	[Fact]
	public void Build_AnchorsCommentsInFrontOfTheirBlockAndClosesWithTheTrailer() {
		var comments = new Dictionary<string, string> {
			["F.main.title"] = " a banner",
			["F.empty"] = " above a block that had nothing in it",
		};

		var tree = KeyTree.Build("F", [Key("F.main.title"), Key("F.main.body")], comments, " the trailer");

		KeyTreeNode main = tree.Children[0];
		var banner = Assert.IsType<CommentTreeNode>(main.Children[0]);
		Assert.Equal(" a banner", banner.Text);
		Assert.Equal(";", banner.Label);
		Assert.Equal("F.main.title.;comment", banner.FullLabel);
		Assert.Equal("title", main.Children[1].Label);
		Assert.Equal("body", main.Children[2].Label);
		//the empty block's comment still needs somewhere to stand: its node is made
		Assert.IsType<CommentTreeNode>(tree.Children[1]);
		Assert.Equal("F.empty", tree.Children[2].FullLabel);
		var trailer = Assert.IsType<CommentTreeNode>(tree.Children[^1]);
		Assert.Equal(" the trailer", trailer.Text);
		Assert.Equal("F.;trailer", trailer.FullLabel);
	}

	[Fact]
	public void Build_FromASession_IsTheWriterOrderThatRoundTrips() {
		// a bare [b] header with two keyed children is the shape the default cut
		// strategy writes back, so the text survives the trip unchanged
		string ini = string.Join(Environment.NewLine, [
			"value-en=English", "comment-en=English", "",
			"[a]", "value=A", "",
			"[b]", "; about b.c", "[.c]", "value=C", "", "[.d]", "value=D", "",
			"; bye",
		]) + Environment.NewLine;
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader(ini), "F");

		KeyTreeNode tree = KeyTree.Build(session, file);
		var output = new StringWriter();
		session.Save(file, tree, output);

		Assert.Equal(["a", "b", ";"], Labels(tree));
		Assert.Equal(ini, output.ToString());
	}

	[Fact]
	public void Relabel_CopiesTheShapeUnderANewPrefixCommentsIncluded() {
		var tree = KeyTree.Build("F", [Key("F.g.k"), Key("F.$c")], new Dictionary<string, string> { ["F.g.k"] = " note" }, " end");

		KeyTreeNode copy = KeyTree.Relabel(tree, "M");

		Assert.True(copy.IsFile);
		Assert.Equal("M", copy.FullLabel);
		Assert.Equal("M.g", copy.Children[0].FullLabel);
		Assert.Equal("g", copy.Children[0].Label);
		Assert.Equal("M.g.k", copy.Children[0].Children[1].FullLabel);
		Assert.Equal(" note", Assert.IsType<CommentTreeNode>(copy.Children[0].Children[0]).Text);
		Assert.Equal("M.$c", copy.Children[1].FullLabel);
		Assert.Equal("c", copy.Children[1].Label);
		Assert.Equal("M.;trailer", copy.Children[2].FullLabel);
		Assert.NotSame(tree.Children[0], copy.Children[0]);
	}
}
