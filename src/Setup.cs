using System;
using System.IO;
using Microsoft.Win32;

/**
 * HackBGRT Setup.
 */
public class Setup: SetupHelper {
	/**
	 * The custom exception class for expected exceptions.
	 */
	public class SetupException: Exception {
		public SetupException(string msg): base(msg) {
		}
	}

	/**
	 * A custom exception class for simply exiting the application.
	 */
	public class ExitSetup: Exception {
		public readonly int Code;
		public ExitSetup(int code) {
			Code = code;
		}
	}

	/**
	 * Find or mount or manually choose the EFI System Partition.
	 */
	public static void InitEspPath() {
		if (!Esp.Find() && !Esp.Mount()) {
			Console.WriteLine("EFI System Partition was not found.");
			Console.WriteLine("Press enter to exit, or give ESP path here: ");
			string s = Console.ReadLine();
			if (s.Length == 1) {
				s = s + ":";
			}
			if (!Esp.TryPath(s, true)) {
				Console.WriteLine("That's not a valid ESP path!");
			}
		}
		if (Esp.Path == null) {
			throw new SetupException("EFI System Partition was not found.");
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
	public class BootLoaderInfo {
		public string Path;
		public bool Exists;
		public BootLoaderType Type;
		public string Arch;

		/**
		 * Constructor. Also recognizes the boot loader.
		 */
		public BootLoaderInfo(string path) {
			Path = path;
			Detect();
		}

		/**
		 * Recognize the boot loader type (MS/HackBGRT) and architecture.
		 */
		public void Detect() {
			Exists = false;
			Type = BootLoaderType.Unknown;
			Arch = null;
			try {
				byte[] data = File.ReadAllBytes(Path);
				Exists = true;
				string tmp = System.Text.Encoding.ASCII.GetString(data);
				if (tmp.IndexOf("HackBGRT") >= 0) {
					Type = BootLoaderType.HackBGRT;
				} else if (tmp.IndexOf("Microsoft Corporation") >= 0) {
					Type = BootLoaderType.MS;
				}
				switch (BitConverter.ToUInt16(data, BitConverter.ToInt32(data, 0x3c) + 4)) {
					case 0x014c: Arch = "ia32"; break;
					case 0x0200: Arch = "ia64"; break;
					case 0x8664: Arch = "x64"; break;
				}
			} catch {
			}
		}

		/**
		 * Replace this boot loader with another.
		 *
		 * @param other The new boot loader.
		 * @return true if the replacement was successful.
		 */
		public bool ReplaceWith(BootLoaderInfo other) {
			if (!other.Exists || !Copy(other.Path, Path)) {
				return false;
			}
			Exists = other.Exists;
			Type = other.Type;
			Arch = other.Arch;
			return true;
		}
	}

	/**
	 * Backup the original boot loader.
	 *
	 * @param hackbgrt Path to the HackBGRT directory on the ESP.
	 * @param msloader Information of the MS boot loader to backup.
	 * @return Information about the boot loader backup.
	 */
	public static BootLoaderInfo Backup(string hackbgrt, BootLoaderInfo msloader) {
		BootLoaderInfo msbackup = new BootLoaderInfo(hackbgrt + "\\bootmgfw-original.efi");
		try {
			if (!Directory.Exists(hackbgrt)) {
				Directory.CreateDirectory(hackbgrt);
			}
			if (msloader.Type == BootLoaderType.HackBGRT) {
				Console.WriteLine(msloader.Path + " already contains a version of HackBGRT, skipping backup.");
			} else if (msloader.Type == BootLoaderType.MS) {
				// Overwrite any previous backup, the file might have changed.
				if (!msbackup.ReplaceWith(msloader)) {
					throw new SetupException("Couldn't backup the original bootmgfw.efi.");
				}
			} else {
				Console.WriteLine(msloader.Path + " doesn't look right, skipping backup.");
			}
		} catch {
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
	 * @param msloader Information of the MS boot loader to replace.
	 * @param msbackup Information of the boot loader backup.
	 */
	public static void Install(string src, string hackbgrt, BootLoaderInfo msloader, BootLoaderInfo msbackup) {
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
	 * @param msloader Information of the MS boot loader to replace.
	 * @param msbackup Information of the boot loader backup.
	 */
	public static void Enable(string hackbgrt, BootLoaderInfo msloader, BootLoaderInfo msbackup) {
		string loaderName = "boot" + msbackup.Arch + ".efi";
		BootLoaderInfo loaderInst = new BootLoaderInfo(hackbgrt + "\\" + loaderName);
		if (!loaderInst.Exists) {
			throw new SetupException(loaderInst + " doesn't exist.");
		}
		if (!msloader.ReplaceWith(loaderInst)) {
			msloader.ReplaceWith(msbackup);
			throw new SetupException("Couldn't install the new bootmgfw.efi.");
		}
		Console.WriteLine("HackBGRT is now enabled.");
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
	public static void Disable(string hackbgrt, BootLoaderInfo msloader) {
		BootLoaderInfo msbackup = new BootLoaderInfo(hackbgrt + "\\bootmgfw-original.efi");
		if (msloader.Type == BootLoaderType.MS) {
			Console.WriteLine(msloader + " is already ok.");
		} else {
			if (!msbackup.Exists) {
				throw new SetupException("Missing the original bootmgfw.efi.");
			}
			if (!msloader.ReplaceWith(msbackup)) {
				throw new SetupException("Failed to restore the original bootmgfw.efi.");
			}
			Console.WriteLine(msloader.Path + " has been restored.");
		}
	}

	/**
	 * Uninstall HackBGRT, restore the original boot loader.
	 *
	 * @param hackbgrt Where HackBGRT is installed.
	 * @param msloader Where to restore the MS boot loader.
	 */
	public static void Uninstall(string hackbgrt, BootLoaderInfo msloader) {
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
	public static void RunSetup(string src) {
		try {
			InitEspPath();
			int secureBoot = -1;
			try {
				secureBoot = (int) Registry.GetValue(
					"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State",
					"UEFISecureBootEnabled",
					-1
				);
			} catch (Exception) {
			}
			if (secureBoot != 0) {
				if (secureBoot == 1) {
					Console.WriteLine("Secure Boot is enabled.");
					Console.WriteLine("HackBGRT doesn't work with Secure Boot.");
					Console.WriteLine("If you install HackBGRT, your machine may become unbootable,");
					Console.WriteLine("unless you manually disable Secure Boot.");
				} else {
					Console.WriteLine("Could not determine Secure Boot status.");
					Console.WriteLine("Your system may be incompatible with HackBGRT.");
					Console.WriteLine("If you install HackBGRT, your machine may become unbootable.");
				}
				Console.WriteLine("Do you still wish to continue? [Y/N]");
				var k = Console.ReadKey().Key;
				Console.WriteLine();
				if (k == ConsoleKey.Y) {
					Console.WriteLine("Continuing. THIS IS DANGEROUS!");
				} else if (k == ConsoleKey.N) {
					Console.WriteLine("Aborting.");
					return;
				} else {
					throw new SetupException("Invalid choice!");
				}
			}
			string hackbgrt = Esp.Path + "\\EFI\\HackBGRT";
			BootLoaderInfo msloader = new BootLoaderInfo(Esp.Path + "\\EFI\\Microsoft\\Boot\\bootmgfw.efi");
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
		} catch (ExitSetup e) {
			Environment.ExitCode = e.Code;
		} catch (SetupException e) {
			Console.WriteLine("Error: {0}", e.Message);
			Environment.ExitCode = 1;
		} catch (Exception e) {
			Console.WriteLine("Unexpected error!\n{0}", e.ToString());
			Console.WriteLine("If this is the most current release, please report this bug.");
			Environment.ExitCode = 1;
		}
		Console.WriteLine("Press any key to quit.");
		Console.ReadKey();
	}
}
