using System.ComponentModel;
using System.Runtime.CompilerServices;
using PatTech.Utils;
using Sample_Shared;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>Coffee: a [Words] member, a Description one, a bare one, and an obsolete name sharing the first's value.</summary>
public enum Coffee {
	[Words("coffee.espresso")] Espresso,
	[Description("A long black")] Americano,
	Plain,
	[Obsolete("an old name")] Ristretto = 0,
}

/// <summary>An attribute of an app's own, as a team migrating to [Words] might already have.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HintAttribute(string text) : Attribute {
	public string Text { get; } = text;
}

/// <summary>Another, registered after <see cref="HintAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LaterHintAttribute(string text) : Attribute {
	public string Text { get; } = text;
}

/// <summary>An attribute nobody registers.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class IgnoredAttribute(string text) : Attribute {
	public string Text { get; } = text;
}

/// <summary>An attribute registered by a lower-case letter, <c>d</c>.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class NoteAttribute(string text) : Attribute {
	public string Text { get; } = text;
}

/// <summary>An attribute registered by a lower-case letter, <c>n</c>.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class GistAttribute(string text) : Attribute {
	public string Text { get; } = text;
}

/// <summary>An attribute of an app's own that names a member's key.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class KeyedAttribute(string key) : Attribute {
	public string Key { get; } = key;
}

/// <summary>An attribute whose reads are counted.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CountedAttribute(string text) : Attribute {
	public static int Reads;
	public string Text {
		get {
			Reads++;
			return text;
		}
	}
}

/// <summary>An attribute registered only after its enum was described.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LateAttribute(string text) : Attribute {
	public string Text { get; } = text;
}

public enum Hinted {
	[Hint("Use the hint")] Hint,
	[Ignored("nobody reads this")] Ignored,
#pragma warning disable CS0618 // the built-in migration aid, registered before any app's
	[Tooltip("The built-in one"), Hint("The app's own")] Both,
#pragma warning restore CS0618
	[Hint("Registered first"), LaterHint("Registered later")] Two,
	[Note("A note"), Gist("The gist")] Noted,
}

public enum Tea {
	[Words("tea.green"), Keyed("tea.other")] Green,
	[Keyed("tea.keyed")] Keyed,
	Black,
}

public enum Juice {
	[Words("juice.orange")] Orange,
	Apple,
	Pear,
}

public enum Counted {
	[Counted("counted")] One,
}

public enum Late {
	[Late("late")] One,
}

[Flags]
public enum Toppings {
	None = 0,
	[Words("top.sugar")] Sugar = 1,
	[Words("top.cocoa")] Cocoa = 2,
	[Words("top.both")] Both = 3,
	Cream = 4,
}

/// <summary>
///     Describe through its engine and the member cache (runtime SPEC: Describe without
///     the type). The registry is the process's, so every test that registers is here,
///     one at a time, each with attributes and letters of its own.
/// </summary>
[Collection("Words globals")]
public class DescribableTests {
	private const string Script = """
		[coffee.espresso]
		value=Espresso shot
		[.tooltip]
		value=Short and strong
		[.sub]
		value=A small cup
		[.desc]
		value=Hot water through fine grounds
		[.hint]
		value=Drink it standing

		[tea.green]
		value=Green tea
		[tea.keyed]
		value=Keyed tea
		[tea.leaf.Black]
		value=Black tea

		[top.sugar]
		value=sugar
		[top.cocoa]
		value=cocoa
		[top.both]
		value=sugar and cocoa
		""";

	//the samples add their slot once per process, from whichever test first touches
	//their words, and CardSourcesTests runs beside these: added here, it cannot clear
	//the cache halfway through one of them
	public DescribableTests() => RuntimeHelpers.RunClassConstructor(typeof(SampleWords).TypeHandle);

	private static IWords Words(string script = Script) => WordsBuilder.Create().LoadString(script).ToWords("en");

