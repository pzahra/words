using System.Text.RegularExpressions;

namespace PatTech.Localization.Authoring {
	/// <summary>How a preview treats a hyperlink of some scheme.</summary>
	public enum LinkMode {
		/// <summary>Report the target; the host app's business is only shown.</summary>
		Popup,
		/// <summary>Confirm, then hand the target to the shell.</summary>
		ShellExec,
	}

	/// <summary>
	///     A <c>/pattern/options/replacement</c> rewrite: a regex replace over a
	///     whole URI. <c>options</c> are the usual letters (<c>i m s x</c>) or
	///     nothing; the pattern may not contain an unescaped <c>/</c>, the
	///     replacement may. A URI the pattern does not match is not rewritten.
	/// </summary>
	public sealed class DecodeRule {
		private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);
		private readonly Regex pattern;
		private readonly string replacement;

		/// <summary>The rule as written.</summary>
		public string Text { get; }

		private DecodeRule(string text, Regex pattern, string replacement) {
			Text = text;
			this.pattern = pattern;
			this.replacement = replacement;
		}

		/// <summary>Parses <paramref name="text"/>; a malformed rule comes back as <paramref name="error"/> instead.</summary>
		public static DecodeRule? TryParse(string text, out string? error) {
			error = null;
			if (text.Length < 3 || text[0] != '/') {
				error = $"decode rule must look like /pattern/options/replacement: {text}";
				return null;
			}
			int end = -1;
			for (int i = 1; i < text.Length; i++) {
				if (text[i] == '\\') {
					i++;
				}
				else if (text[i] == '/') {
					end = i;
					break;
				}
			}
			int optionsEnd = end < 0 ? -1 : text.IndexOf('/', end + 1);
			if (optionsEnd < 0) {
				error = $"decode rule must look like /pattern/options/replacement: {text}";
				return null;
			}
			var options = RegexOptions.CultureInvariant;
			foreach (char letter in text[(end + 1)..optionsEnd]) {
				switch (letter) {
					case 'i': options |= RegexOptions.IgnoreCase; break;
					case 'm': options |= RegexOptions.Multiline; break;
					case 's': options |= RegexOptions.Singleline; break;
					case 'x': options |= RegexOptions.IgnorePatternWhitespace; break;
					default:
						error = $"unknown regex option '{letter}' in decode rule: {text}";
						return null;
				}
			}
			try {
				return new DecodeRule(text, new Regex(text[1..end], options, Patience), text[(optionsEnd + 1)..]);
			}
			catch (ArgumentException ex) {
				error = $"bad pattern in decode rule {text}: {ex.Message}";
				return null;
			}
		}

		/// <summary>The rewrite of <paramref name="input"/>, or <see langword="null"/> when the pattern does not match it.</summary>
		public string? Apply(string input) {
			try {
				return pattern.IsMatch(input) ? pattern.Replace(input, replacement) : null;
			}
			catch (RegexMatchTimeoutException) {
				return null;
			}
		}

