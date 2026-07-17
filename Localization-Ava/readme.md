# Words

## Include Words

``` csharp
public override void Initialize() {
	Words.Known = Words.Builder()
		// Use as many of these as you need.
		.LoadResource("avares://My-Project/Assets/words.ini")
		// Select langauge to use.
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
