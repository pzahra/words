using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace PatTech.Markdown.Wpf {
	public delegate bool CanExecuteHyperlink(Uri uri);
	public delegate void ExecutedHyperlink(Uri uri);

	public static class Markdown {
		//private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

		public static readonly RoutedCommand HyperlinkClick = new RoutedCommand(
				nameof(HyperlinkClick),
				typeof(Markdown));

		public static void CanExecuteHyperlinkWithShell(
				CanExecuteRoutedEventArgs e) {
			CanExecuteHyperlink(e, null);
		}
		public static void CanExecuteHyperlink(
				CanExecuteRoutedEventArgs e,
				CanExecuteHyperlink? canExecute) {
			if (!TryGetUriFromHyperlinkCommand(e, out var uri)) {
				return;
			}
			if (!CanExecuteUsingShell(uri) && canExecute?.Invoke(uri) is false) {
				return;
			}

			e.CanExecute = true;
			e.Handled = true;
		}

		public static void ExecutedHyperlinkWithShell(
				ExecutedRoutedEventArgs e) {
			ExecutedHyperlink(e, null);
		}
		public static void ExecutedHyperlink(
				ExecutedRoutedEventArgs e,
				ExecutedHyperlink? executed) {
			if (!TryGetUriFromHyperlinkCommand(e, out var uri)) {
				return;
			}

			if (CanExecuteUsingShell(uri)) {
				e.Handled = true;
				ExecutedUsingShell(uri);
			}
			else {
				e.Handled = true;
				executed?.Invoke(uri);
			}
		}

		public static bool TryGetUriFromHyperlinkCommand(
				CanExecuteRoutedEventArgs e,
				[MaybeNullWhen(false)] out Uri uri) {
			return TryGetUriFromHyperlinkCommand(e.Command, e.Parameter, out uri);
		}
		public static bool TryGetUriFromHyperlinkCommand(
				ExecutedRoutedEventArgs e,
				[MaybeNullWhen(false)] out Uri uri) {
			return TryGetUriFromHyperlinkCommand(e.Command, e.Parameter, out uri);
		}
		private static bool TryGetUriFromHyperlinkCommand(
				ICommand command,
				object? parameter,
				[MaybeNullWhen(false)] out Uri uri) {
			if (command != HyperlinkClick) {
				uri = null;
				return false;
			}
			if (parameter is not string uriString) {
				logger.Error("parameter is not a string?! `{0}`", parameter);
				uri = null;
				return false;
			}
			if (!Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out uri)) {
				logger.Error("parameter is not a valid uri?! `{0}`", uriString);
				return false;
			}

			return true;
		}

		public static bool CanExecuteUsingShell(Uri uri)
			=> uri.Scheme is "http" or "https" or "mailto";
		public static Process? ExecutedUsingShell(Uri uri)
			=> Process.Start(
				new ProcessStartInfo(uri.ToString()) {
					UseShellExecute = true
				});
	}
}
