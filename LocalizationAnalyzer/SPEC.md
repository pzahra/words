# Words analyzer and Rider plugin — what they check and offer

The analyzer is the `PatTech.Localization.Analyzer` package: two Roslyn rules, PTL001 on
`[Localized]` and PTL002 on `[WordsKey]`, and the small assembly that defines both
attributes. The Rider plugin, WordsXaml (under `../WordsXaml/`), offers the same keys as
completion and tooltips in the editor, in XAML and in C#, and squiggles an unknown key
where the compiler cannot look; it ships on the analyzer's release track. Neither
references the runtime: each copies the runtime's reading of a `words.ini`, and the
runtime's spec ([../Localization-Core/SPEC.md](../Localization-Core/SPEC.md)), its *Key
names* and *Plural forms* above all, is the grammar both follow. Wordsmith, which writes
the files, has its own ([../WordsEdit/SPEC.md](../WordsEdit/SPEC.md)). Everything up to
*Planned upgrades* is what the two do today; the last part is what they do not do yet.

## Where the words come from

**The analyzer's files.** PTL002 checks keys against the `*words.ini` files a project hands
its compilation as AdditionalFiles: any whose path ends in `words.ini`, case aside, so
`words.ini`, `framework-words.ini` and `sample-words.ini` all count.

```xml
<AdditionalFiles Include="Assets\framework-words.ini;..\Sample-Shared\sample-words.ini" />
```

The convention is to name every words file `*words.ini`; a file named otherwise is never
read. Wordsmith, LocalizedSample and the samples hand theirs over this way. The keys of
every file go into one set, read once per compilation. With no such file, or none that
declares a key, the rule registers nothing and stays silent, so a project that keeps its
words elsewhere never sees a false positive.

**Read as the runtime reads.** The analyzer cannot reference Core, so `WordsIniKeys` copies
`WordsParser`'s patterns as written there. Only names matter; values are never kept.

- A header is `[name]` at the start of its line, the name as written, untrimmed:
  `  [indented]` is no header, and `[ spaced ]` names ` spaced `. Text after the `]` is
  ignored.
- `[.child]` appends to the last full header; a dot-header never becomes the base. The
  resolved name must pass the key-name grammar (`WordsParser.IsKeyName`, the runtime's
  *Key names*). A block that fails is no key, and nor are the `[.child]` headers under it.
  A constant, `[$unit]`, has no children, since `$unit.child` is no name, and a `[.x]`
  before any full header resolves to `.x`, which is none either.
- `[]` is a header that names no key, and it leaves no base: a `[.child]` under it
  resolves to `.child`, not to the key above's child.
