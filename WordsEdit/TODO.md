# Wordsmith Editor — prioritised TODO

The work left to make the editor match [SPEC.md](SPEC.md), in the order it
should be done. Items reference files and symbols, not line numbers, so they
stay true as the code moves. Tick them off here.

## Decisions (settled — don't re-open)

- **Dialogs become real windows.** Material's `DialogHost` is dropped; the
  theme and `PackIcon`s stay. `DialogHost` allows one active session, which is
  why a dialog cannot open another (Language Manager → Edit Language silently
  fails), why `PopupDialog.Push` needs a dispatcher detour, and why its
  failures vanish into an unobserved task. Owned `Window`s nest freely, are
  properly modal, get Escape/Enter for free, and put a seam (`IDialogs`) where
  the ViewModels can be tested.
- **The parallel `Keys` collection goes.** `MainWindowViewModel.Keys` mirrors
  `allKeys`, is bound by nothing, and is hand-synchronised in eight places.
  One store: the dictionary, owned by the document.
- **The god class is dismantled by moving the document out, not by slicing the
  ViewModel.** `MainWindowViewModel` (1085 lines) is a ViewModel doing the
  processing SPEC assigns to `Localization.Authoring`. Phase 2 moves the
  document there; what remains is split by concern in Phase 3.

## Why this order

Phase 0 is bugs that are minutes each and depend on nothing. Phase 1 (windows
+ `IDialogs`) is what makes every command testable, so it comes before the big
move. Phase 2 does the extraction — and it is where the drag-drop, rename and
collision bugs get fixed *once*, in tested operations, instead of patched in
the ViewModel and then moved. Phases 4–6 are the payoff and the polish.

---

## Phase 0 — Small, independent fixes

Verified bugs first. Each is a few lines; none blocks or is blocked.

- [x] `Views/EditLanguageView.xaml` binds `LanguageNativeName` and
      `LanguageEnglishName`; the VM has `NativeName`/`EnglishName`. Both boxes
      are inert, validation demands them, so **a language can never be added**.
- [x] `Views/MergeControl.xaml` binds `DataContext.LocalizationLanguages`; the
      VM has `Languages`. The per-file language list is always empty, so
      **merge cannot be driven from the UI**.
- [x] `TestParametersViewModel.DoAddParameter` is `for (i < Count)` with
      `Add` inside: a no-op on an empty list, unbounded growth otherwise.
      `int i = 0; while (Parameters.Any(p => p.Key == $"P{i}")) i++;` then add.
- [x] `KeyNameViewModel`: `CancelCommand` is gated on `CanProceed`, so the Add
      dialog opens with both buttons disabled. Cancel is never conditional.
- [x] `EditLanguageViewModel.Validate`: only the code branch clears errors;
      one bad keystroke in either name disables Add forever. `ClearErrors` at
      the head of every branch, `ClearAllErrors` on the all-path (as
      `KeyNameViewModel` does).
- [x] `DragDrop.KeyDragDropHandler.Drop`, drop onto own descendant: the guard
      re-inserts `targetKeyNode` where `draggedKeyNode` was. Subtree vanishes,
      keys linger, descendant duplicated.
- [x] `DragDrop.KeyDragDropHandler.Drop`, no `InsertPosition` matched:
      `newParentNode` stays null, the subtree is rooted with its file prefix
      stripped and is never saved. Restore and bail; a non-file node never
      sits at the root.
- [x] `IsDirty` gaps: `DoToggleKeyNeedsReview` (the raise-hand) never dirties;
      editing a parameter's key/value/type in the dialog never dirties (the
      `TODO` in `TestParametersViewModel` is real — observe item
      `PropertyChanged`).
- [x] `LanguageManagerViewModel.DoRemoveLanguage`: SPEC (Languages) says
      entries are deleted **after confirmation**; there was none. (The audit's
      claim that `CanRemoveLanguage` contradicted its message was a misread —
      `> 1` and "at least two" agree.)
