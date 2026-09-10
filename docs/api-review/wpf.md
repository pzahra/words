No files were modified. Static review only; I did not run the test suite because it would write build/test artifacts.

## Findings

1. **High — [`ImageSchemes.cs:263`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ImageSchemes.cs:263)**  
   **Problem:** `FolderImageResolver.ResolvePath` provides only lexical containment. `GetFullPath` plus `StartsWith` blocks `..`, rooted paths, and encoded separators, but does not stop an `Assets` subdirectory that is a junction or symbolic link from resolving outside the root. This contradicts the README’s strong “only that folder” guarantee at [`readme.md:87`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/readme.md:87).  
   **Recommendation:** Resolve/reject reparse points component by component, or open the file through a handle and verify its final resolved path remains beneath the resolved root. If that guarantee is not implemented, weaken the documentation to explicitly state that containment is lexical and trusted filesystem layout is required.

2. **High — [`ImageSchemes.cs:141`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ImageSchemes.cs:141)**  
   **Problem:** `StaticResImageResolver` returns a resource-owned `FrameworkElement` or `Shape` directly. Normal WPF resources are shared, so the first inline parents the element and later uses fail because a `FrameworkElement` cannot have multiple logical/visual parents. `WithFill` also mutates the shared `Shape`, leaking one markdown image’s foreground into unrelated consumers; frozen/read-only resources will throw immediately.  
   **Recommendation:** Never parent or mutate a shared resource instance. Accept factory-like resources (`DataTemplate`, delegate, or explicit resolver), clone supported `Freezable`/drawing values, and return `null` with a clear diagnostic for arbitrary shared elements that cannot safely be cloned. Document that custom resolvers must return a fresh, unparented element.

3. **Medium — [`MarkdownConverter.cs:41`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/MarkdownConverter.cs:41)**  
   **Problem:** The target-type test is effectively reversed. When `targetType` is `TextBlock`, the converter returns an `Inline`, despite declaring conversion to `TextBlock`; for an `Inline`/`Span` target it returns a `TextBlock`. The XML comment repeats this contradiction, while the README says the converter produces “WPF inlines.” The sample only works because `ContentControl.Content` has target type `object`, which takes the `TextBlock` branch.  
   **Recommendation:** Define one unambiguous contract. Prefer returning a `TextBlock` for `TextBlock`/`FrameworkElement`/`object` targets and an `Inline` for `Inline` targets; return `DependencyProperty.UnsetValue` for unsupported targets. Correct the attributes, XML documentation, and README accordingly.

4. **Medium — [`Hyperlink.cs:31`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/Hyperlink.cs:31)**  
   **Problem:** `hooked` and `current` are unsynchronized process-wide state. Concurrent registrations can install the supposedly single class handler more than once, making one click invoke the current delegate repeatedly. Concurrent registration/disposal/navigation also has no visibility or ordering guarantee. The API does not enforce the WPF dispatcher/STA assumptions.  
   **Recommendation:** Require registration and disposal on `Application.Current.Dispatcher` and enforce that contract, or synchronize initialization and delegate access with `Interlocked`/locking. Add cross-thread tests and explicitly document that callbacks execute on the hyperlink’s dispatcher thread.

5. **Medium — [`Hyperlink.cs:52`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/Hyperlink.cs:52)**  
   **Problem:** A stale subscription can unregister a newer registration when both registrations use the same delegate instance. Equality is based on the delegate, not registration identity: `Register(h); var newer = Register(h); old.Dispose()` clears `newer`, contradicting the comment and replace-on-reregister contract. A runtime-null handler is also silently treated as unregistration.  
   **Recommendation:** Assign each registration a unique token and clear only when the active token matches. Reject null with `ArgumentNullException.ThrowIfNull(handler)`.

6. **Medium — [`MarkdownParser.cs:27`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/MarkdownParser.cs:27)**  
   **Problem:** `MarkdownParser.Default` and its public `Dictionary` registry are globally mutable without synchronization. A converter can read `Default` while another thread replaces it, and a dictionary mutation can race image lookup. More fundamentally, generated WPF objects and application resources are dispatcher-affine, but the public parser contract presents no UI-thread restriction.  
   **Recommendation:** Make default configuration immutable after startup, expose resolver registration through synchronized methods or an immutable snapshot, and document/assert dispatcher access for parsing that creates WPF objects. Consider injecting a parser into `WordsInline`/`MarkdownConverter` instead of relying solely on ambient global state.

