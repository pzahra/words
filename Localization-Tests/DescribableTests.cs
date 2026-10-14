using System.ComponentModel;
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

/// <summary>A subclass of [Words], naming one key.</summary>
public sealed class EspressoAttribute() : WordsAttribute("coffee.espresso");

/// <summary>A subclassed [Description], as an app's translated one commonly is.</summary>
public sealed class ShoutedDescriptionAttribute(string text) : DescriptionAttribute {
	public override string Description => text.ToUpperInvariant();
}

#pragma warning disable CS0618 // the built-in migration aid, subclassed
/// <summary>A subclass of [Tooltip].</summary>
public sealed class HelpAttribute(string text) : TooltipAttribute(text);
#pragma warning restore CS0618

public enum Derived {
	[Espresso] Keyed,
	[ShoutedDescription("a long black")] Described,
	[Help("Some help")] Helped,
}

/// <summary>A subclassed [Description] with a property of its own, registered to read it.</summary>
public sealed class DetailedDescriptionAttribute(string text, string detail) : DescriptionAttribute(text) {
	public string Detail { get; } = detail;
}

public enum Detailed {
	[DetailedDescription("The plain text", "The detail")] One,
}

/// <summary>Another system's attribute, holding its key where [Description] holds text.</summary>
public sealed class ResourceDescriptionAttribute(string resource) : DescriptionAttribute(resource) {
	public string Resource { get; } = resource;
}

public enum Migrated {
	[ResourceDescription("Coffee_Espresso")] Espresso,
	[ResourceDescription("Tea_Green")] Green,
}

/// <summary>Plain [Description]s that held keys all along.</summary>
public enum Legacy {
	[Description("coffee.espresso")] Espresso,
	[Description("tea.green")] Green,
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

public enum Milk {
	Whole,
	Oat,
}

public enum Syrup {
	Maple,
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
///     the type). Each test registers on a builder of its own, and the dictionary it makes
///     carries the registry; the ones that read <see cref="Words.Known"/> or its logger
///     are in the globals' collection.
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

	private static IWords Words(string script = Script) => WordsBuilder.Create().LoadString(script).ToWords("en");

	//words describing by what describe registers
	private static IWords Words(Action<IDescribeBuilder> describe, string script = Script)
		=> WordsBuilder.Create().ConfigureDescribe(describe).LoadString(script).ToWords("en");

	//the keys looked up, in order
	private sealed class Counting(IWords inner) : IWords {
		public List<string> Asked { get; } = [];
		public IWordsProvider Provider => inner.Provider;
		public DescribeRegistry DescribeRegistry => inner.DescribeRegistry;
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
		Assert.Equal("Espresso shot", Coffee.Espresso.Describe("N", words));
		Assert.Equal("Hot water through fine grounds", Coffee.Espresso.Describe("d", words));
		Assert.Equal("A small cup", Coffee.Espresso.Describe("S", words));
		Assert.Equal("Short and strong", Coffee.Espresso.Describe("T", words));
		Assert.Equal("Espresso", Coffee.Espresso.Describe("s", words)); //not the obsolete name sharing its value
		Assert.Equal("0", Coffee.Espresso.Describe("D", words));          //the number, as Enum reads D
		Assert.Equal(Coffee.Plain.ToString("D"), Coffee.Plain.Describe("D", words));
		Assert.Equal("Espresso shot", Coffee.Espresso.Describe(null, words));

		//a Description and no key: the general text falls back to it, the strict name does not
		Assert.Equal("A long black", Coffee.Americano.Describe("G", words));
		Assert.Equal("Americano", Coffee.Americano.Describe("N", words));
		Assert.Equal("A long black", Coffee.Americano.Describe("d", words));
		Assert.Equal("", Coffee.Americano.Describe("T", words));
		Assert.Equal("Plain", Coffee.Plain.Describe("G", words));
		Assert.Equal("2", Coffee.Plain.Describe("D", words));

