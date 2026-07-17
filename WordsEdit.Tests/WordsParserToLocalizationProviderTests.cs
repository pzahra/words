using PatTech.Localization;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;
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
