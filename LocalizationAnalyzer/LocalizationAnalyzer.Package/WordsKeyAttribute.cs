using System;

namespace PatTech.Localization
{
	/// <summary>
	/// Declares that the marked parameter, property, field, or return value holds a
	/// <em>words key</em> (for example <c>"params.focal-law-base.capture-delay"</c>)
	/// used to look up localized text, rather than the localized text itself.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the input-side counterpart to <see cref="LocalizedAttribute"/>:
	/// <c>[Localized]</c> flags things that carry a translated string, whereas
	/// <c>[WordsKey]</c> flags things that take a key used to resolve one. Tooling
	/// enlists the target in analyzer rule <c>PTL002</c> ("Unknown words key"): a
	/// compile-time-constant string supplied to the target must be a key declared in a
	/// <c>*words.ini</c> made available to the compilation as an AdditionalFile, or the
	/// analyzer reports a warning. The Rider plugin uses the same marker to offer key
	/// completion and tooltips at these sites.
	/// </para>
	/// <example>
	/// <code>
	/// static void Show([WordsKey] string key) { ... }
	///
	/// Show("main.greeting"); // fine, if that key exists
	/// Show("main.greetng");  // warning PTL002 (typo)
	/// </code>
	/// </example>
	/// </remarks>
	[AttributeUsage(
		AttributeTargets.Parameter
		| AttributeTargets.ReturnValue
		| AttributeTargets.Property
		| AttributeTargets.Field,
		AllowMultiple = false)]
	public class WordsKeyAttribute : Attribute { }
}
