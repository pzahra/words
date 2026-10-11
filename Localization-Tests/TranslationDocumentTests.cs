using PatTech.Localization.Authoring;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
///     The machine translation document (editor SPEC: Machine translation): what a
///     request asks for and carries, and an answer read back and checked key by
///     key. The command line's calls over it are <see cref="WordsCommandTests"/>'.
/// </summary>
public class TranslationDocumentTests {
	private static string Ini(params string[] lines) => string.Join("\n", lines) + "\n";

	private static readonly string Words = Ini(
		"value=!en",
		"value-en=English",
		"value-it=Italiano",
		"comment-it=Italian",
		"value-mt=Malti",
		"",
		"[files]",
		"context=The status line after Save.",
		"comment=Counted, as the app passes it.",
		"value={0} file saved",
		"value#other={0} files saved",
		"param-0=int:the files saved",
		"value-mt={0} fajl",
		"",
		"[menu.file]",
		"value=__File",
		"",
		"[lines]",
		"value=first\\",
		"second",
		"",
		"[done]",
		"value=Done",
		"value-it=Finito",
		"stale-it=2026-10-01",
		"",
		"[note]",
		"value=See `words.ini` and [the docs](https://example.com/docs) for {>menu.file}",
		"",
		"[$brand]",
		"value=Words");

	private static IniPatcher Patch() => IniPatcher.FromBytes(Encoding.UTF8.GetBytes(Words));

	private static JsonElement Request(string code, params string[] keys) {
		var patcher = Patch();
		string json = TranslationRequest.Write("words.ini", new("en", patcher.LanguageName("en")), new(code, patcher.LanguageName(code)), keys.Select(key => patcher.Find(key)!));
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	private static Dictionary<string, string?> Slots(JsonElement slots) => slots.EnumerateObject().ToDictionary(slot => slot.Name, slot => slot.Value.GetString());

	private static string Counts(string code, string form) => FormNumbers.Describe(code, form, CultureInfo.InvariantCulture);

	[Fact]
	public void ARequest_CarriesEachKeysDefault_ItsNotes_AndANullPerSlot_InTheValuesOwnText() {
		var request = Request("it", "files", "menu.file", "lines");

		Assert.Equal(TranslationRequest.Kind, request.GetProperty("words").GetString());
		Assert.StartsWith("Translate each item's default from English (en) into Italian (it).", request.GetProperty("instructions").GetString());
		Assert.False(request.TryGetProperty("brief", out _));
		Assert.Equal("words.ini", request.GetProperty("file").GetString());
		Assert.Equal(new Dictionary<string, string?> { ["value"] = Counts("en", "one"), ["value#other"] = Counts("en", "other") }, Slots(request.GetProperty("source").GetProperty("forms")));
		Assert.Equal(new Dictionary<string, string?> { ["value"] = Counts("it", "one"), ["value#other"] = Counts("it", "other") }, Slots(request.GetProperty("target").GetProperty("forms")));

		var items = request.GetProperty("items").EnumerateArray().ToList();
		Assert.Equal(["files", "menu.file", "lines"], items.Select(item => item.GetProperty("key").GetString()));
		var files = items[0];
		Assert.Equal(new Dictionary<string, string?> { ["value"] = "{0} file saved", ["value#other"] = "{0} files saved" }, Slots(files.GetProperty("default")));
		Assert.Equal("The status line after Save.", files.GetProperty("context").GetString());
		Assert.Equal("Counted, as the app passes it.", files.GetProperty("comment").GetString());
		Assert.Equal("int", files.GetProperty("parameters").GetProperty("0").GetProperty("type").GetString());
		Assert.Equal("the files saved", files.GetProperty("parameters").GetProperty("0").GetProperty("description").GetString());
		Assert.False(files.TryGetProperty("current", out _));
		Assert.Equal(new Dictionary<string, string?> { ["value"] = null, ["value#other"] = null }, Slots(files.GetProperty("translation")));
		//no doubled _, no continuation: the words as get prints them
		Assert.Equal("_File", items[1].GetProperty("default").GetProperty("value").GetString());
		Assert.False(items[1].TryGetProperty("context", out _));
		Assert.Equal("first\nsecond", items[2].GetProperty("default").GetProperty("value").GetString());
	}

	[Fact]
	public void APluralKey_AsksForEachFormTheTargetRequires_WithTheCountsItReads() {
		var patcher = Patch();

		var request = Request("mt", "files");

		//Maltese counts by two, few, many and other, and only few has no stand-in
		Assert.Equal(["value", "value#few"], TranslationRequest.Slots(patcher.Find("files")!, "mt"));
		var item = request.GetProperty("items")[0];
		Assert.Equal(new Dictionary<string, string?> { ["value"] = "{0} fajl" }, Slots(item.GetProperty("current")));
		Assert.Equal(new Dictionary<string, string?> { ["value#few"] = null }, Slots(item.GetProperty("translation"))); //the plain value has words
		Assert.Equal(new Dictionary<string, string?> { ["value#few"] = Counts("mt", "few") }, Slots(request.GetProperty("target").GetProperty("forms")));
		Assert.Contains("3–10", Counts("mt", "few"));
		Assert.Equal(["value"], TranslationRequest.Slots(patcher.Find("menu.file")!, "mt"));
	}

	[Fact]
	public void AStaleEntry_AsksForEverySlot_WithItsCurrentWords_AndABriefIsPassedOn() {
		var patcher = Patch();
		string json = TranslationRequest.Write("words.ini", new("en", null), new("it", null), [patcher.Find("done")!, patcher.Find("$brand")!, patcher.Find("note")!], "Informal: tu.");
		using var document = JsonDocument.Parse(json);
		var request = document.RootElement;

		Assert.True(TranslationRequest.Stale(patcher.Find("done")!, "it"));
		Assert.False(TranslationRequest.Stale(patcher.Find("$brand")!, "it"));
		Assert.Equal("Informal: tu.", request.GetProperty("brief").GetString());
		Assert.StartsWith("Translate each item's default from en into it.", request.GetProperty("instructions").GetString());
		Assert.False(request.GetProperty("source").TryGetProperty("name", out _));
		Assert.False(request.GetProperty("target").TryGetProperty("forms", out _)); //no key with forms to read them for
		var done = request.GetProperty("items")[0];
		Assert.Equal(new Dictionary<string, string?> { ["value"] = "Finito" }, Slots(done.GetProperty("current")));
		Assert.Equal(new Dictionary<string, string?> { ["value"] = null }, Slots(done.GetProperty("translation")));
		Assert.Equal(["done", "note"], request.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("key").GetString())); //a constant never goes
		Assert.DoesNotContain("\r", json);
	}

