using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The framework-free half of image handling, hoisted to Core so it is proven once
/// rather than per framework: query parsing, the staticres:/dynres: spelling,
/// dimension cleaning, and the clamped path under a root the assets: scheme relies on.
/// </summary>
public class ImageQueryTests {

	[Fact]
	public void Parse_ReadsKnownAndCustomOptions() {
		var query = ImageQuery.Parse("?width=24&height=12.5&foreground=DarkRed&kind=ContentSave");

		Assert.Equal(24, query.Width);
		Assert.Equal(12.5, query.Height);
		Assert.Equal("DarkRed", query.Foreground); // raw: the framework makes the brush
		Assert.Null(query.Background);
		Assert.Equal("ContentSave", query.Options["kind"]);
		Assert.Equal("24", query.Options["WIDTH"]); // by name, case-insensitively, the well-known ones included
	}

	[Fact]
	public void Parse_NonsenseOrBlankNumber_IsUnspecified() {
		var query = ImageQuery.Parse("width=very&height=");

		Assert.Null(query.Width);
		Assert.Null(query.Height);
	}

	[Fact]
	public void Parse_Nothing_IsTheEmptyQuery() {
		Assert.Same(ImageQuery.Empty, ImageQuery.Parse(null));
		Assert.Same(ImageQuery.Empty, ImageQuery.Parse(""));
		Assert.Empty(ImageQuery.Empty.Options);
	}

	[Fact]
	public void Parse_Uri_SplitsTheQueryOffByHand() {
		// the query carries display options, not asset identity: the resolver gets a clean URI
		var source = new Uri("fake:thing.png?width=32");

		var query = ImageQuery.Parse(ref source);

		Assert.Equal(32, query.Width);
		Assert.Equal("fake:thing.png", source.OriginalString);
	}

	[Fact]
	public void Parse_Uri_WithoutQuery_LeavesTheUriAlone() {
		var source = new Uri("fake:thing.png");
		var before = source;

		Assert.Same(ImageQuery.Empty, ImageQuery.Parse(ref source));
		Assert.Same(before, source);
	}

	[Theory]
	[InlineData("staticres:key", "key")]
	[InlineData("scheme://host/a/b", "a/b")]
	[InlineData("fake:tiny%20image.png", "tiny%20image.png")] // escapes left as they are
	public void PathOf_IsTheSchemelessRemainder(string uri, string expected) {
		Assert.Equal(expected, ImageQuery.PathOf(new Uri(uri)));
	}

	[Theory]
	[InlineData("dynres:Accent", "Accent", true)]
	[InlineData("staticres:Accent", "Accent", false)]
	[InlineData("DYNRES:Accent", "Accent", true)]
	[InlineData("staticres: Accent ", "Accent", false)]
	public void ResourceReference_TryParse_ReadsTheImageSchemesSpelling(string value, string key, bool isDynamic) {
		Assert.True(ResourceReference.TryParse(value, out var reference));
		Assert.Equal(key, reference.Key);
		Assert.Equal(isDynamic, reference.IsDynamic);
	}

	[Theory]
	[InlineData("DarkRed")] // a bare word is a color, the framework's business
	[InlineData("dynres:")]
	[InlineData("staticres:   ")]
	[InlineData("")]
	[InlineData(null)]
	public void ResourceReference_TryParse_AnythingElse_IsNotAReference(string? value) {
		Assert.False(ResourceReference.TryParse(value, out _));
	}

	[Theory]
	[InlineData(16.0, 16.0)]
	[InlineData(0.0, 0.0)]
	[InlineData(100000.0, 4096.0)] // capped
	public void CleanDimension_FiniteNonNegative_Capped(double value, double expected) {
		Assert.Equal((double?)expected, ImageQuery.CleanDimension(value));
	}

	[Theory]
	[InlineData(-5.0)] // nonsense: unspecified, never thrown
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	public void CleanDimension_Nonsense_IsUnspecified_NeverThrown(double value) {
		Assert.Null(ImageQuery.CleanDimension(value));
	}

	[Fact]
	public void CleanDimension_Unspecified_StaysUnspecified() {
		Assert.Null(ImageQuery.CleanDimension(null));
	}

	private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Assets");

	[Theory]
	[InlineData("../secret.png")]
	[InlineData("icons/../../secret.png")]
	[InlineData(@"..\..\secret.png")]
	[InlineData("C:/Windows/notepad.exe")]
	[InlineData(@"\\server\share\x.png")]
	public void SafePath_EscapeAttempts_AreClamped(string relative) {
		Assert.Null(SafePath.Under(Root, relative));
	}

	[Fact]
	public void SafePath_HonestPath_ResolvesUnderTheRoot() {
		var path = SafePath.Under(Root, "icons/save.png");

		Assert.NotNull(path);
		Assert.StartsWith(Root, path);
		Assert.EndsWith(Path.Combine("Assets", "icons", "save.png"), path);
	}

	[Fact]
	public void SafePath_RootWithTrailingSeparator_StillResolves() {
		// however the root was spelled, one separator
		Assert.NotNull(SafePath.Under(Root + Path.DirectorySeparatorChar, "save.png"));
	}

	[Fact]
	public void SafePath_Uri_UnescapesToTheRealFileName() {
		var path = SafePath.Under(Root, new Uri("assets:tiny%20image.png"));

		Assert.NotNull(path);
		Assert.EndsWith(Path.Combine("Assets", "tiny image.png"), path);
	}

	[Theory]
	[InlineData("assets:../secret.png")]
	[InlineData("assets:..%5C..%5Csecret.png")]
	[InlineData("assets:C:/Windows/notepad.exe")]
	public void SafePath_Uri_EscapeAttempts_AreClamped(string uri) {
		Assert.Null(SafePath.Under(Root, new Uri(uri)));
	}
}
