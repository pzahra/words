using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PatTech.Localization {
	/// <summary>
	/// This interface provides words.
	/// </summary>
	public interface IWordsProvider {
		/// <summary>
		/// Retrieve words from the dictionary if they exist.
		/// </summary>
		/// <exception cref="KeyNotFoundException"/>
		/// <param name="key">The key to lookup.</param>
		/// <returns>The localized string.</returns>
		string this[string key] { get; }

		/// <summary>
		/// Checks if a given key exists in the dictionary.
		/// </summary>
		/// <param name="key">The key to lookup.</param>
		/// <returns><see langword="true"/> if the key is present; otherwise <see langword="false"/>.</returns>
		bool ContainsKey(string key);

		/// <summary>
		/// Retrieve words from the dictionary if they exist.
		/// </summary>
		/// <param name="key">The key to lookup.</param>
		/// <param name="value">Output the localized value, or <see langword="null"/> if not found.</param>
		/// <returns><see langword="true"/> if found, otherwise <see langword="false"/>.</returns>
		bool TryGetValue(string key, [MaybeNullWhen(false), Localized] out string value);
	}

	/// <summary>
	/// Helpful extensions for <see cref="IWordsProvider"/>.
	/// </summary>
	public static class WordsProvider {
		/// <summary>
		/// An empty <see cref="IWordsProvider"/> for the purpose of non-null stubs.
		/// </summary>
		/// <returns>An empty dictionary.</returns>
		public static IWordsProvider Empty() => EmptyProvider.Instance;

		/// <summary>
		/// Retrieve words from the dictionary with silent failure.
		/// </summary>
		/// <param name="provider">The dictionary to read.</param>
		/// <param name="key">The key to lookup.</param>
		/// <returns>The localized string, or <see langword="null"/> if not found.</returns>
		[return:Localized]
		public static string? GetValue(this IWordsProvider provider, string key) {
			if (provider.TryGetValue(key, out var value)) {
				return value;
			}
			else {
				return null;
			}
		}

		class EmptyProvider : IWordsProvider {
			public static EmptyProvider Instance { get; } = new EmptyProvider();

			public string this[string key] => throw new KeyNotFoundException();

			public bool ContainsKey(string key) => false;
			public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
				value = null;
				return false;
			}
		}
	}
	/// <summary>
	/// A mutable <see cref="IWordsProvider"/> that is simply a
	/// <see cref="Dictionary{TKey, TValue}"/> of key to text. Used while loading,
	/// where entries are still being added and overwritten.
	/// </summary>
	public class DictionaryWordsProvider : Dictionary<string, string>, IWordsProvider, ICloneable {
		/// <summary>
		/// Returns a new provider containing a shallow copy of every entry.
		/// </summary>
		public DictionaryWordsProvider Clone() {
			var clone = new DictionaryWordsProvider();
			clone.CopyFrom(this);
			return clone;
		}

		/// <summary>
		/// Copies every entry from <paramref name="other"/> into this provider,
		/// overwriting existing keys; entries only in this provider are kept.
		/// </summary>
		public void CopyFrom(DictionaryWordsProvider other) {
			foreach (var (key, value) in other) {
				this[key] = value;
			}
		}

		object ICloneable.Clone() => Clone();
	}
	/// <summary>
	/// An <see cref="IWordsProvider"/> wrapping a dictionary that is no longer meant
	/// to change — the shape <see cref="WordsBuilder.Flatten(string, bool)"/> hands out.
	/// The wrapper adds no copying: mutate the backing dictionary and the provider
	/// sees it.
	/// </summary>
	public class ReadOnlyWordsProvider : IWordsProvider {
		/// <inheritdoc/>
		public string this[string key] => _backing[key];

		private readonly IReadOnlyDictionary<string, string> _backing;

		/// <summary>
		/// Wraps <paramref name="backing"/> without copying it.
		/// </summary>
		/// <param name="backing">The dictionary to expose.</param>
		/// <exception cref="ArgumentNullException"><paramref name="backing"/> is <see langword="null"/>.</exception>
		public ReadOnlyWordsProvider(IReadOnlyDictionary<string, string> backing) {
			ArgumentNullException.ThrowIfNull(backing);

			_backing = backing;
		}

		/// <inheritdoc/>
		public bool ContainsKey(string key) {
			return _backing.ContainsKey(key);
		}
		/// <inheritdoc/>
		public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			return _backing.TryGetValue(key, out value);
		}
	}
}
