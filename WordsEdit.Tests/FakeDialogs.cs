using System.Diagnostics.CodeAnalysis;
using WordsEdit.Utils;
using WordsEdit.ViewModels;

namespace WordsEdit.Tests;

/// <summary>
///     Answers by script and remembers what was asked, so dialog-driven flows run
///     headless. <see cref="OnShow"/> stands in for the user working a modal: it
///     receives the view model and may drive its commands before it "closes".
/// </summary>
public sealed class FakeDialogs : IDialogs {
	public List<DialogViewModel> Shown { get; } = [];
	public List<string> Confirmations { get; } = [];
	public List<string> Notices { get; } = [];

	public bool ConfirmAnswer { get; set; } = true;
	public CloseAnswer SaveAnswer { get; set; } = CloseAnswer.Save;
	/// <summary>The files the open dialog "picks"; null cancels it.</summary>
	public string[]? FilesToOpen { get; set; }
	/// <summary>The file the save dialog "picks"; null cancels it.</summary>
	public string? FileToSave { get; set; }
	public Action<DialogViewModel>? OnShow { get; set; }

	public void Show(DialogViewModel dialog) {
		Shown.Add(dialog);
		OnShow?.Invoke(dialog);
	}

	public bool Confirm(string message) {
		Confirmations.Add(message);
		return ConfirmAnswer;
	}

	public CloseAnswer AskToSave(string message) => SaveAnswer;

	public void Tell(string message) => Notices.Add(message);

	public bool TryOpenFiles(string title, string filter, [NotNullWhen(true)] out string[]? fileNames) {
		if (FilesToOpen is null) {
			fileNames = null;
			return false;
		}
		fileNames = FilesToOpen;
		return true;
	}

	public bool TrySaveFile(string title, string filter, [NotNullWhen(true)] out string? fileName) {
		if (FileToSave is null) {
			fileName = null;
			return false;
		}
		fileName = FileToSave;
		return true;
	}
}
