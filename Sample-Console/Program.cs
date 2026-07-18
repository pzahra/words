using PatTech.Localization;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Words gripes about missing keys and broken markdown through Words.Logger.
// The console parsers behind Console.WriteWords were built long before this
// line runs, but they forward through ITakeException.Global, so they hear it.
Words.Logger = new ConsoleGripes();

Words.Known = WordsBuilder.Create()
	.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "words.ini"))
	.ToWords("en");

Console.WriteWordsLine("demo.title");
Console.WriteLine();
Console.WriteWordsLine("demo.styles");
Console.WriteWordsLine("demo.scripts");
Console.WriteWordsLine("demo.entities");
Console.WriteWordsLine("demo.links");
Console.WriteWordsLine("demo.image");
Console.WriteWordsLine("demo.reference");
Console.WriteWordsLine("demo.greeting", Environment.UserName);
Console.WriteLine();

// And this key does not exist, on purpose: it renders as #key# and gripes.
Console.WriteWordsLine("demo.missing-on-purpose");
Console.ReadKey(true);

/// <summary>Writes Words' complaints to stderr, so the demo shows them off.</summary>
class ConsoleGripes : ITakeException {
	public void Warn(string text)
		=> Console.Error.WriteLine($"  (gripe) {text}");
	public void Error(Exception exception, string message)
		=> Console.Error.WriteLine($"  (gripe) {message}: {exception.Message}");
}
