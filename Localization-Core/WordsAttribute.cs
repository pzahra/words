using System;

namespace PatTech.Localization {
	/// <summary>
	/// Provides a base key for multiple forms of display text for a given Enum.
	/// Use in conjunction with <see cref="Utils.Extensions.Describe(Enum, string?, IWords?)"/>.
	/// <list type="bullet">
	/// <item>key = Primary display name (G, N)</item>
	/// <item>key<i>.tooltip</i> = Popup help text (T)</item>
	/// <item>key<i>.sub</i> = Short description (S)</item>
	/// <item>key<i>.desc</i> = Long description (d)</item>
	/// </list>
	/// An app keeps more beside each key with slots of its own (<see cref="Describable.Slot"/>),
	/// such as <c>.unit</c>, a suffix to another value.
	/// </summary>
	/// <param name="key">The base key for the primary text.</param>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class WordsAttribute([WordsKey] string key) : Attribute {
		/// <summary>
		/// The base key for the primary text.
		/// </summary>
		[WordsKey]
		public string Key { get; } = key;
	}
	
	/// <summary>
	/// Migration aid for <see cref="Utils.Extensions.Describe(Enum, string?, IWords?)"/>.
	/// Enums already using <see cref="System.ComponentModel.DescriptionAttribute"/>
	/// are picked up by Describe automatically, and a custom attribute an enum
	/// already carries is better registered (<see cref="Describable.Fill"/>), which
	/// leaves the enum as it is. Describe reads this one's text (formats "T" and
	/// "S") for the code that already uses it, and the obsolete warning keeps a
	/// reminder ticking until the text moves to a words.ini key and the attribute
	/// is replaced by a <see cref="WordsAttribute"/>.
	/// </summary>
	/// <param name="text">Default text to display in a popup, tooltip or subtitle.</param>
	[Obsolete("Use this for migration purposes only.")]
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class TooltipAttribute(string text) : Attribute {
		/// <summary>
		/// Text to display in a popup, tooltip or subtitle.
		/// </summary>
		public string Text { get; } = text;
	}
}