- A field, `field-lang#form=text` or with `:`, whose text ends in a continuation (a
  trailing `\` or `_`) runs on through the next line, whatever that line says, so a
  continued line is never a header. The test pairs each `\` or `_` with the character
  after it, so a doubled `\\` or `__` is an escape and continues nothing. Every field
  runs on so, kept or read past: `comment=`, a language that is no code.
- `;` lines, indented or not, are comments, and any other line is skipped.

A header is enough: a block counts as a key whether or not it holds a value.

**The plugin's index.** `WordsIndexService` walks the folder of every project in the
solution, everything under it included (build output too), for files named `*words.ini`,
parses each with `WordsIniParser` (*The plugin's reader*) and builds one `WordsIndex`: an
entry per key, compared exactly, case and all. Unlike the analyzer the plugin keeps
values, for the tooltip: a key's `value=` per language (`Values`, the default under `""`)
and its plural forms per language (`Forms`). A key declared in several files is one
entry, its values merged per language and per form with the later file winning, as a
later `Load` wins in the runtime; it keeps the first declaration's file and line. "Later"
is the order the files are enumerated in. The index is rebuilt from scratch when the
solution loads and on every change the solution reports.

**Why.** The analyzer runs inside the compiler, on netstandard2.0 with no package of its
own to lean on, and the plugin inside Rider's backend; neither can load Core. Copying the
runtime's patterns keeps "a key the code may name" and "a key the app will find" the
same: a block the runtime skips with `WP:NAME` must not satisfy the analyzer, and a line
the runtime reads as a continuation must not declare a key. Both copies say so beside
their patterns: keep them in step.

**Tests.** `WordsKeyAnalyzerTests` reads a header whose name is none as no key, its child
too, with `[x#y]`, an untrimmed `[ spaced ]` and an indented header among them; `[]`
leaves no base for a child; a `[.early]` before any full header is no key; a constant has
no children; the line after a continued field is no header, whether the field is a
value, a comment or a language's, ends in `\` or `_`, or runs over two lines, while `\\`
and `__` continue nothing; and with no
`*words.ini` the rule is silent. `WordsIndexTests` merges a key from two files per
language and form, the later file winning and the first file kept.

## PTL001: `[Localized]`

`[Localized]` marks a parameter, property, field or return value as wanting localized
text. PTL001 ("Expecting localized value", category `PatTech.Localization`, a warning, on
by default) is reported at each expression handed to a marked parameter, property or
field that is not localized itself.

**Where it looks.** The arguments of an invocation that bind to a marked parameter:
positional, named, and each argument a `params` parameter takes. An `out` parameter is
passed by, since it receives rather than gives. Simple and `+=` assignments to a marked
property or field, and, inside a method, to its own marked `ref` or `out` parameter. The
message names the target: ``Parameter `message` in method `WriteLocal` expects a localized
value``, ``Property `ViewModel.Message` expects a localized value``, or the same for a
field. A constructor's, an indexer's or an attribute's arguments are not looked at, and
neither is what a `[return: Localized]` method returns. Generated code is skipped.

**What counts as localized.** An expression that reads a marked member — a name, a member
access, or an indexer marked as Core's are — or calls a method marked
`[return: Localized]`, an extension method by its definition; looked at through
parentheses, `await` and `?.`. A `?:` is localized when both its arms are, and a switch
expression when every arm is; at the top of the expression each offending arm is
reported on its own, so the one bad branch lights up and not the statement. A local
carries no attribute, so it is traced: its initializer and every simple assignment to it
in the block that declares it must all be localized, and a local with none of them in
sight is not. Anything else is not localized: a literal, an interpolation, a
concatenation, an unmarked member, a name that does not resolve.

**Why.** Localized text and a raw string are both `string`, so the type system cannot keep
them apart; the attribute says which seam wants which. The tracing exists so that
`var s = Words.Known["key"]; Show(s);` is not a false positive. It is conservative: one
raw assignment anywhere in the block makes the local suspect, wherever it sits.

**Tests.** `LocalizationAnalyzerUnitTest` passes an empty source; warns at a literal handed
to a marked parameter, naming the parameter and the method, and at a literal assigned to
a marked property and to a marked field; passes a `[return: Localized]` call, a marked
property, and a marked field assigned to another; and flags only the literal arm of a
`?:`. `LocalVariableTrackingTests` passes a local holding a localized value, and one
reassigned to another, and flags a local holding an unmarked value or reassigned to one.
Each test compiles its own copy of the attribute.

## PTL002: `[WordsKey]`

`[WordsKey]` marks a parameter, property or field as taking a words key: the input side,
where `[Localized]` is the output. PTL002 ("Unknown words key", the same category, a
warning, on by default) checks the key a marked target is handed against the keys the
files declare (*Where the words come from*).

**The targets.** A parameter, property or field marked itself, or a parameter or property
marked through a member it overrides or implements, at any remove: a class's indexer that
implements `IWords`' is marked as that one is, without repeating the attribute. A generic
method called as `Show<int>` implements as its definition, `Show<T>`. A public member
implements an interface's for the type that declares it, and for the type the call is
made on, which may implement the interface through a member it inherits: with
`Derived : Base, IKeyed`, `Base.Show` is marked when called on a `Derived` and not when
called on a `Base`. A field is marked by its own attribute only.

**The call shapes.** The arguments of an invocation, of `new T(…)` with an argument list,
of an indexer (`words["key"]` and `words?["key"]`) and of an attribute's constructor,
each bound to its parameter by name, by position, or, from a `params` parameter's
position on, to that parameter; an attribute's `Name = "key"`, as an assignment to that
property or field; and a simple assignment to a marked property or field, in a statement
or an object initializer. The count indexer, `this[key, count]`, checks its key and not
its count.

**A constant key.** A compile-time constant string — a literal, a `const`, constants
concatenated and folded — is checked exactly, and reported on the whole expression with
one of two messages:

- `'material.metals#other' is not a words key name: keys are dotted segments of letters,
  digits, '_' and '-', and a plural form is picked by a count, never named` when it fails
  the key-name grammar, as a `#`, a space, a doubled or leading dot does;
- `'nope.not.here' is not a known words key` when it is a name no file declares.

The first is wrong in every file, and no key added fixes it; the second is a typo or a
key not written yet. The fixes differ, so the messages do. `""` names nothing on purpose,
as an inline or a markup extension takes it to mean no key, and is never reported.

**Why `"key#form"` is no key.** Code names keys language-neutrally. Which plural forms a
key has is each language's business — English writes `#other`, Maltese `#two`, `#few`,
`#many` and `#other` — and a count picks among them: `Words.Known[key, n]`, or a `{0#key}`
selector (the runtime's *Plural forms*). A form named in code would read that form in
every language, which is English's rule hard-coded again, the very thing forms took out
of the code. So a `#` is reported as a name a key can never have, even where the file
holds the key and that form, and the message says why.

**A built key.** A string built at run time cannot be checked, but its fixed start can:
the constant left side of a concatenation, folded on into the right while the left stays
constant, or the text before an interpolation's first hole. If no key name could begin
so (`"material.metals#" + form`, `$"material..{form}"`) it is reported as no key name; if
no declared key begins so (`"bogus." + x`), as ``No words key starts with 'bogus.'``. A
fixed start some key shares is never reported, and a value with none
(`SomeRuntime() + ".suffix"`, a call) is left alone.

**What isn't a key.** A marked `object` parameter takes a key or something else:
`WordsExtension(object)` a key or a binding, `ConvertValue` whatever a converter was
handed. Only a string is checked there. A binding, a variable, `null` or `42` is silent,
while a literal, or a built string's fixed start, is checked as it is anywhere.

**Where the compiler cannot look.** `{l:Words key}` in XAML is compiled by the framework's
XAML compiler, which runs no analyzer, so PTL002 never sees it. In Rider the plugin's
squiggle covers it (*Keys in XAML*).

**Tests.** `WordsKeyAnalyzerTests` runs against an API of its own — a key-taking method,
constructor and property, indexers shaped like `IWords`', an interface with an
implementation that does not repeat the mark, an attribute — and one `helios-words.ini`.
Declared keys pass, `[.metals]` under `[material]` included, and an unmarked parameter
takes anything; an unknown key is reported on a method, a property and a constructor; a
plural form, a space, and the fixed starts `"material.metals#"` and `"material.."` are no
key names, while `"material." + form` passes; a call's result is ignored; a concatenation
or interpolation with a known start passes, one with an unknown start is reported, a
wholly dynamic one is ignored, and two constants fold into one exact key; an indexer's
key is checked plain, through `?[…]` and named, and an `int` indexer's is not; the count
indexer checks the key; an implementing, a generic and an inherited member are marked as
the interface member, the inherited one only on the type that implements; `""` is never
reported; an `object` key checks only strings; an attribute's constructor and named
arguments are checked.

## The keys Words marks

Core, WPF and Avalonia carry `[WordsKey]` on every member that takes a key, so once an
app hands its words to the build, the analyzer checks every key it names through them:

- Core: `IWords`' indexers, the key alone and the key with a count, and `CulturedWords`'
  and `EchoWords`' indexers; `CulturedWords.GetValue`; both `RenderKey`s; `Format`,
  `FormatByName`, `FormatParams`, `FormatKnown` and `FormatKnownByName`; `ConvertValue`'s
  `object` parameter; `LazyWords`' constructor, `LazyWords.Of` and `LazyWords.Key`;
  `WordsAttribute`'s constructor and its `Key`; `ConsoleWords.WriteWords` and
  `WriteWordsLine`.
- WPF and Avalonia: `WordsExtension`'s `object` constructor and its `Key`;
  `WordsInline.Key`; `WordsConverter.Format`.

Some members take a string near a key and are deliberately left unmarked:

- `RenderText`'s `baseKey` is a context, not a key: it resolves the text's relative
  references, `{>.sub}` reading `baseKey.sub`, and need hold no value of its own.
- `EchoWords.GetValue` echoes whatever it is given as `#key#`, declared or not. Its
  indexer stays marked, so echoing an undeclared literal through it warns; that was
  accepted.
- `TryGetValue` and `ContainsKey`, on `IWords`, `CulturedWords` and `EchoWords`, and the
  `TryGetValue` extension ask whether a key is there, so a key that is not is an answer.
- `Words.FormKey` names the entry a count reads, for a tool that holds keys read from
  the files.

**Tests.** Nothing pins the marks themselves; the analyzer's tests mirror their shapes.
LocalizedSample builds with one PTL001 and one PTL002 on purpose, against its
`localized-words.ini`.

## Release tracking and the package

**The rules' record.** `AnalyzerReleases.Shipped.md` lists PTL001 under Release 1.0 and
PTL002 under Release 1.4.0; `AnalyzerReleases.Unshipped.md` is empty. A new rule goes
into Unshipped and moves under a release's heading when that release is cut. The files
are a record kept by hand: the analyzer project does not hand them to its build as
AdditionalFiles, so the release-tracking analyzers do not check them. 1.3.0, the
package's last published version and the one its consumers pin today, carried an earlier
PTL002 listed as unshipped; 1.4.0 ships it, now that Core, WPF and Avalonia mark their
keys.

**The package.** `LocalizationAnalyzer.Package` packs `PatTech.Localization.Analyzer` at
`AnalyzerVersion` (`Versions.props`):

- `analyzers/dotnet/cs/LocalizationAnalyzer.dll`, both analyzers, for C# only;
- `lib/netstandard2.0/PatTech.Localization.dll`, compiled from `LocalizedAttribute.cs`
  and `WordsKeyAttribute.cs`: the attributes come as an assembly, not as source;
- the package readme, and the `install.ps1` and `uninstall.ps1` that add the analyzer
  to a `packages.config` project.

It declares no dependencies and is no development dependency. Core references it with
`PrivateAssets="none"`, so every app on Words gets both rules and both attributes without
asking, and a project that does not use Words can reference it alone.

**The hand-pinned consumers.** Core (and WPF and Avalonia through it), Wordsmith,
LocalizedSample, Sample-Wpf and Sample-Ava restore the package from nuget.org at a
version written into each project by hand, not at `AnalyzerVersion`, so `AnalyzerVersion`
can move ahead of nuget.org. A release that changes the analyzer goes in this order
(`.github/workflows/build.yml`, its header):

1. Bump `AnalyzerVersion`, commit, and tag `analyzer/X.Y.Z` on that commit. The workflow
   fails a tag whose number `Versions.props` does not carry, runs the test suites, then
   packs and pushes the analyzer alone to nuget.org, and releases the Rider plugin beside
   it (*Shipping the plugin*).
2. Once X.Y.Z is on nuget.org, bump the five pins to it in a commit of their own. Pushed
   before then, that commit breaks every restore.
3. Tag `api/` on that commit, so Core and its siblings depend on the new analyzer.

**The VSIX.** `LocalizationAnalyzer.Vsix` is a debug harness, not a product. As the startup
project in Visual Studio, F5 opens an experimental instance (`/rootsuffix Roslyn`) with
the analyzer loaded, and breakpoints in it bind. It deploys only when built inside Visual
Studio, so `dotnet build` passes it by, and it ships nowhere.

**Tests.** `tests.yml` runs the analyzer's suite, and every publish waits on it.

## The Rider plugin

WordsXaml is a backend-only Rider plugin: a Gradle project (the IntelliJ Platform Gradle
Plugin) around a ReSharper-backend assembly, with no Kotlin frontend and no rdgen
protocol. `plugin.xml` declares the id `ai.evosonic.wordsxaml` and the name "Words XAML",
and depends on `com.intellij.modules.rider` alone; Rider's backend loads `WordsXaml.dll`
and `WordsXaml.Core.dll` from the plugin's `dotnet/` folder.

**Two assemblies.** `WordsXaml.Core`, on netstandard2.0, holds everything that can be
tested without the SDK: the parser, the index, the tooltip's body, and where a key sits
in markup or in a literal. `WordsXaml`, on net8.0, is thin glue over it, compiled against
`JetBrains.ReSharper.SDK` 2025.3.4.1.

**Requirements.** Rider 2025.3 or later: `sinceBuild` is 253 and there is no
`untilBuild`, so a newer Rider installs the plugin without a rebuild. The backend still
binds to one SDK's API; if a Rider update breaks it, the fix is to bump the SDK and
`riderSdkVersion` together, not to cap the build. `runIde` launches a sandbox Rider of
`riderSdkVersion`, 2025.3.4, which must match the SDK the backend compiles against.

**Why Rider, and not Roslyn.** Roslyn sees C# only and never runs inside a XAML file.
XAML editing in Rider is the ReSharper engine's, so completion, tooltips and squiggles in
markup can only come from a plugin to that engine. Once there, it serves C# too.

**Checked by hand.** A build proves the wiring compiles, not that a list pops up or a
tooltip shows. The 1.4.0 release, first built as 0.2.0, was checked by hand in a live
Rider on 2026-10-09; a change to the glue is checked the same way, in `runIde`'s sandbox.

## Keys in XAML

**Where a key is.** `MarkupKeys` reads the key from the extension's text, so the three
spellings agree however the XAML PSI shapes them:

```
{l:Words main.title}         positional: the constructor's [WordsKey] object key
{l:Words Key=main.title}     named: the [WordsKey] Key property
{l:Words 'main.title'}       quoted, in either quote
```

The prefix is whatever the file maps the namespace to, or none, and `WordsExtension` is
the same extension. A named `Key=` wins over a positional argument, as the property is
set after the constructor runs. A nested extension is no key: `{l:Words {Binding Name}}`
binds its key at run time. `{l:Words}` names none, and `{l:Wordsmith x}` is no Words
extension. Where a key is due but not typed — after `{l:Words `, after `Key=`, between
`''` — it is the empty key, at that place, and an unterminated extension, as it is while
being typed, reads to the end of the text. Completion and the tooltip find the extension
around the caret in the document's text, looking back through the last few `{` and never
past a `<`, so a half-typed `{l:Words ma` need not parse; the squiggle reads each markup
node's text.

**Completion.** One level of the dotted tree at a time (`WordsIndex.CompleteSegments`). The
typed key up to its last `.` is the committed prefix (`CommittedPrefix`), matched case
aside, and the list is the next level under it: each branch once, ending in `.` and
showing how many keys it holds (`3 keys`), and each leaf key with its preview. Items are
sorted ordinally and inserted in the key's own casing. Accepting a branch opens the next
level, and Rider's matcher filters a level by what is typed after the last dot. The typed
prefix runs from the key's start to the caret, and an item replaces the whole key, so a
quote stays where it is.

**The tooltip.** Hover or Ctrl+Q on a known key shows (`WordsQuickDoc.Html`):

- the key, in bold;
- its preview: the default value, or else the first language's value, unescaped and
  joined as the runtime reads it, collapsed to one line, trimmed and cut at 100
  characters with an ellipsis; `{>key}` references and `[icon:x]` tokens show as written;
- the plural forms it has in any language, in CLDR's order (`forms: #few #other`);
- the name of the file that declares it.

An unknown or empty key gets nothing from the plugin. The provider runs at priority -10,
ahead of the declared-element providers, and claims only a caret on a key.

**The squiggle.** A key no file declares is marked `Unknown words key 'x'`: a warning by
default, its severity configurable under Code Notification (`WordsXaml.UnknownKey`). It
is silent on the empty key, and while the solution holds no keys at all, so a project
that keeps its words elsewhere is not covered in squiggles. It does not tell a malformed
name from a missing one.

**Why.** An unknown key renders `#key#` at run time, and nothing in the build says so for
markup (*Where the compiler cannot look*). The squiggle turns that silent miss into a
mark at the line, and completion keeps most of them from being typed.

## Keys in C#

**Where a key is.** A string literal bound to a `[WordsKey]` target, as PTL002 finds one:
an argument of a method, constructor, indexer or attribute, an attribute's `Key = "…"`,
an object initializer's member, or an assignment to a marked property or field. A
parameter or property is marked through any member it overrides or implements, at any
remove; a field by its own mark only. `LiteralKeys` takes the key from between the
quotes of a regular, verbatim (`@"…"`) or one-line raw (`"""…"""`) literal; an
interpolated, a UTF-8 (`u8`) or a multi-line raw literal holds no key, and an
unterminated one, as it is while being typed, runs to the end of its token. The caret
must be inside the quotes.

**What it offers.** The same completion and tooltip as in XAML. Rider does not pop
completion up inside a string of its own accord; Ctrl+Space asks for it.

**No squiggle.** PTL002 already reports an unknown key in C#, in every IDE and every
build of a project that hands its words to the compiler, and a second mark would only
double it.

**Tests.** `KeySiteTests` finds the key in each XAML spelling — positional, `Key=` spaced
or quoted, either quote, `WordsExtension`, no prefix, spaces around — and none in a
nested binding, `{l:Words}`, `{Binding …}`, `{l:Wordsmith …}` or plain text; a named key
over a positional one; the empty key where one is due; the key under a caret in a
half-typed or quoted extension, and none with the caret just past it, on the name, in a
binding or in another attribute; and a literal's content as its key in a regular,
verbatim, raw, unterminated or empty literal, and none in an interpolated, UTF-8 or char
literal. `WordsIndexTests` covers the committed prefix, one level of branches with
their counts, drilling in, leaves with previews, the key's own casing, the preview's
truncation, its verbatim tokens and its unescaping, an unknown key, a key with forms
alone, the tooltip's HTML, and the fuzzy `Match`, which completion does not use. The SDK
glue has no automated tests.

## The plugin's reader

`WordsIniParser` copies `WordsParser`'s patterns as `WordsIniKeys` does, so everything in
*Read as the runtime reads* holds for it: headers, `[.child]`, constants, `[]`,
continuations, comments. It keeps values as `WordsParserToWordsProvider` does, and
follows the runtime there too, where that surprises:

- A repeated `value=` for the same key and language overwrites the earlier one, as the
  runtime does (it warns `WB:KOVR`), and a continuation follows the line that won. 0.1.0
  concatenated them.
- A trailing `\` joins with a newline and a trailing `_` with nothing; `\\`, `__` and
  `''` are one character each and never continue. Since the test pairs each `\` or `_`
  with the character after it, `a_\` does not continue: `_\` is an escaped pair. An odd
  run, `\\\`, still does.
- A header or a field starts at the start of its line, so an indented one is neither. A
  header may have text, a comment say, after its `]`.
- A field separates with `=` or `:`, spaces allowed before it; the spaces after it are
  dropped and trailing ones kept.
- Only `value` fields are kept, under that exact name: `Value=` is no value field, and
  the labels before the first block, `comment=`, `context=` and `param-x=` are read past.
- A field whose language is no language code — `value-english=`, `value-en_GB=`, four
  parts — is read past, continuation and all. A code is cased by kind: `sr-latn-rs`
  reads `sr-Latn-RS`.
- A form, `value#other=` or `value-ru#few=`, is lowercased and kept as a form of the key,
  never as a language. `#one` (the plain value is that form) and a category CLDR does not
  have are read past with their continuations.
- A block named twice in a file is one key, its fields merged, at its first line.
- A `#` line is no comment to the runtime, and no header or field either, so it is
  skipped all the same. A continued line is text, never a header, a field or a comment.

**Tests.** `WordsIniParserTests` reads section keys, default and language values, `\` and
`_` continuations, and dot-sections hanging off the last full header, siblings included;
`[]` pours nothing into the key above; header lines are numbered from 1; a repeated value
overwrites, its continuation following; a header whose name is none is skipped with its
fields and children, a constant has no children, and a dot-header before any full header
is no key; headers are read at the line start, untrimmed, with a comment after one; a
block named twice is one key; continued lines are never headers, fields or comments;
doubled escapes unescape and do not continue, `a_\` included, and an odd run continues;
`;` lines, indented too, are comments and a `#` line is skipped; separators, spacing and
`Value=`; forms are cased, continued, and refused when `#one` or no CLDR category; codes
of three parts are cased by kind, and a field whose language is none is read past with
its continuation; and `IsKeyName` takes and refuses what the runtime's grammar does.

## Shipping the plugin

**Its number is the analyzer's.** The plugin ships on the analyzer's track: its version is
`AnalyzerVersion`, which `build.gradle.kts` reads from `Versions.props`, and `plugin.xml`
gets it at build time. One number for the two means "the plugin of analyzer 1.4.0" needs no
table, and the tag's version check covers both. A release that changes only the plugin
still bumps `AnalyzerVersion` and republishes the analyzer, unchanged, at the new number.

**The build.** `./gradlew buildPlugin -PbuildConfiguration=Release` builds the backend with
`dotnet build` and packs it into `build/distributions/WordsXaml-<version>.zip`: the two
assemblies and their `.pdb` files under `dotnet/`, and a stub jar carrying `plugin.xml`.
Without the property it builds Debug, `gradle.properties`' default for `runIde`, so a
debugger binds. Searchable options are not built: the plugin has no settings pages, and
building them launches a headless Rider.

**The release.** An `analyzer/X.Y.Z` tag runs `build.yml`'s `plugin` job once the test
suites pass. It builds the zip as above and attaches `WordsXaml-X.Y.Z.zip` to a GitHub
Release for the tag, titled "Analyzer X.Y.Z", its notes saying what the plugin does and how
to install it, above GitHub's list of the pull requests since the previous `analyzer/`
tag. The release is not marked Latest; that is Wordsmith's. A zip of any other number is
refused, and a re-run uploads over what an interrupted one left.

**Installing.** Rider 2025.3 or later installs the zip from **Settings → Plugins → ⚙ →
Install Plugin from Disk…**. It is not on the JetBrains Marketplace.

**Tests.** `WordsXaml.Tests`, the core's xUnit suite, runs in `tests.yml` beside the
analyzer's, and so gates the plugin's release as it does every publish. It is in `Words.slnx` with the core; the plugin assembly is not, since it
pulls in the SDK, and builds through `WordsXaml.slnx` or Gradle.

---

# Planned upgrades

Not built. Each section here is the shape the change takes when it is.

## References checked inside the ini files

The ini files name keys too: a value's `{>key}` reference renders `#key#` at run time when
no file declares the key, and nothing checks it before then. The analyzer already holds
the files, so the check belongs there: each `{>key}`, resolved as the runtime resolves it
(`{>.sub}` against its own block), checked against the declared keys with PTL002's
grammar and reported at its line in the file. It waited for this spec, so that the
reading it checks against is written down first.

## PTL002 by operations

PTL002 walks syntax, and misses four shapes that predate 1.4.0:

- a target-typed `new("x")`, which is no `ObjectCreationExpression`;
- an omitted argument whose default is a key;
- an explicit `params` array, or a collection expression, whose elements are never
  looked at;
- a marked field's or property's initializer, `[WordsKey] string Key = "x";`, which is
  no assignment.

Roslyn's operation tree sees all four: an argument operation for a default and for a
`params` array, and initializers as operations of their own. Walking operations instead
of syntax covers them in one pass.

## Faster prefix and marker lookups

The prefix check scans every key for each built key, and the marker lookup walks the
override and interface chain afresh at every site: about 8 s for 1,000 checks of absent
prefixes against 100,000 keys. Keys sorted once per compilation make a prefix check a
binary search, and each symbol's answer cached makes the marker lookup once per symbol.

## PTL001 where it does not look yet

PTL001 checks the arguments of an invocation and assignments, and nothing else; its
documentation says so. These would bring it level with PTL002's reach: a constructor's, an indexer's and an attribute's arguments bound to a
marked parameter, and what a method marked `[return: Localized]` returns, each `return`
statement and expression body checked as an assignment to that return value is.

## RenderText's base key as a prefix

`RenderText`'s `baseKey` is no key, so it stays unmarked. Its relative references read
`baseKey.sub`, though, so a base that no key starts with is as likely a typo as a built
key's wrong start. Checked as a built key's fixed start is, and never exactly, a wrong
base is caught while a base with no value of its own stays quiet.

## The plugin's index

- An empty block, or one with plural forms alone, counts as a known key, since the index
  makes an entry at the header. The runtime holds no plain value for either, so its
  indexer renders `#key#`; a forms-only key still answers a count. The analyzer counts
  headers the same way.
- "Later wins" follows the order the files are enumerated in, not the order the app
  loads them, which the index cannot know.
- Every file is reparsed on every change. An `ICache` over the `*words.ini` source files
  would reparse only the one that changed and keep its results across sessions; the
  parser and the index stay as they are, and only `WordsIndexService` changes.
- Each entry records its header's file and line for go to definition, and nothing uses
  them yet.
- A headless test of the SDK glue (`JetBrains.ReSharper.TestFramework`) would check
  completion, the tooltip and the squiggle the way they are now checked by hand.

## Unicode key names

The grammar both copy is the runtime's `\w`, which misses spacing combining marks,
supplementary-plane letters and ZWNJ, so `[हिंदी]` is skipped today. The proposal, its
decision pending, is a UAX #31 segment (XID_Start, then XID_Continue) plus `-`, joined by
`.`, NFC-normalised and matched by code points. The runtime's parser, the analyzer's
`WordsIniKeys` (its key-name and key-start patterns both), the plugin's `WordsIniParser`
and Wordsmith change together, in step as they are now.
