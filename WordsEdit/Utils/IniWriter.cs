using System.IO;
using System.Text.RegularExpressions;

namespace WordsEdit.Utils {
	sealed class IniWriter : IDisposable, IAsyncDisposable {
		private readonly TextWriter _writer;

		public IniWriter(TextWriter writer) {
			_writer = writer;
		}

		public void WriteBlockHeader(string name) {
			_writer.WriteLine("[" + name + "]");
		}
		public void WritePair(string key, string value) {
			value = Regex.Replace(value, @"\r\n?|\n\r?", "\\" + _writer.NewLine);
			value = Regex.Replace(value, @"['_]", m => string.Concat(m.ValueSpan, m.ValueSpan));
			value = Regex.Replace(value, @"(.{50}(?=.{40})\S*)(?=\W+\w)", "$1_" + _writer.NewLine);
			_writer.Write(key);
			_writer.Write('=');
			if (Regex.IsMatch(value, @"^\s")) {
				_writer.WriteLine('_');
			}
			_writer.WriteLine(value);
		}
		public void WriteLine() {
			_writer.WriteLine();
		}

		public void Dispose() => _writer.Dispose();
		public ValueTask DisposeAsync() => _writer.DisposeAsync();
	}
}