	[Fact]
	public void AnAnswer_IsReadFromItsFirstBraceToItsLast_PastAFence_ACommentAndTrailingCommas() {
		string text = """
			Here is the document:
			```json
			{
			  "words": "translation/1", // as asked
			  "target": { "code": "it" },
			  "items": [
			    { "key": "a", "default": { "value": "A" }, "translation": { "value": "Ah", "value#other": null, }, },
			  ],
			}
			```
			""";

		var answer = TranslationAnswer.Read(text);

		Assert.Equal("it", answer.Target);
		var item = Assert.Single(answer.Items);
		Assert.Equal("a", item.Key);
		Assert.Equal(new Dictionary<string, string> { ["value"] = "A" }, item.Default);
		Assert.Empty(item.Current);
		Assert.Equal(new Dictionary<string, string?> { ["value"] = "Ah", ["value#other"] = null }, item.Translation);
	}

	[Theory]
	[InlineData("no JSON here", "no JSON object")]
	[InlineData("{ \"words\": ", "no JSON")]
	[InlineData("""{ "words": "translation/2", "target": { "code": "it" }, "items": [] }""", "no translation document")]
	[InlineData("""{ "words": "translation/1", "items": [] }""", "no target.code")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" } }""", "no items")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" }, "items": [ { "translation": {} } ] }""", "item 0 names no key")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" }, "items": [ { "key": "a", "translation": { "value": 1 } } ] }""", "a: the translation's value is no text")]
	public void WhatIsNoTranslationDocument_IsRefused_SayingWhy(string text, string why) {
		var refused = Assert.Throws<InvalidDataException>(() => TranslationAnswer.Read(text));

		Assert.Contains(why, refused.Message);
	}

	//the answer for key as the request carried it, with these slots filled
	private static TranslationVerdict Check(string key, string code, Dictionary<string, string?> translation, Action<IniPatcher>? since = null) {
		var patcher = Patch();
		var sent = patcher.Find(key);
		var item = new TranslationItem(key, sent is null ? new Dictionary<string, string>() : TranslationRequest.Held(sent),
			TranslationRequest.Held(sent?.Entries.GetValueOrDefault(code)), translation);
		since?.Invoke(patcher);
		var keys = patcher.Keys.ToDictionary(found => found.BlockKey);
		return item.Check(keys.GetValueOrDefault(key), code, new DefaultWordsProvider(keys, []), patcher.DefaultLanguage, new LanguageWordsProvider(keys, code, []));
	}

	[Fact]
	public void AFilledAnswer_WritesItsSlots_AndLeavesItsNulls() {
		var full = Check("files", "it", new() { ["value"] = "{0} file salvato", ["value#other"] = "{0} file salvati" });
		var half = Check("files", "it", new() { ["value"] = "{0} file salvato", ["value#other"] = null });
		var blank = Check("note", "it", new() { ["value"] = " " });

		Assert.Equal([(new WordsField("value", "it", ""), "{0} file salvato"), (new WordsField("value", "it", "other"), "{0} file salvati")], full.Writes);
		Assert.Empty(full.Flags);
		Assert.Equal(0, full.Left);
		Assert.Single(half.Writes);
		Assert.Equal(1, half.Left);
		Assert.Equal((0, 0, 1), (blank.Writes.Count, blank.Flags.Count, blank.Left)); //blank is no translation
	}

	[Theory]
	[InlineData("files", "value", "file salvati", "drop {0}, which the default uses")]
	[InlineData("files", "value", "{0} file in {1}", "use {1}, which the default does not")]
	[InlineData("note", "value", "Vedi `words.ini` e [i documenti](https://example.com/docs) per {>menu.archivio}", "drop {>menu.file}, which the default holds")]
	[InlineData("note", "value", "Vedi `words.ini` e [i documenti](https://example.com/docs) per {>menu.file} {>menu.archivio}", "hold {>menu.archivio}, which the default does not")]
	[InlineData("note", "value", "Vedi words.ini e [i documenti](https://example.com/docs) per {>menu.file}", "drop the code span `words.ini`")]
	[InlineData("note", "value", "Vedi `words.ini` e [i documenti](https://example.com/it/docs) per {>menu.file}", "drop the link to https://example.com/docs")]
	[InlineData("files", "value#few", "{0} file", "value#few is no slot it has in it")]
	[InlineData("menu.file", "value#other", "_File", "value#other is no slot it has in it")]
	public void AnAnswerThatBreaksATokenOrASlot_IsFlagged_AndWritesNothing(string key, string slot, string text, string flag) {
		var verdict = Check(key, "it", new() { [slot] = text });

		Assert.Empty(verdict.Writes);
		Assert.Contains(verdict.Flags, said => said.Contains(flag));
	}

	[Fact]
	public void AKeyThatChangedSinceTheRequest_OrWent_IsFlagged_UnlessNothingIsFilled() {
		Dictionary<string, string?> filled = new() { ["value"] = "Fatto" };

		Assert.Contains("its default changed since the request", Check("done", "it", filled, patcher => patcher.Set("done", new("value", "", ""), "Finished")).Flags);
		Assert.Contains("its it words changed since the request", Check("done", "it", filled, patcher => patcher.Set("done", new("value", "it", ""), "Completato")).Flags);
		Assert.Contains("no such key now", Check("done", "it", filled, patcher => patcher.Remove("done")).Flags);
		Assert.Contains("a constant, which is not translated", Check("$brand", "it", filled).Flags);
		var untouched = Check("done", "it", new() { ["value"] = null }, patcher => patcher.Remove("done"));
		Assert.Equal((0, 0, 1), (untouched.Writes.Count, untouched.Flags.Count, untouched.Left));
	}
}