- [x] `MainWindow.xaml` toggle buttons carry both `Command` and a two-way
      `IsChecked` to the same node flag, so binding and command write the node
      twice and disagree when there is no key data. `IsChecked` one-way; the
      command is the only writer.
- [x] `KeyNode()` parameterless ctor: zero call sites, sole source of the three
      CS9264 warnings, exists only as a throwaway accumulator in `GetFileNode`
      (give it a `List<KeyNode>`). Delete it, then delete the `FullLabel is
      null` guards it made necessary (`GetParentNode`, `DeepestVisible…`).
- [x] `MainWindow.MainWindow_Closing`/`ConfirmClose`: "No" calls `Shutdown()`
      with `IsDirty` still true, which can re-raise `Closing` (verify); set a
      closing flag first. `ConfirmClose` has no `await` — drop the `async`
      wrapper and the `SafeFireAndForget`.
- [x] `MergeControlViewModel.DoMerge`: a normal key-set conflict throws
      `"Merge Failed"` although `FilesChanged` already builds the message.
      Show it; bind `HasConflict` (unused) instead of the
      `StringIsEmptyVisibilityConverter` on the panel, which looks inverted
      (verify).
- [x] `MergeControlViewModel.DoMerge` writes `Parent.KnownLanguages` (the
      session union) and no `preamble:`/`imageSchemes:`, so the merged file
      loses the base file's table, preamble and image mappings on the round
      trip SPEC guarantees. Pass `LanguagesFor(base)`, its preamble, its
      schemes. (Phase 2 makes this a one-liner on the session.)
- [x] Every `throw` for an ordinary UI state becomes an early return:
      `MainWindow.xaml.cs` (×7, incl. null `SelectedKey` in the preview
      `Checked` handlers), `MergeControl.xaml.cs` (×2),
      `MainWindowViewModel.OnSelectedKeyValueChanged`/`OnSelectedEntryChanged`
      ("Phantom Key Value Change" — thrown from a keystroke),
      `DoRemoveLocalizationKey`/`DoAddLocalizationKey` (`InvalidDataException`
      for "nothing selected"), `LanguageManagerViewModel.DoRemoveLanguage`,
      the reachable ones in `DragDrop`. Its `MainWindow is null` guards stay:
      an unwired handler is a broken invariant, which is what exceptions are for.
- [x] `MainWindow.FormatParameters`: `Regex.Escape(parameter.Key)` — a key
      with `(`, `+`, `.` throws on every render. Interim; deleted in Phase 2.

## Phase 1 — Dialogs become windows, `IDialogs` appears

- [x] `IDialogs` (editor, VM-facing): `Confirm`, `AskToSave`, `Tell`,
      `TryOpenFiles`, `TrySaveFile`, `Show(DialogViewModel)`. Injected into
      `MainWindowViewModel`; the dialog VMs get it from their parent.
- [x] `Views/DialogWindow.xaml`: one shell `Window` — `ContentPresenter`,
      `Owner` = main window, `SizeToContent`, Escape closes, no fixed sizes.
      Content chosen by `DataTemplate` per VM type, so the existing
      `UserControl`s become templates with no rewrite.
- [x] `WpfDialogs : IDialogs` opens a `DialogWindow` with `ShowDialog()`;
      `FakeDialogs` for tests records calls and returns scripted answers.
- [x] Migrate every `PopupDialog.Push(...)` site (`MainWindowViewModel` ×9,
      `LanguageManagerViewModel`, `EditLanguageViewModel`, `MergeControlViewModel`).
      Sub-dialogs (Edit Language from the manager) just open another owned
      window — the "close before push" dance is gone.
- [x] Remove `md:DialogHost` from `MainWindow.xaml`; delete `PopupDialog.Push(Control)`
      and `PopupDialog.Close()`; fold `Push(string)`/`ShowDialog`/`TryFileOpen`/
      `TryFileSave` into `WpfDialogs`. `Utils/PopupDialog.cs` goes.
