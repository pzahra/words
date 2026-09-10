Read-only review completed. No files were modified, and I did not run the build/tests because they would write under `bin/`, `obj/`, or `TestResults`. I inspected the existing Avalonia tests and the WPF twin for context.

## Findings

1. **High — [Hyperlink.cs:134](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:134)**  
   Hyperlink hit-testing can activate the wrong link. `HitTestPoint`’s `IsInside` is ignored, so a point outside the rendered text can be clamped to the nearest character. The index walker also uses an off-by-one calculation (`index -= length; index < 1`) and assigns no text position to `InlineUIContainer`, even though markdown images introduce those containers. Links adjacent to images or other runs can consequently lose their first character or gain a neighbouring character as clickable area.  
   **Recommendation:** Reject hits where `!hit.Value.IsInside`; build explicit text-position ranges from the same flattened inline representation used by `TextLayout`, including the object-replacement position for `InlineUIContainer`; test run/link boundaries, nested spans, images before links, wrapped lines, RTL text, and clicks beyond the text bounds.

2. **High — [WordsInline.cs:69](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/WordsInline.cs:69), [Hyperlink.cs:55](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:55)**  
   Format parameters are substituted before markdown parsing. A value such as a user-controlled display name can inject links or images into an otherwise trusted localized template. The navigation API then forwards every absolute URI scheme without policy. This becomes particularly dangerous with the readme’s catch-all `Process.Start` example at [readme.md:26](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/readme.md:26), which would shell-open schemes such as `file:`.  
   **Recommendation:** Treat format arguments as literal text by default—escape markdown or insert them as `Run` nodes after parsing the template. Provide an explicit trusted-markdown argument type if needed. Document and demonstrate a strict navigation allowlist, normally `https` plus explicitly registered application schemes.

3. **Medium — [Hyperlink.cs:134](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:134)**  
   Navigation occurs on pointer press, for any mouse button and potentially during touch scrolling. It is marked handled even if there is no navigate listener. There is no press/release matching, movement threshold, keyboard activation, focus, or automation support. This is materially weaker than WPF’s native `Hyperlink` and interferes with selection or parent pointer gestures.  
   **Recommendation:** Activate on a primary-button release only after a matching press on the same link, without excessive movement. Mark the pointer event handled only when navigation was accepted. Add keyboard/focus/automation support or use an accessible control-based representation.

4. **Medium — [Hyperlink.cs:124](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:124), [Hyperlink.cs:176](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:176)**  
   Tooltip handling takes ownership of the host `TextBlock`’s attached tooltip and never restores the previous value. There is no `PointerExited` handler, so a manually opened tooltip can remain open after leaving the control. Moving between links with equal tooltip content returns early and does not reposition it. Disabling hyperlinks also leaves the cursor and tooltip state untouched.  
   **Recommendation:** Add exit/detach cleanup, restore any pre-existing tooltip and cursor, track the active link rather than comparing tooltip values, and preferably let the normal tooltip service manage opening delays and closure.

5. **Medium — [Hyperlink.cs:96](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:96), [Hyperlink.cs:107](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:107)**  
   A hyperlink permanently sets `EnableHyperlinks` on its first host. There is no `OnDetachedFromLogicalTree`, reference counting, or rescan of the inline collection. Removing the final hyperlink leaves pointer handlers installed, and the unused `owner` field retains a reference until the hyperlink itself is collected.  
   **Recommendation:** Clear `owner` on detach and manage host subscriptions according to the number of attached links, or require explicit host opt-in and remove the automatic mutation.

6. **Medium — [Hyperlink.cs:55](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Hyperlink.cs:55)**  
   `globalNavigateSubscription` is unsynchronized process-wide state. Concurrent registrations can interleave disposal and assignment, leaving more than one class handler installed while only one subscription is tracked. A throwing handler propagates through pointer dispatch. The single replaceable handler also makes independently composed libraries silently disable each other.  
   **Recommendation:** Serialize registration under a lock or on the UI dispatcher, validate `handler`, return a token that only removes its own current registration, and define exception handling. Prefer a routed event or additive subscription API, with an optional single application policy layered over it.

