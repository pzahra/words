namespace PatTech.Localization.Authoring {
	/// <summary>
	///     An <see cref="ITakeException"/> that hands every gripe to whoever is
	///     <see cref="Listen">listening</see> on the current thread — a preview
	///     render, say — and drops the rest. Installed as <see cref="Words.Logger"/>
	///     it turns the runtime's terse codes (<c>WORDS:KEY</c>, <c>IMG:RES</c>…)
	///     into a list a tool can show. Thread-bound on purpose: listeners on other
	///     threads (parallel tests, most likely) never hear each other.
	/// </summary>
	public sealed class GripeCollector : ITakeException {
		private readonly AsyncLocal<List<string>?> sink = new();

		/// <summary>
		///     Everything logged on this thread lands in <paramref name="into"/> until
		///     the returned scope is disposed; scopes nest, the innermost hearing.
		/// </summary>
		public IDisposable Listen(List<string> into) {
			var previous = sink.Value;
			sink.Value = into;
			return new Scope(this, previous);
		}

		/// <inheritdoc/>
		public void Warn(string text) => sink.Value?.Add(text);

		/// <inheritdoc/>
		public void Error(Exception exception, string message) => sink.Value?.Add($"{message}: {exception.Message}");

		private sealed class Scope(GripeCollector owner, List<string>? previous) : IDisposable {
			public void Dispose() => owner.sink.Value = previous;
		}
	}
}