- [x] Dialog VMs stop reaching for statics: `PopupDialog.Close()` becomes
      `DialogViewModel.CloseRequested`, which the shell subscribes to.
- [x] Tests unlocked by this: `DoLoadFiles`, `DoReset` (confirm path),
      `DoRemoveLocalizationKeyAndNode` (file-removal confirm), the merge and
      language flows end to end with `FakeDialogs`.

## Phase 2 — The document moves to `Localization.Authoring`; `Keys` goes

The god-class fix, part one. Everything here is testable with a `StringReader`
and no WPF.

- [ ] **`WordsSession`** (Authoring): the document a session holds.
      Per file (keyed by **full path**, label kept separately): path, label,
      preamble, trailer, declared languages, image schemes, load gripes.
      Session-wide: `Keys` (the one dictionary, prefixed), `KnownLanguages`.
      `Load(TextReader, path)`, `Unload(path)`, `Save(path)`/`SaveAll()`,
      `Reset()`. It absorbs `LoadFile`'s 95 lines (prefixing, empty-key drop,
      replace-on-reload, label reconciliation, library detection) and both
      copies of the entry-backfill loop, scoped to the loaded file's keys.
      Keying by path fixes same-named files in different folders writing over
      each other; `Unload`/reload removing keys under the prefix first
      (`RemoveKeysUnder` exists) fixes keys deleted on disk surviving a reload.
      `Errors` are kept per file for Phase 4.
- [ ] `Load` and `Save` catch and report per file; the parser's bare
      `Exception("Name for language never declared")` becomes a gripe, not a
      crash. A bad file never kills the session.
- [ ] **Delete `MainWindowViewModel.Keys`.** `session.Keys` is the store; the
      eight sync sites go; iteration is `.Values`.
- [ ] **`KeyTree.Build(session, label)`** (Authoring) replaces
      `MainWindowViewModel.GetFileNode`: dotted keys → file/group/key nodes,
      `$` stripping, comment nodes anchored from `BlockComments`, preamble and
      trailer organizers. Returns structural nodes (`IKeyTreeNode`/`ICommentNode`,
      already defined for the writer); `KeyNode` in the editor wraps them with
      UI state (`IsSelected`, `IsExpanded`, `IsVisible`, badges).
- [ ] `KeyNode.Parent` set on insert/move. Kills `GetParentNode` (the
      self-flagged "FIXME: this is awful", O(depth×breadth), called twice per
      `DragOver`) and its duplicate walker `DeepestVisibleKeyNodeInBranch`.
- [ ] Badges computed once, from the model: `WordsKey.HasStaleValue(code)`
      exists; add `HasRegionalOverride(code)`; one marker pass replaces the
      copies in `GetFileNode`, `MarkStaleNodes`, `MarkOverwrittenNodes` and the
      duplicated loop in `OnSelectedLanguageChanged`.
- [ ] **`WordsOperations.Rename(keys, oldPrefix, newPrefix, out collisions)`**,
      **`Move(keys, oldPrefix, newParentPrefix, out collisions)`**,
      **`SetConstant(keys, blockKey, bool)`**. These replace
      `RenameLocalizationKeyAndNode`, `UpdateChildFullLabels`, `MoveKey`,
      `SetConstantMarker` and the key assembly in `DragDrop.Drop`. Collisions
      are *reported*, never silently deleted (today: colliding key removed,
      its descendants orphaned in `allKeys`; `foo` + `$foo` throws
      `ArgumentException`; renaming a file node desyncs four dictionaries and
      breaks `Save`). `Drop` shrinks to "node X becomes child N of node Y".
- [ ] Toggling constant wipes every translation: confirm first (via
      `IDialogs`), and `SetConstant` should preserve entries unless told to
      clear.
