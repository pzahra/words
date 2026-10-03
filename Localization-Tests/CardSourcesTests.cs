using Sample_Shared;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The samples' card How (Sample-Shared SPEC: How each card is made): the markup cut
/// between a card's marker comments, and the ini blocks its keys look up.
/// </summary>
public class CardSourcesTests {
	private const string Page = """
		<StackPanel>
		    <!-- card: demo.one -->
		    <c:DemoCard Key="demo.one">
		      <TextBlock Text="{l:Words demo.one.text}"/>
		    </c:DemoCard>
		    <!-- /card -->

		    <!-- card: demo.two -->
		    <c:DemoCard Key="demo.two"/>
		    <!-- /card -->
		</StackPanel>
		""";

	private const string Ini = """
		; the file's own comment, about nothing in particular
		value-en=English

		[topic]
		[.one]
		value=Topic one

		; Section, its own paragraph

		[demo.one]
		value=One
		[.guide]
		value=Shows **{{>key}**, then {>topic.one} and {>.text}.
		[.text]
		value=Text with {$name}

		; the second card's own comment
		[demo.two]
		value=Two, continued \
		[not.a.header]\
		still the value
		[.item]
		value=An item

		[demo.twofold]
		value=Not under demo.two

		[enums.thing]
		value=A thing in code
		""";

	private const string Framework = """
		; constants
		[$name]
		value=Constant
		""";

	private static CardSources Sources() => new([new("Page.xaml", Page)], [new("sample.ini", Ini), new("framework.ini", Framework)]);

	[Fact]
	public void Markup_CutsBetweenTheMarkers_ItsIndentTakenOff() {
		Assert.Equal("""
			<c:DemoCard Key="demo.one">
			  <TextBlock Text="{l:Words demo.one.text}"/>
			</c:DemoCard>
			""", CardSources.Markup(Page, "demo.one"));
		Assert.Null(CardSources.Markup(Page, "demo.three"));
	}

	[Fact]
	public void How_ShowsTheMarkupThenTheWords_FileByFile() {
		var how = Sources().How("demo.one", null);

		Assert.Equal(["Page.xaml", "sample.ini", "framework.ini"], how.Select(source => source.File));
		// a comment right above a header is the block's own
		Assert.Equal("""
			; constants
			[$name]
			value=Constant
			""", how[2].Text);
	}

	[Fact]
	public void Words_OwnBlocksFirst_ThenWhatTheyReference() {
		var words = Sources().How("demo.one", null)[1].Text;

		// {{>key} is escaped, so not followed; [.one] away from [topic] is written out in full
		Assert.Equal("""
			[demo.one]
			value=One
			[.guide]
			value=Shows **{{>key}**, then {>topic.one} and {>.text}.
			[.text]
			value=Text with {$name}

			[topic.one]
			value=Topic one
			""", words);
	}

	[Fact]
	public void Words_AContinuedValue_HoldsAHeaderLikeLine_AndOwnCommentsCome() {
		var words = Sources().How("demo.two", null)[1].Text;

		// the key's own comment comes along, the section comment and demo.twofold do not
		Assert.Equal("""
			; the second card's own comment
			[demo.two]
			value=Two, continued \
			[not.a.header]\
			still the value
			[.item]
			value=An item
			""", words);
	}

	[Fact]
	public void Words_MoreKeys_JoinTheCards() {
		var how = Sources().How("demo.two", "enums.thing, enums.missing");

		Assert.EndsWith("""
			value=An item

			[enums.thing]
			value=A thing in code
			""", how[1].Text);
		Assert.DoesNotContain(how, source => source.File == "framework.ini");
	}

	[Fact]
	public void TheRealWords_CutTheCodeCardWhole() {
		var sources = new CardSources([], [SampleWords.ReadShared()]);

		var words = Assert.Single(sources.How("markdown.code", null)).Text;

		// the demo's continued lines, header-like and fence alike, stay in its value
		Assert.StartsWith("[markdown.code]\n", words);
		Assert.Contains("[greeting]\\\n", words);
		Assert.EndsWith("value=Ciao, *mondo*\\\n```", words);
	}
}