		public override string ToString() => Text;
	}

	/// <summary>One <c>[images]</c> rule: the folder a scheme's paths are looked up under, and how a URI becomes such a path.</summary>
	public sealed class ImageRule {
		/// <summary>The URI scheme, without its colon.</summary>
		public string Scheme { get; }
		/// <summary>The folder as written — relative to the settings file.</summary>
		public string Folder { get; }
		/// <summary>The <c>-decode</c> rule as written, or <see langword="null"/> for the scheme's built-in shape.</summary>
		public string? Decode { get; }
		/// <summary>The folder resolved to an absolute path: the one place this scheme's images may come from.</summary>
		public string Root { get; }

		/// <param name="scheme">The URI scheme, without its colon.</param>
		/// <param name="folder">The folder, relative to <paramref name="directory"/>.</param>
		/// <param name="decode">A <c>/pattern/options/replacement</c> rule, or <see langword="null"/>.</param>
		/// <param name="directory">The settings file's folder; an empty string keeps <paramref name="folder"/> as it is.</param>
		public ImageRule(string scheme, string folder, string? decode, string directory = "") {
			Scheme = scheme;
			Folder = folder;
			Decode = decode;
			Root = directory == "" ? folder : System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, folder));
		}

		internal ImageRule(ImageRule folderFrom, ImageRule? decodeFrom)
			: this(folderFrom.Scheme, folderFrom.Folder, decodeFrom?.Decode) {
			Root = folderFrom.Root;
		}
	}

	/// <summary>One <c>[hyperlinks]</c> rule: what a click on a scheme does, and how the URI is rewritten first.</summary>
	/// <param name="scheme">The URI scheme, without its colon.</param>
	/// <param name="mode">The mode, or <see langword="null"/> for the scheme's default.</param>
	/// <param name="decode">A <c>/pattern/options/replacement</c> rule, or <see langword="null"/>.</param>
	public sealed class LinkRule(string scheme, LinkMode? mode, string? decode) {
		public string Scheme { get; } = scheme;
		public LinkMode? Mode { get; } = mode;
		public string? Decode { get; } = decode;
	}

	/// <summary>
	///     The editor metadata a <c>words.ini</c> names in its <c>param</c> slots
	///     (SPEC: Markdown previews): Words ini syntax with an <c>[images]</c>
	///     table — <c>scheme=folder</c> and <c>scheme-decode=/pattern/options/replacement</c> —
	///     and a <c>[hyperlinks]</c> table — <c>scheme=popup|shellexec</c> and the same
	///     <c>-decode</c>. The built-in image schemes (<c>assets</c>, <c>avares</c>,
	///     <c>pack</c>, <c>resx</c>, <c>staticres</c>) need only a folder, their
	///     shape being known; any other needs a decode rule, and a decode rule on a
	///     built-in overrides its shape. The editor reads and writes this file, so
	///     it is plainer than a dictionary: the two tables and nothing else —
	///     comments in a hand-written one are read past, not kept. The runtime
	///     never reads it. Nothing here touches the file system but
	///     <see cref="Load(string)"/>, <see cref="Save(string)"/> and
	///     <see cref="TryResolveImage"/>.
	/// </summary>
	public sealed class ProjectSettings {
		private static readonly string[] BuiltIn = ["assets", "avares", "pack", "resx", "staticres"];
		private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp"];
		private static readonly IReadOnlyList<string> NoErrors = [];

		/// <summary>No rules: every image is alt text, every link is reported.</summary>
		public static ProjectSettings Empty { get; } = new("", [], []);

		/// <summary>Where the settings were loaded from, or empty.</summary>
		public string Path { get; }
		/// <summary>The image rules, in file order.</summary>
		public IReadOnlyList<ImageRule> Images { get; }
		/// <summary>The hyperlink rules, in file order.</summary>
		public IReadOnlyList<LinkRule> Links { get; }
		/// <summary>What was wrong with the file; the rules that made sense still apply.</summary>
		public IReadOnlyList<string> Errors { get; }

		private readonly Dictionary<string, (ImageRule rule, DecodeRule? decode)> images = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, (LinkRule rule, DecodeRule? decode)> links = new(StringComparer.OrdinalIgnoreCase);
		//what the file itself was wrong about, as opposed to what the rules are:
		//the rules are re-judged whenever they are layered, the file's gripes stand
		private readonly IReadOnlyList<string> carried;

		/// <summary>Settings from rules, as a dialog assembles them. Malformed rules are reported in <see cref="Errors"/> and skipped.</summary>
		/// <param name="path">Where the settings live, for saving.</param>
		/// <param name="images">The image rules; a later duplicate scheme wins.</param>
		/// <param name="links">The hyperlink rules; a later duplicate scheme wins.</param>
		/// <param name="errors">Gripes to carry along with the ones found here.</param>
		public ProjectSettings(string path, IEnumerable<ImageRule> images, IEnumerable<LinkRule> links, IEnumerable<string>? errors = null) {
			Path = path;
			carried = [.. errors ?? NoErrors];
			List<string> gripes = [.. carried];
			foreach (ImageRule rule in images) {
				DecodeRule? decode = null;
				if (rule.Decode is not null) {
					decode = DecodeRule.TryParse(rule.Decode, out string? error);
					if (decode is null) {
						gripes.Add($"[images] {rule.Scheme}-decode: {error}");
					}
				}
				else if (!BuiltIn.Contains(rule.Scheme, StringComparer.OrdinalIgnoreCase)) {
					gripes.Add($"[images] {rule.Scheme}: not a built-in scheme, so it needs a {rule.Scheme}-decode rule to become a path");
				}
				this.images[rule.Scheme] = (rule, decode);
			}
			foreach (LinkRule rule in links) {
				DecodeRule? decode = null;
				if (rule.Decode is not null) {
					decode = DecodeRule.TryParse(rule.Decode, out string? error);
					if (decode is null) {
						gripes.Add($"[hyperlinks] {rule.Scheme}-decode: {error}");
					}
				}
				this.links[rule.Scheme] = (rule, decode);
			}
			Images = [.. this.images.Values.Select(pair => pair.rule)];
			Links = [.. this.links.Values.Select(pair => pair.rule)];
			Errors = gripes;
		}

		/// <summary>Reads settings from <paramref name="reader"/>; <paramref name="path"/> is where they live, which folders resolve against.</summary>
		public static ProjectSettings Load(TextReader reader, string path) {
			var read = new Reader();
			new WordsParser(read).Load(reader);
			return read.ToSettings(path);
		}

		/// <summary>Reads the settings file at <paramref name="path"/>. A file that is not there, or cannot be read, is an error carried in <see cref="Errors"/>, not thrown.</summary>
		public static ProjectSettings Load(string path) {
			try {
				using var reader = File.OpenText(path);
				return Load(reader, path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				return new ProjectSettings(path, [], [], [$"settings file {path}: {ex.Message}"]);
			}
		}

		/// <summary>Writes the two tables in Words ini syntax.</summary>
		public void Write(TextWriter writer) {
			using var ini = new IniWriter(writer);
			ini.WriteBlockHeader("images");
			foreach (ImageRule rule in Images) {
				ini.WritePair(rule.Scheme, rule.Folder);
				if (rule.Decode is not null) {
					ini.WritePair($"{rule.Scheme}-decode", rule.Decode);
				}
			}
			ini.WriteLine();
			ini.WriteBlockHeader("hyperlinks");
			foreach (LinkRule rule in Links) {
				if (rule.Mode is { } mode) {
					ini.WritePair(rule.Scheme, mode == LinkMode.ShellExec ? "shellexec" : "popup");
				}
				if (rule.Decode is not null) {
					ini.WritePair($"{rule.Scheme}-decode", rule.Decode);
				}
			}
			ini.WriteLine();
		}

		/// <summary>Writes the settings to <paramref name="path"/>. I/O failures propagate.</summary>
		public void Save(string path) {
			using var writer = new StreamWriter(path);
			Write(writer);
		}

		/// <summary>
		///     These settings layered over <paramref name="under"/>, key by key: a
		///     scheme's folder, decode or mode named here replaces the one below,
		///     everything else falls through. What a language's file does to the
		///     dictionary's.
		/// </summary>
		public ProjectSettings Over(ProjectSettings under) {
			var mergedImages = new Dictionary<string, ImageRule>(StringComparer.OrdinalIgnoreCase);
			foreach (ImageRule rule in under.Images.Concat(Images)) {
				if (mergedImages.TryGetValue(rule.Scheme, out var below)) {
					mergedImages[rule.Scheme] = new ImageRule(rule, rule.Decode is null ? below : rule);
				}
				else {
					mergedImages[rule.Scheme] = rule;
				}
			}
			var mergedLinks = new Dictionary<string, LinkRule>(StringComparer.OrdinalIgnoreCase);
			foreach (LinkRule rule in under.Links.Concat(Links)) {
				mergedLinks[rule.Scheme] = mergedLinks.TryGetValue(rule.Scheme, out var below)
					? new LinkRule(rule.Scheme, rule.Mode ?? below.Mode, rule.Decode ?? below.Decode)
					: rule;
			}
			return new ProjectSettings(Path, mergedImages.Values, mergedLinks.Values, under.carried.Concat(carried));
		}

		/// <summary>
		///     Where an image URI's file would be: the scheme's <paramref name="root"/>
		///     folder and the <paramref name="relativePath"/> under it — the decode
		///     rule's output, or the built-in shape's. False for a scheme with no
		///     rule, a URI the decode rule does not match, or a shape that yields
		///     nothing. Clamping the path to the root is the resolver's job.
		/// </summary>
		public bool TryLocate(Uri uri, out string root, out string relativePath) {
			root = relativePath = "";
			if (!images.TryGetValue(uri.Scheme, out var image)) {
				return false;
			}
			string text = uri.OriginalString;
			int query = text.IndexOf('?');
			if (query >= 0) {
				text = text[..query];
			}
			string? path = image.decode is not null
				? image.decode.Apply(text)
				: image.rule.Decode is null ? BuiltInPath(uri.Scheme, text) : null;
			if (string.IsNullOrEmpty(path)) {
				return false;
			}
			root = image.rule.Root;
			relativePath = path;
			return true;
		}

		/// <summary>
		///     The image file for <paramref name="uri"/>: <see cref="TryLocate"/>'s
		///     path canonicalized under its root and clamped there — <c>../</c>
		///     trickery, rooted paths and UNC shares resolve to nothing, and no
		///     variable is ever expanded, wherever the path came from — then taken as
		///     it is, or with the first image extension that names a file (so
		///     <c>staticres:Logo</c> finds <c>Logo.png</c>). False when there is no
		///     such file.
		/// </summary>
		public bool TryResolveImage(Uri uri, out string filePath) {
			filePath = "";
			if (!TryLocate(uri, out string root, out string relativePath)) {
				return false;
			}
			string fullRoot = System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar;
			// GetFullPath resolves any ../ and ./ segments; a rooted or UNC path survives
			// Path.Combine untouched. Either way, anything outside the root is refused
			string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot,
				relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
			if (!candidate.StartsWith(fullRoot, StringComparison.Ordinal)) {
				return false;
			}
			if (File.Exists(candidate)) {
				filePath = candidate;
				return true;
			}
			//appending an extension to a clamped path keeps it clamped
			foreach (string extension in ImageExtensions) {
				if (File.Exists(candidate + extension)) {
					filePath = candidate + extension;
					return true;
				}
			}
			return false;
		}

		//what the stock resolvers would look up, minus the app they would look in
		private static string? BuiltInPath(string scheme, string text) {
			string? path = scheme switch {
				"assets" or "resx" or "staticres" => text[(text.IndexOf(':') + 1)..],
				"avares" when text.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
					=> text.IndexOf('/', "avares://".Length) is int slash and >= 0 ? text[(slash + 1)..] : null,
				"pack" when text.IndexOf(";component/", StringComparison.OrdinalIgnoreCase) is int component and >= 0
					=> text[(component + ";component/".Length)..],
				"pack" when text.StartsWith("pack://application:,,,/", StringComparison.OrdinalIgnoreCase)
					=> text["pack://application:,,,/".Length..],
				_ => null,
			};
			return path is null ? null : Uri.UnescapeDataString(path.TrimStart('/'));
		}

		/// <summary>
		///     What a click on <paramref name="uri"/> does: the target after any
		///     decode rule, and the <paramref name="mode"/> — the scheme's, or by
		///     default <see cref="LinkMode.ShellExec"/> for <c>http</c>, <c>https</c>
		///     and <c>mailto</c> and <see cref="LinkMode.Popup"/> for anything else.
		/// </summary>
		public string Link(Uri uri, out LinkMode mode) {
			links.TryGetValue(uri.Scheme, out var link);
			mode = link.rule?.Mode ?? (uri.Scheme is "http" or "https" or "mailto" ? LinkMode.ShellExec : LinkMode.Popup);
			return link.decode?.Apply(uri.OriginalString) ?? uri.OriginalString;
		}

		//the parse events of a settings file, gathered into rules; comments go by
		private sealed class Reader : IWordsParserConsumer {
			private readonly List<string> errors = [];
			private readonly List<(string block, string scheme, string suffix, string value)> fields = [];
			private string block = "";

			public void VisitBlock(string baseKey, string name) => block = baseKey.ToLowerInvariant();

			public void VisitFieldDeclaration(FieldKey key, string text) {
				var (_, scheme, suffix) = key;
				if (block is not ("images" or "hyperlinks")) {
					errors.Add(block == ""
						? $"{scheme}: a rule outside any table; put it under [images] or [hyperlinks]"
						: $"[{block}] {scheme}: unknown table; only [images] and [hyperlinks] are read");
					return;
				}
				if (suffix is not ("" or "decode")) {
					errors.Add($"[{block}] {scheme}-{suffix}: only {scheme}= and {scheme}-decode= are understood");
					return;
				}
				fields.Add((block, scheme, suffix, text));
			}

			public void VisitFieldContinuation(FieldKey key, string value) {
				if (fields.Count > 0) {
					var last = fields[^1];
					fields[^1] = (last.block, last.scheme, last.suffix, last.value + value);
				}
			}

			public ProjectSettings ToSettings(string path) {
				string directory = path == "" ? "" : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "";
				var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var imageDecodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var modes = new Dictionary<string, LinkMode?>(StringComparer.OrdinalIgnoreCase);
				var linkDecodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				List<string> imageOrder = [], linkOrder = [];
				foreach (var (table, scheme, suffix, value) in fields) {
					if (table == "images") {
						if (!imageOrder.Contains(scheme, StringComparer.OrdinalIgnoreCase)) {
							imageOrder.Add(scheme);
						}
						if (suffix == "") {
							folders[scheme] = value;
						}
						else {
							imageDecodes[scheme] = value;
						}
					}
					else {
						if (!linkOrder.Contains(scheme, StringComparer.OrdinalIgnoreCase)) {
							linkOrder.Add(scheme);
						}
						if (suffix == "decode") {
							linkDecodes[scheme] = value;
						}
						else if (value.Equals("popup", StringComparison.OrdinalIgnoreCase)) {
							modes[scheme] = LinkMode.Popup;
						}
						else if (value.Equals("shellexec", StringComparison.OrdinalIgnoreCase)) {
							modes[scheme] = LinkMode.ShellExec;
						}
						else {
							errors.Add($"[hyperlinks] {scheme}: '{value}' is neither popup nor shellexec");
							modes.TryAdd(scheme, null);
						}
					}
				}
				List<ImageRule> images = [];
				foreach (string scheme in imageOrder) {
					if (folders.TryGetValue(scheme, out string? folder)) {
						images.Add(new ImageRule(scheme, folder, imageDecodes.GetValueOrDefault(scheme), directory));
					}
					else {
						errors.Add($"[images] {scheme}-decode: a decode rule with no {scheme}= folder to look under");
					}
				}
				List<LinkRule> links = [.. linkOrder.Select(scheme => new LinkRule(scheme, modes.GetValueOrDefault(scheme), linkDecodes.GetValueOrDefault(scheme)))];
				return new ProjectSettings(path, images, links, errors);
			}
		}
	}
}
