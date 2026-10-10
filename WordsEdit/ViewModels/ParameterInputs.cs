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

	/// <summary>Whether any of <paramref name="key"/>'s definitions has an input with text: whether its previews fill in.</summary>
	public bool AnyTyped(WordsKey key)
		=> inputs.TryGetValue(key, out var typed) && key.Parameters.Any(parameter => typed.GetValueOrDefault(parameter.Key, "") != "");

	/// <summary>
	///     The values <paramref name="key"/>'s definitions' inputs stand for, each
	///     read as its definition's type, an <c>enum</c> described in
	///     <paramref name="words"/>.
	/// </summary>
	/// <exception cref="FormatException">An input is none of what its type reads.</exception>
	public Dictionary<string, object?> Read(WordsKey key, IWords words) {
		var typed = Of(key);
		return WordsOperations.ReadInputs(key.Parameters.Select(parameter => (parameter.Key, parameter.DataType, typed.GetValueOrDefault(parameter.Key, ""))), words);
	}

	/// <summary>Drops every key's inputs: Reset.</summary>
	public void Clear() => inputs.Clear();
}
