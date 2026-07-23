using System.Runtime.ExceptionServices;
using System.Windows.Navigation;
using PatTech.Localization.Wpf;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// Covers the WPF global navigate handler: routing, replace-on-reregister,
/// and unsubscribe via disposal — mirroring the Avalonia package's contract.
/// The handler is package-global state, so these tests share one class.
/// </summary>
public class HyperlinkHandlerTests {

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA. Bridge the gap.</summary>
	private static T RunSta<T>(Func<T> func) {
		T result = default!;
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try {
				result = func();
			}
			catch (Exception e) {
				error = ExceptionDispatchInfo.Capture(e);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
		return result;
	}

	/// <summary>Raises RequestNavigate on a real Hyperlink, as a click would.</summary>
	private static bool Click(string uri) {
		var link = new System.Windows.Documents.Hyperlink { NavigateUri = new Uri(uri) };
		var args = new RequestNavigateEventArgs(link.NavigateUri, null) {
			RoutedEvent = System.Windows.Documents.Hyperlink.RequestNavigateEvent,
			Source = link,
		};
		link.RaiseEvent(args);
		return args.Handled;
	}

	[Fact]
	public void HandlerReceivesUriAndMarksHandled() {
		RunSta<object?>(() => {
			var seen = new List<Uri>();
			using var subscription = Hyperlink.RegisterGlobalNavigateHandler(seen.Add);

			var handled = Click("appcmd:ping");

			Assert.True(handled);
			var uri = Assert.Single(seen);
			Assert.Equal("appcmd:ping", uri.OriginalString);
			return null;
		});
	}

	[Fact]
	public void Reregistering_ReplacesThePreviousHandler() {
		RunSta<object?>(() => {
			var first = new List<Uri>();
			var second = new List<Uri>();
			var stale = Hyperlink.RegisterGlobalNavigateHandler(first.Add);
			using var subscription = Hyperlink.RegisterGlobalNavigateHandler(second.Add);

			Click("appcmd:once");

			Assert.Empty(first);
			Assert.Single(second);

			// disposing the replaced subscription must not kill the current one
			stale.Dispose();
			Click("appcmd:twice");
			Assert.Equal(2, second.Count);
			return null;
		});
	}

	[Fact]
	public void Disposing_Unregisters() {
		RunSta<object?>(() => {
			var seen = new List<Uri>();
			var subscription = Hyperlink.RegisterGlobalNavigateHandler(seen.Add);
			subscription.Dispose();

			var handled = Click("appcmd:ghost");

			Assert.False(handled);
			Assert.Empty(seen);
			return null;
		});
	}
}
