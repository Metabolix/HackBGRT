using System;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

/**
 * HackBGRT Setup.
 */
public class Setup {
	/**
	 * The custom exception class for expected exceptions.
	 */
	public class SetupException: Exception {
		public SetupException(string msg): base(msg) {
		}
	}

	/**
	 * EFI System Partition mounter.
	 */
	public class ESP {
		/** EFI System Partition mount point. */
		public string Path = null;

		/** Was the ESP mounted by this instance? */
		private bool Mounted = false;

		/**
		 * Mount the EFI System Partition, or determine an existing mount point.
		 */
		public void Mount() {
			try {
				// Match "The EFI System Partition is mounted at E:\" with some language support.
				var re = new Regex(" EFI[^\n]*(?:\n[ \t]*)?([A-Z]:)\\\\");
				Path = re.Match(Execute("mountvol", "", false)).Groups[1].Captures[0].Value;
				return;
			} catch {
			}
			// Try to mount somewhere.
			for (char c = 'A'; c <= 'Z'; ++c) {
				if (Execute("mountvol", c + ": /S", true) != null) {
					Console.WriteLine("The EFI System Partition is mounted in " + c + ":\\");
					Path = c + ":";
					Mounted = true;
					return;
				}
			}
			throw new SetupException("The EFI System Partition is not mounted.");
		}

		/**
		 * Unmount the EFI System Partition, if it was previously mounted by this instance.
		 */
		public void Unmount() {
			if (Mounted) {
				Execute("mountvol", Path + " /D", true);
				Mounted = false;
				Path = null;
			}
		}
	}

	/**
	 * Boot loader type (MS/HackBGRT).
	 */
	public enum BootLoaderType {
		Unknown,
		MS,
		HackBGRT
	}

	/**
	 * Information about a boot loader: type and architecture.
	 */
	public struct BootLoaderInfo {
		public string Path;
		public bool Exists;
		public BootLoaderType Type;
		public string Arch;
	}

	/**
	 * Recognize the boot loader type (MS/HackBGRT) and architecture.
	 *
	 * @param path Path to the boot loader.
	 * @return Information about the boot loader.
	 */
	public static BootLoaderInfo RecognizeLoader(string path) {
		BootLoaderInfo info;
		info.Path = path;
		info.Exists = false;
		info.Type = BootLoaderType.Unknown;
		info.Arch = null;
		try {
			byte[] data = File.ReadAllBytes(path);
			info.Exists = true;
			string tmp = System.Text.Encoding.ASCII.GetString(data);
			if (tmp.IndexOf("HackBGRT") >= 0) {
				info.Type = BootLoaderType.HackBGRT;
			} else if (tmp.IndexOf("Microsoft Corporation") >= 0) {
				info.Type = BootLoaderType.MS;
			}
			switch (BitConverter.ToUInt16(data, BitConverter.ToInt32(data, 0x3c) + 4)) {
				case 0x014c: info.Arch = "ia32"; break;
				case 0x0200: info.Arch = "ia64"; break;
				case 0x8664: info.Arch = "x64"; break;
			}
		} catch {
		}
		return info;
	}

	/**
	 * Backup the original boot loader.
	 *
	 * @param hackbgrt Path to the HackBGRT directory on the ESP.
	 * @param msloader Path of the MS boot loader to backup.
	 * @return Information about the boot loader backup.
	 */
	public static BootLoaderInfo Backup(string hackbgrt, string msloader) {
		try {
			if (!Directory.Exists(hackbgrt)) {
				Directory.CreateDirectory(hackbgrt);
			}
			BootLoaderInfo info = RecognizeLoader(msloader);
			if (info.Type == BootLoaderType.HackBGRT) {
				Console.WriteLine(msloader + " already contains a version of HackBGRT, skipping backup.");
			} else if (info.Type == BootLoaderType.MS) {
				// Overwrite any previous backup, the file might have changed.
				Copy(msloader, hackbgrt + "\\bootmgfw-original.efi");
			} else {
				Console.WriteLine(msloader + " doesn't look right, skipping backup.");
			}
		} catch {
		}
		BootLoaderInfo msbackup = RecognizeLoader(hackbgrt + "\\bootmgfw-original.efi");
		if (!msbackup.Exists) {
			throw new SetupException("Couldn't backup the original bootmgfw.efi.");
		}
		if (msbackup.Arch == null || msbackup.Type != BootLoaderType.MS) {
			throw new SetupException("The boot loader backup (" + msbackup.Path + ") doesn't look like a supported MS boot loader");
		}
		return msbackup;
	}

