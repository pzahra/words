using PatTech.Localization;
using PatTech.Localization.Authoring;
using Xunit;

namespace PatTech.Localization.Tests;
public class WordsParserToLocalizationProviderTests {

	public static WordsParserToLocalizationProvider CreateProvider() {
		//Arrange
		WordsParserToLocalizationProvider consumer = new();
		WordsParser parser = new(consumer);
		const string MockINIFile = @"
value-en-US=English (Simplified)
value-en-GB=English (Traditional)

[key1]
value=Default value
context=context line 1\
second line\
third line
comment=comment
param-0=Double:22
param-one=String:one
param-date=DateTimeOffset:6/13/2023
stale=

value-en-US=US value
context-en-US=US context
comment-en-GB=GB comment
stale-en-GB=2023/05/11 10:30:00 -03:00

value-en-US=Override check
value-en-GB=BritishValue

[$key2]
[key3]
[key4]
";

		//Act
		parser.Load(new StringReader(MockINIFile));

		return consumer;
	}

	[Fact]
	public void WordsParserToLocalizationProvider_KeysHaveAllAndOnlyKnownLanguagesTest() {
		//Arrange+Act
		var provider = CreateProvider();

		//Assert
		var localizationKeyList = provider.WordKeys.Values.ToArray();
		var localizationLanguageDictionary = provider.KnownLanguages;
		bool keysContainAllLanguages = true;
		bool keysContainOnlyKnownLanguages = true;
		int languagesChecked;
		int keyLanguages;

		foreach (WordsKey localizationKey in localizationKeyList) {
			languagesChecked = 0;
			keyLanguages = localizationKey.Entries.Keys.Count;
			foreach (LanguageEntry language in localizationLanguageDictionary.Values) {
				if (!localizationKey.Entries.ContainsKey(language.Code)) {
					keysContainAllLanguages = false;
				}
				languagesChecked++;
			}
			if (keyLanguages != languagesChecked) {
				keysContainOnlyKnownLanguages = false;
			}
		}
		Assert.True(keysContainAllLanguages);
		Assert.True(keysContainOnlyKnownLanguages);
	}