	//the keys looked up, in order
	private sealed class Counting(IWords inner) : IWords {
		public List<string> Asked { get; } = [];
		public IWordsProvider Provider => inner.Provider;
		public string this[string key] => inner[key];
		public bool ContainsKey(string key) => inner.ContainsKey(key);
		public bool TryGetValue(string key, out string value) {
			Asked.Add(key);
			return inner.TryGetValue(key, out value!);
		}
		public void SetCulture() { }
	}

	private sealed class CaptureLogger : ITakeException {
		public List<string> Messages { get; } = [];
		public void Warn(string text) => Messages.Add(text);
		public void Error(Exception exception, string message) => Messages.Add(message);
	}

	[Fact]
	public void EveryBuiltInLetter_ReadsAsItDid() {
		IWords words = Words();

		Assert.Equal("Espresso shot", Coffee.Espresso.Describe("G", words));
		Assert.Equal("Espresso shot", Coffee.Espresso.Describe("n", words));
		Assert.Equal("Espresso shot", Coffee.Espresso.Describe("N", words));
		Assert.Equal("Hot water through fine grounds", Coffee.Espresso.Describe("d", words));
		Assert.Equal("Hot water through fine grounds", Coffee.Espresso.Describe("D", words));
		Assert.Equal("A small cup", Coffee.Espresso.Describe("S", words));
		Assert.Equal("Short and strong", Coffee.Espresso.Describe("T", words));
		Assert.Equal("Espresso", Coffee.Espresso.Describe("s", words)); //not the obsolete name sharing its value
		Assert.Equal("0", Coffee.Espresso.Describe("i", words));
		Assert.Equal("Espresso shot", Coffee.Espresso.Describe(null, words));

		//a Description and no key: the general text falls back to it, the strict name does not
		Assert.Equal("A long black", Coffee.Americano.Describe("G", words));
		Assert.Equal("Americano", Coffee.Americano.Describe("N", words));
		Assert.Equal("A long black", Coffee.Americano.Describe("d", words));
		Assert.Equal("", Coffee.Americano.Describe("T", words));
		Assert.Equal("Plain", Coffee.Plain.Describe("G", words));
		Assert.Equal("2", Coffee.Plain.Describe("i", words));

		//quoted text and anything that is no letter are written as they are
		Assert.Equal("Espresso shot - A small cup", Coffee.Espresso.Describe("G' - 'S", words));
		Assert.Equal("Espresso shot (A small cup)", Coffee.Espresso.Describe("G (S)", words));
		Assert.Equal("it's Espresso", Coffee.Espresso.Describe("'it''s 's", words));
	}

	[Fact]
	public void AKeyIsLookedUp_OnlyWhenALetterAsks_AndOnce() {
		var words = new Counting(Words());

		Coffee.Espresso.Describe("G", words);
		Assert.Equal(["coffee.espresso"], words.Asked);

		words.Asked.Clear();
		Coffee.Espresso.Describe("TT", words);
		Assert.Equal(["coffee.espresso.tooltip"], words.Asked);

		words.Asked.Clear();
		Coffee.Espresso.Describe("si", words); //the name and the number read no key
		Assert.Empty(words.Asked);
	}

	[Fact]
	public void AMember_IsReadOnce_HoweverOftenItIsDescribed() {
		Describable.Fill<CountedAttribute>('T', counted => counted.Text);
		int before = CountedAttribute.Reads;

		for (int i = 0; i < 5; i++) {
			Assert.Equal("counted", Counted.One.Describe("T", Words()));
		}
		Assert.Equal(1, CountedAttribute.Reads - before);
	}

	[Fact]
	public void ARegisteredAttribute_FillsItsSlot_AndTheFirstRegisteredWins() {
		Describable.Fill<HintAttribute>((char)DescribeSlot.Tooltip, hint => hint.Text);
		Describable.Fill<LaterHintAttribute>('T', hint => hint.Text);

		Assert.Equal("Use the hint", Hinted.Hint.Describe("T"));
		Assert.Equal("", Hinted.Ignored.Describe("T"));            //nobody registered it
		Assert.Equal("The built-in one", Hinted.Both.Describe("T")); //[Tooltip] came first
		Assert.Equal("The built-in one", Hinted.Both.Describe("S")); //one attribute, two slots
		Assert.Equal("Registered first", Hinted.Two.Describe("T"));
	}