7. **Medium — [ImageSchemes.cs:183](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/ImageSchemes.cs:183)**  
   `staticres:` returns shared `Shape` and `Control` resources as-is, then mutates their fill, dimensions, tooltip, and logical parent. Reusing the same resource in two markdown values can fail because one control cannot have two parents; earlier instances can also change when later queries mutate the shared object. The WPF twin has the same defect.  
   **Recommendation:** Resource values should be immutable image/geometry data, a factory, `IDataTemplate`, or another mechanism that creates a fresh control. Do not accept and reparent a shared `Control` instance.

8. **Medium — [ImageSchemes.cs:160](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/ImageSchemes.cs:160)**  
   The `assets:` clamp is lexical only. A symbolic link or directory junction inside `Assets` can point outside it, contradicting the “and only that folder” guarantee in [readme.md:89](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/readme.md:89). The `Ordinal` prefix check also does not reflect case-insensitive Windows/macOS filesystem semantics, producing inconsistent false rejections.  
   **Recommendation:** If this is a security boundary, resolve and verify links/reparse points component-by-component, or reject them entirely. Use an OS-appropriate path comparison and `Path.GetRelativePath`/root-containment check rather than a raw prefix test. Clearly state if the guarantee assumes a trusted, link-free Assets tree.

9. **Medium — [MarkdownParser.cs:24](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/MarkdownParser.cs:24), [MarkdownParser.cs:111](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/MarkdownParser.cs:111)**  
   “Font height” is actually a fixed parser constructor value, defaulting to 13. It does not follow the host `TextBlock.FontSize`, so geometry icons and sub/superscript are incorrectly sized in larger or smaller text. The readme’s statement that geometry defaults to “the font height” is therefore misleading. WPF behaves the same way.  
   **Recommendation:** Derive sizing from the eventual host/inherited font size, use relative typography where Avalonia permits it, or expose a parser/font-size property on `WordsInline` and `MarkdownConverter`. Validate `baseFontSize` as positive and finite.

10. **Medium — [ImageSchemes.cs:101](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/ImageSchemes.cs:101), [MarkdownParser.cs:111](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/MarkdownParser.cs:111)**  
    Width and height accept negative values, zero, `NaN`, infinity, and arbitrarily large finite values. Some become Avalonia property-validation exceptions and unexpectedly produce alt text; others can cause extreme layout or allocation. Malformed percent escapes in public `ImageOptions.Parse` can also throw despite the general fail-soft design.  
    **Recommendation:** Accept only finite values in a documented positive range, report rejected options, and make query decoding explicitly fail-soft. Consider limits on decoded bitmap dimensions as well.

11. **Medium — [WordsInline.cs:61](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/WordsInline.cs:61)**  
    Formatting, named-property access, array extraction, and markdown construction are not protected by a failure boundary. A bad localized format string, throwing property getter, multidimensional array (`GetValue(int)` is rank-one only), or parser error escapes from `OnPropertyChanged` and can break XAML loading or a binding update.  
    **Recommendation:** Share one formatting helper with `WordsConverter`, support arbitrary arrays by enumeration or reject non-vector arrays clearly, log contextual errors including the key, and render a visible safe fallback rather than throwing from a styled-property callback.

12. **Medium — [FlagsDescriptionConverter.cs:60](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/FlagsDescriptionConverter.cs:60)**  
    Flag decomposition relies on localized `Enum.ToString()` spelling and splitting specifically on `", "`. Named composite values remain composite rather than being rendered flag-by-flag. `Convert.ToInt64` throws for `ulong`-backed values above `long.MaxValue`. When `AsArray` is true, the result is a lazy `IEnumerable`, not an array/list, so it redoes parsing and localization on every enumeration and observes later converter-property changes.  
    **Recommendation:** Validate `[Flags]`, decompose the underlying bits without signed overflow, define behaviour for composites/unknown bits, and materialize a stable `string[]`. Use `NotSupportedException` or `AvaloniaProperty.UnsetValue` for `ConvertBack`, consistently with the other converters. The WPF twin should be corrected in parallel.

