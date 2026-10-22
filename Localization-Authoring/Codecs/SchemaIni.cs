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
	///     each with its own line break (CR, LF or CRLF, as the driver reads them), in
	///     ANSI or UTF-8 with no byte-order mark, which is all the driver reads; a
	///     folder with no <c>schema.ini</c> gets one. A file in UTF-16 or UTF-32 is
	///     none the driver reads: it reads as no file, and is never written over.
	/// </summary>
	public static partial class SchemaIni {
		/// <summary>The file's name, beside the sheets it describes.</summary>
		public const string FileName = "schema.ini";

		/// <summary>The <c>schema.ini</c> beside <paramref name="sheet"/>.</summary>
		public static string For(string sheet) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sheet)) ?? "", FileName);

		/// <summary>The sections the <c>schema.ini</c> beside <paramref name="sheet"/> has, in its order; none when there is no file, or none the driver reads.</summary>
		public static IReadOnlyList<string> Sections(string sheet)
			=> Load(For(sheet), writing: false) is { } schema ? [.. schema.Lines.Select(line => Header(line.Text)).OfType<string>()] : [];

		/// <summary>
		///     The section of the <c>schema.ini</c> beside <paramref name="sheet"/> named
		///     <paramref name="section"/>, the sheet's own name when none is given;
		///     <see langword="null"/> when there is no file, no such section, or a file
		///     in UTF-16 or UTF-32, which the driver does not read.
		/// </summary>
		public static FormatOptions? Read(string sheet, string? section = null) {
			if (Load(For(sheet), writing: false) is not { } schema || Find(schema.Lines, section ?? Path.GetFileName(sheet)) is not { } found) {
				return null;
			}
			FormatOptions options = [];
			for (int i = found.Start + 1; i < found.End; i++) {
				if (Pair(schema.Lines[i].Text) is { } pair) {
					options[pair.Key] = pair.Value;
				}
			}
			return options;
		}

		/// <summary>Writes <paramref name="options"/> as <paramref name="sheet"/>'s section, making the file when there is none.</summary>
		/// <exception cref="InvalidDataException">The file is in UTF-16 or UTF-32, which the driver does not read; it is left as it is.</exception>
		public static void Write(string sheet, FormatOptions options) {
			string path = For(sheet);
			string name = Path.GetFileName(sheet);
			Schema schema = Load(path, writing: true) ?? new([], new UTF8Encoding(false));
			List<Line> lines = schema.Lines;
			List<string> section = [$"[{name}]", .. Ordered(options).Select(pair => $"{pair.Key}={pair.Value}")];
			//the section's lines end as the line they replace, or as the file's last that ends
			string newLine = lines.LastOrDefault(line => line.End != "")?.End ?? "\r\n";
			if (Find(lines, name) is { } found) {
				section[0] = lines[found.Start].Text; //its header as the file spells it
				if (lines[found.Start].End != "") {
					newLine = lines[found.Start].End;
				}
				//the blanks and comments that end the old section sit above the next one, and stay
				int kept = found.End;
				while (kept > found.Start + 1 && (lines[kept - 1].Text.Trim() == "" || lines[kept - 1].Text.TrimStart().StartsWith(';'))) {
					kept--;
				}
				//a file ending without a line break still does, when the section was its end
				bool open = kept == lines.Count && lines[^1].End == "";
				List<Line> written = [.. section.Select(text => new Line(text, newLine))];
				if (open) {
					written[^1] = written[^1] with { End = "" };
				}
				lines.RemoveRange(found.Start, kept - found.Start);
				lines.InsertRange(found.Start, written);
			}
			else {
				if (lines.Count > 0 && lines[^1].End == "") {
					lines[^1] = lines[^1] with { End = newLine }; //the line the section now follows
				}
				if (lines.Count > 0 && lines[^1].Text.Trim() != "") {
					lines.Add(new("", newLine));
				}
				lines.AddRange(section.Select(text => new Line(text, newLine)));
			}
			IniWriter.WriteAtomic(path, writer => {
				foreach (Line line in lines) {
					writer.Write(line.Text);
					writer.Write(line.End);
				}
			}, schema.Encoding);
		}

		//a line and the break that ends it: CRLF, LF or CR, as the driver reads them, or none
		//at the end of a file without one
		private sealed record Line(string Text, string End);

		//the file as lines, and the encoding it is written back in: UTF-8 when its bytes read
		//as UTF-8, a byte-order mark read past and never written, else the machine's ANSI code
		//page, which the driver reads it in
		private sealed record Schema(List<Line> Lines, Encoding Encoding);

		//none for a file in UTF-16 or UTF-32, which the driver does not read, unless it is to be
		//written, which is refused
		private static Schema? Load(string path, bool writing) {
			if (!File.Exists(path)) {
				return null;
			}
			byte[] bytes = File.ReadAllBytes(path);
			int? mark = CodePages.Bom(bytes);
			if (mark is not (null or 65001)) {
				return writing
					? throw new InvalidDataException($"{path} is in UTF-16 or UTF-32, which the text driver does not read, so it was left as it is")
					: null;
			}
			int skip = mark is null ? 0 : 3;
			Encoding encoding = CodePages.IsUtf8(bytes[skip..]) ? new UTF8Encoding(false) : CodePages.Ansi(null);
			return new Schema(Lines(encoding.GetString(bytes, skip, bytes.Length - skip)), encoding);
		}

		private static List<Line> Lines(string text) {
			List<Line> lines = [];
			int start = 0;
			for (int i = 0; i < text.Length; i++) {
				if (text[i] is '\r' or '\n') {
					string end = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : text[i].ToString();
					lines.Add(new(text[start..i], end));
					i += end.Length - 1;
					start = i + 1;
				}
			}
			if (start < text.Length) {
				lines.Add(new(text[start..], ""));
			}
			return lines;
		}

		//a section's header line and the line its section ends before, the next header's or the end
		private static (int Start, int End)? Find(List<Line> lines, string name) {
			int start = lines.FindIndex(line => string.Equals(Header(line.Text), name, StringComparison.OrdinalIgnoreCase));
			if (start < 0) {
				return null;
			}
			int end = lines.FindIndex(start + 1, line => Header(line.Text) is not null);
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