	[Fact]
	public void ALowerCaseLetter_IsItsCapitalsSlot() {
		Describable.Fill<NoteAttribute>('d', note => note.Text);
		Describable.Fill<GistAttribute>('n', gist => gist.Text);

		Assert.Equal('D', (char)DescribeSlot.Description);
		Assert.Equal("A note", Hinted.Noted.Describe("D"));
		Assert.Equal("A note", Hinted.Noted.Describe("d"));
		Assert.Equal("The gist", Hinted.Noted.Describe("G"));
		Assert.Equal("The gist", Hinted.Noted.Describe("n"));
	}

	[Fact]
	public void AKeyPrefix_KeysTheMembersWithNone_AndAKeyAttributeOfOnesOwn_ComesAfterWords() {
		Describable.FillKey<KeyedAttribute>(keyed => keyed.Key);
		Describable.Keys<Tea>("tea.leaf");
		IWords words = Words();

		Assert.Equal("Green tea", Tea.Green.Describe("G", words)); //[Words] wins over the app's own
		Assert.Equal("Keyed tea", Tea.Keyed.Describe("G", words)); //the app's own wins over the prefix
		Assert.Equal("Black tea", Tea.Black.Describe("G", words)); //tea.leaf.Black
		Assert.Throws<ArgumentException>(() => Describable.Keys<Tea>("no key"));
	}

	[Fact]
	public void AFunction_KeysAnEnumOneDoesNotOwn_MemberByMember_OnceEach() {
		int asked = 0;
		Describable.Keys<DayOfWeek>(day => {
			asked++;
			return day switch {
				DayOfWeek.Saturday or DayOfWeek.Sunday => "days.weekend",
				DayOfWeek.Wednesday => null,
				_ => $"days.{day}",
			};
		});
		IWords words = Words("""
			[days.Monday]
			value=Monday, again
			[days.weekend]
			value=The weekend
			""");

		Assert.Equal("Monday, again", DayOfWeek.Monday.Describe("G", words));
		Assert.Equal("Monday, again", DayOfWeek.Monday.Describe("G", words));
		Assert.Equal("The weekend", DayOfWeek.Sunday.Describe("G", words));
		Assert.Equal("The weekend", DayOfWeek.Saturday.Describe("G", words));
		Assert.Equal("Wednesday", DayOfWeek.Wednesday.Describe("G", words)); //no key: its name
		Assert.Equal(7, asked);
	}

	[Fact]
	public void AFunction_KeysOnlyTheMembersWithNone_AndTheLatestForAType_Stands() {
		Describable.Keys<Juice>(juice => $"juice.first.{juice}");
		Describable.Keys<Juice>(juice => juice == Juice.Pear ? null : $"juice.picked.{juice}");
		IWords words = Words("""
			[juice.orange]
			value=Orange juice
			[juice.picked.Orange]
			value=Not this one
			[juice.picked.Apple]
			value=Apple juice
			[juice.first.Pear]
			value=Not this one either
			""");

		Assert.Equal("Orange juice", Juice.Orange.Describe("G", words)); //[Words] wins
		Assert.Equal("Apple juice", Juice.Apple.Describe("G", words));
		Assert.Equal("Pear", Juice.Pear.Describe("G", words));
	}

	[Fact]
	public void ASlotOfOnesOwn_ReadsItsSuffix_ThenItsAttribute() {
		Describable.Slot('H', ".hint");
		Describable.Fill<HintAttribute>('H', hint => hint.Text);

		Assert.Equal("Drink it standing", Coffee.Espresso.Describe("H", Words()));
		Assert.Equal("Use the hint", Hinted.Hint.Describe("H", Words())); //no key: the attribute
		Assert.Equal("Espresso shot: Drink it standing", Coffee.Espresso.Describe("G': 'H", Words()));
	}

