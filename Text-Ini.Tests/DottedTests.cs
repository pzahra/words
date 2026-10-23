using System.Text;
using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>
///     The dotted dialect (SPEC: Its dialect, Cutting): a <c>[.child]</c> chains off the last full
///     header; a section added goes beside its nearest sibling, into a chain it extends; a strategy
///     that bases a new group re-heads its siblings and changes no other line; a deeper cut takes its
///     run from an ancestor's base; a bare base the edits left basing nothing goes; an untouched
///     chain keeps its cut; several edits cut as one; a
///     renamed section's chained sections keep their names, and a subtree renamed section by
///     section chains under the new name; the check passes a bare base either way.
/// </summary>
public class DottedTests {
	private static IniDocument Read(string text, IniRules? rules = null)
		=> IniDocument.Read(new UTF8Encoding(false).GetBytes(text), IniDialect.Dotted(rules ?? new IniRules()));

	[Fact]
	public void AChild_ChainsOffTheLastFullHeader_AndBracketsEndTheChain() {
		IniDocument document = Read("[group.a]\nx=1\n[.b]\ny=2\n[.c]\nz=3\n[]\nw=4\n[.d]\nv=5\n");
		Assert.Equal(["group.a", "group.a.b", "group.a.c", "", ".d"], document.Sections);
		Assert.Equal("3", document.Get("group.a.c", "z"));
		Assert.True(document.Elements.OfType<IniHeader>().Single(header => header.Name == "group.a.b").IsChained);

		//the sections dialect names each as written
		IniDocument plain = Ini.Read("[group.a]\n[.b]\ny=2\n");
		Assert.Equal(["group.a", ".b"], plain.Sections);
	}

	[Fact]
	public void ASectionAdded_JoinsTheChainItExtends_AfterItsNearestSibling() {
		IniDocument document = Read("[menu]\n[.file]\nv=File\n\n[.edit]\nv=Edit\n\n; about\n[about]\nv=About\n");
		document.Set("menu.file.recent", "v", "Recent");
		document.Set("menu.view", "v", "View");
		//the headers after [.file.recent] still chain off [menu], and the comment stays above [about]
		Assert.Equal("[menu]\n[.file]\nv=File\n\n[.file.recent]\nv=Recent\n\n[.edit]\nv=Edit\n\n[.view]\nv=View\n\n; about\n[about]\nv=About\n", document.Text);
		Assert.Equal(["menu", "menu.file", "menu.file.recent", "menu.edit", "menu.view", "about"], document.Sections);
	}

	[Fact]
	public void AStrategyThatBasesANewGroup_ReheadsItsSiblings_AndChangesNoOtherLine() {
		IniDocument document = Read("[app.title]\nv=Words\n\n; about\n[about]\nv=About\n");
		document.Set("app.version", "v", "1.0");
		Assert.Equal("[app]\n[.title]\nv=Words\n\n[.version]\nv=1.0\n\n; about\n[about]\nv=About\n", document.Text);

		//a strategy that never cuts leaves the full headers
		IniDocument never = IniDocument.Read(Encoding.UTF8.GetBytes("[app.title]\nv=Words\n"), IniDialect.Dotted(new IniRules(), new GroupCuts(minimumKeys: 3)));
		never.Set("app.version", "v", "1.0");
		Assert.Equal("[app.title]\nv=Words\n[app.version]\nv=1.0\n", never.Text);
	}

	[Fact]
	public void ADeeperCut_TakesItsRunFromAnAncestorsBase_WhichGoesOnceItBasesNothing() {
		IniDocument document = Read("[tools]\n[.a.x]\nv=X\n\n[.b]\nv=B\n");
		document.Set("tools.a.y", "v", "Y");
		//tools.a gathers two and heads them; tools keeps one, not worth its bare header
		Assert.Equal("[tools.a]\n[.x]\nv=X\n\n[.y]\nv=Y\n\n[tools.b]\nv=B\n", document.Text);
		Assert.Equal(["tools.a", "tools.a.x", "tools.a.y", "tools.b"], document.Sections);
	}

