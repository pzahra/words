Read-only review complete. No files were modified. I found five high-severity issues, chiefly silent data loss, global-state races, and formatting/parser failure modes.

## Findings

1. **High — [WordsParserToWordsProvider.cs:49](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsParserToWordsProvider.cs:49)**  
   Repeated `value=` declarations overwrite the previous value; they do not append. This directly contradicts [readme.md:48](C:/Users/PatrickZ/Source/imports/words/Localization-Core/readme.md:48) and [SKILL.md:51](C:/Users/PatrickZ/Source/imports/words/Localization-Core/SKILL.md:51), whose example claims repeated fields continue the line. With the documented example, only `"all on one line"` survives, and the warning is discarded by default.  
   **Recommendation:** Decide the contract explicitly. If repetition should append, distinguish declarations within one source from later `Load` overlays, then append within a source while allowing later sources to replace. Otherwise correct both documents and remove the misleading example.

2. **High — [Words.cs:83](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:83), [CulturedWords.cs:26](C:/Users/PatrickZ/Source/imports/words/Localization-Core/CulturedWords.cs:26)**  
   `Known` is atomically published before its culture side effect. Concurrent assignments can leave `Known` from one assignment and process culture from another. Even without concurrency, an atomic reference swap cannot make ambient culture and dictionary selection atomic for readers. The statement that the swap is “safe from any thread” overstates the guarantee.  
   **Recommendation:** Remove culture mutation from the property setter. Make culture activation an explicit startup operation, or carry culture with lookup/formatting operations. If compatibility requires the setter side effect, serialize assignments and document that hot-swapping or concurrent assignment is unsupported.

3. **High — [WordsParser.cs:194](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsParser.cs:194), [WordsParser.cs:216](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsParser.cs:216)**  
   Malformed input is silently discarded, while block headers are only prefix-matched: `[valid]garbage` is accepted. Invalid declarations, unsupported language tags, stray text, and unfinished continuations may therefore become missing translations without a load-time diagnostic. This conflicts with [SKILL.md:79](C:/Users/PatrickZ/Source/imports/words/Localization-Core/SKILL.md:79), which says parse problems reach the builder logger.  
   **Recommendation:** Anchor the full grammar, track line/source locations, warn or throw on unrecognized lines, and diagnose an unfinished continuation at EOF. A permissive mode can remain available, but strict parsing should be the production default.

4. **High — [Words.cs:139](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:139), [Words.cs:211](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:211)**  
   Reference rendering collapses `{{` before `string.Format` runs. Thus a valid composite format such as `{{0}}`, intended to render literal `{0}`, becomes malformed and throws `FormatException` whenever arguments are supplied. The named-format regex can similarly recognize a tag inside escaped braces.  
   **Recommendation:** Define one coherent escaping grammar and parse it in one pass. Preserve composite-format escaped braces until after formatting, and add tests for literal braces mixed with positional, named, and Words references.

5. **High — [Words.cs:174](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:174)**  
   Circular references are detected, but an arbitrarily long acyclic reference chain recurses without a depth or output limit. A bad localization file can cause `StackOverflowException`, which normally terminates the process, or excessive allocation through repeated expansion.  
   **Recommendation:** Use iterative expansion or impose configurable depth/output limits, log the complete reference trail, and return a diagnostic placeholder when a limit is exceeded.

6. **Medium — [WordsBuilder.cs:125](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsBuilder.cs:125)**  
   `Flatten("")` directly indexes the default language. It throws `KeyNotFoundException` when nothing/default-less content was loaded, contradicting the documented “empty provider if nothing was loaded” contract. It also returns the builder’s live mutable `DictionaryWordsProvider`, whereas non-empty selections receive snapshots.  
   **Recommendation:** Use `TryGetValue`, return `WordsProvider.Empty()` when absent, and always return an immutable snapshot with consistent lifetime semantics.

7. **Medium — [WordsProvider.cs:105](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsProvider.cs:105)**  
   `ReadOnlyWordsProvider` is only a read-only interface over aliased storage. Concurrent mutation of the supplied dictionary can make lookup results inconsistent or unsafe. Combined with publicly exposed mutable language providers, the library cannot substantiate its “read-only view” or thread-safety implications.  
   **Recommendation:** Copy into `FrozenDictionary`, `ImmutableDictionary`, or a privately owned dictionary. Clearly state whether custom providers must support concurrent reads.

8. **Medium — [Words.cs:96](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:96), [ITakeException.cs:15](C:/Users/PatrickZ/Source/imports/words/Localization-Core/ITakeException.cs:15)**  
   Both `Words.Logger` and even `ITakeException.Dummy` are mutable public fields. They can be assigned `null`, turning non-fatal missing-key paths into `NullReferenceException`; replacement visibility is also not explicitly synchronized. Logger implementations are invoked concurrently without any stated contract.  
   **Recommendation:** Make both non-null properties, keep `Dummy` immutable/get-only, use `Volatile.Read/Write` or `Interlocked.Exchange`, and document that installed loggers must be thread-safe.

9. **Medium — [WordsBuilder.cs:130](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsBuilder.cs:130), [WordsParser.cs:145](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsParser.cs:145)**  
   Language handling is neither BCP-47 validation nor correct canonicalization. `\w+` accepts digits, underscores, and arbitrary Unicode; script subtags are uppercased like regions (`zh-Hans` becomes `zh-HANS`); three-part tags such as `zh-Hant-TW` are rejected. Fallback assumes exactly one hyphen instead of following culture parents.  
   **Recommendation:** Validate/canonicalize through `CultureInfo`, retain canonical `CultureInfo.Name`, and traverse `CultureInfo.Parent` for fallback.

