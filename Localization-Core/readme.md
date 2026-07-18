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
 continue the string on the same line without a line break.
comment=Repeating the field also continues the line.

value=first part 
value=second part, 
value=all on one line

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
Words.Known = WordsBuilder.Create()
	// You can stack as many of these as you want,
	// each one adds or overwrites as they are read.
	.Load("path/to/assets/words.ini")
	// This is the selected language,
	// use a config file to choose,
	// as it doesn't change after startup.
	.ToWords("en");
```

## Use Words

`Words.Known["key"]` returns the translated value of the specified key.

Use the attribute `[Localized]` to mark fields, properties, return values or
parameters that expect or provide localised strings. If there is a mismatch,
the compiler will produce a warning.

Use the attribute `[Words("key")]` to mark enum values. The `Enum.Describe`
function will assume the existence of "key.tooltip", "key.sub", "key.desc"
and "key.unit" as well as the exact name, to provide additional variations
of the text associated with an enum item.

Use the container `LazyWords` to preload a key for services that statically
initialise before the dictionary has been loaded. The words will resolve
once the Value is accessed the first time.

Use the formatter `Words.Known.Format` as you would `String.Format`, but
you can also use `Words.Known.FormatByName` to access properties as
named parameters.