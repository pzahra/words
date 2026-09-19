# Words

This is Words. It gives you Words.

Write your strings in a `words.ini`, load them at startup, and ask for them by
key. Words handles the languages, the fallbacks, the parameters, and the
references, so your code never has to know what "hello" is in `en-GB`.

## Make Words

Place a `words.ini` in the project assets or resources. It can be loaded from a
local file, or as an embedded resource.

``` INI
value-en=!English (Common)
comment-en=exclamation mark means it isn't displayed
value-en-GB=English (Traditional)
comment-en-GB=you can use this header to create a list of available display languages.

[group.key]
value=Default fallback value
value-en=Language Family version
value-en-GB=Region version
context=notes the programmer left about this value
context-en: equals or colon works, either way is fine
comment=notes the translators left about this value
comment-en=notes the translators left about this version
stale=programmer's attention is required
stale-en=this value is out of date and needs re-translating

[$rsi-unit]
comment=Regular keys can be referenced with {>view.section.key} but_
 cannot contain numbered parameters. This is a constant, which can_
 be referenced like {$this} and cannot contain sub-keys.
value=m2·K/W

[main.circle-1]
value=circular referencing {>main.circle-2}
comment=The engine actually prevents circular references from starting a runaway loop.

[main.circle-2]
value=works because {>main.circle-1}

[main.multiline]
comment=The words.ini format supports multiple lines.\
Use a backslash to break the line, and an underscore to_
 continue the string on the same line without a line break.\
Repeating a field is not a continuation: the last one wins (a repeated_
 value= also warns), the same way a file loaded on top overrides an earlier one.

value=first draft
value=the last value wins

value-en-CA=line 1\
line 2\
line 3\
line 4


[main.single-line]
value=line 1 _
still line 1

[format]
value={0:N4}
[.object]
value=ToString() -> {0}
comment: Key name inheritance. This actually reads as `format.object`
[.named]
value=N{Top:g2}, E{Right:g2}, S{Bottom:g2}, W{Left:g2}
comment: unlike with String.Format, Words.FormatNamed can take an object_
 and read properties by name.

[enum.none]
value=No Selection

[enum.two]
value=Two Selection
[.tooltip]
value=With Tooltip
[.desc]
value=With Desc

[prefix-whitespace]
value=_
 prefix whitespace

[whitespace-only]
value=_
 

```

## Import Words

``` csharp
WordsBuilder.Create()
	// You can stack as many of these as you want,
	// each one adds or overwrites as they are read.
	.Load("path/to/assets/words.ini")
	// This is the selected language,
	// use a config file to choose,
	// as it doesn't change after startup.
	// Digest installs the result as Words.Known.
	.Digest("en");
```

The top-of-file `value-xx=` labels double as your language menu:
`WordsBuilder.GetLanguages()` returns the code/label pairs in file order,
skipping labels that are empty or start with `!`. There is also a
`Digest(lang, out languages)` overload that installs the words and hands you
the menu in one call — see Sample-Ava's language dropdown for the pattern,
relaunch and all. (`ToWords` is the same build without the install, for a
dictionary that is not the process-wide one; `.Debug()` before either brands
values that fell back to another language, to spot missing translations.)

Relaunch is the operative word. `Words.Known` is process-wide and nothing that
already read it — `LazyWords`, strings a view model composed and kept, XAML
that has loaded — is told when it changes. Pick the language at startup and
restart the process to change it; do not hot-swap the dictionary in a running
app and expect the screen to follow.

That is the contract the whole library assumes: the process-wide statics —
`Words.Known`, `Words.Logger`, `MarkdownParser.Default` and its scheme registry,
the one global hyperlink handler — are set up **once at startup** and read from a
**single UI thread** thereafter. `Words.Logger` is never null (assign
`ITakeException.Dummy` to silence it, not `null`). Configure everything before the
first lookup renders and you never touch the concurrency questions the statics
would otherwise raise.

Numbers and dates in substituted arguments format with the thread's
`CurrentCulture` — the same one whether they flow through `WordsInline` or
`Words.Format`. Selecting a language with `Digest` sets both the *UI* culture
(which picks the text) and the *formatting* culture to that language, so by
default your numbers match your words. Want English text but, say, system
decimal commas or system date formats? Chain `.UseSystemNumbers()` before
`Digest`: the words stay in the language (still the UI culture), while the
formatting culture stays the one the process started in — `Words.SystemCulture`,
captured before Words touches anything. `WordsConverter`, being a converter,
formats with the culture the binding hands it, like any other: Avalonia passes
`CurrentCulture` unless a `ConverterCulture` says otherwise, so it agrees with
the above; WPF passes the element's `Language`, `en-US` unless the WPF `Digest`
flag repoints it — see the WPF readme.