		//quoted text and anything that is no letter are written as they are
		Assert.Equal("Espresso shot - A small cup", Coffee.Espresso.Describe("G' - 'S", words));
		Assert.Equal("Espresso shot (A small cup)", Coffee.Espresso.Describe("G (S)", words));
		Assert.Equal("it's Espresso", Coffee.Espresso.Describe("'it''s 's", words));
	}

	//a subclass reads as the attribute it derives from, its overrides included
	[Fact]
	public void ASubclassedBuiltIn_ReadsAsItsBase() {
		IWords words = Words();

		Assert.Equal("Espresso shot", Derived.Keyed.Describe("G", words));
		Assert.Equal("Short and strong", Derived.Keyed.Describe("T", words));
		Assert.Equal("A LONG BLACK", Derived.Described.Describe("G", words));
		Assert.Equal("A LONG BLACK", Derived.Described.Describe("d", words));
		Assert.Equal("Some help", Derived.Helped.Describe("S", words));
		Assert.Equal("Some help", Derived.Helped.Describe("T", words));
	}

	//registered itself, a subclass is read as itself alone, though [Description] came
	//first: its base's reading may not fit it
	[Fact]
	public void ARegisteredSubclass_IsReadAsItselfAlone() {
		var builder = WordsBuilder.Create().ConfigureDescribe(describe => describe.Fill<DetailedDescriptionAttribute>('T', detailed => detailed.Detail));
		IWords words = builder.ToWords("en");
		Assert.Equal("The detail", Detailed.One.Describe("T", words));
		Assert.Equal("", Detailed.One.Describe("d", words)); //not [Description]'s reading

		words = builder.ConfigureDescribe(describe => describe.Fill<DetailedDescriptionAttribute>('d', detailed => detailed.Detail)).ToWords("en");
		Assert.Equal("The detail", Detailed.One.Describe("d", words));
		Assert.Equal("The detail", Detailed.One.Describe("G", words)); //the general text falls back to it
	}

	//migrating from another system, its attribute holding a key in [Description]'s text:
	//registered as naming the key, mapped onto this one's, the source stays as it is,
	//and the old key never shows as a description
	[Fact]
	public void AnotherSystemsKey_HeldInADescription_IsReadAsTheKey_AndNeverShown() {
		Assert.Equal("Tea_Green", Migrated.Green.Describe("d", Words())); //unregistered, it is a [Description], as in 1.5.0

		IWords words = Words(describe => describe.FillKey<ResourceDescriptionAttribute>(resource => resource.Resource.Replace('_', '.').ToLowerInvariant()));
		Assert.Equal("Espresso shot", Migrated.Espresso.Describe("G", words));
		Assert.Equal("Hot water through fine grounds", Migrated.Espresso.Describe("d", words)); //the key's .desc
		Assert.Equal("Green tea", Migrated.Green.Describe("G", words));
		Assert.Equal("", Migrated.Green.Describe("d", words)); //no .desc, and the old key is no description
	}

	//a [Description] that held a key all along: the app's registration replaces Words'
	//reading of it as text, and the app's next one adds to its own
	[Fact]
	public void ABuiltInRegisteredByTheApp_IsReadAsTheAppSays() {
		Assert.Equal("tea.green", Legacy.Green.Describe("d", Words())); //text, by default

		var builder = WordsBuilder.Create().LoadString(Script).ConfigureDescribe(describe => describe.FillKey<DescriptionAttribute>(description => description.Description));
		IWords words = builder.ToWords("en");
		Assert.Equal("Espresso shot", Legacy.Espresso.Describe("G", words));
		Assert.Equal("Hot water through fine grounds", Legacy.Espresso.Describe("d", words)); //the key's .desc
		Assert.Equal("Green tea", Legacy.Green.Describe("G", words));
		Assert.Equal("", Legacy.Green.Describe("d", words)); //the key is no description

		words = builder.ConfigureDescribe(describe => describe.Fill<DescriptionAttribute>('T', description => $"see {description.Description}")).ToWords("en");
		Assert.Equal("see tea.green", Legacy.Green.Describe("T", words));
		Assert.Equal("Green tea", Legacy.Green.Describe("G", words)); //still the key
		Assert.Equal("A long black", Coffee.Americano.Describe("d", Words())); //another builder reads Words' own
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
		Coffee.Espresso.Describe("sD", words); //the name and the number read no key
		Assert.Empty(words.Asked);
	}

