using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;

public class KeyTreeTests {
	private static WordsKey Key(string blockKey) => new(blockKey) { DefaultValue = blockKey };

	private static IEnumerable<string> Labels(KeyTreeNode node) => node.Children.Select(child => child.Label);

	[Fact]
	public void Build_NestsDottedKeysInOrderOfFirstAppearance() {
		var tree = KeyTree.Build(
			[Key("view.section.key"), Key("view.section.key.tip"), Key("$unit"), Key("main"), Key("main.title"), Key("view.other")],
			new Dictionary<string, string>());

		Assert.True(tree.IsFile);
		Assert.Equal("", tree.FullLabel); //the root is the file, labelled by nothing
		Assert.Equal(["view", "unit", "main"], Labels(tree));
		KeyTreeNode view = tree.Children[0];
		Assert.Equal("view", view.FullLabel); //a group: made for its descendants, has no key
		Assert.Equal(["section", "other"], Labels(view));
		Assert.Equal(["key"], Labels(view.Children[0]));
		Assert.Equal(["tip"], Labels(view.Children[0].Children[0]));
		Assert.Equal("view.section.key.tip", view.Children[0].Children[0].Children[0].FullLabel); //each node by its path
		Assert.Equal("$unit", tree.Children[1].FullLabel); //the marker stays in the key, not the label
	}

	//a file's own tree labels each node by its path in the file, and its root by nothing
	[Fact]
	public void Build_FromAFile_LabelsEachNodeByItsPath() {
		var session = new WordsSession();
		WordsFile file = session.Load(new StringReader("[view.key]\nvalue=V\n\n[$unit]\nvalue=m\n\n; bye\n"), "F");

		KeyTreeNode tree = KeyTree.Build(file);

		Assert.Equal("", tree.FullLabel);
		Assert.Equal("view", tree.Children[0].FullLabel);
		Assert.Equal("view.key", tree.Children[0].Children[0].FullLabel);
		Assert.Equal("$unit", tree.Children[1].FullLabel);
		Assert.Equal(";trailer", tree.Children[2].FullLabel);
	}

	[Fact]
	public void Build_AnchorsCommentsInFrontOfTheirBlockAndClosesWithTheTrailer() {
		var comments = new Dictionary<string, string> {
			["main.title"] = " a banner",
			["empty"] = " above a block that had nothing in it",
		};

		var tree = KeyTree.Build([Key("main.title"), Key("main.body")], comments, " the trailer");

		KeyTreeNode main = tree.Children[0];
		var banner = Assert.IsType<CommentTreeNode>(main.Children[0]);
		Assert.Equal(" a banner", banner.Text);
		Assert.Equal(";", banner.Label);
		Assert.Equal("main.title.;comment", banner.FullLabel);
		Assert.Equal("title", main.Children[1].Label);
		Assert.Equal("body", main.Children[2].Label);
		//the empty block's comment still needs somewhere to stand: its node is made
		Assert.IsType<CommentTreeNode>(tree.Children[1]);
		Assert.Equal("empty", tree.Children[2].FullLabel);
		var trailer = Assert.IsType<CommentTreeNode>(tree.Children[^1]);
		Assert.Equal(" the trailer", trailer.Text);
		Assert.Equal(";trailer", trailer.FullLabel);
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

		KeyTreeNode tree = KeyTree.Build(file);
		var output = new StringWriter();
		session.Save(file, tree, output);

		Assert.Equal(["a", "b", ";"], Labels(tree));
		Assert.Equal(ini, output.ToString());
	}
}