The `!` prefix is for multi-assembly setups: each assembly ships its own
`words.ini`, and a subordinate library may carry more languages than the
host app offers. Declaring those languages with a `!Name` label tells the
editor they are present on purpose — not an error — while keeping them out
of the app's menu. Only the languages the app actually offers get plain
labels, typically in the app's own file.

## Use Words

`Words.Known["key"]` returns the translated value of the specified key.

Use the attribute `[Localized]` to mark fields, properties, return values or
parameters that expect or provide localised strings. If there is a mismatch,
the compiler will produce a warning.

Use the attribute `[Words("key")]` to mark enum values. The `Enum.Describe`
function will assume the existence of "key.tooltip", "key.sub", "key.desc"
and "key.unit" as well as the exact name, to provide additional variations
of the text associated with an enum item.

Migrating an existing enum? `Describe` already understands
`[Description("...")]` and uses it as fallback display text. If your tooltips
or subtitles live in some custom attribute instead, move the text to
`[Tooltip("...")]`: `Describe` reads it for the tooltip and subtitle formats,
and its obsolete warning keeps reminding you that those words really belong
in a `words.ini` under a `[Words]` key.

Use the container `LazyWords` to preload a key for services that statically
initialise before the dictionary has been loaded. The words will resolve
once the Value is accessed the first time.

Use the formatter `Words.Known.Format` as you would `String.Format`, but
you can also use `Words.Known.FormatByName` to access properties as
named parameters.

An argument is substituted into the value and then rendered as markdown along
with it, so an argument can carry markdown of its own — a dynamic
`[link](appcmd:open?id=42)`, say. That is by design and deliberately not
escaped: treat format arguments as author-trusted, and don't build them from
untrusted input you wouldn't want rendered (and, for the terminal renderer,
raw control characters are stripped from every value whatever their source —
see [ConsoleWords](https://github.com/pzahra/words/blob/main/Localization-Core/ConsoleWords.cs)).

## Teach your agents

The package carries an agent skill — a `SKILL.md` that teaches coding agents
(Claude Code and friends) the `words.ini` format, the lookup API, the markdown
dialect, and the XAML integrations. Opt in from any project that references
Words (directly or transitively):

``` xml
<PropertyGroup>
	<WordsAgentSkill>true</WordsAgentSkill>
</PropertyGroup>
```

The next build copies it to `.claude/skills/pattech-words/SKILL.md`, where
agents discover it on their own. Point `WordsAgentSkillDir` somewhere else if
your agent reads skills from a different folder. Commit the file; it only
changes when the package does.

## Words on the console

Use the Words extension to put Words in the terminal:

``` csharp
Console.WriteWordsLine("main.title");
Console.WriteWordsLine("main.greeting", userName);
```

Markdown comes along for the ride: bold and italic become ANSI styling, links
become genuinely clickable OSC 8 hyperlinks (underlined and blue in the
traditional manner), `m^2^` becomes `m²`, and images bow out gracefully as
their alt text, marked `[🖼️!alt]`. When output is redirected to a pipe or
file, the escape codes stay home and you get plain text with links spelled
out as `text (url)`.

The `Console.WriteWords` extension needs .NET 10 (it hangs static members off
`Console` itself); on .NET 8, use `ConsoleMarkdownParser` directly:

``` csharp
var parser = new ConsoleMarkdownParser(useAnsi: !Console.IsOutputRedirected);
Console.WriteLine(parser.ToInline(Words.Known["main.title"]));
```

## The rest of the suite

- **[PatTech.Localization.WPF](https://www.nuget.org/packages/PatTech.Localization.WPF)** — Words in the XAML: the `{l:Words key}` markup extension, markdown inlines, converters and image schemes.
- **[PatTech.Localization.Avalonia](https://www.nuget.org/packages/PatTech.Localization.Avalonia)** — the same, for Avalonia's AXAML.
- **[PatTech.Localization.Analyzer](https://www.nuget.org/packages/PatTech.Localization.Analyzer)** — the `[Localized]` attribute and rule PTL001, which flags a localized seam handed a raw string. Core already depends on it, so you have it.
- **Wordsmith** — the desktop editor for `words.ini` files, published on [GitHub Releases](https://github.com/pzahra/words/releases).