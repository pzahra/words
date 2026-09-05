using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PatTech.Localization {
	/// <summary>
	/// The standard <see cref="IWords"/> implementation: a flattened provider paired
	/// with the culture of its language. Lookups render <c>{$constant}</c> and
	/// <c>{&gt;key}</c> references via
	/// <see cref="Words.RenderKey(IWordsProvider, string, object[])"/>, so missing keys
	/// come back as <c>#key#</c> rather than throwing.
	/// </summary>
	/// <param name="provider">The flattened words for the selected language, typically from <see cref="WordsBuilder.Flatten(string, bool)"/>.</param>
	/// <param name="setCulture">The culture applied by <see cref="SetCulture"/>.</param>
	public class CulturedWords(IWordsProvider provider, CultureInfo setCulture) : IWords {
		/// <inheritdoc/>
		public string this[string key] => GetValue(key);

		/// <inheritdoc/>
		public IWordsProvider Provider { get; } = provider;

		/// <summary>
		/// Sets the current thread's culture and UI culture, and the process-wide
		/// defaults for future threads, to the culture this dictionary was built with.
		/// </summary>
		public void SetCulture()
			=> CultureInfo.DefaultThreadCurrentCulture
			= CultureInfo.DefaultThreadCurrentUICulture
			= CultureInfo.CurrentCulture
			= CultureInfo.CurrentUICulture
			= setCulture;

		/// <summary>
		/// Looks up and renders <paramref name="key"/>; a missing key renders as
		/// <c>#key#</c> and warns via <see cref="Words.Logger"/>.
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
		public string GetValue(string key) {
			ArgumentNullException.ThrowIfNull(key);

			return Words.RenderKey(Provider, key);
		}
		/// <inheritdoc/>
		public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			if (Provider.ContainsKey(key)) {
				value = GetValue(key);
				return true;
			}
			else {
				value = null;
				return false;
			}
		}
		/// <inheritdoc/>
		public bool ContainsKey(string key) => Provider.ContainsKey(key);
	}
}