- [ ] **`LanguageTable`** (Authoring): per-file declared codes in order plus
      the session union; `Add`/`Remove`/`Rename`/`Reorder` that also backfill
      or strip `WordsKey.Entries`. Replaces `AddLanguageCode`/`RemoveLanguageCode`/
      `ReplaceLanguageCode`/`LanguagesFor`/`fileLanguages` and the hand-rolled
      entry loops in `LanguageManagerViewModel`. Fixes: reordering languages
      by drag dirties the session but the writer never sees it; a removed
      file's languages linger in the dropdown.
- [ ] `SelectedLanguage` re-pointed after load by code, not identity
      (`LanguageEntry` has no value equality): today the ComboBox writes
      `null` into a non-nullable property and the next language change
      dereferences it. Make it `LanguageEntry?` or reject null in the setter;
      never leave `KnownLanguages` empty.
- [ ] Delete `MainWindow.FormatParameters`. It is a diverging copy of
      `Words.PreFormatByName` (`"g"` default, hard-coded culture, unknown
      names silently ignored). `WordsOperations.FormatSample(WordsKey, text)`
      delegates to Core so the preview shows what the host app renders.
- [ ] `Merge` and `Split` on the session: `session.Merge(basePath,
      languageSources, outPath)` writes the base file's table/preamble/schemes
      and loads the result; `Split` is the same call in reverse. Both have
      tests before either has UI.
- [ ] Tests, all without WPF: rename cascade over a group with children;
      move with collision; constant on a `$`-sibling; reload after a key was
      deleted on disk; two `strings.ini` in different folders; a file with
      keys and no labels; `!`-only file beside a normal one; language
      add/remove/reorder round trip; `FormatSample` with regex metacharacters
      and an unparseable sample; `Split`; `Merge` output carrying the base
      file's table and preamble.

## Phase 3 — Split what is left of `MainWindowViewModel`

The god-class fix, part two. After Phase 2 the class is selection, filters,
badges and command wiring.

- [ ] Split by concern (partial classes first if the risk feels high):
      `MainWindowViewModel` (session, commands, dirty/title),
      `TreeViewModel` (`KeyNodes`, selection, filters, badges), with the key
      and language commands as thin calls into `WordsOperations`/`LanguageTable`.
      Delete the five "Sections / Subsections" index comments — the split is
      the index.
- [ ] Preview moves to the VM: `RenderedDefault`, `RenderedTranslation`,
      `PreviewError` strings; the View binds a `TextBlock` through a
      markdown-inline converter. Removes `DefaultPreview_Checked`/
      `LocalizationPreview_Checked` and the name-based visibility flips, and
      the preview updates live while open instead of once per toggle.
- [ ] Delete `Preview_Clicked`, `FollowLink`, `FindClickedHyperlink`,
      `FindHyperlinkInline*` (~80 lines of hit-testing). The Wpf package has
      `Hyperlink.RegisterGlobalNavigateHandler`; use it. The confirm prompt,
      if kept, goes in the handler.
- [ ] `MergeControl.xaml.cs` empties: multi-select and "one file per
      language" become VM rows (`IsSelected`, `Languages` per file) enforced in
      `MergeControlViewModel`; the two identical mouse-wheel handlers go.
- [ ] `MainWindow_Closing` asks the VM (`TryClose()` → save / discard /
      cancel) and only cancels the event.
- [ ] `ShowDefaultPreview`/`ShowLocalizationPreview` vs `ElementName`
      bindings vs code-behind visibility: one mechanism.

## Phase 4 — UX the spec asks for

- [ ] Surface provider gripes (SPEC: Out of scope → now in): a badge on the
      file node, a details dialog listing `session.Errors[file]`. Today they
      are collected and thrown away.
- [ ] Keyboard shortcuts — there are none. Ctrl+O/S, F2 rename, Delete
      remove, Ctrl+F focus search, Ctrl+Shift+S stale-all.