	[Fact]
	public void ALetterTaken_OrNoLetter_OrNoSuffix_IsRefused() {
		Describable.Slot('J', ".j");

		Assert.Throws<ArgumentException>(() => Describable.Slot('J', ".k"));    //added already
		Assert.Throws<ArgumentException>(() => Describable.Slot('T', ".tip"));  //built in
		Assert.Throws<ArgumentException>(() => Describable.Slot('n', ".n"));    //G's other letter
		Assert.Throws<ArgumentException>(() => Describable.Slot('=', ".eq"));   //no letter
		Assert.Throws<ArgumentException>(() => Describable.Slot('K', "k"));     //no dot
		Assert.Throws<ArgumentException>(() => Describable.Slot('K', ".a.b"));  //more than a segment
		Assert.Throws<ArgumentException>(() => Describable.Fill<HintAttribute>('N', hint => hint.Text)); //the strict name takes none
		Assert.Throws<ArgumentException>(() => Describable.Fill<HintAttribute>('i', hint => hint.Text));
		Assert.Throws<ArgumentException>(() => Describable.Fill<HintAttribute>('V', hint => hint.Text)); //no such slot
	}

	[Fact]
	public void ALetterNoSlotAnswers_ReadsAsTheGeneralText_Marked_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		PatTech.Localization.Words.Logger = logger;

		Assert.Equal("Espresso shot#!Z#", Coffee.Espresso.Describe("Z", Words()));
		Assert.Equal("Plain#!Z#", Coffee.Plain.Describe("Z", Words()));
		Assert.Equal(2, logger.Messages.Count(message => message == "WORDS:SLOT:`Z`"));
	}

	[Fact]
	public void TheSamples_AddTheUnit_ASlotOfTheirOwn() {
		IWords words = WordsBuilder.Create().LoadShared().ToWords("en"); //their words register it

		Assert.Equal("shot", Brew.Espresso.Describe("U", words));
		Assert.Equal("", Brew.Americano.Describe("U", words)); //no key, and no attribute fills it
	}

	[Fact]
	public void AFlagsValue_IsTheMembersDotNetNamesForIt() {
		IWords words = Words();

		Assert.Equal(["sugar and cocoa"], Describable.Members(Toppings.Sugar | Toppings.Cocoa).Describe("G", words)); //the member that covers both
		Assert.Equal(["sugar", "Cream"], Describable.Members(Toppings.Sugar | Toppings.Cream).Describe("G", words));
		Assert.Equal(["sugar and cocoa", "Cream"], Describable.Members(Toppings.Both | Toppings.Cream).Describe("G", words));
		Assert.Equal(["None"], Describable.Members(Toppings.None).Describe("G", words));
		Assert.Equal(["8"], Describable.Members((Toppings)8).Describe("G", words));

		//described whole, a combination still reads as its names and its number
		Assert.Equal("Sugar, Cream", (Toppings.Sugar | Toppings.Cream).Describe("G", words));
		Assert.Equal("5", (Toppings.Sugar | Toppings.Cream).Describe("i", words));
	}

	[Fact]
	public void ARegistration_AfterADescribe_ReadsTheTypeAgain() {
		Assert.Equal("", Late.One.Describe("T", Words()));

		Describable.Fill<LateAttribute>('T', late => late.Text);

		Assert.Equal("late", Late.One.Describe("T", Words()));
	}

	[Fact]
	public void TheCacheHoldsNoWords_SoAnotherLanguageReadsItsOwn() {
		IWords italian = Words("[coffee.espresso]\nvalue=Caffè\n");

		Assert.Equal("Espresso shot", Coffee.Espresso.Describe("G", Words()));
		Assert.Equal("Caffè", Coffee.Espresso.Describe("G", italian));
	}

	[Fact]
	public void AKeyAlone_ReadsItsSlots_WithNoTypeBehindIt() {
		IDescribable espresso = Describable.OfKey("coffee.espresso");

		Assert.Equal("espresso", espresso.Name);
		Assert.Equal("", espresso.Number);
		Assert.Equal("Espresso shot: Short and strong", espresso.Describe("G': 'T", Words()));
		Assert.Equal("cocoa", Describable.OfKey("top.cocoa").Describe("G", Words()));
		Assert.Equal("missing", Describable.OfKey("top.missing").Describe("G", Words())); //no words: its name
	}
}
