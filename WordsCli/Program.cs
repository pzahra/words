using PatTech.Localization.Cli;
using System.Text;

//a redirected stream speaks UTF-8 with \n, whatever the console's code page; a console keeps its own
var utf8 = new UTF8Encoding(false);
TextReader input = Console.IsInputRedirected ? new StreamReader(Console.OpenStandardInput(), utf8) : Console.In;
TextWriter output = Console.IsOutputRedirected ? new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" } : Console.Out;
TextWriter error = Console.IsErrorRedirected ? new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true, NewLine = "\n" } : Console.Error;
return WordsCommand.Run(args, input, output, error);
