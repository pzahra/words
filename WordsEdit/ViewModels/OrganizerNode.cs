using PatTech.Localization.Authoring;

namespace WordsEdit.ViewModels;

/// <summary>
///     The tree presentation of a freeform <c>;</c> comment run. The base class
///     is presentation-only, delegating <see cref="Text"/> to external storage —
///     used for the file preamble, which the writer emits above the language
///     table rather than from the tree. Editing the node edits the comment;
///     deleting it deletes the comment.
/// </summary>
public class OrganizerNode : KeyNode {
	private string text;
	private readonly Func<string>? read;
	private readonly Action<string>? write;

	/// <summary>A standalone organizer owning its own text.</summary>
	protected OrganizerNode(string fullLabel, string text)
			: base(";", fullLabel) {
		this.text = text;
	}

	/// <summary>An organizer presenting text that lives elsewhere.</summary>
	public OrganizerNode(string fullLabel, Func<string> read, Action<string> write)
			: base(";", fullLabel) {
		text = "";
		this.read = read;
		this.write = write;
	}

	public string Text {
		get => read?.Invoke() ?? text;
		set {
			if (Text == value) {
				return;
			}
			if (write is not null) {
				write(value);
			}
			else {
				text = value;
			}
			AffectProperty(nameof(Text));
			AffectProperty(nameof(Caption));
		}
	}

	/// <summary>The first line of the comment, for the tree row.</summary>
	public string Caption {
		get {
			var trimmed = Text.TrimStart();
			int newline = trimmed.IndexOf('\n');
			var line = (newline >= 0 ? trimmed[..newline] : trimmed).Trim();
			return line == "" ? "(comment)" : line;
		}
	}
}

/// <summary>
///     A standalone comment in the tree: the writer emits it in place
///     (<see cref="ICommentNode"/>), so its anchor is wherever it stands —
///     interject a key between the comment and its original block, or delete
///     the block and the comment rides above the next one.
/// </summary>
public class CommentNode : OrganizerNode, ICommentNode {
	public CommentNode(string fullLabel, string text = "")
			: base(fullLabel, text) {
	}
}