7. **Medium — [`ImageSchemes.cs:213`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ImageSchemes.cs:213)**  
   **Problem:** `ResxImageResolver` scans every type in every loaded assembly on every resolution. `assembly.GetTypes()` and `ResourceManager` property access occur outside the narrow catch, so one unloadable/type-forwarding/dynamic assembly or throwing getter aborts the entire search. It also checks only the first `Resources`-like type in each assembly, making duplicate resource classes and keys nondeterministic.  
   **Recommendation:** Require or allow an explicit assembly/resource base name. Cache discovered resource managers, iterate all candidate resource types, handle `ReflectionTypeLoadException` using its successfully loaded types, and isolate failures per assembly/type. Define deterministic precedence for duplicate keys.

8. **Medium — [`ImageSchemes.cs:106`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ImageSchemes.cs:106)**  
   **Problem:** Width and height accept `NaN`, infinity, negative numbers, and arbitrarily large finite values. These parse successfully but can throw when assigned to WPF properties or provoke excessive layout/allocation. The parser catches the result and degrades to alt text, so a bad display option misleadingly looks like a missing asset.  
   **Recommendation:** Accept only finite, non-negative values within a documented maximum. Log option-validation failures separately from resolution failures.

9. **Medium — [`ImageSchemes.cs:247`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ImageSchemes.cs:247)**  
   **Problem:** File and RESX bitmap decoding happens synchronously and fully on the UI thread with `BitmapCacheOption.OnLoad`. Width/height options are applied after decoding rather than via `DecodePixelWidth`/`DecodePixelHeight`; a large or malicious image can stall the UI or exhaust memory even when rendered as a small icon. The RESX path also leaves its temporary `MemoryStream` undisposed.  
   **Recommendation:** Impose file/pixel limits, pass requested decode dimensions into bitmap initialization, cache frozen image sources where safe, and dispose the RESX stream after `EndInit`.

10. **Medium — [`WordsInline.cs:45`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/WordsInline.cs:45)**  
    **Problem:** The method clears existing inlines before formatting and parsing. A malformed format string, reflective property getter exception, invalid parser state, or custom resolver/logger failure can propagate through a dependency-property callback and leave the control blank. This is inconsistent with the image path’s stated graceful-degradation philosophy.  
    **Recommendation:** Build the replacement inline collection first, then swap it in only after success. Define a failure policy—escaped source text, `#key#`, or an error run—and log through a non-throwing boundary.

11. **Medium — [`FlagsDescriptionConverter.cs:61`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/FlagsDescriptionConverter.cs:61)**  
    **Problem:** The converter does not reliably mean “each flag set.” It splits `Enum.ToString()`, which can prefer a named composite member instead of its constituent bits and emits a numeric token when unknown bits exist. With `IncludeNone == false`, `Convert.ToInt64` throws for unsigned enum values above `long.MaxValue`. Non-`[Flags]` enums are accepted despite the converter’s purpose.  
    **Recommendation:** Validate `[Flags]`, enumerate defined atomic values using an unsigned underlying representation, specify how composites, aliases, zero, and unknown bits are handled, and avoid `Convert.ToInt64`.

12. **Medium — [`FlagsDescriptionConverter.cs:66`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/FlagsDescriptionConverter.cs:66)**  
    **Problem:** `AsArray` does not return an array; it returns a deferred LINQ sequence. Parsing, localization lookup, exceptions, and reads of mutable converter/global settings can therefore happen later and repeatedly, potentially after the language or `IncludeNone` changed. This disagrees with the property name and weakens binding predictability.  
    **Recommendation:** Materialize the result once (`string[]` or a documented immutable list), snapshot all settings at conversion time, and rename `AsArray` if the intended contract is merely enumerable output.

