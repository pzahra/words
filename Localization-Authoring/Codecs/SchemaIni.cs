using System.Text;
using System.Text.RegularExpressions;
using PatTech.Text.Ini;

namespace PatTech.Localization.Authoring.Codecs {
	/// <summary>
	///     The ODBC text driver's <c>schema.ini</c> (the editor's SPEC: Spreadsheets,
	///     The schema): one a folder, a section a file, named by the file's name, each
	///     a bag of key=value pairs. A sheet's section is its options
	///     (<see cref="FormatOptions"/>); its names are matched without case, as the
	///     driver matches them. It is read and written through the ini driver (the
	///     editor's SPEC: Save as a patch): writing one section replaces its pairs, in
	///     the driver's order, where they stood, and leaves every other line, comment
	///     and blank as it was, each with its own line break, in ANSI or UTF-8 with no
	///     byte-order mark, which is all the driver reads, a line added breaking CRLF
	///     where the file has no break of its own; a folder with no <c>schema.ini</c>
	///     gets one. A file in UTF-16 or UTF-32 is none the driver reads: it reads as
	///     no file, and is never written over.
	/// </summary>
	public static partial class SchemaIni {
		/// <summary>The file's name, beside the sheets it describes.</summary>
		public const string FileName = "schema.ini";

		/// <summary>The <c>schema.ini</c> beside <paramref name="sheet"/>.</summary>
		public static string For(string sheet) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sheet)) ?? "", FileName);

		//names without case, `=`, `;` comments; UTF-8 where its bytes read so, else the
		//machine's ANSI code page, which the driver reads it in; a UTF-8 byte-order mark read
		//past and never written, UTF-16 and UTF-32 refused
		private static IniDialect Dialect => IniDialect.Sections(new IniRules {
			Encodings = [new UTF8Encoding(false), CodePages.Ansi(null)],
			ByteOrderMarks = IniByteOrderMarks.Dropped,
			LineBreak = "\r\n",
		});

		/// <summary>The <c>schema.ini</c> beside <paramref name="sheet"/>, read once, to read as many of its sections as asked.</summary>
		/// <exception cref="IniCommitException">The file is held, or a write to it is under way or will not settle.</exception>
		public static SchemaFile Load(string sheet) {
			using IniTransaction schema = IniTransaction.Open(For(sheet), Dialect);
			return new SchemaFile(schema.Document.IsNew || !schema.Document.IsText ? null : schema.Document);
		}

		/// <summary>The sections the <c>schema.ini</c> beside <paramref name="sheet"/> has, in its order; none when there is no file, or none the driver reads.</summary>
		/// <exception cref="IniCommitException">The file is held, or a write to it is under way or will not settle.</exception>
		public static IReadOnlyList<string> Sections(string sheet) => Load(sheet).Sections;

		/// <summary>
		///     The section of the <c>schema.ini</c> beside <paramref name="sheet"/> named
		///     <paramref name="section"/>, the sheet's own name when none is given;
		///     <see langword="null"/> when there is no file, no such section, or a file
		///     in UTF-16 or UTF-32, which the driver does not read.
		/// </summary>
		/// <exception cref="IniCommitException">The file is held, or a write to it is under way or will not settle.</exception>
		public static FormatOptions? Read(string sheet, string? section = null) => Load(sheet).Read(section ?? Path.GetFileName(sheet));

		/// <summary>Writes <paramref name="options"/> as <paramref name="sheet"/>'s section, making the file when there is none.</summary>
		/// <exception cref="IniException">Refused, the file as it was: one in UTF-16 or UTF-32 among them (<see cref="IniEncodingException"/>); or left pending (<see cref="IniPendingException"/>).</exception>
		public static void Write(string sheet, FormatOptions options) {
			using IniTransaction schema = Prepare(sheet, options);
			schema.Commit();
		}

		/// <summary>
		///     <paramref name="sheet"/>'s section set to <paramref name="options"/> and
		///     prepared: the file checked and held until the transaction commits, once the
		///     sheet is written, or is disposed, the file as it was.
		/// </summary>
		/// <exception cref="IniException">Refused, the file as it was: one in UTF-16 or UTF-32 among them (<see cref="IniEncodingException"/>).</exception>
		public static IniTransaction Prepare(string sheet, FormatOptions options) {
			IniTransaction schema = IniTransaction.Open(For(sheet), Dialect);
			try {
				schema.ReplaceSection(Path.GetFileName(sheet), Ordered(options).Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Trim())));
				schema.Prepare();
				return schema;
			}
			catch {
				schema.Dispose();
				throw;
			}
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

	/// <summary>A <c>schema.ini</c> as it read once (<see cref="SchemaIni.Load"/>): its sections, each read from memory as often as asked.</summary>
	public sealed class SchemaFile {
		private readonly IniDocument? document;

		//null for no file, or none the driver reads
		internal SchemaFile(IniDocument? document) => this.document = document;

		/// <summary>Its sections, in its order; none when there is no file, or none the driver reads.</summary>
		public IReadOnlyList<string> Sections => document is null ? [] : [.. document.Sections.Where(name => name.Length > 0)];

		/// <summary>The section named <paramref name="section"/>; <see langword="null"/> when there is none.</summary>
		public FormatOptions? Read(string section) {
			ArgumentNullException.ThrowIfNull(section);
			if (document is null || !document.HasSection(section)) {
				return null;
			}
			FormatOptions options = [];
			foreach (string key in document.Keys(section)) {
				options[key] = document.Get(section, key)!;
			}
			return options;
		}
	}
}
