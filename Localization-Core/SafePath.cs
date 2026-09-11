using System;
using System.IO;

namespace PatTech.Localization {
	/// <summary>
	/// Resolves a path under one folder and nowhere else — the discipline behind the
	/// <c>assets:</c> image scheme: the path is canonicalized and clamped, so <c>../</c>
	/// trickery, rooted paths and UNC shares resolve to nothing, and environment
	/// variables are never expanded (a <c>%</c> is just a filename character). A
	/// convenience, not a security boundary: a symlink planted inside the folder is
	/// out of scope.
	/// </summary>
	public static class SafePath {
		/// <summary>
		/// The absolute path of <paramref name="relative"/> under <paramref name="root"/>,
		/// or <see langword="null"/> if it would land anywhere else.
		/// </summary>
		/// <param name="root">The one folder paths are allowed to resolve under.</param>
		/// <param name="relative">The path asked for, <c>/</c> or <c>\</c> separated.</param>
		/// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="relative"/> is <see langword="null"/>.</exception>
		public static string? Under(string root, string relative) {
			ArgumentNullException.ThrowIfNull(root);
			ArgumentNullException.ThrowIfNull(relative);
			// one trailing separator, however the root was spelled, so the prefix test
			// can't match a sibling folder that merely starts with the root's name
			var fullRoot = Path.GetFullPath(root);
			if (!Path.EndsInDirectorySeparator(fullRoot)) fullRoot += Path.DirectorySeparatorChar;
			// GetFullPath resolves any ../ and ./ segments; a rooted or UNC path survives
			// Path.Combine untouched. Either way, anything outside the root is refused.
			var filePath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
			return filePath.StartsWith(fullRoot, StringComparison.Ordinal) ? filePath : null;
		}

		/// <summary>
		/// Same, for the path an image URI names (<see cref="ImageQuery.PathOf"/>), its
		/// percent-escapes decoded to the real file name.
		/// </summary>
		/// <param name="root">The one folder paths are allowed to resolve under.</param>
		/// <param name="source">The (query-less) image URI.</param>
		/// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="source"/> is <see langword="null"/>.</exception>
		public static string? Under(string root, Uri source) {
			ArgumentNullException.ThrowIfNull(source);
			return Under(root, Uri.UnescapeDataString(ImageQuery.PathOf(source)));
		}
	}
}
