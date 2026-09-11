using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     WPF culture helpers for the Words runtime.
	/// </summary>
	public static class CultureExtensions {
		private static bool frameworkElementCultureApplied;

		/// <inheritdoc cref="Digest(WordsBuilder, string, out IEnumerable{KeyValuePair{string, string}}, bool)"/>
		public static IWords Digest(this WordsBuilder builder, string languageCode, bool includeFrameworkElements) {
			ArgumentNullException.ThrowIfNull(builder);
			var words = builder.Digest(languageCode);
			if (includeFrameworkElements) ApplyToFrameworkElements();
			return words;
		}

		/// <summary>
		///     The core <see cref="WordsBuilder.Digest(string, out IEnumerable{KeyValuePair{string, string}})"/>
		///     — build, install as <see cref="Words.Known"/>, sync the thread cultures — plus
		///     the one WPF step apps tend to leave out: when
		///     <paramref name="includeFrameworkElements"/> is true, the default
		///     <see cref="FrameworkElement.LanguageProperty"/> is pointed at the selected
		///     language too, so ordinary bindings (<c>StringFormat</c>, other converters)
		///     format in it instead of WPF's built-in <c>en-US</c>.
		/// </summary>
		/// <remarks>
		///     Words' own <see cref="WordsInline"/> and <see cref="WordsConverter"/> already
		///     format with <see cref="CultureInfo.CurrentCulture"/>, so they follow the
		///     language either way; the flag is for everything else bound in XAML. The
		///     override is process-global and one-shot — the first call sets it and later
		///     calls leave it — so apply it once at startup, in keeping with the rest of
		///     the library's contract. Leave it off for English words with system number
		///     and date formats, and set <see cref="CultureInfo.CurrentCulture"/> yourself.
		/// </remarks>
		/// <param name="builder">The builder with your words loaded.</param>
		/// <param name="languageCode">The language to select, e.g. <c>"en"</c> or <c>"en-GB"</c>.</param>
		/// <param name="languages">Return a list of available languages (see <see cref="WordsBuilder.GetLanguages"/>)</param>
		/// <param name="includeFrameworkElements">Also make WPF bindings default to this language.</param>
		/// <returns>The installed dictionary, the same object now in <see cref="Words.Known"/>.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
		public static IWords Digest(this WordsBuilder builder, string languageCode, out IEnumerable<KeyValuePair<string, string>> languages, bool includeFrameworkElements) {
			ArgumentNullException.ThrowIfNull(builder);
			var words = builder.Digest(languageCode, out languages);
			if (includeFrameworkElements) ApplyToFrameworkElements();
			return words;
		}

		/// <summary>
		///     Points WPF's default <c>FrameworkElement.Language</c> at the current culture,
		///     which the Digest just set. Once per process: OverrideMetadata cannot be
		///     called twice for the same type.
		/// </summary>
		private static void ApplyToFrameworkElements() {
			if (frameworkElementCultureApplied) return;
			FrameworkElement.LanguageProperty.OverrideMetadata(
				typeof(FrameworkElement),
				new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
			frameworkElementCultureApplied = true;
		}
	}
}
