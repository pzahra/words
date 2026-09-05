using System;

namespace PatTech.Localization {
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
