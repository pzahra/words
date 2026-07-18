# Words for Avalonia

Use the Words extension to put Words in the AXAML.

## Include Words

``` csharp
public override void Initialize() {
	Words.Known = Words.Builder()
		// Use as many of these as you need.
		.LoadResource("avares://My-Project/Assets/words.ini")
		// Select the language to use.
		.ToWords("en");
	AvaloniaXamlLoader.Load(this);
}
```

## Handle Hyperlinks

``` csharp
public override void OnFrameworkInitializationCompleted() {
	Hyperlink.RegisterGlobalNavigateHandler(uri => {
		if (uri.Scheme is "appcmd") {
			// Handle application command hyperlinks.
		}
		else {
			// Handle URL hyperlinks.
			Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
		}
	});

	// ...
```

## Use Words in AXAML

``` xml
	xmlns:l="pattech.words"
	Title="{l:Words main.title}">

	<TextBlock>
      <l:WordsInline Key="main.sample-markdown"/>
    </TextBlock>
```

## Put pictures in your Words

Markdown images work in any rendered value, with the URI scheme deciding where
the picture comes from:

``` ini
[main.save-hint]
value=Press ![save icon](staticres:SaveIconGeometry?height=16&foreground=DarkGreen) to save.
```

Out of the box the parser speaks `avares:` (embedded assets), `assets:` (files
under the application's `Assets` folder), and `staticres:` (application
resource by `x:Key`). Query options `width`, `height`, `background`, and
`foreground` apply whatever the scheme. Anything that fails to resolve renders
as the image's alt text, because a missing icon should never eat your sentence.

Teach it new schemes by registering an `IImageSchemeResolver` on the shared
parser at startup — say, Material Design icons:

``` csharp
class PackIconResolver : IImageSchemeResolver {
	public Control? Resolve(Uri source, ImageOptions options)
		=> Enum.TryParse<PackIconKind>(source.AbsolutePath.TrimStart('/'), out var kind)
			? new PackIcon { Kind = kind, Foreground = options.Foreground ?? Brushes.Black }
			: null;
}

// at startup:
MarkdownParser.Default.ImageSchemes["md"] = new PackIconResolver();
// and now `![save](md:ContentSave)` gives you Words with icons in them.
```
