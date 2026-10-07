using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PatTech.Localization {
	/// <summary>
	/// The standard <see cref="IWords"/> implementation: a flattened provider paired
	/// with the cultures it applies — the language's for the UI and, for formatting,
	/// either the same or (after <see cref="WordsBuilder.UseSystemNumbers"/>) the
	/// system's. Lookups render <c>{$constant}</c> and <c>{&gt;key}</c> references via
	/// <see cref="Words.RenderKey(IWordsProvider, string, object[])"/>, so missing keys
	/// come back as <c>#key#</c> rather than throwing.
	/// </summary>
	/// <param name="provider">The flattened words for the selected language, typically from <see cref="WordsBuilder.Flatten(string)"/>.</param>
	/// <param name="setCulture">The formatting culture applied by <see cref="SetCulture"/>.</param>
	/// <param name="setUICulture">The UI culture applied by <see cref="SetCulture"/>; <see langword="null"/> to use <paramref name="setCulture"/> for both.</param>
	public class CulturedWords(IWordsProvider provider, CultureInfo setCulture, CultureInfo? setUICulture = null) : IWords {
		/// <inheritdoc/>
		[Localized]
		public string this[string key] => GetValue(key);

		/// <inheritdoc/>
		[Localized]
		public string this[string key, decimal count] => Words.RenderCount(this, key, count);

		/// <inheritdoc/>
		public IWordsProvider Provider { get; } = provider;

		/// <summary>The selected language: the UI culture this dictionary was built with.</summary>
		public CultureInfo UICulture => setUICulture ?? setCulture;

		private readonly string? language;
		/// <summary>
		/// The language whose plural rules pick a count's form: the code the dictionary was
		/// built for, which <see cref="UICulture"/> cannot always carry — .NET turns codes it
		/// does not know, such as <c>ceb</c> or <c>iw</c>, into the invariant culture. Without
		/// one, <see cref="UICulture"/>'s name.
		/// </summary>
		public string Language { get => language ?? UICulture.Name; init => language = value; }

		/// <summary>
		/// Sets the current thread's culture and UI culture, and the process-wide
		/// defaults for future threads, to the cultures this dictionary was built with.
		/// </summary>
		public void SetCulture() {
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = setCulture;
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = setUICulture ?? setCulture;
		}

		/// <summary>
		/// Looks up and renders <paramref name="key"/>; a missing key renders as
		/// <c>#key#</c> and warns via <see cref="Words.Logger"/>.
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
		[return: Localized]
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
