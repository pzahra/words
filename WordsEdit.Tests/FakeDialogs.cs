using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Answers by script and remembers what was asked, so dialog-driven flows run
///     headless. <see cref="OnShow"/> stands in for the user working a modal: it
///     receives the view model and may drive its commands before it "closes".
///     Every string that reaches it is checked for a <c>#key#</c> leak: a key
///     with no words renders as its own name, and nothing the editor says may.
/// </summary>
public sealed class FakeDialogs : IDialogs {
	private static readonly Regex rxLeak = new(@"#[\w.$-]+#", RegexOptions.Compiled);

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

	/// <summary>The text as the user would read it: resolved, no key showing through.</summary>
	public static string Rendered(string text) {
		Assert.DoesNotMatch(rxLeak, text);
		return text;
	}

	public void Show(DialogViewModel dialog) {
		Rendered(dialog.Title);
		Shown.Add(dialog);
		OnShow?.Invoke(dialog);
	}

	public bool Confirm(string message) {
		Confirmations.Add(Rendered(message));
		return ConfirmAnswer;
	}

	public CloseAnswer AskToSave(string message) {
		Rendered(message);
		return SaveAnswer;
	}

	public void Tell(string message) => Notices.Add(Rendered(message));

	public bool TryOpenFiles(string title, string filter, [NotNullWhen(true)] out string[]? fileNames) {
		Rendered(title);
		Rendered(filter);
		if (FilesToOpen is null) {
			fileNames = null;
			return false;
		}
		fileNames = FilesToOpen;
		return true;
	}

	public bool TrySaveFile(string title, string filter, [NotNullWhen(true)] out string? fileName) {
		Rendered(title);
		Rendered(filter);
		if (FileToSave is null) {
			fileName = null;
			return false;
		}
		fileName = FileToSave;
		return true;
	}
}
