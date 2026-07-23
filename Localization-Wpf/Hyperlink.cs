using System;
using System.Windows;
using System.Windows.Navigation;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     Application-wide navigation for the hyperlinks <see cref="MarkdownParser"/>
	///     renders — the WPF twin of the Avalonia package's
	///     <c>Hyperlink.RegisterGlobalNavigateHandler</c>. WPF's own
	///     <see cref="System.Windows.Documents.Hyperlink"/> raises
	///     <see cref="System.Windows.Documents.Hyperlink.RequestNavigateEvent"/> and then does
	///     nothing; this routes every such click to one handler of your choosing.
	/// </summary>
	public static class Hyperlink {
		private static Action<Uri>? current;
		private static bool hooked;

		/// <summary>
		///     Registers the application-wide handler for hyperlink activation: from then on, every
		///     hyperlink clicked in any flow content passes its <see cref="Uri"/> to
		///     <paramref name="handler"/>. Typically called once at startup to route <c>http:</c>
		///     links to the shell and custom schemes (e.g. <c>appcmd:</c>) to application commands.
		/// </summary>
		/// <remarks>
		///     There is one global handler: calling this again replaces the previous registration,
		///     so a click is never handled twice. Dispose the returned subscription to unregister
		///     without replacing.
		/// </remarks>
		/// <param name="handler">Receives the activated hyperlink's URI.</param>
		/// <returns>A subscription that removes the handler when disposed.</returns>
		public static IDisposable RegisterGlobalNavigateHandler(Action<Uri> handler) {
			if (!hooked) {
				// class handlers cannot be removed, so hook exactly one and let
				// registrations swap the delegate it forwards to
				EventManager.RegisterClassHandler(
					typeof(System.Windows.Documents.Hyperlink),
					System.Windows.Documents.Hyperlink.RequestNavigateEvent,
					new RequestNavigateEventHandler(OnRequestNavigate));
				hooked = true;
			}
			current = handler;
			return new Subscription(handler);
		}

		private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e) {
			if (current is { } handler) {
				handler(e.Uri);
				e.Handled = true;
			}
		}

		private sealed class Subscription(Action<Uri> handler) : IDisposable {
			public void Dispose() {
				// a stale subscription (already replaced) must not kill the current one
				if (current == handler) {
					current = null;
				}
			}
		}
	}
}
