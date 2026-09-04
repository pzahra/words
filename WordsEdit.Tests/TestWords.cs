using System.Runtime.CompilerServices;
using WordsEdit.Utils;

namespace WordsEdit.Tests;

/// <summary>
///     The editor speaks its own words in tests too: loaded once, in English,
///     before the first test, so every message a view model composes resolves.
/// </summary>
internal static class TestWords {
	[ModuleInitializer]
	internal static void Load() => EditorWords.Load(EditorWords.Fallback);
}