	[Fact]
	public void WordsParser_CommentsAreSemicolonOnly() {
		// `;` starts a comment (even indented, even containing `=`); blank lines
		// are skipped; a continuation line starting with `;` is still content
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(@"
value-en=English

[k]
; a comment
  ; an indented comment
;disabled=not a value
value=kept_
;still the value

[m]
value=other
"));

		Assert.Equal("kept;still the value", consumer.WordKeys["k"].DefaultValue);
		Assert.Equal("other", consumer.WordKeys["m"].DefaultValue);
		Assert.Empty(consumer.Errors);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_UndeclaredLanguageGetsPlaceholderAndGripe() {
		// a code used on fields without a top-of-file label is auto-added with a
		// `!code` placeholder (still selectable) and a gripe; it is NOT part of
		// the file's declared table
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(@"
value-en=English

[k]
value=x
value-de=de-x
"));

		Assert.Equal(["en"], consumer.DeclaredLanguages);
		Assert.True(consumer.KnownLanguages["de"].IsPlaceholder);
		Assert.Equal("!de", consumer.KnownLanguages["de"].NativeName);
		Assert.False(consumer.KnownLanguages["en"].IsPlaceholder);
		Assert.Contains(consumer.Errors, error => error.Contains("'de'"));
		Assert.Equal("de-x", consumer.WordKeys["k"].Entries["de"].Value);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_CommentsAnchorByPosition() {
		// runs in the language section join the preamble; a run above a header
		// banners that block; a run between fields hoists to its block's banner;
		// a run at EOF is the trailer
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(@"; file preamble
value-en=English
; more preamble
value-it=Italiano

; banner k
[k]
value=first
; interior, hoists to k
comment=notes

; banner m line 1
; banner m line 2
[m]
value=other

; trailing remarks
"));

		Assert.Equal(" file preamble\n more preamble", consumer.Preamble);
		Assert.Equal(" banner k\n interior, hoists to k", consumer.BlockComments["k"]);
		Assert.Equal(" banner m line 1\n banner m line 2", consumer.BlockComments["m"]);
		Assert.Equal(" trailing remarks", consumer.Trailer);
		Assert.Empty(consumer.Errors);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_ParamContinuationAppends() {
		// continuation lines on a param- field must extend the parameter's value
		// (this used to be silently dropped by an inverted existence check)
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(@"
value-en=English

[k]
param-x=String:abc_
def
"));

		var parameter = Assert.Single(consumer.WordKeys["k"].Parameters);
		Assert.Equal("x", parameter.Key);
		Assert.Equal("abcdef", parameter.Value);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_TopOfFileParamIsCapturedAsImageSchemeMapping() {
		// keyless param- fields in the language section are an authoring tool's
		// scheme→folder mappings: captured, not dropped, and not turned into keys
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(@"
value-en=English
param-md=icons
param-shot=../captures

[k]
value=x
"));

		Assert.Equal("icons", consumer.ImageSchemeMappings["md"]);
		Assert.Equal("icons", consumer.ImageSchemeMappings["MD"]); // scheme lookup is case-insensitive
		Assert.Equal("../captures", consumer.ImageSchemeMappings["shot"]);
		Assert.Single(consumer.WordKeys);
		Assert.False(consumer.WordKeys.ContainsKey("md"));
		Assert.Empty(consumer.Errors);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_TopOfFileParamContinuationAppends() {
		// a folder path long enough to have wrapped comes back in one piece — the
		// continuation lands in the language section, where there is no block yet
		WordsParserToLocalizationProvider consumer = new();
		new WordsParser(consumer).Load(new StringReader(@"
value-en=English
param-md=first/part_
second/part

[k]
value=x
"));

		Assert.Equal("first/partsecond/part", consumer.ImageSchemeMappings["md"]);
		Assert.Empty(consumer.Errors);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_CreateLanguages() {
		//Arrange+Act
		var provider = CreateProvider();

		//Assert
		var localizationLanguagesDictionary = provider.KnownLanguages;
		Assert.Equal("English (Simplified)", localizationLanguagesDictionary["en-US"].NativeName);
		Assert.Equal("English (Traditional)", localizationLanguagesDictionary["en-GB"].NativeName);
	}

	[Fact]
	public void WordsParserToLocalizationProvider_CreateLocalizationKeyTests() {
		//Arrange+Act
		var provider = CreateProvider();

		//Assert
		var localizationKeyDictionary = provider.WordKeys;
		Assert.Equal("key1", localizationKeyDictionary["key1"].BlockKey);
		Assert.Equal("Default value", localizationKeyDictionary["key1"].DefaultValue);
		Assert.Equal("context line 1\nsecond line\nthird line", localizationKeyDictionary["key1"].Context);
		Assert.Equal("comment", localizationKeyDictionary["key1"].Comment);
		Assert.Equal("0", localizationKeyDictionary["key1"].Parameters[0].Key);
		Assert.Equal(typeof(double), localizationKeyDictionary["key1"].Parameters[0].DataType.DataType);
		Assert.Equal("22", localizationKeyDictionary["key1"].Parameters[0].Value);
		Assert.Equal("one", localizationKeyDictionary["key1"].Parameters[1].Key);
		Assert.Equal(typeof(string), localizationKeyDictionary["key1"].Parameters[1].DataType.DataType);
		Assert.Equal("one", localizationKeyDictionary["key1"].Parameters[1].Value);
		Assert.Equal("date", localizationKeyDictionary["key1"].Parameters[2].Key);
		Assert.Equal(typeof(DateTimeOffset), localizationKeyDictionary["key1"].Parameters[2].DataType.DataType);
		Assert.Equal("6/13/2023", localizationKeyDictionary["key1"].Parameters[2].Value);
		Assert.True(localizationKeyDictionary["key1"].NeedsReview);
		Assert.Equal("Override check", localizationKeyDictionary["key1"].Entries["en-US"].Value);
		Assert.Equal("US context", localizationKeyDictionary["key1"].Entries["en-US"].Context);
		Assert.Equal("GB comment", localizationKeyDictionary["key1"].Entries["en-GB"].Comment);
		Assert.False(localizationKeyDictionary["key1"].HasStaleValue("en-US"));
		Assert.Null(localizationKeyDictionary["key1"].Entries["en-US"].Stale);
		Assert.True(localizationKeyDictionary["key1"].HasStaleValue("en-GB"));
		Assert.Equal(
			DateTimeOffset.Parse("2023 - 05 - 11 10:30:00 -03:00"),
			DateTimeOffset.Parse(localizationKeyDictionary["key1"].Entries["en-GB"].Stale!));
		Assert.True(localizationKeyDictionary["$key2"].IsConstant);
		Assert.False(localizationKeyDictionary["$key2"].NeedsReview);
		Assert.True(localizationKeyDictionary.ContainsKey("key3"));
		Assert.Equal("", localizationKeyDictionary["key4"].DefaultValue);
	}
}
