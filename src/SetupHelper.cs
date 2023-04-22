using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

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
	 * Check for required privileges (access to ESP and EFI vars).
	 *
	 * @return True, if the file was successfully copied.
	 */
	public static bool HasPrivileges() {
		try {
			var id = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(id);
			var admin = WindowsBuiltInRole.Administrator;
			// FIXME: Check access to ESP as well.
			if (!principal.IsInRole(admin)) {
				return false;
			}
			Efi.EnablePrivilege();
			return true;
		} catch {
		}
		return false;
	}

	/**
	 * Run another process as admin, return the exit code.
	 *
	 * @param app Path to the application.
	 * @param args The argument string.
	 */
	public static int RunElevated(string app, string args) {
		ProcessStartInfo startInfo = new ProcessStartInfo(app);
		startInfo.Arguments = args;
		startInfo.Verb = "runas";
		startInfo.UseShellExecute = true;
		Process p = Process.Start(startInfo);
		p.WaitForExit();
		return p.ExitCode;
	}
}
