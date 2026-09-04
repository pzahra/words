using System.Diagnostics.CodeAnalysis;
using WordsEdit.ViewModels;

namespace WordsEdit.Utils;

/// <summary>What the user can answer when closing with unsaved changes.</summary>
public enum CloseAnswer { Save, Discard, Cancel }

/// <summary>
///     How the editor asks and tells. View models take one of these instead of
///     reaching for windows and message boxes, so the app can give them real
///     modal windows (<see cref="Views.WpfDialogs"/>) and tests can give them a
///     fake that answers by script.
/// </summary>
public interface IDialogs {
	/// <summary>Shows <paramref name="dialog"/> modally; returns when it closes.</summary>
	void Show(DialogViewModel dialog);
	/// <summary>A yes/no question. True on yes.</summary>
	bool Confirm(string message);
	/// <summary>Save, discard or cancel — for closing with unsaved changes.</summary>
	CloseAnswer AskToSave(string message);
	/// <summary>A notice the user dismisses.</summary>
	void Tell(string message);
	bool TryOpenFiles(string title, string filter, [NotNullWhen(true)] out string[]? fileNames);
	bool TrySaveFile(string title, string filter, [NotNullWhen(true)] out string? fileName);
}