	/**
	 * Install HackBGRT, copy files and replace the msloader with our own.
	 *
	 * @param src Path to the installation files.
	 * @param hackbgrt Path to the HackBGRT directory on the ESP.
	 * @param msloader Path of the MS boot loader to replace.
	 * @param msbackup Information of the boot loader backup.
	 */
	public static void Install(string src, string hackbgrt, string msloader, BootLoaderInfo msbackup) {
		string loaderName = "boot" + msbackup.Arch + ".efi";
		string loaderSrc = src + "\\" + loaderName;
		string loaderInst = hackbgrt + "\\" + loaderName;
		if (!File.Exists(loaderSrc)) {
			throw new SetupException(loaderName + " doesn't exist.");
		}
		Copy(loaderSrc, loaderInst);
		if (!File.Exists(hackbgrt + "\\config.txt")) {
			Copy(src + "\\config.txt", hackbgrt + "\\config.txt");
		}
		if (!File.Exists(hackbgrt + "\\splash.bmp")) {
			Copy(src + "\\splash.bmp", hackbgrt + "\\splash.bmp");
		}
		Enable(hackbgrt, msloader, msbackup);
		Configure(hackbgrt);
	}

	/**
	 * Enable HackBGRT, replace the msloader with our own.
	 *
	 * @param hackbgrt Path to the HackBGRT directory on the ESP.
	 * @param msloader Path of the MS boot loader to replace.
	 * @param msbackup Information of the boot loader backup.
	 */
	public static void Enable(string hackbgrt, string msloader, BootLoaderInfo msbackup) {
		string loaderName = "boot" + msbackup.Arch + ".efi";
		string loaderInst = hackbgrt + "\\" + loaderName;
		if (!File.Exists(loaderInst)) {
			throw new SetupException(loaderInst + " doesn't exist.");
		}
		if (!Copy(loaderInst, msloader)) {
			Copy(msbackup.Path, msloader);
			throw new SetupException("Couldn't install the new bootmgfw.efi.");
		}
		Console.WriteLine("HackBGRT is now enabled.");
		Console.WriteLine("Remember to disable Secure Boot, or HackBGRT will not boot.");
	}

	/**
	 * Configure HackBGRT.
	 *
	 * @param hackbgrt Path to the HackBGRT directory on the ESP.
	 */
	public static void Configure(string hackbgrt) {
		// Open config.txt in notepad.
		Console.WriteLine("Check the configuration in " + hackbgrt + "\\config.txt.");
		Console.WriteLine("Use the supplied config.txt as reference.");
		Console.WriteLine("Be sure to check for any format changes if updating!");
		try {
			StartProcess("notepad", hackbgrt + "\\config.txt").WaitForExit();
		} catch {
			Console.WriteLine("Editing config.txt with notepad failed! Edit it manually.");
		}

		// Open splash.bmp in mspaint.
		Console.WriteLine("Draw or copy your preferred image to splash.bmp.");
		try {
			StartProcess("mspaint", hackbgrt + "\\splash.bmp").WaitForExit();
		} catch {
			Console.WriteLine("Editing splash.bmp with mspaint failed! Edit it manually.");
		}
	}

