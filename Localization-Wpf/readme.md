# Words for WPF

Use the Words extension to put Words in the XAML.

## Include Words

Load your Words once, before any window shows up:

``` csharp
public partial class App : Application {
	public App() {
		Words.Known = WordsBuilder.Create()
			// LoadResource reads straight out of your pack resources.
			.LoadResource("pack://application:,,,/My-Project;Component/Assets/words.ini")
			// Select the language to use.
			.ToWords("en");
	}
}
```

## Use Words in XAML

One namespace gives you everything:

``` xml
<Window xmlns:l="pattech.words"
        Title="{l:Words main.title}">

	<TextBlock>
		<l:WordsInline Key="main.sample-markdown"/>
	</TextBlock>
</Window>
```

- `{l:Words key}` — a markup extension that resolves to the localized string.
- `<l:WordsInline Key="key"/>` — an inline that renders the value, markdown
  and all, inside a `TextBlock`.

## Convert Words

For values that only exist at runtime, there are converters:

- `WordsConverter` — binds a key, gets Words out.
- `MarkdownConverter` — turns a markdown string into WPF inlines.
- `FlagsDescriptionConverter` — turns a `[Words]`-decorated enum (flags
  included) into its display text.
