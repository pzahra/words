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
		"[size]",
		"value={0:N1} MB",
		"",
		"[tick]",
		"value=Type ``a`b`` or see [the docs](https://example.com/docs \"The guide\")",
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
		//up top, the translator's own name to fill, which the instructions ask for
		Assert.Equal(["words", "translator"], request.EnumerateObject().Take(2).Select(property => property.Name));
		Assert.Equal(JsonValueKind.Null, request.GetProperty("translator").ValueKind);
		Assert.Contains("Put your own name in \"translator\"", request.GetProperty("instructions").GetString());
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

	//the name a translator gives itself, on one line and cut short, follows the mark; none,
	//a blank or anything but text leaves the mark as it is, and costs the answer nothing
	[Theory]
	[InlineData("\"GPT-5\"", "GPT-5")]
	[InlineData("\"  Claude\\n  Opus 5.5 \"", "Claude Opus 5.5")]
	[InlineData("null", null)]
	[InlineData("\"  \"", null)]
	[InlineData("5", null)]
	public void TheTranslatorsName_FollowsTheMark(string json, string? name) {
		var answer = TranslationAnswer.Read($$"""{ "words": "translation/1", "translator": {{json}}, "target": { "code": "it" }, "items": [] }""");

		Assert.Equal(name, answer.Translator);
		Assert.Equal(name is null ? "machine translated" : $"machine translated: {name}", answer.MarkOf("machine translated"));
	}

	[Fact]
	public void ATranslatorsLongName_IsCut() {
		var answer = TranslationAnswer.Read($$"""{ "words": "translation/1", "translator": "{{new string('x', 200)}}", "target": { "code": "it" }, "items": [] }""");

		Assert.Equal(new string('x', TranslationAnswer.TranslatorLength), answer.Translator);
	}

	[Theory]
	[InlineData("no JSON here", "no JSON object")]
	[InlineData("{ \"words\": ", "no JSON")]
	[InlineData("""{ "words": "translation/2", "target": { "code": "it" }, "items": [] }""", "no translation document")]
	[InlineData("""{ "words": "translation/1", "items": [] }""", "no target.code")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" } }""", "no items")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" }, "items": [ { "translation": {} } ] }""", "item 0 names no key")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" }, "items": [ { "key": "a", "translation": { "value": 1 } } ] }""", "a: the translation's value is no text")]
	//a lone surrogate, which JSON escapes as readily as a character, is no text
	[InlineData("""{ "words": "translation/1", "target": { "code": "it" }, "items": [ { "key": "a", "translation": { "value": "\ud800" } } ] }""", "the answer's item 0 holds broken Unicode")]
	[InlineData("""{ "words": "translation/1", "target": { "code": "\udfff" }, "items": [] }""", "the answer holds broken Unicode")]
	public void WhatIsNoTranslationDocument_IsRefused_SayingWhy(string text, string why) {
		var refused = Assert.Throws<InvalidDataException>(() => TranslationAnswer.Read(text));

		Assert.Contains(why, refused.Message);
	}

	//a model's answer that dropped a quote: where, in the text as given, past the sentence
	//above it, a character not a byte, the line counted from 1
	[Fact]
	public void AnAnswerThatIsNoJson_SaysWhereItStops_InTheTextAsGiven() {
		string text = "Ecco:\n{ \"words\": \"translation/1\",\n  \"target\": { \"code\": \"it\" },\n  \"items\": [ { \"key\": \"é\", \"translation\": { \"value\": Il ruolo } } ] }";

		var refused = Assert.Throws<InvalidDataException>(() => TranslationAnswer.Read(text));

		int column = text.Split('\n')[3].IndexOf("Il ruolo", StringComparison.Ordinal) + 1;
		Assert.StartsWith($"the answer is no JSON at line 4, column {column}: ", refused.Message);
		var (at, reason) = TranslationAnswer.FaultOf(refused)!.Value;
		Assert.Equal(text.IndexOf("Il ruolo", StringComparison.Ordinal), at);
		Assert.Equal(refused.Message[$"the answer is no JSON at line 4, column {column}: ".Length..], reason);
		Assert.DoesNotContain("LineNumber", refused.Message);
		Assert.Null(TranslationAnswer.FaultOf(Assert.Throws<InvalidDataException>(() => TranslationAnswer.Read("no JSON here"))));
	}

	//the answer for key as the request carried it, with these slots filled
	private static TranslationVerdict Check(string key, string code, Dictionary<string, string?> translation, Action<IniPatcher>? since = null) {
		var patcher = Patch();
		var sent = patcher.Find(key);
		var item = new TranslationItem(key, sent is null ? new Dictionary<string, string>() : TranslationRequest.Held(sent),
			TranslationRequest.Held(sent?.Entries.GetValueOrDefault(code)), translation);
		since?.Invoke(patcher);
		var keys = patcher.Keys.ToDictionary(found => found.BlockKey);
		return item.Check(keys.GetValueOrDefault(key), code, new DefaultWordsProvider(keys), patcher.DefaultLanguage, new LanguageWordsProvider(keys, code));
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
	//markdown as the app reads it: a span fenced by two backticks, a link with a title
	[InlineData("tick", "value", "Scrivi `a` o vedi [la guida](https://example.com/docs \"La guida\")", "drop the code span ``a`b``")]
	[InlineData("tick", "value", "Scrivi ``a`b`` o vedi la guida", "the it value holds no link where the default holds 1 link")]
	[InlineData("note", "value", "Vedi `words.ini` e [i documenti](https://example.com/docs) o [qui](https://example.com/docs) per {>menu.file}", "the it value holds 2 links where the default holds 1 link")]
	[InlineData("note", "value", "Vedi `words.ini` e [i <https://example.com> documenti](https://example.com/docs) per {>menu.file}", "the it value holds markdown the app cannot read")]
	[InlineData("files", "value#few", "{0} file", "value#few is no slot it has in it")]
	[InlineData("menu.file", "value#other", "_File", "value#other is no slot it has in it")]
	//a format the app marks, its argument not taking it, or one the default does not give
	[InlineData("files", "value", "{0:Q} file salvato", "hold {0:Q}, which the default does not")]
	[InlineData("size", "value", "{0} MB", "drop {0:N1}, which the default holds")]
	[InlineData("size", "value", "{0:N2} MB", "hold {0:N2}, which the default does not")]
	[InlineData("size", "value", "{0,8:N1} MB", "hold {0,8:N1}, which the default does not")]
	//a brace that opens nothing, which the app shows as written
	[InlineData("size", "value", "{0:N1} MB {", "the it value holds `{`, a brace that opens nothing")]
	[InlineData("files", "value#other", "{0} file {0:{>fmt}}", "the it value#other holds `{0:{>fmt}}`, a brace that opens nothing")]
	public void AnAnswerThatBreaksATokenOrASlot_IsFlagged_AndWritesNothing(string key, string slot, string text, string flag) {
		var verdict = Check(key, "it", new() { [slot] = text });

		Assert.Empty(verdict.Writes);
		Assert.Contains(verdict.Flags, said => said.Contains(flag));
	}

	//some pages have one per language, so a link's target may change, but no service
	//decides that alone: the key is written with its hand raised, the change named
	[Fact]
	public void AChangedLinkTarget_IsWritten_RaisingTheKeysHand() {
		var moved = Check("tick", "it", new() { ["value"] = "Scrivi ``a`b`` o vedi [la guida](https://example.com/it/docs \"La guida\")" });
		var kept = Check("tick", "it", new() { ["value"] = "Scrivi `` a`b `` o vedi [la guida](https://example.com/docs \"La guida\")" });

		Assert.Single(moved.Writes);
		Assert.Empty(moved.Flags);
		Assert.Equal(["the it words link to https://example.com/it/docs in place of https://example.com/docs"], moved.Raised);
		Assert.Single(kept.Writes);
		Assert.Empty(kept.Flags); //a span's padding is markdown's, and comes off
		Assert.Empty(kept.Raised); //a title is the label's, translated
	}

	//a format item as the default writes it, its number as string.Format reads it
	[Fact]
	public void AFormatItem_KeptAsTheDefaultWritesIt_IsWritten() {
		Assert.Empty(Check("size", "it", new() { ["value"] = "{00:N1} MB" }).Flags);
		Assert.Empty(Check("size", "it", new() { ["value"] = "{0:N1} MB, {0:N1} in tutto" }).Flags);

		Assert.Equal([("0", "{0,-5:N1}"), ("Count", "{Count:C}"), ("1", "{1}")], ParameterUse.Items("{0 , -5:N1} and {Count:C} and {1:}"));
		Assert.Equal([("2", "{2}")], ParameterUse.Items("{{{{0}} {>a} {3#b} {2}")); //a brace, a reference and a selector hold none
	}

	//Maltese's plain value is translated, so the request asked for its few alone: an
	//answer filling the plain value too would overwrite a translation
	[Fact]
	public void ASlotTheRequestDidNotAskFor_IsFlagged_AndWritesNothing() {
		var verdict = Check("files", "mt", new() { ["value"] = "{0} fajls", ["value#few"] = "{0} fajli" });

		Assert.Empty(verdict.Writes);
		Assert.Equal(["value was not asked for: it has mt words already"], verdict.Flags);
		Assert.Single(Check("files", "mt", new() { ["value#few"] = "{0} fajli" }).Writes);
		Assert.Empty(Check("done", "it", new() { ["value"] = "Fatto" }).Flags); //stale, so asked again
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

	//a file whose keys refer to one another: saved defines a parameter its default
	//leaves out, which a translation may use, and the keys referring to it do not
	private static readonly string Referring = Ini(
		"value=!en",
		"value-en=English",
		"value-it=Italiano",
		"",
		"[saved]",
		"value=Saved",
		"param-0=int:the files saved",
		"",
		"[result]",
		"value=Result: {>saved}",
		"",
		"[other]",
		"value=Other: {>saved}",
		"value-it=Altro: {>saved}",
		"",
		"[kept]",
		"value=Kept",
		"param-0=int:the files kept",
		"value-it=Tenuti {0}",
		"stale-it=2026-10-01",
		"",
		"[shown]",
		"value=Shown: {>kept}");

	//the whole answer for these keys as the request carried them, checked against the file
	private static TranslationBatch Batch(string ini, string code, params (string Key, string? Text)[] answers) {
		var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes(ini));
		var items = answers.Select(answer => {
			var sent = patcher.Find(answer.Key)!;
			return new TranslationItem(answer.Key, TranslationRequest.Held(sent), TranslationRequest.Held(sent.Entries.GetValueOrDefault(code)),
				new Dictionary<string, string?> { ["value"] = answer.Text });
		});
		return new TranslationAnswer(code, [.. items]).Check(patcher.Keys.ToDictionary(found => found.BlockKey), code, patcher.DefaultLanguage);
	}

	//one key's translation is seen by the keys that refer to it: saved may use its
	//{0}, but result, reading it, would print one nothing passes
	[Fact]
	public void AKey_ReadsTheTranslationTheBatchWrites_ForAKeyItRefersTo() {
		var batch = Batch(Referring, "it", ("saved", "Salvati {0}"), ("result", "Risultato: {>saved}"));

		Assert.Single(batch.Verdicts[0].Writes);
		Assert.Empty(batch.Verdicts[1].Writes);
		Assert.Equal(["the it words use {0}, which the default does not and no param- defines"], batch.Verdicts[1].Flags);
		//other is in no answer, and its words now read saved's: named, and not skipped
		Assert.Equal(["other: through the words written, the it words use {0}, which the default does not and no param- defines"], batch.Broken);

		//each alone passes, as the file stands
		Assert.Empty(Batch(Referring, "it", ("result", "Risultato: {>saved}")).Verdicts[0].Flags);
	}

	//kept's stale Italian uses its {0}, so shown, reading it, prints one: an answer
	//taking the {0} out lets shown through, unless it is skipped itself
	[Fact]
	public void AKey_SkippedLeavesTheBatch_AndTheKeysReadingItAreCheckedWithoutIt() {
		var mended = Batch(Referring, "it", ("kept", "Tenuti"), ("shown", "Mostrati: {>kept}"));
		var broken = Batch(Referring, "it", ("kept", "Tenuti {"), ("shown", "Mostrati: {>kept}"));

		Assert.Equal([1, 1], mended.Verdicts.Select(verdict => verdict.Writes.Count));
		Assert.Empty(mended.Broken);
		Assert.Equal(["the it value holds `{`, a brace that opens nothing"], broken.Verdicts[0].Flags);
		Assert.Equal(["the it words use {0}, which the default does not and no param- defines"], broken.Verdicts[1].Flags);
		Assert.Empty(broken.Verdicts[1].Writes);
	}

	//each target counted: one swapped for another the slot already links to is a change,
	//where comparing them as sets saw none
	[Fact]
	public void ALinkTargetSwappedForAnotherOfTheSlots_RaisesTheHand() {
		string ini = Ini("value=!en", "value-it=Italiano", "", "[two]", "value=See [one](https://example.com/a) and [two](https://example.com/b)");

		var swapped = Batch(ini, "it", ("two", "Vedi [uno](https://example.com/b) e [due](https://example.com/b)"));
		var reordered = Batch(ini, "it", ("two", "Vedi [due](https://example.com/b) e [uno](https://example.com/a)"));

		Assert.Single(swapped.Verdicts[0].Writes);
		Assert.Equal(["the it words link to https://example.com/b in place of https://example.com/a"], swapped.Verdicts[0].Raised);
		Assert.Single(reordered.Verdicts[0].Writes);
		Assert.Empty(reordered.Verdicts[0].Raised); //the same targets in another order
	}

	//the whole answer, each key's slots as given
	private static TranslationBatch Slotted(string ini, string code, params (string Key, Dictionary<string, string?> Slots)[] answers) {
		var patcher = IniPatcher.FromBytes(Encoding.UTF8.GetBytes(ini));
		var items = answers.Select(answer => {
			var sent = patcher.Find(answer.Key)!;
			return new TranslationItem(answer.Key, TranslationRequest.Held(sent), TranslationRequest.Held(sent.Entries.GetValueOrDefault(code)), answer.Slots);
		});
		return new TranslationAnswer(code, [.. items]).Check(patcher.Keys.ToDictionary(found => found.BlockKey), code, patcher.DefaultLanguage);
	}

	//each form of a and b refers to the other's plain value, which ends there; the answer
	//moves each reference into the plain value, every token kept, and a reads b reads a.
	//a, skipped for it, leaves the batch, and b, reading a's default again, goes in
	[Fact]
	public void WordsComingBackRoundToThemselves_ThroughTheBatch_AreSkipped() {
		string ini = Ini("value=!en", "value-it=Italiano", "",
			"[a]", "value=A", "value#other={>b}", "",
			"[b]", "value=B", "value#other={>a}");

		var batch = Slotted(ini, "it",
			("a", new() { ["value"] = "{>b}", ["value#other"] = "Aa" }),
			("b", new() { ["value"] = "{>a}", ["value#other"] = "Bb" }));
		var alone = Slotted(ini, "it", ("a", new() { ["value"] = "{>b}", ["value#other"] = "Aa" }));

		Assert.Empty(batch.Verdicts[0].Writes); //both were written, and a read # ∞ #
		Assert.Equal(["the it words come back round to themselves, which an app shows as # ∞ #"], batch.Verdicts[0].Flags);
		Assert.Equal(2, batch.Verdicts[1].Writes.Count);
		Assert.Empty(batch.Broken);
		Assert.Equal(2, alone.Verdicts[0].Writes.Count); //b's plain value is still B
	}

	//a slot no key has is flagged on its own key; the rest of the batch still goes in
	[Fact]
	public void ASlotTheKeyHasNot_SkipsItsKey_AndNotTheBatch() {
		var batch = Slotted(Referring, "it",
			("saved", new() { ["text"] = "Ah" }), //threw, and words apply wrote nothing
			("kept", new() { ["value"] = "Tenuti {0}" }));

		Assert.Equal(["text is no slot it has in it"], batch.Verdicts[0].Flags);
		Assert.Single(batch.Verdicts[1].Writes);
	}

	[Fact]
	public void AKeyAnsweredTwice_KeepsItsFirstAnswer() {
		var batch = Batch(Referring, "it", ("result", "Risultato: {>saved}"), ("result", "Esito: {>saved}"), ("result", null));

		Assert.Equal((new WordsField("value", "it", ""), "Risultato: {>saved}"), Assert.Single(batch.Verdicts[0].Writes));
		Assert.Equal(["answered twice, and the first answer stands"], batch.Verdicts[1].Flags);
		Assert.Equal((0, 0, 1), (batch.Verdicts[2].Writes.Count, batch.Verdicts[2].Flags.Count, batch.Verdicts[2].Left)); //saying nothing, it is no error
	}
}
