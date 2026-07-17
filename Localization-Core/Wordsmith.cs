using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PatTech.Localization {
	public class Wordsmith(IWordsProvider provider, CultureInfo setCulture) : IWords {
		public string this[string key] => GetValue(key);

		public IWordsProvider Provider { get; } = provider;

		public void SetCulture()
			=> CultureInfo.DefaultThreadCurrentCulture
			= CultureInfo.DefaultThreadCurrentUICulture
			= CultureInfo.CurrentCulture
			= CultureInfo.CurrentUICulture
			= setCulture;

		public string GetValue(string key) {
			ArgumentNullException.ThrowIfNull(key);

			return Words.RenderKey(Provider, key);
		}
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
		public bool ContainsKey(string key) => Provider.ContainsKey(key);
	}

	public interface ITakeException {
		public static ITakeException Dummy = new DummyLogger();
		
		void Warn(string text);
		void Error(Exception exception, string message);

		private class DummyLogger : ITakeException {
			public void Error(Exception exception, string message) { }
			public void Warn(string text) { }
		}
	}
}