- [ ] Tree context menu for the structure edits (today: a button strip).
- [ ] Title: bind `TitleMarked` (exists, unbound) and include the loaded file
      names.
- [ ] `IsLibraryFile` badge — computed and tested, bound to nothing. SPEC (The
      document): `!` labels are intentional and must read that way.
- [ ] Language dropdown fed by the selected key's file (SPEC: translation
      pane), not the session union.
- [ ] Filters: match values, context and comments (what a translator searches),
      not just key names; show a match count / "N hidden"; a clear button;
      stale-only stops silently including empty values.
- [ ] Test Parameters shows the formatted result (SPEC: parameter testing) and
      its "Close" is not a `CancelCommand` that reverts nothing.
- [ ] Stale timestamp read-only beside the toggle, not a free-text box.
- [ ] Confirmations on the destructive actions: constant toggle, remove key
      data, collisions. Then consider an undo stack over `WordsSession`.
- [ ] Split UI, hosted in the merge dialog (its output is what Merge consumes).
- [ ] Image schemes reach past the ini's folder (SPEC: Markdown previews): the
      manager takes absolute and `..` folders, and location-bearing schemes
      (`pack:`, `assets:`, `resx:`, `staticres:`) resolve from the URI itself
      instead of a flat scheme→folder row. The path inside a URI stays
      clamped to whatever root results; nothing is fetched remotely.
- [ ] Language Manager keeps a local selection and commits on OK — clicking
      down its list currently re-contextualises the whole window behind it.
- [ ] Merge dialog: a way to clear the base file; stop mutating tree nodes
      (`IsBaseFile`) for display; no fixed 450×800.
- [ ] Selection follows the filter: `ApplyFilters` never re-checks that the
      selected node is still visible.

## Phase 5 — Cleanup (ride along with any phase)

Dead code, measured by reference count, not guessed:

- [ ] `Utils/DateTimeOffsetToStringConverter.cs` — 0 refs (`Stale` is a
      `string?`). Delete.
- [ ] `KeyNode.DeepestVisibleKeyNodeInBranch` — 0 callers (or wire it for the
      selection-follows-filter item, then delete `GetParentNode` instead).
- [ ] `DelegateCommand` and `DelegateCommand<T>`: `SafeExecute`,
      `RaiseCanExecuteChanged`, `CanExecute(T)` — 0 uses; the null-vs-value-type
      ceremony guards nothing the app does.
- [ ] `Utils/Extensions.cs`: `GetEnumMemberInfo` ×3, `GetEnumMemberAttribute`,
      `TryMatch`, `TryGetGroup`, `WhereNotNull` ×2 — 0 uses **and** verbatim
      copies of `Localization-Core/Extensions.cs`; `SafeFireAndForget` lost its
      last caller in Phase 1. Keep `IsNullOrEmpty`, `FindIndex`, `ForEach`.
- [x] `WordsEdit/Extensions.cs` — a second `static class Extensions` with one
      method, a dangling doc comment and five unused usings. Deleted in Phase 1;
      `IsAffirmative` had no callers left to fold.
- [ ] `App.xaml.cs`: the command-line startup-file parser is commented out, so
      `StartupFiles` is always empty and `MainWindow`'s `ForEach(LoadFile)` is
      dead. Restore it or remove both ends.
- [x] Commented-out code: `//Words.Known = …` (`MainWindow.xaml.cs`),
      `//FollowLink(…).SafeFireAndForget` (same), `//ResetPopup().SafeFireAndForget`
      (`MainWindowViewModel`).
- [ ] `AffectProperty(nameof(SelectedLanguage))` refresh hacks after stale
      changes — `WordsEntry.Stale` already raises `PropertyChanged` and is
      bound directly.
- [ ] `LanguageManagerViewModel.DoOkay` assigns two properties to themselves.
- [ ] `DataViewModelBase`: a `Lock` and five `lock` blocks around a
      UI-thread-only dictionary. `ViewModelSaveBase.ChangeProperty(…, bool
      dirty)`: never called with `true`.