	[Fact]
	public void AMember_IsReadOnce_HoweverOftenItIsDescribed_ByEveryDictionaryOfOneRegistry() {
		var builder = WordsBuilder.Create().LoadString(Script).ConfigureDescribe(describe => describe.Fill<CountedAttribute>('T', counted => counted.Text));
		int before = CountedAttribute.Reads;

		for (int i = 0; i < 5; i++) {
			Assert.Equal("counted", Counted.One.Describe("T", builder.ToWords(i % 2 == 0 ? "en" : "it")));
		}
		Assert.Equal(1, CountedAttribute.Reads - before);
	}

	[Fact]
	public void ARegisteredAttribute_FillsItsSlot_AndTheFirstRegisteredWins() {
		IWords words = Words(describe => describe
			.Fill<HintAttribute>((char)DescribeSlot.Tooltip, hint => hint.Text)
			.Fill<LaterHintAttribute>('T', hint => hint.Text));

		Assert.Equal("Use the hint", Hinted.Hint.Describe("T", words));
		Assert.Equal("", Hinted.Ignored.Describe("T", words));            //nobody registered it
		Assert.Equal("The built-in one", Hinted.Both.Describe("T", words)); //[Tooltip] came first
		Assert.Equal("The built-in one", Hinted.Both.Describe("S", words)); //one attribute, two slots
		Assert.Equal("Registered first", Hinted.Two.Describe("T", words));
	}

	[Fact]
	public void TheSlotsLetters_AreTheirFormatsLetters_OneApiece() {
		IWords words = Words(describe => describe
			.Fill<NoteAttribute>((char)DescribeSlot.Description, note => note.Text)
			.Fill<GistAttribute>((char)DescribeSlot.General, gist => gist.Text));

		Assert.Equal('G', (char)DescribeSlot.General);
		Assert.Equal('d', (char)DescribeSlot.Description);
		Assert.Equal('D', (char)DescribeSlot.Number);
		Assert.Equal("A note", Hinted.Noted.Describe("d", words));
		Assert.Equal("4", Hinted.Noted.Describe("D", words)); //the number, never the description
		Assert.Equal("The gist", Hinted.Noted.Describe("G", words));
		Assert.Throws<ArgumentException>(() => Words(describe => describe.Fill<GistAttribute>('n', gist => gist.Text))); //no slot's
	}

	[Fact]
	public void AKeyPrefix_KeysTheMembersWithNone_AndAKeyAttributeOfOnesOwn_ComesAfterWords() {
		IWords words = Words(describe => describe
			.FillKey<KeyedAttribute>(keyed => keyed.Key)
			.Keys<Tea>("tea.leaf"));

		Assert.Equal("Green tea", Tea.Green.Describe("G", words)); //[Words] wins over the app's own
		Assert.Equal("Keyed tea", Tea.Keyed.Describe("G", words)); //the app's own wins over the prefix
		Assert.Equal("Black tea", Tea.Black.Describe("G", words)); //tea.leaf.Black
		Assert.Throws<ArgumentException>(() => Words(describe => describe.Keys<Tea>("no key")));
	}

