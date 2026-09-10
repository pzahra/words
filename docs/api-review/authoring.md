Read-only review completed. No files were modified. I inspected the authoring module, its tests, the core parser/provider contracts, editor call sites, and the specification.

## Findings

1. **High — `IWordsProvider` indexer is unusable**

   File: [LocalizationClasses.cs:195](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/LocalizationClasses.cs:195)

   `WordsProviderBase` implements `IWordsProvider.this[string]` by always throwing `NotImplementedException`. The interface promises lookup and `KeyNotFoundException` only for missing keys. Both derived providers therefore violate the public contract, even when the key exists.

   Recommendation: implement the indexer through `TryGetValue`, returning the value or throwing `KeyNotFoundException(key)`. Add tests using both exact and cross-file bare keys through the indexer.

2. **High — writer does not escape literal backslashes, allowing structural corruption**

   File: [IniWriter.cs:184](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/IniWriter.cs:184)

   `WritePair` escapes apostrophes and underscores but not `\`, even though the parser treats a terminal backslash as a newline continuation and collapses doubled backslashes. A value ending in a literal `\` is written as a continuation; on reload, the next physical line—even a block header—is consumed as value text. This can merge or delete subsequent blocks, not merely change bytes.

   Recommendation: encode literal `\` as `\\` before inserting newline-continuation backslashes. Define the transformation order explicitly and test terminal backslashes, repeated backslashes, embedded newlines followed by headers, and save-load-save stability.

3. **High — `Shift` destroys translation metadata during collisions**

   File: [WordsOperations.cs:123](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsOperations.cs:123)

   Collision handling only considers `Value`:

   - A target with empty `Value` but nonempty context, comment, or stale marker is replaced wholesale.
   - A source’s context, comment, and stale marker are discarded when the target value wins.
   - Parking the source value overwrites the target’s existing context.
   - Equal values still cause the source metadata to disappear.

   This contradicts the document model’s no-loss expectation and the spec’s statement that each language carries four independent fields.

   Recommendation: define field-by-field conflict rules and never overwrite occupied metadata silently. Prefer returning a structured conflict result or preserving displaced content in a dedicated conflict object rather than overloading `Context`.

4. **High — nested constants are not recognized after reload**

   File: [WordsParserToLocalizationProvider.cs:224](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsParserToLocalizationProvider.cs:224)

   Constant detection checks `baseKey[0] == '$'`. Thus `[$unit]` works, but `[group.$unit]` and relative `[.$unit]` under `group` do not. This conflicts directly with `SetConstantMarker`, which deliberately marks the last segment, e.g. `F.view.$key`.

   For an otherwise empty nested constant, the consequences compound: `IsConstant` remains false and `WordsSession.Load` drops it as empty at [WordsSession.cs:77](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsSession.cs:77).

   Recommendation: detect `$` on `WordsOperations.LastSegment(baseKey)`, and add save/reload tests for nested constants created through `SetConstant`.

5. **High — intentional empty keys are indistinguishable from synthetic group headers and are deleted**

   Files: [WordsSession.cs:77](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsSession.cs:77), [IniWriter.cs:208](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/IniWriter.cs:208)

   Every `WordsKey` satisfying `IsEmpty()` is dropped, not merely bare group headers. Consequently `[key] value=` and an intentionally empty `[key]` cease to exist. The runtime provider distinguishes an existing key whose value is empty from a missing key, so this is semantic loss.

   The writer also omits explicit empty value/context/comment declarations, preventing the model from preserving presence versus absence.

   Empty blocks with comments are especially unstable: `KeyTree.Build` processes surviving keys before comment-only block paths at [KeyTree.cs:59](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/KeyTree.cs:59), moving such blocks and comments toward the end.

   Recommendation: represent block existence separately from field content and distinguish structural cut headers from authored empty keys. Preserve document order as explicit block records rather than reconstructing it from surviving values.

6. **High — repeated declarations and reopened blocks cannot round-trip**

   Files: [WordsParserToLocalizationProvider.cs:120](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsParserToLocalizationProvider.cs:120), [WordsParserToLocalizationProvider.cs:237](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsParserToLocalizationProvider.cs:237)

   Repeated value/context/comment declarations overwrite earlier declarations; duplicate parameters are ignored. Reopened blocks collapse into one dictionary entry. Comments attached to a later reopening are combined under the same key and subsequently emitted at the first tree position.

   This disagrees with the core README’s documented “repeating the field continues the line” behavior and with the authoring spec’s claims about exact field, comment, and order preservation.

   Recommendation: decide whether repeated fields append or use last-wins semantics, align the core implementation and documentation, and retain an ordered syntax/document layer if physical declarations and reopened-block positions must survive editing.

7. **High — save can silently omit keys and can corrupt the target on failure**

   Files: [WordsSession.cs:138](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsSession.cs:138), [IniWriter.cs:91](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/IniWriter.cs:91)

   `Save` accepts any tree, never verifies that its root matches `file.Label`, and never checks that the tree covers every key owned by the file. `IniWriter.WriteKeys` silently ignores dictionary keys absent from the walk. A stale or wrong tree therefore produces a successful-looking save that deletes data.

   File overloads open/truncate the destination before serialization. Exceptions from a custom tree, cut strategy, invalid key, or writer leave a partial file. Merge and Split use the same direct-write path.

   Recommendation: validate root ownership and tree/key-set equality before opening the destination. Serialize to memory or a temporary sibling file, flush it, then atomically replace the destination.

8. **Medium — unknown fields and parameter types are lossy despite “never lose data”**

   Files: [WordsParserToLocalizationProvider.cs:146](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsParserToLocalizationProvider.cs:146), [LocalizationClasses.cs:175](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/LocalizationClasses.cs:175)

   Unknown block fields are reported and discarded; unknown top-of-file fields are silently discarded. Unknown parameter types are silently coerced to `String`, so the next save rewrites the type name. Long stale values are also truncated because `VisitFieldContinuation` has no stale case at [WordsParserToLocalizationProvider.cs:190](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsParserToLocalizationProvider.cs:190).

   Recommendation: preserve unknown fields and original parameter type names as opaque ordered data. Add stale continuation support. Treat unsupported constructs as blocking save errors if lossless preservation is unavailable.

9. **Medium — Merge can silently retain the wrong language entry**

   File: [WordsOperations.cs:75](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsOperations.cs:75)

   Key-set equality is checked, but presence of the requested language entry is not. If a source key lacks that entry, Merge simply continues, leaving the base file’s cloned entry in place. The result violates “each language’s entries from its source” while reporting success.

   The well-formed `WordsSession` invariant usually masks this, but the method is public and accepts arbitrary dictionaries whose mutable models can easily violate that invariant.

   Recommendation: validate every `(language, key)` mapping before constructing the result. Report missing entries separately from key-set conflicts and never fall back to base data implicitly.

10. **Medium — Split writes settings references for languages it does not declare**

    File: [WordsSession.cs:251](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsSession.cs:251)

    Split declares only the selected language but passes the entire source `LanguageSettings` dictionary to the writer. The resulting file can contain `param-xx` for unrelated languages and immediately reload with errors generated at [WordsFile.cs:65](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsFile.cs:65).

    Recommendation: retain only the selected language’s settings reference, plus the common `param`, or explicitly declare every language whose setting is carried.

11. **Medium — `GroupCuts` caches results across mutable inputs**

    File: [IniWriter.cs:57](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/IniWriter.cs:57)

    `GroupCuts` retains subtree counts by node identity while both the key dictionary and `Children` sequences are externally mutable. Reusing the strategy after adding/removing keys or nodes returns stale cut decisions. `minimumKeys <= 0` is accepted and can emit pointless headers, and neither the strategy nor writer protects against cyclic custom trees.

    Recommendation: make `GroupCuts` a per-write computation or invalidate/cache against an immutable snapshot. Reject `minimumKeys < 1`. Validate or document acyclic, stable tree requirements.

12. **Medium — public mutability makes the documented invariants unenforceable**

    Files: [LocalizationClasses.cs:8](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/LocalizationClasses.cs:8), [LanguageTable.cs:16](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/LanguageTable.cs:16), [WordsFile.cs:28](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsFile.cs:28)

    `WordsKey.BlockKey`, `LanguageEntry.Code`, `LanguageTable.Known`, `WordsKey.Entries`, and `WordsFile.Languages` are all publicly mutable. Callers can therefore:

   - Make dictionary keys disagree with `WordsKey.BlockKey`.
   - Change a language code without moving entries.
   - Add/remove known languages without backfilling keys.
   - Introduce null strings despite non-null annotations.
   - Create invalid or noncanonical keys/language codes that the writer cannot safely encode.

   Merge, Split, provider lookup, and writer logic all assume those states cannot occur.

   Recommendation: make identity fields immutable or session-controlled; expose read-only collections; add explicit mutation methods with validation and canonicalization. Guard constructor and method arguments with `ThrowIfNull`.

13. **Medium — relative file identity can change with the process working directory**

    Files: [WordsSession.cs:34](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsSession.cs:34), [WordsFile.cs:57](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsFile.cs:57)

    `WordsFile` stores the path exactly as supplied, while `FileAt`, `Directory`, settings resolution, and Save resolve it later. If `Environment.CurrentDirectory` changes, the same `WordsFile` can no longer be found and may save to a different location. The code also forces case-insensitive identity on every platform.

    Recommendation: canonicalize and store the absolute path at load time. Use the platform-appropriate path comparer, or explicitly make Authoring Windows-only.

14. **Medium — parser failure reporting is much weaker than documented**

    File: [WordsSession.cs:54](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsSession.cs:54)

    The documentation says bad content never throws and parser gripes land in `WordsFile.Errors`. In reality, the core parser silently skips many malformed lines, while the authoring consumer silently ignores some unsupported top-level fields. Errors lack source line numbers. A `WordsParserToLocalizationProvider` instance also cannot honor the core parser’s repeated-`Load`/concatenation contract: once it contains a block, the next source’s top-of-file fields are treated as block fields and can index `wordKeys[""]`.

    Recommendation: make this consumer explicitly single-document/single-use, or add document-boundary/reset support. Extend parser events with locations and report every ignored construct. Narrow the “bad content” guarantee if full recovery is not intended.

15. **Low — culture and language normalization differ from the runtime API**

    Files: [LocalizationClasses.cs:226](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/LocalizationClasses.cs:226), [WordsOperations.cs:270](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsOperations.cs:270)

    The core `WordsBuilder` normalizes requested language casing; `LanguageWordsProvider` does not. Public callers using `EN-gb` can miss entries stored as `en-GB`. Family fallback also truncates at the first hyphen rather than following `CultureInfo.Parent`, limiting future support for script-based tags.

    Recommendation: normalize at provider construction and language-table mutation boundaries. Base fallback on a clearly documented supported tag grammar or `CultureInfo.Parent`.

16. **Low — writer unexpectedly owns caller-supplied `TextWriter`**

    File: [IniWriter.cs:95](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/IniWriter.cs:95)

    The `TextWriter` overload disposes the supplied writer because it wraps it in `using`. This is surprising for a helper that did not create the stream and prevents callers from composing additional output.

    Recommendation: leave caller-owned writers open, or add an explicit `leaveOpen` option and document ownership consistently.

17. **Low — repeated global scans and regex allocations will scale poorly**

    Files: [WordsOperations.cs:12](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/WordsOperations.cs:12), [IniWriter.cs:184](C:/Users/PatrickZ/Source/imports/words/Localization-Authoring/IniWriter.cs:184)

    Merge scans the complete global dictionary once per source language, provider lookup probes every file for every bare reference, and each written value passes through several regex transformations and allocations. The wrapping expression can backtrack heavily on long unbroken text.

    Recommendation: maintain a per-file key index, precompute provider precedence maps for preview batches, and replace the escaping/wrapping regex pipeline with a single linear encoder.

## Documentation mismatch

The broad byte-stability claim in [WordsEdit/readme.md:27](C:/Users/PatrickZ/Source/imports/words/WordsEdit/readme.md:27) is false for arbitrary input. The implementation normalizes line endings, separators, blank lines, comments, field ordering, wrapping, encoding/BOM, and final newlines. The narrower claim in [WordsEdit/SPEC.md:75](C:/Users/PatrickZ/Source/imports/words/WordsEdit/SPEC.md:75)—canonical save → load → save stability—is more defensible, but still fails on cases such as terminal backslashes and wrapped stale values.

The existing tests principally prove stability for already-canonical fixtures. They do not cover the destructive edge cases above.

## Prioritized plan

1. Fix the provider indexer, backslash encoding, nested-constant detection, and `Shift` metadata loss.
2. Introduce a lossless ordered document representation for empty blocks, repeated/reopened fields, comments, and unknown fields.
3. Validate tree/key/source-language contracts and make Save/Merge/Split atomic.
4. Seal the model invariants behind validated mutation APIs and canonical absolute paths.
5. Make `GroupCuts` per-write, add adversarial round-trip tests, then narrow the byte-stability documentation to the guarantees actually supported.

Codex session ID: 01a08a45-7a24-7ce2-b1e2-fcac7b421aa3
Resume in Codex: codex resume 01a08a45-7a24-7ce2-b1e2-fcac7b421aa3