13. **Medium — [MarkdownConverter.cs:39](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/MarkdownConverter.cs:39)**  
    The return-type decision is based on whether `targetType` is `TextBlock`, but that branch returns an `Inline`; all other targets receive a `TextBlock`. This violates normal converter type expectations and conflicts with the readme claim that a whole `TextBlock` is returned “when the target wants a control.” It happens to work for the sample’s `ContentControl` because its target type is broad.  
    **Recommendation:** Define explicit supported target contracts: return an inline for `Inline`/inline-collection contexts, a `TextBlock` for compatible control/object targets, and `UnsetValue` for incompatible types. Add direct converter tests for each declared target type.

14. **Low — [WordsInline.cs:74](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/WordsInline.cs:74), [WordsInline.cs:88](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/WordsInline.cs:88)**  
    Positional parameters use `CurrentUICulture`, named parameters use `CurrentCulture`, while `WordsConverter` uses Avalonia’s supplied binding culture. They usually coincide because `Words.Known` sets both, but diverge under custom binding/thread culture and make the APIs observably inconsistent. The WPF twin repeats this.  
    **Recommendation:** Establish one formatting-culture contract—prefer the binding culture when available, otherwise the selected Words culture—and use it everywhere.

15. **Low — [WordsExtension.cs:25](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/WordsExtension.cs:25), [WordsExtension.cs:65](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/WordsExtension.cs:65)**  
    Null keys/builders and malformed, relative, or non-`avares:` resource strings fail through incidental exceptions. `LoadResource` promises an `avares:` resource but does not validate that contract or add path context to `AssetLoader.Open` failures. Resolution in the markup extension’s `Key` setter also makes initialization order less predictable than resolving in `ProvideValue`.  
    **Recommendation:** Add argument validation, accept a `Uri` overload, require an absolute `avares:` URI, and wrap failures with the resource URI. Resolve the markup value in `ProvideValue`, or explicitly justify and test early resolution.

16. **Low — [MarkdownParser.cs:25](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/MarkdownParser.cs:25), [MarkdownParser.cs:46](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/MarkdownParser.cs:46)**  
    `MarkdownParser.Default` and its exposed mutable `Dictionary` are global, unsynchronized extension points. Tests, plugins, or parallel UI work can replace the parser or mutate the registry while another conversion reads it. `WordsInline` offers no per-instance parser escape hatch.  
    **Recommendation:** Provide parser injection/properties on consumers, expose an immutable or snapshot-based resolver registry, and restrict global configuration to an atomic startup operation.

17. **Low — [Localization-Ava.csproj:3](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Localization-Ava.csproj:3), [Localization-Ava.csproj:19](C:/Users/PatrickZ/Source/imports/words/Localization-Ava/Localization-Ava.csproj:19)**  
    The module produces `net8.0` and `net10.0`, but the test project exercises only `net10.0-windows`. The wide Avalonia range `[11.0,12.0)` means the library restore compiles against 11.0 while tests are unified to 11.3.18; neither the `net8.0` runtime asset nor the minimum/latest-supported combinations are directly tested. Hyperlink hit-testing has no Avalonia test coverage at all—the existing global-navigation tests cover only WPF.  
    **Recommendation:** Add a matrix for both TFMs and at least Avalonia 11.0 plus the chosen current 11.x version, with headless Avalonia tests covering pointer boundaries, tooltips, detach, registration replacement/disposal, and accessibility behaviour.

## Prioritized plan

1. Fix hyperlink hit-testing and activation semantics, then add comprehensive Avalonia headless tests.
2. Close the markdown-parameter/navigation-policy security gap and replace the unsafe readme example.
3. Make `staticres:` controls instance-safe and strengthen the `assets:` containment contract.
4. Remove or contain global mutable state and make font sizing host-aware.
5. Unify formatting/culture/error handling across `WordsInline` and converters.
6. Correct flags conversion and add a net8/net10 × Avalonia-version test matrix, applying shared fixes to the WPF twin where noted.

Codex session ID: 01a08a45-d630-7593-872f-336c65ce6c27
Resume in Codex: codex resume 01a08a45-d630-7593-872f-336c65ce6c27
