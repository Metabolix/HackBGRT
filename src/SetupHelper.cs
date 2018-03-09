using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Linq;

/**
 * Helper functions.
 */
public class SetupHelper {
	/**
	 * Start another process.
	 *
	 * @param app Path to the application.
	 * @param args The argument string.
	 * @return The started process.
	 */
	public static Process StartProcess(string app, string args) {
		try {
			var info = new ProcessStartInfo(app, args);
			info.UseShellExecute = false;
			return Process.Start(info);
		} catch {
			return null;
		}
	}

	/**
	 * Execute another process, return the output.
	 *
	 * @param app Path to the application.
	 * @param args The argument string.
	 * @param nullOnFail If set, return null if the program exits with a code other than 0, even if there was some output.
	 * @return The output, or null if the execution failed.
	 */
	public static string Execute(string app, string args, bool nullOnFail) {
		try {
			var info = new ProcessStartInfo(app, args);
			info.UseShellExecute = false;
			info.RedirectStandardOutput = true;
			var p = Process.Start(info);
			string output = p.StandardOutput.ReadToEnd();
			p.WaitForExit();
			return (nullOnFail && p.ExitCode != 0) ? null : output;
		} catch {
			return null;
		}
	}

	/**
	 * Copy a file, overwrite by default.
	 *
	 * @param src The source path.
	 * @param dest The destination path.
	 * @return True, if the file was successfully copied.
	 */
	public static bool Copy(string src, string dest) {
		try {
			File.Copy(src, dest, true);
			return true;
		} catch {
			return false;
		}
	}

	/**
	 * The Main program.
	 *
	 * @param args The arguments.
	 */
	public static void Main(string[] args) {
		#if GIT_DESCRIBE
			Console.WriteLine("HackBGRT installer version: {0}", GIT_DESCRIBE.data);
		#else
			Console.WriteLine("HackBGRT installer version: unknown; not an official release?");
		#endif
		var self = Assembly.GetExecutingAssembly().Location;
		try {
			// Relaunch as admin, if needed.
			var id = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(id);
			var admin = WindowsBuiltInRole.Administrator;
			if (!principal.IsInRole(admin) && !args.Contains("no-elevate")) {
				ProcessStartInfo startInfo = new ProcessStartInfo(self);
				startInfo.Arguments = "no-elevate " + String.Join(" ", args);
				startInfo.Verb = "runas";
				startInfo.UseShellExecute = true;
				Process p = Process.Start(startInfo);
				p.WaitForExit();
				Environment.ExitCode = p.ExitCode;
				return;
			}
			Efi.EnablePrivilege();
		} catch {
			Console.WriteLine("This installer needs to be run as administrator!");
			Console.WriteLine("Press any key to quit.");
			Console.ReadKey();
			Environment.ExitCode = 1;
			return;
		}
		Setup.RunSetup(Path.GetDirectoryName(self), args);
	}
}