	/**
	 * Disable HackBGRT, restore the original boot loader.
	 *
	 * @param hackbgrt Where HackBGRT is installed.
	 * @param msloader Where to restore the MS boot loader.
	 */
	public static void Disable(string hackbgrt, string msloader) {
		BootLoaderInfo info = RecognizeLoader(msloader);
		if (info.Type == BootLoaderType.MS) {
			Console.WriteLine(msloader + " is already ok.");
		} else {
			if (!File.Exists(hackbgrt + "\\bootmgfw-original.efi")) {
				throw new SetupException("Missing the original bootmgfw.efi.");
			}
			if (!Copy(hackbgrt + "\\bootmgfw-original.efi", msloader)) {
				throw new SetupException("Failed to restore the original bootmgfw.efi.");
			}
			Console.WriteLine(msloader + " has been restored.");
		}
	}

	/**
	 * Uninstall HackBGRT, restore the original boot loader.
	 *
	 * @param hackbgrt Where HackBGRT is installed.
	 * @param msloader Where to restore the MS boot loader.
	 */
	public static void Uninstall(string hackbgrt, string msloader) {
		Disable(hackbgrt, msloader);
		try {
			Directory.Delete(hackbgrt, true);
			Console.WriteLine("HackBGRT has been removed.");
		} catch {
			Console.WriteLine("The directory " + hackbgrt + " couldn't be removed.");
		}
	}

	/**
	 * Run the setup.
	 *
	 * @param src Path to the installation files.
	 */
	protected static void RunSetup(string src) {
		ESP esp = new ESP();
		try {
			esp.Mount();
			string hackbgrt = esp.Path + "\\EFI\\HackBGRT";
			string msloader = esp.Path + "\\EFI\\Microsoft\\Boot\\bootmgfw.efi";
			if (!Directory.Exists(hackbgrt)) {
				Install(src, hackbgrt, msloader, Backup(hackbgrt, msloader));
			} else {
				Console.WriteLine("Choose action (press a key):");
				Console.WriteLine(" I = install, upgrade, fix installation");
				Console.WriteLine(" C = configure (edit config.txt and splash.bmp)");
				Console.WriteLine(" E = enable (after disabling); if in doubt, choose 'I' instead");
				Console.WriteLine(" D = disable, restore the original boot loader");
				Console.WriteLine(" R = remove completely; fully delete " + hackbgrt);
				var k = Console.ReadKey().Key;
				Console.WriteLine();
				if (k == ConsoleKey.I) {
					Install(src, hackbgrt, msloader, Backup(hackbgrt, msloader));
				} else if (k == ConsoleKey.C) {
					Configure(hackbgrt);
				} else if (k == ConsoleKey.E) {
					Enable(hackbgrt, msloader, Backup(hackbgrt, msloader));
				} else if (k == ConsoleKey.D) {
					Disable(hackbgrt, msloader);
				} else if (k == ConsoleKey.R) {
					Uninstall(hackbgrt, msloader);
				} else {
					throw new SetupException("Invalid choice!");
				}
			}
		} catch (SetupException e) {
			Console.WriteLine("Error: {0}", e.Message);
			Environment.ExitCode = 1;
		} catch (Exception e) {
			Console.WriteLine("Unexpected error!\n{0}", e.ToString());
			Console.WriteLine("If this is the most current release, please report this bug.");
			Environment.ExitCode = 1;
		} finally {
			esp.Unmount();
		}
	}

	/**
	 * Start another process.
	 *
	 * @param app Path to the application.
	 * @param args The argument string.
	 * @return The started process.
	 */
	protected static Process StartProcess(string app, string args) {
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
	protected static string Execute(string app, string args, bool nullOnFail) {
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
		var self = Assembly.GetExecutingAssembly().Location;
		RunSetup(Path.GetDirectoryName(self));
		Console.WriteLine("Press any key to quit.");
		Console.ReadKey();
	}
}
