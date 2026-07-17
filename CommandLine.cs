using System.Text;
using System.Text.RegularExpressions;

namespace PatTech.Utils;

public class CommandLine {
	public string Default;
	public char Short;
	public string Long;
	public string Description;
	public string Value;
	public bool Active;

	public CommandLine(char Short) : this(Short, null, "", "") { }
	public CommandLine(char Short, string Default) : this(Short, Default, "", "") { }
	public CommandLine(char Short, string Long, string Description) : this(Short, null, Long, Description) { }
	public CommandLine(char Short, string Default, string Long, string Description) {
		this.Default = Default;
		this.Short = Short;
		this.Long = Long;
		this.Description = Description;
		Value = null;
		Active = false;
	}

	public static void Parse(string[] args, params CommandLine[] template) {
		bool help = false;
		foreach (string arg in args) {
			if (arg.StartsWith("--")) {
				if (arg == "--help") help = true;
				foreach (CommandLine aa in template) {
					if (aa.Long == "") continue;
					Match m = Regex.Match(arg, "^--" + Regex.Escape(aa.Long) + "=(.*)$");
					if (m.Success) {
						aa.Active = true;
						aa.Value = m.Groups[1].ToString();
						break;
					}
				}
			}
			else if (arg[0] == '-') {
				if (arg == "-?") help = true;
				foreach (CommandLine aa in template) {
					if (arg.StartsWith("-" + aa.Short)) {
						aa.Active = true;
						if (arg.Length > 2) aa.Value = arg.Substring(2, arg.Length - 2);
						break;
					}
				}
			}
			else {
				if (arg == "/?") help = true;
				foreach (CommandLine aa in template) {
					if (aa.Short == '\0') {
						aa.Active = true;
						aa.Value = arg;
						break;
					}
				}
			}
		}
		foreach (CommandLine aa in template) {
			if (!aa.Active) aa.Value = aa.Default;
		}
		if (help) {
			//TODO: this feature is untested
			Console.WriteLine("Usage: ");
			foreach (CommandLine aa in template) {
				Console.WriteLine(" -{0}{1} {2}", aa.Short, aa.Long == null
					? new String(' ', 22) : (", --" + Pad(aa.Long, 18)), Wrap(aa.Description, 54, 20, 60));
				if (aa.Default != null) Console.WriteLine(new String(' ', 18) + "Default: " + aa.Default);
			}
		}
	}

	private static string Pad(string s, int l) {
		if (s.Length >= l) return s;
		if (l < 0) {
			return s + new string(' ', l - s.Length);
		}
		else {
			return new string(' ', -l - s.Length) + s;
		}
	}

	private static string Wrap(string s, int w1, int d, int w2) {
		//TODO: this function is untested
		StringBuilder sb = new StringBuilder();
		int cut, length, stop = 0, width = w1;
		do {
			length = 0;
			for (cut = Math.Min(stop + width, s.Length - 1); cut > stop; --cut) {
				if (char.IsWhiteSpace(s[cut])) ++length;
				else if (length > 0) break;
			}
			if (cut == stop) cut = Math.Min(stop + width, s.Length - 1);
			sb.Append(s.Substring(stop, cut - stop));
			stop = cut + length;
			width = w2;
			if (stop < s.Length - 1) {
				sb.AppendLine();
				sb.Append(' ', d);
			}
			else break;
		} while (true);
		return sb.ToString();
	}
}