	[Fact]
	public void SeveralEdits_InOneDocument_CutAsOne() {
		IniDocument document = Read("[x]\nv=1\n\n");
		document.Set("g.a", "k", "1");
		document.Set("g.b", "k", "2");
		Assert.Equal("[x]\nv=1\n\n[g]\n[.a]\nk=1\n[.b]\nk=2\n", document.Text);
	}

	[Fact]
	public void ABareBase_TheEditsLeftBasingNothing_Goes_AndAnUntouchedChainKeepsItsCut() {
		IniDocument document = Read("[a]\n[a.x]\nv=1\n\n[menu]\n[.file]\nv=1\n\n[about]\nv=2\n");
		document.RemoveSection("menu.file");
		//[a] bases nothing as written, but no edit touched it
		Assert.Equal("[a]\n[a.x]\nv=1\n\n[about]\nv=2\n", document.Text);
		Assert.Equal(["a", "a.x", "about"], document.Sections);
	}

	[Fact]
	public void AHeaderThatBasesAChain_StaysBare_WhenItsSectionGoes() {
		IniDocument emptied = Read("[menu]\nv=Menu\n\n[.file]\nv=File\n");
		emptied.Remove("menu", "v");
		Assert.Equal("[menu]\n\n[.file]\nv=File\n", emptied.Text);

		IniDocument removed = Read("[menu]\nv=Menu\n\n[.file]\nv=File\n");
		removed.RemoveSection("menu");
		Assert.Equal("[menu]\n\n[.file]\nv=File\n", removed.Text);
		Assert.Equal("File", removed.Get("menu.file", "v"));
	}

	[Fact]
	public void ARenamedSection_ItsChainedSectionsKeepTheirNames_AndASubtreeRenamedChainsUnderTheNewName() {
		IniDocument document = Read("[menu]\nv=M\n[.file]\nv=F\n[.edit]\nv=E\n");
		document.RenameSection("menu", "top");
		Assert.Equal("[top]\nv=M\n[menu]\n[.file]\nv=F\n[.edit]\nv=E\n", document.Text);
		Assert.Equal("F", document.Get("menu.file", "v"));

		document.RenameSection("menu.file", "top.file");
		document.RenameSection("menu.edit", "top.edit");
		Assert.Equal("[top]\nv=M\n[.file]\nv=F\n[.edit]\nv=E\n", document.Text);
		Assert.Equal(["top", "top.file", "top.edit"], document.Sections);

		Assert.Equal(IniEditFailure.NameTaken, Assert.Throws<IniEditException>(() => document.RenameSection("top.file", "top.edit")).Failure);
		Assert.Equal(IniEditFailure.InvalidName, Assert.Throws<IniEditException>(() => document.RenameSection("top.file", ".file")).Failure);
	}

	[Fact]
	public void ASectionMoved_IsGatheredWhole_UnderAHeaderThatNamesIt() {
		IniDocument document = Read("[menu]\n[.file]\nv=F\n\n[.edit]\nv=E\n\n[about]\nv=A\n");
		document.MoveSection("menu.file", "about");
		//the blank line under [.file] parts [menu] from [.edit], and stays
		Assert.Equal("[menu]\n\n[.edit]\nv=E\n\n[about]\nv=A\n\n[menu.file]\nv=F\n", document.Text);
		Assert.Equal("F", document.Get("menu.file", "v"));
	}

	[Fact]
	public void GroupCuts_CountsTheSectionsThatWouldChainOffAGroup() {
		var cuts = new GroupCuts();
		string[] sections = ["menu.file", "menu.file.recent", "app.title", "tools.a.x", "tools.a.y", "tools.b"];
		Assert.True(cuts.Cuts("menu", sections, StringComparer.Ordinal)); //menu.file, and the one under it
		Assert.False(cuts.Cuts("app", sections, StringComparer.Ordinal)); //one is not worth a header
		Assert.False(cuts.Cuts("menu.file", sections, StringComparer.Ordinal)); //a section's header bases its chain already
		Assert.True(cuts.Cuts("tools.a", sections, StringComparer.Ordinal));
		Assert.False(cuts.Cuts("tools", sections, StringComparer.Ordinal)); //tools.a heads its own, so tools.b alone is left
		Assert.Throws<ArgumentOutOfRangeException>(() => new GroupCuts(0));
	}
}
