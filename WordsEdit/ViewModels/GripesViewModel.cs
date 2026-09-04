using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     A list of gripes to read and copy: what a preview or a file load
///     complained about. The text is one line per gripe, so it pastes well.
/// </summary>
public class GripesViewModel : DialogViewModel {
	private readonly string title;
	public override string Title => title;
	public IReadOnlyList<string> Gripes { get; }
	public string Text => string.Join(Environment.NewLine, Gripes);
	public ICommand CloseCommand { get; }

	public GripesViewModel([Localized] string title, IReadOnlyList<string> gripes) {
		this.title = title;
		Gripes = gripes;
		CloseCommand = new DelegateCommand(Close);
	}
}