	[Fact]
	public void AFunction_KeysAnEnumOneDoesNotOwn_MemberByMember_OnceEach() {
		int asked = 0;
		IWords words = Words(describe => describe.Keys<DayOfWeek>(day => {
			asked++;
			return day switch {
				DayOfWeek.Saturday or DayOfWeek.Sunday => "days.weekend",
				DayOfWeek.Wednesday => null,
				_ => $"days.{day}",
			};
		}), """
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

	//four first describes at once build the type once, the function asked once a member
	[Fact]
	public async Task TheFirstDescribesOfAType_RunTogether_BuildItOnce() {
		int asked = 0;
		IWords words = Words(describe => describe.Keys<Milk>(milk => {
			Interlocked.Increment(ref asked);
			Thread.Sleep(100); //holds the build open while the others arrive
			return $"milk.{milk}";
		}), """
			[milk.Oat]
			value=Oat milk
			""");

		using var start = new Barrier(4);
		string[] read = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() => {
			start.SignalAndWait();
			return Milk.Oat.Describe("G", words);
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));

		Assert.All(read, text => Assert.Equal("Oat milk", text));
		Assert.Equal(2, asked);
	}

	//a build that threw is not kept: the next describe tries again
	[Fact]
	public void AFirstDescribeThatThrew_IsNotKept() {
		bool fail = true;
		IWords words = Words(describe => describe.Keys<Syrup>(syrup => fail ? throw new InvalidOperationException("not yet") : $"syrup.{syrup}"), """
			[syrup.Maple]
			value=Maple syrup
			""");

		Assert.Throws<InvalidOperationException>(() => Syrup.Maple.Describe("G", words));
		fail = false;
		Assert.Equal("Maple syrup", Syrup.Maple.Describe("G", words));
	}

	[Fact]
	public void AFunction_KeysOnlyTheMembersWithNone_AndTheLatestForAType_Stands() {
		IWords words = Words(describe => describe
			.Keys<Juice>(juice => $"juice.first.{juice}")
			.Keys<Juice>(juice => juice == Juice.Pear ? null : $"juice.picked.{juice}"), """
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
		IWords words = Words(describe => describe
			.Slot('H', ".hint")
			.Fill<HintAttribute>('H', hint => hint.Text));

		Assert.Equal("Drink it standing", Coffee.Espresso.Describe("H", words));
		Assert.Equal("Use the hint", Hinted.Hint.Describe("H", words)); //no key: the attribute
		Assert.Equal("Espresso shot: Drink it standing", Coffee.Espresso.Describe("G': 'H", words));
	}

	[Fact]
	public void ALetterTaken_OrNoLetter_OrNoSuffix_IsRefused() {
		var builder = WordsBuilder.Create().ConfigureDescribe(describe => describe.Slot('J', ".j"));
		void refused(Action<IDescribeBuilder> describe) => Assert.Throws<ArgumentException>(() => builder.ConfigureDescribe(describe));

		refused(describe => describe.Slot('J', ".k"));    //added already
		refused(describe => describe.Slot('T', ".tip"));  //built in
		refused(describe => describe.Slot('N', ".n"));    //the strict name's
		refused(describe => describe.Slot('=', ".eq"));   //no letter
		refused(describe => describe.Slot('K', "k"));     //no dot
		refused(describe => describe.Slot('K', ".a.b"));  //more than a segment
		refused(describe => describe.Fill<HintAttribute>('N', hint => hint.Text)); //the strict name takes none
		refused(describe => describe.Fill<HintAttribute>('D', hint => hint.Text)); //the number takes none
		refused(describe => describe.Slot('D', ".d"));    //the number's
		refused(describe => describe.Fill<HintAttribute>('V', hint => hint.Text)); //no such slot
	}

	//what the callback registers is kept only once it returns: a refusal keeps none of it
	[Fact]
	public void ACallbackRefused_LeavesTheBuilderAsItWas() {
		var builder = WordsBuilder.Create().LoadString(Script);

		Assert.Throws<ArgumentException>(() => builder.ConfigureDescribe(describe => describe.Slot('H', ".hint").Slot('T', ".tip")));
		using (var globals = new WordsGlobals()) {
			PatTech.Localization.Words.Logger = new CaptureLogger();
			Assert.Equal("Espresso shot#!H#", Coffee.Espresso.Describe("H", builder.ToWords("en"))); //no H
		}

		builder.ConfigureDescribe(describe => describe.Slot('H', ".hint")); //not added already
		Assert.Equal("Drink it standing", Coffee.Espresso.Describe("H", builder.ToWords("en")));
	}

	//nothing registers into a registry once it is frozen: what the callback was handed
	//refuses once it returns
	[Fact]
	public void WhatTheCallbackWasHanded_RefusesOnceItReturns() {
		IDescribeBuilder? kept = null;
		IWords words = Words(describe => kept = describe.Slot('H', ".hint"));

		Assert.Throws<InvalidOperationException>(() => kept!.Fill<HintAttribute>('H', hint => hint.Text));
		Assert.Equal("Drink it standing", Coffee.Espresso.Describe("H", words));
		Assert.Equal("", Hinted.Hint.Describe("H", words)); //no attribute fills it
	}

	[Fact]
	public void ALetterNoSlotAnswers_ReadsAsTheGeneralText_Marked_AndWarns() {
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		PatTech.Localization.Words.Logger = logger;

		Assert.Equal("Espresso shot#!Z#", Coffee.Espresso.Describe("Z", Words()));
		Assert.Equal("Plain#!Z#", Coffee.Plain.Describe("Z", Words()));
		Assert.Equal(2, logger.Messages.Count(message => message == "WORDS:SLOT:`Z`"));
		Assert.Equal("Espresso shot#!i#", Coffee.Espresso.Describe("i", Words())); //retired for D
		Assert.Equal("Espresso shot#!n#", Coffee.Espresso.Describe("n", Words())); //retired: G is the one
		Assert.Contains("WORDS:SLOT:`i`", logger.Messages);
		Assert.Contains("WORDS:SLOT:`n`", logger.Messages);
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
		Assert.Equal("5", (Toppings.Sugar | Toppings.Cream).Describe("D", words));
	}

	//a dictionary keeps the registry it was made with: a later registration reads in
	//the dictionaries made after it
	[Fact]
	public void ARegistration_AfterADictionary_ReadsInTheNextOneAlone() {
		var builder = WordsBuilder.Create().LoadString(Script);
		IWords before = builder.ToWords("en");
		Assert.Equal("", Late.One.Describe("T", before));

		IWords after = builder.ConfigureDescribe(describe => describe.Fill<LateAttribute>('T', late => late.Text)).ToWords("en");

		Assert.Equal("late", Late.One.Describe("T", after));
		Assert.Equal("", Late.One.Describe("T", before));
	}

	//two dictionaries side by side, each describing by its own registrations alone
	[Fact]
	public void TwoDictionaries_EachDescribeByTheirOwnRegistrations() {
		IWords hinted = Words(describe => describe.Slot('H', ".hint").Fill<HintAttribute>('T', hint => hint.Text));
		IWords plain = Words();

		Assert.Equal("Use the hint", Hinted.Hint.Describe("T", hinted));
		Assert.Equal("", Hinted.Hint.Describe("T", plain));
		Assert.Equal("Drink it standing", Coffee.Espresso.Describe("H", hinted));
		using var globals = new WordsGlobals();
		PatTech.Localization.Words.Logger = new CaptureLogger();
		Assert.Equal("Espresso shot#!H#", Coffee.Espresso.Describe("H", plain)); //no slot H here
	}

	//Describe without words reads Known's, the registry with them; a live switch
	//digests from the builder, so the registrations come along
	[Fact]
	public void WithoutWords_DescribeReadsKnowns_AndALiveSwitchKeepsItsRegistry() {
		using var globals = new WordsGlobals();
		WordsBuilder.Create()
			.ConfigureDescribe(describe => describe.Slot('H', ".hint").Fill<HintAttribute>('T', hint => hint.Text))
			.LoadString(Script)
			.LoadString("[coffee.espresso.hint]\nvalue-it=Bevilo in piedi\n")
			.Live()
			.Digest("en");

		Assert.Equal("Use the hint", Hinted.Hint.Describe("T"));
		Assert.Equal("Drink it standing", Coffee.Espresso.Describe("H"));

		PatTech.Localization.Words.SwitchLanguage("it");
		Assert.Equal("Use the hint", Hinted.Hint.Describe("T"));
		Assert.Equal("Bevilo in piedi", Coffee.Espresso.Describe("H"));
	}

	//a dictionary of one's own that does not say describes by Words' readings alone
	[Fact]
	public void ADictionaryThatDoesNotSay_DescribesByWordsOwnReadings() {
		IWords echo = new EchoWords();

		Assert.Equal("A long black", Coffee.Americano.Describe("d", echo));
		Assert.Equal("", Hinted.Hint.Describe("T", echo));
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

	private const string Templates = Script + """

		[order]
		value={0}: {0:T} ({0:D})
		[.named]
		value={Brew:S}, x{Count}
		[.stray]
		value={0:X}
		""";

	[Fact]
	public void ATemplate_DescribesAnEnum_ItsFormatDescribesLetters() {
		// runtime SPEC: Describe in a template. Format hands an enum argument over as
		// its describable, so {0} reads its words and a letter reads a slot
		IWords words = Words(Templates);

		Assert.Equal("Espresso shot: Short and strong (0)", words.Format("order", Coffee.Espresso));
		Assert.Equal("A long black:  (1)", words.Format("order", Coffee.Americano)); //its Description, and no tooltip
		Assert.Equal("Sugar, Cream:  (5)", words.Format("order", Toppings.Sugar | Toppings.Cream)); //no member: its own name
		Assert.Equal("A small cup, x2", words.FormatByName("order.named", new Dictionary<string, object?> { ["Brew"] = Coffee.Espresso, ["Count"] = 2 }));
		Assert.Equal("A small cup, x2", words.FormatParams("order.named", new { Brew = Coffee.Espresso, Count = 2 }));
		Assert.Equal("Espresso shot: Short and strong (0)", words.RenderText("{0}: {0:T} ({0:D})", null, [Coffee.Espresso]));
	}

	[Fact]
	public void ATemplate_DescribesInTheWordsItCameFrom() {
		IWords italian = Words("[coffee.espresso]\nvalue=Caffè\n[.tooltip]\nvalue=Corto e forte\n[order]\nvalue={0}: {0:T}\n");

		Assert.Equal("Caffè: Corto e forte", italian.Format("order", Coffee.Espresso));
		Assert.Equal("Caffè: Corto e forte", italian.Format("order", Describable.OfKey("coffee.espresso"))); //a describable as it is
	}

	[Fact]
	public void ATemplateWithoutADictionary_DescribesWhatNeedsNone() {
		Assert.Equal("Espresso is 0", PatTech.Localization.Words.FormatByName("{0:s} is {0:D}", null, Coffee.Espresso));
		Assert.Equal("A long black", PatTech.Localization.Words.FormatByName("{Brew}", new { Brew = Coffee.Americano }));
	}

	[Fact]
	public void ATemplatesEnumLetter_NoSlotAnswers_IsStray() {
		// Enum's own X and F are no slot's: the letters are Describe's now
		using var globals = new WordsGlobals();
		var logger = new CaptureLogger();
		PatTech.Localization.Words.Logger = logger;

		Assert.Equal("Espresso shot#!X#", Words(Templates).Format("order.stray", Coffee.Espresso));
		Assert.Contains("WORDS:SLOT:`X`", logger.Messages);
	}
}
