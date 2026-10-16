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

	/// <summary>
	///     Carries <paramref name="key"/>'s input from one name to another, as a rename
	///     and its undo carry it, unless the other name has its own: neither is dropped,
	///     so a definition renamed onto a parameter found with an input leaves each
	///     name its own, and its undo finds both where they were.
	/// </summary>
	public void Carry(WordsKey key, string from, string to) {
		Dictionary<string, string> typed = Of(key);
		if (from != to && !typed.ContainsKey(to) && typed.Remove(from, out string? input)) {
			typed[to] = input;
		}
	}

	/// <summary>Drops every key's inputs: Reset.</summary>
	public void Clear() => inputs.Clear();
}