- [ ] `App.xaml` `Zero` resource and `TestParameters.xaml` `IsTrue` converter:
      unreferenced. Unused usings/xmlns per file (`System.Data`,
      `System.Diagnostics.CodeAnalysis`, redundant `PatTech.Localization.Authoring`
      beside the project-wide `<Using>`, `xmlns:v`, `xmlns:local` ×3).

Naming (SPEC: "Short names"):

- [ ] The `*Localization*` commands and methods (`RemoveLocalizationKeyAndNodeCommand`
      and six siblings, up to 41 chars) name a type that no longer exists.
      `RemoveNode`, `RenameNode`, `AddNode`, `AddKey`, `RemoveKey`,
      `ToggleReview`, `ToggleConstant`; variables `selectedLocalizationKey` →
      `key`.
- [ ] `KeyDragDropHandler.MainWindow` is a ViewModel (`LanguageDragDropHandler`
      calls its own `LanguageManager`). `Vm` in both.
- [ ] `DragDrop.cs` holds two handlers named neither `DragDrop` nor after the
      file, and shadows `GongSolutions.Wpf.DragDrop` in the mind: `KeyDrag.cs`,
      `LanguageDrag.cs`.
- [ ] View files vs classes: `MergeControl.xaml`, `KeyName.xaml`,
      `TestParameters.xaml` → `*View.xaml` like their siblings.
- [ ] `Utils/PopupDialog.cs` declares `namespace WordsEdit.ViewModels` (moot
      after Phase 1); `Utils/DateTimeOffsetToStringConverter.cs` declares
      `WordsEdit.Views` (moot after deletion).
- [ ] `AllKeyNodes` (PascalCase private field) beside `allKeys`; `result2` ×4
      with no `result1`; hard-coded "Default English". ("Lanuage" and "reset
      the Language Manager" went in Phases 0 and 1.)
- [ ] `ApplyFilters()` returns a constant `true` so a setter can `&&` it.
- [ ] Organizers use synthetic dotted keys (`file.;preamble`, `parent.;comment`)
      as identity, so two under one parent share a `FullLabel` and
      `UpdateChildFullLabels` rewrites them to `parent.;`. A stable id.

## Phase 6 — Tests not already listed above

- [ ] Drag-and-drop, currently untested entirely: reparent a subtree, drop
      before/after a sibling, reorder files, drop a comment node, drop onto own
      descendant, drop with no insert position, drop onto a same-named sibling.
- [ ] One `Assert.True(IsDirty)` per mutation (add key/node, rename, remove,
      drop, constant, needs-review, stale, organizer edit, image-scheme edit);
      `Assert.False` after `Save` and `Reset`.
- [ ] Filters: `NeedsReviewFilter` alone; needs-review + search; stale +
      needs-review; filter then clear; organizer text matched by search;
      selection vs visibility.
- [ ] `RemoveFileNodeCore` with two files: selection moves to the survivor,
      the survivor is untouched.
- [ ] `ImageSchemesViewModel.DoOkay`: blank rows dropped, duplicate scheme
      last-wins, case-insensitive, dirties; `Save` threading schemes for two files.
- [ ] `MainWindowViewModel_AddLocalizationKeyTest` asserts a key is created
      **on a file node**, which SPEC (The tree) forbids. Fix the test, add the
      guard.
- [ ] Hygiene: `MergeTest`'s 24 assertions appear twice verbatim; the
      accumulate-a-bool pattern (`CanBeConstantTest`, `StaleAllLanguagesTest`,
      `AddLocalizationKeyTest`) becomes `Assert.All`/`Assert.Contains` so a
      failure names the node. `WordsEdit.Tests.csproj` still removes an
      `ExampleFile.ini` that lives in `Resources/`.
