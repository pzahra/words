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
	public class Wordsmith(IWordsProvider provider, CultureInfo setCulture) : IWords {
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

	/// <summary>
	/// Implement this when you take exception to your Words. A minimal logging seam
	/// so the library can object to missing keys, stale words and parse oddities
	/// without depending on a logging framework.
	/// </summary>
	public interface ITakeException {
		/// <summary>
		/// A logger that swallows everything. The default wherever a logger is optional.
		/// Note this is a mutable static field, so it can technically be replaced
		/// process-wide.
		/// </summary>
		public static ITakeException Dummy = new DummyLogger();

		/// <summary>
		/// A logger that forwards to wherever <see cref="Words.Logger"/> points at the
		/// moment of the call, so it stays current no matter how late the application
		/// assigns its real logger. The default for shared parsers that are constructed
		/// before startup wiring runs. (Assigning it to <see cref="Words.Logger"/> itself
		/// would be circular; it declines to echo into its own ear.)
		/// </summary>
		public static readonly ITakeException Global = new GlobalLogger();

		/// <summary>
		/// Reports a non-fatal condition, e.g. a missing key or an overwritten value.
		/// Messages use terse machine-greppable codes like <c>WORDS:KEY:`the.key`</c>.
		/// </summary>
		void Warn(string text);
		/// <summary>
		/// Reports an exception, usually just before it is thrown.
		/// </summary>
		void Error(Exception exception, string message);

		private class DummyLogger : ITakeException {
			public void Error(Exception exception, string message) { }
			public void Warn(string text) { }
		}

		private class GlobalLogger : ITakeException {
			public void Error(Exception exception, string message) {
				var current = Words.Logger;
				if (!ReferenceEquals(current, this)) current.Error(exception, message);
			}
			public void Warn(string text) {
				var current = Words.Logger;
				if (!ReferenceEquals(current, this)) current.Warn(text);
			}
		}
	}
}
