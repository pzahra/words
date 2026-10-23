using System.Diagnostics;

namespace PatTech.Text.Ini.Tests;

//the recovery tests' helper process (SPEC: Recovery, by a process killed at each step):
//`commit <file> <step> <value>` sets [s] x to the value and commits, `open <file> <step>` opens
//the file, settling its log; either is killed outright at the step it names
internal static class Program {
	public static int Main(string[] args) {
		IniFiles.Step = step => {
			if (step == args[2]) {
				Process.GetCurrentProcess().Kill();
			}
		};
		using IniTransaction file = IniTransaction.Open(args[1], IniDialect.Sections(new IniRules { LineBreak = "\n" }));
		if (args[0] == "commit") {
			file.Set("s", "x", args[3]);
			file.Commit();
		}
		return 0;
	}

	//runs the helper; its exit code, not 0 when it was killed
	public static int Run(params string[] args) {
		var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		start.ArgumentList.Add(typeof(Program).Assembly.Location);
		foreach (string arg in args) {
			start.ArgumentList.Add(arg);
		}
		using Process process = Process.Start(start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		string error = process.StandardError.ReadToEnd();
		if (!process.WaitForExit(60_000)) {
			process.Kill();
			throw new TimeoutException($"The helper never ended: {string.Join(' ', args)}");
		}
		_ = output.Result;
		if (process.ExitCode == 0 && error.Length > 0) {
			throw new InvalidOperationException(error);
		}
		return process.ExitCode;
	}
}
