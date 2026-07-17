using System;

namespace PatTech.Localization {
	/// <summary>
	/// Provides a base key for multiple forms of display text for a given Enum.
	/// Use in conjunction with <see cref="Utils.Extensions.Describe"/>.
	/// <list type="bullet">
	/// <item>key = Primary display name</item>
	/// <item>key<i>.tooltip</i> = Popup help text</item>
	/// <item>key<i>.sub</i> = Short description</item>
	/// <item>key<i>.desc</i> = Long description</item>
	/// <item>key<i>.unit</i> = Suffix to be applied to another value</item>
	/// </list>
	/// </summary>
	/// <param name="key">The base key for the primary text.</param>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class WordsAttribute(string key) : Attribute {
		/// <summary>
		/// The base key for the primary text.
		/// </summary>
		public string Key { get; } = key;
	}
	
	/// <summary>
	/// Provides a subtitle text option for <see cref="Utils.Extensions.Describe"/>
	/// when paired with <see cref="System.ComponentModel.DescriptionAttribute"/>.
	/// Both attributes should be replaced with a single <see cref="WordsAttribute"/>.
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