10. **Medium — [WordsBuilder.cs:187](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsBuilder.cs:187)**  
    `ToWords("en")` silently chooses a specific culture via `CreateSpecificCulture`, even though a neutral translation family does not identify whether formatting should be `en-US`, `en-GB`, or another regional culture. Translation selection and formatting culture are incorrectly treated as one decision.  
    **Recommendation:** Add an overload accepting an explicit `CultureInfo`. Require a specific culture for ambient formatting, while allowing a separate neutral language/fallback selector.

11. **Medium — [MarkdownParser.cs:209](C:/Users/PatrickZ/Source/imports/words/Localization-Core/MarkdownParser.cs:209)**  
    A caller that disallows `MarkdownElementType.Basic` can still receive styled content inside a hyperlink because hyperlink-label recursion clears the `Basic` flag. This violates the flag’s public contract.  
    **Recommendation:** Preserve all caller-supplied prohibition flags. Track “currently inside hyperlink” separately and only add the nested-hyperlink prohibition.

12. **Medium — [ConsoleWords.cs:37](C:/Users/PatrickZ/Source/imports/words/Localization-Core/ConsoleWords.cs:37)**  
    OSC 8 targets and translated content are emitted without control-character sanitization. If localization content is externally supplied, ESC/BEL/string-terminator characters can break out of the hyperlink sequence and inject terminal control commands.  
    **Recommendation:** Reject or strip terminal control characters from URI targets and provide a safe rendering mode that sanitizes untrusted translated text, independent of redirection detection.

13. **Medium — [Extensions.cs:155](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Extensions.cs:155)**  
    `Describe(..., "i")` unboxes every enum as `int`. It throws `InvalidCastException` for enums backed by `byte`, `short`, `long`, `uint`, or `ulong`, despite promising the enum’s numeric value.  
    **Recommendation:** Format according to `Enum.GetUnderlyingType`, using `Convert.ToUInt64`/`Convert.ToInt64` as appropriate.

14. **Medium — [Words.cs:493](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:493), [Words.cs:521](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:521)**  
    Changing `LazyWords.Key` does not invalidate the cached value, concurrent reads/writes are unsynchronized, and the public singleton `LazyWords.Empty` is itself mutable. Results therefore depend on timing, and any caller can corrupt the shared “empty” instance.  
    **Recommendation:** Make `LazyWords` immutable apart from a thread-safe one-time cache, remove the `Key` setter, and expose `Empty` as an immutable special instance.

15. **Low — [CulturedWords.cs:15](C:/Users/PatrickZ/Source/imports/words/Localization-Core/CulturedWords.cs:15), [WordsAttribute.cs:17](C:/Users/PatrickZ/Source/imports/words/Localization-Core/WordsAttribute.cs:17)**  
    Public primary constructors do not validate `provider`, `setCulture`, attribute keys, or tooltip text. Nullable annotations discourage null in C#, but reflection, older languages, and disabled-nullability callers can create objects whose advertised invariants are false. `FieldKey` has the related unavoidable `default(FieldKey)` problem.  
    **Recommendation:** Use explicit validating constructors; reject null and, for keys, empty/whitespace values. Consider properties or a reference type where a non-null default state matters.

16. **Low — [Words.cs:466](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Words.cs:466)**  
    `EchoWords.TryGetValue` always returns true while `ContainsKey` always returns false. Although documented, this breaks the dictionary-style contract and changes fallback behavior in generic consumers such as `Describe`.  
    **Recommendation:** Return false while still populating the diagnostic value, or separate echo behavior from `IWords` so normal lookup invariants remain reliable.

17. **Low — [MarkdownParser.cs:320](C:/Users/PatrickZ/Source/imports/words/Localization-Core/MarkdownParser.cs:320), [ConsoleWords.cs:22](C:/Users/PatrickZ/Source/imports/words/Localization-Core/ConsoleWords.cs:22)**  
    A directly constructed markdown/console parser discards diagnostics by default, while [SKILL.md:78](C:/Users/PatrickZ/Source/imports/words/Localization-Core/SKILL.md:78) implies runtime markdown and image gripes reach `Words.Logger`. Only selected shared parsers explicitly use `ITakeException.Global`.  
    **Recommendation:** Either default parsers to `ITakeException.Global` or state clearly that callers must pass it to obtain global runtime diagnostics.

18. **Low — [Extensions.cs:91](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Extensions.cs:91), [Extensions.cs:105](C:/Users/PatrickZ/Source/imports/words/Localization-Core/Extensions.cs:105)**  
    `Describe` repeats reflection and performs five localized lookups on every call even if the format requests only one form. UI converters can call this frequently, making avoidable reflection, dictionary probes, and reference expansion part of rendering.  
    **Recommendation:** Cache enum metadata per type/member and resolve only variants actually referenced by the requested format. Do not cache translated values across `Words.Known` changes.

## Prioritized plan

1. Fix data-loss and crash paths: repeated fields, strict parser diagnostics, brace escaping, and reference-depth limits.
2. Redesign or sharply constrain `Words.Known`/culture/logger global state.
3. Make flattened providers immutable and make empty/default behavior consistent.
4. Replace language parsing/fallback with `CultureInfo`-based handling and separate formatting culture from translation selection.
5. Correct markdown restrictions, enum conversion, `LazyWords`, null contracts, and documentation; then add focused concurrency and malformed-input tests.

Codex session ID: 01a08a45-5095-7071-af34-009d1b8bc66c
Resume in Codex: codex resume 01a08a45-5095-7071-af34-009d1b8bc66c
