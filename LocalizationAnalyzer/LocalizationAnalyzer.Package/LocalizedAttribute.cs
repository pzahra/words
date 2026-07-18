using System;

namespace PatTech.Localization
{
	/// <summary>
	/// Declares that the marked parameter, property, field, or return value expects
	/// localized text rather than a raw, hard-coded string.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Applying this attribute enlists the target in analyzer rule
	/// <c>PTL001</c> ("Expecting localized value"): any expression supplied to the
	/// target — a method argument, a property or field assignment, or a value
	/// returned from a method whose return is marked <c>[return: Localized]</c> —
	/// must itself be localized, or the analyzer reports a warning at the offending
	/// expression.
	/// </para>
	/// <para>
	/// An expression counts as localized when it reads from another
	/// <see cref="LocalizedAttribute"/>-marked member (including indexers such as a
	/// Words lookup) or calls a method whose return value is marked
	/// <c>[return: Localized]</c>. String literals, interpolated strings, and
	/// unmarked members all trigger the warning. The analyzer looks through
	/// parentheses, <c>await</c>, conditional (<c>?:</c>) expressions, and
	/// <c>switch</c> expressions, flagging only the branches that misbehave.
	/// </para>
	/// <example>
	/// <code>
	/// static void WriteLocal([Localized] string message) { ... }
	///
	/// WriteLocal(Words.Known["main.greeting"]); // fine
	/// WriteLocal("hello");                      // warning PTL001
	/// </code>
	/// </example>
	/// </remarks>
	[AttributeUsage(
		AttributeTargets.Parameter
		| AttributeTargets.ReturnValue
		| AttributeTargets.Property
		| AttributeTargets.Field,
		AllowMultiple = false)]
	public class LocalizedAttribute : Attribute { }
}