13. **Medium — [`WordsInline.cs:57`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/WordsInline.cs:57)**  
    **Problem:** Positional arrays format with `CurrentUICulture`, while named-object parameters format with `CurrentCulture`. Dates and numbers can therefore change format solely because callers switch from an object to an array. `WordsConverter` instead uses the binding-supplied culture, creating a third observable contract.  
    **Recommendation:** Use one policy throughout—normally the binding/element language culture when available, otherwise `CurrentCulture`. Do not use `CurrentUICulture` for numeric/date formatting.

14. **Low — [`WordsInline.cs:35`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/WordsInline.cs:35)**  
    **Problem:** Re-rendering occurs only when the `Params` dependency-property reference changes. Changes inside a bound object, collection, or array do not trigger rendering, despite the README’s broad “whenever `Params` changes” claim at [`readme.md:39`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/readme.md:39). `Key` is also declared non-nullable even though its dependency-property default is null.  
    **Recommendation:** Clarify that only DP value replacement triggers rendering, or subscribe weakly to `INotifyPropertyChanged`/`INotifyCollectionChanged`. Make `Key` nullable or give it a non-null default and validate assignments.

15. **Low — [`MarkdownParser.cs:115`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/MarkdownParser.cs:115)**  
    **Problem:** Geometry and sub/superscript sizes use the parser’s fixed default of 13, not the surrounding `TextBlock`/flow-content font size. Consequently the README statement that geometry defaults to “the font height” at [`readme.md:94`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/readme.md:94) is false for any differently sized control.  
    **Recommendation:** Bind sizing to inherited font size, pass the owner’s effective font size into parsing, or describe it accurately as the parser’s configured base size.

16. **Low — [`ImageSchemes.cs:265`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ImageSchemes.cs:265)**  
    **Problem:** `GetFullPath(root) + separator` mishandles callers that already supply a trailing separator: the comparison prefix can contain a doubled separator while the candidate path is normalized. `StringComparison.Ordinal` also encodes case-sensitive semantics for a Windows-only component.  
    **Recommendation:** Normalize with `Path.TrimEndingDirectorySeparator`, append exactly one separator, and use an OS-appropriate comparison. Validate that `root` is non-empty and absolute at construction.

17. **Low — [`WordsExtension.cs:63`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/WordsExtension.cs:63)**  
    **Problem:** A missing resource produces a null dereference at `.Stream`; malformed or non-pack URIs and null builders also fail without API-specific diagnostics. This is an application-startup path, so the current failure is abrupt and opaque.  
    **Recommendation:** Validate `wb` and `packUri`, require an absolute `pack:` URI if that is the advertised contract, test the returned `StreamResourceInfo`, and throw `FileNotFoundException`/`ArgumentException` containing the URI.

18. **Low — [`ArrayMultiConverter.cs:33`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/ArrayMultiConverter.cs:33), [`EnumDescriptionConverter.cs:49`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/EnumDescriptionConverter.cs:49), [`FlagsDescriptionConverter.cs:88`](C:/Users/PatrickZ/Source/imports/words/Localization-Wpf/FlagsDescriptionConverter.cs:88)**  
    **Problem:** Unsupported reverse conversion throws `NotImplementedException`, while the other converters use `NotSupportedException`. Accidental `TwoWay` bindings consequently create inconsistent binding-engine failures.  
    **Recommendation:** Use a consistent one-way contract—prefer `Binding.DoNothing`/`DependencyProperty.UnsetValue` where binding resilience matters, or uniformly throw `NotSupportedException`.

## Prioritized plan

1. Fix filesystem containment and stop returning/mutating shared WPF resources.
2. Correct `MarkdownConverter`’s output contract and harden global `Hyperlink`/`MarkdownParser.Default` state.
3. Replace RESX discovery and flag decomposition with deterministic, cached implementations.
4. Validate image dimensions, bound decode cost, and make `WordsInline` updates transactional.
5. Unify culture/null/error contracts, then update the README and add behavioral tests for converters, repeated `staticres:` use, symlink/junction escapes, same-delegate subscriptions, and non-default font sizes.

Codex session ID: 01a08a45-a507-7011-b204-645ff1d3f760
Resume in Codex: codex resume 01a08a45-a507-7011-b204-645ff1d3f760
