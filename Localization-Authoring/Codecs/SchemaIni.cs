using System.Text;
using System.Text.RegularExpressions;

namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>
	///     The ODBC text driver's <c>schema.ini</c> (the editor's SPEC: Spreadsheets,
	///     The schema): one a folder, a section a file, named by the file's name, each
	///     a bag of key=value pairs. A sheet's section is its options
	///     (<see cref="FormatOptions"/>); its names are matched without case, as the
	///     driver matches them. Writing one section replaces it whole, its pairs in the
	///     driver's order, and leaves every other line, comment and blank as it was,
	///     in the encoding and the line break the file was read in; a folder with no
	///     <c>schema.ini</c> gets one.
	/// </summary>
	public static partial class SchemaIni {
		/// <summary>The file's name, beside the sheets it describes.</summary>
		public const string FileName = "schema.ini";

		/// <summary>The <c>schema.ini</c> beside <paramref name="sheet"/>.</summary>
		public static string For(string sheet) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sheet)) ?? "", FileName);

		/// <summary>The sections the <c>schema.ini</c> beside <paramref name="sheet"/> has, in its order; none when there is no file.</summary>
		public static IReadOnlyList<string> Sections(string sheet)
			=> Load(For(sheet)) is { } schema ? [.. schema.Lines.Select(Header).OfType<string>()] : [];

		/// <summary>
		///     The section of the <c>schema.ini</c> beside <paramref name="sheet"/> named
		///     <paramref name="section"/>, the sheet's own name when none is given;
		///     <see langword="null"/> when there is no file or no such section.
		/// </summary>
		public static FormatOptions? Read(string sheet, string? section = null) {
			if (Load(For(sheet)) is not { } schema || Find(schema.Lines, section ?? Path.GetFileName(sheet)) is not { } found) {
				return null;
			}
			FormatOptions options = [];
			for (int i = found.Start + 1; i < found.End; i++) {
				if (Pair(schema.Lines[i]) is { } pair) {
					options[pair.Key] = pair.Value;
				}
			}
			return options;
		}

		/// <summary>Writes <paramref name="options"/> as <paramref name="sheet"/>'s section, making the file when there is none.</summary>
		public static void Write(string sheet, FormatOptions options) {
			string path = For(sheet);
			string name = Path.GetFileName(sheet);
			Schema schema = Load(path) ?? new([], "\r\n", new UTF8Encoding(false));
			List<string> lines = schema.Lines;
			List<string> section = [$"[{name}]", .. Ordered(options).Select(pair => $"{pair.Key}={pair.Value}")];
			if (Find(lines, name) is { } found) {
				section[0] = lines[found.Start]; //its header as the file spells it
				//the blanks and comments that end the old section sit above the next one, and stay
				int kept = found.End;
				while (kept > found.Start + 1 && (lines[kept - 1].Trim() == "" || lines[kept - 1].TrimStart().StartsWith(';'))) {
					kept--;
				}
				lines.RemoveRange(found.Start, kept - found.Start);
				lines.InsertRange(found.Start, section);
			}
			else {
				if (lines.Count > 0 && lines[^1].Trim() != "") {
					lines.Add("");
				}
				lines.AddRange(section);
			}
			IniWriter.WriteAtomic(path, writer => {
				foreach (string line in lines) {
					writer.Write(line);
					writer.Write(schema.NewLine);
				}
			}, schema.Encoding);
		}

		//the file as lines, the line break it uses, and the encoding it was read in: the one
		//its byte-order mark names, UTF-8 when its bytes read as UTF-8, else the machine's
		//ANSI code page, which the driver reads it in
		private sealed record Schema(List<string> Lines, string NewLine, Encoding Encoding);

		private static Schema? Load(string path) {
			if (!File.Exists(path)) {
				return null;
			}
			byte[] bytes = File.ReadAllBytes(path);
			var (text, encoding) = CodePages.Decode(bytes, CodePages.IsUtf8(bytes) ? new UTF8Encoding(false) : CodePages.Ansi(null));
			string newLine = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
			List<string> lines = [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];
			if (lines.Count > 0 && lines[^1] == "") {
				lines.RemoveAt(lines.Count - 1); //the last line's break, which writing puts back
			}
			return new Schema(lines, newLine, encoding);
		}

		//a section's header line and the line its section ends before, the next header's or the end
		private static (int Start, int End)? Find(List<string> lines, string name) {
			int start = lines.FindIndex(line => string.Equals(Header(line), name, StringComparison.OrdinalIgnoreCase));
			if (start < 0) {
				return null;
			}
			int end = lines.FindIndex(start + 1, line => Header(line) is not null);
			return (start, end < 0 ? lines.Count : end);
		}

		private static string? Header(string line) {
			string trimmed = line.Trim();
			return trimmed.Length > 1 && trimmed[0] == '[' && trimmed[^1] == ']' ? trimmed[1..^1].Trim() : null;
		}

		private static (string Key, string Value)? Pair(string line) {
			string trimmed = line.Trim();
			int equals = trimmed.IndexOf('=');
			return trimmed.StartsWith(';') || equals <= 0 ? null : (trimmed[..equals].Trim(), trimmed[(equals + 1)..].Trim());
		}

		//the driver's order: the header, the format and the character set, the text
		//delimiter, the columns by number, and then whatever else the section holds
		private static IEnumerable<KeyValuePair<string, string>> Ordered(FormatOptions options)
			=> options.OrderBy(pair => pair.Key.ToUpperInvariant() switch {
				"COLNAMEHEADER" => (0, 0),
				"FORMAT" => (1, 0),
				"CHARACTERSET" => (2, 0),
				"TEXTDELIMITER" => (3, 0),
				_ when ColumnNumber(pair.Key) is { } number => (4, number),
				_ => (5, 0),
			});

		/// <summary>The column a <c>ColN</c> key names, counting from one; <see langword="null"/> for any other key.</summary>
		public static int? ColumnNumber(string key)
			=> RxColumn().Match(key) is { Success: true } match && int.TryParse(match.Groups[1].Value, out int number) && number > 0 ? number : null;

		[GeneratedRegex(@"^col(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
		private static partial Regex RxColumn();
	}
}
