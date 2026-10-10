using System.Runtime.CompilerServices;

namespace WordsEdit.ViewModels;

/// <summary>
///     What the translator typed for each key's parameters, by name, kept for the
///     session (SPEC: Parameters → The inputs): no part of the document, so typing
///     one dirties nothing and enters no undo. A key let go of takes its inputs
///     with it; Reset drops them all.
/// </summary>
public sealed class ParameterInputs {
	private readonly ConditionalWeakTable<WordsKey, Dictionary<string, string>> inputs = new();

	/// <summary>The inputs typed for <paramref name="key"/>, by parameter name: the one store, written as typed.</summary>
	public Dictionary<string, string> Of(WordsKey key) => inputs.GetValue(key, _ => []);

	/// <summary>Drops every key's inputs: Reset.</summary>
	public void Clear() => inputs.Clear();
}
