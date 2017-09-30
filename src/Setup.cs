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
		Console.WriteLine("EFI System Partition location is " + Esp.Path);
	}

	/**
	 * Boot loader type (MS/HackBGRT).
	 */
	public enum BootLoaderType {
		Unknown,
		MS,
		Own
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
					Type = BootLoaderType.Own;
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
	 * Install files and replace the MsLoader with our own.
	 */
	protected void Install() {
		try {
			if (!Directory.Exists(Destination)) {
				Directory.CreateDirectory(Destination);
			}
		} catch {
			throw new SetupException("Failed to copy files to ESP!");
		}
		if (MsLoader.Type == BootLoaderType.MS) {
			if (!MsLoaderBackup.ReplaceWith(MsLoader)) {
				throw new SetupException("Failed to backup MS boot loader!");
			}
		}
		if (MsLoaderBackup.Type != BootLoaderType.MS) {
			// Duplicate check, but better to be sure...
			throw new SetupException("Failed to detect MS boot loader!");
		}
		if (!NewLoader.ReplaceWith(NewLoaderSource)) {
			throw new SetupException("Couldn't copy new HackBGRT to ESP.");
		}
		if (!File.Exists(Config)) {
			Copy(Source + "\\config.txt", Config);
		}
		if (!File.Exists(Splash)) {
			Copy(Source + "\\splash.bmp", Splash);
		}
		Configure();
		if (!MsLoader.ReplaceWith(NewLoader)) {
			MsLoader.ReplaceWith(MsLoaderBackup);
			throw new SetupException("Couldn't copy new HackBGRT over the MS loader (bootmgfw.efi).");
		}
		Console.WriteLine("HackBGRT is now installed.");
	}

	/**
	 * Configure HackBGRT.
	 */
	protected void Configure() {
		// Open config.txt in notepad.
		Console.WriteLine("Check the configuration in " + Config + ".");
		Console.WriteLine(" - Use the supplied config.txt as reference.");
		Console.WriteLine(" - Be sure to check for any format changes if updating!");
		try {
			StartProcess("notepad", Config).WaitForExit();
		} catch {
			Console.WriteLine("Editing config.txt with notepad failed!");
		}
		Console.WriteLine();

		// Open splash.bmp in mspaint.
		Console.WriteLine("Draw or copy your preferred image to " + Splash + ".");
		try {
			StartProcess("mspaint", Splash).WaitForExit();
		} catch {
			Console.WriteLine("Editing splash.bmp with mspaint failed!");
		}
		Console.WriteLine();
	}

	/**
	 * Disable HackBGRT, restore the original boot loader.
	 */
	protected void Disable() {
		if (MsLoader.Type != BootLoaderType.MS) {
			if (!MsLoader.ReplaceWith(MsLoaderBackup)) {
				throw new SetupException("Couldn't restore the old MS loader.");
			}
		}
		Console.WriteLine(MsLoader.Path + " has been restored.");
	}

	/**
	 * Uninstall HackBGRT completely.
	 */
	protected void Uninstall() {
		try {
			Directory.Delete(Destination, true);
			Console.WriteLine("HackBGRT has been removed.");
		} catch {
			throw new SetupException("The directory " + Destination + " couldn't be removed.");
		}
	}

	/**
	 * Check Secure Boot status and inform the user.
	 */
	public static void HandleSecureBoot() {
		int secureBoot = EfiGetSecureBootStatus();
		if (secureBoot == 0) {
			Console.WriteLine("Secure Boot is disabled, good!");
		} else {
			if (secureBoot == 1) {
				Console.WriteLine("Secure Boot is probably enabled.");
			} else {
				Console.WriteLine("Secure Boot status could not be determined.");
			}
			Console.WriteLine("It's very important to disable Secure Boot before installing.");
			Console.WriteLine("Otherwise your machine may become unbootable.");
			Console.WriteLine("Choose action (press a key):");
			Console.WriteLine(" I = Install anyway; THIS MAY BE DANGEROUS!");
			Console.WriteLine(" C = Cancel");
			var k = Console.ReadKey().Key;
			Console.WriteLine();
			if (k == ConsoleKey.I) {
				Console.WriteLine("Continuing. THIS MAY BE DANGEROUS!");
			} else {
				Console.WriteLine("Aborting because of Secure Boot.");
				throw new ExitSetup(1);
			}
		}
	}

	/**
	 * Check if Secure Boot is enabled.
	 *
	 * @return 0 for disabled, 1 for enabled, other for unknown.
	 */
	public static int EfiGetSecureBootStatus() {
		try {
			return (int) Registry.GetValue(
				"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State",
				"UEFISecureBootEnabled",
				-1
			);
		} catch (Exception) {
			return -1;
		}
	}

	protected string Source, Destination, Config, Splash;
	protected BootLoaderInfo MsLoader, MsLoaderBackup, NewLoader, NewLoaderSource;
	protected string EfiArch;

	/**
	 * Initialize information for the Setup.
	 */
	protected Setup(string source, string esp) {
		Source = source;
		Destination = esp + "\\EFI\\HackBGRT";
		Config = Destination + "\\config.txt";
		Splash = Destination + "\\splash.bmp";
		MsLoaderBackup = new BootLoaderInfo(Destination + "\\bootmgfw-original.efi");
		MsLoader = new BootLoaderInfo(esp + "\\EFI\\Microsoft\\Boot\\bootmgfw.efi");
		if (MsLoader.Type == BootLoaderType.MS) {
			EfiArch = MsLoader.Arch;
		} else if (MsLoaderBackup.Type == BootLoaderType.MS) {
			EfiArch = MsLoaderBackup.Arch;
		} else {
			throw new SetupException("Failed to detect MS boot loader!");
		}
		string loaderName = "boot" + EfiArch + ".efi";
		NewLoaderSource = new BootLoaderInfo(Source + "\\" + loaderName);
		NewLoader = new BootLoaderInfo(Destination + "\\" + loaderName);
		if (!NewLoaderSource.Exists) {
			throw new SetupException("Couldn't find required files! Missing: " + NewLoaderSource.Path);
		}
	}

	/**
	 * Ask for user's choice and install/uninstall.
	 */
	protected void HandleUserAction() {
		bool isEnabled = MsLoader.Type == BootLoaderType.Own;
		bool isInstalled = NewLoader.Type == BootLoaderType.Own;

		if (isEnabled) {
			Console.WriteLine("HackBGRT is currently enabled.");
		} else {
			if (isInstalled) {
				Console.WriteLine("HackBGRT is currently disabled.");
			} else {
				Console.WriteLine("HackBGRT is currently not installed.");
			}
		}
		Console.WriteLine();
		Console.WriteLine("Choose action (press a key):");
		Console.WriteLine(" I = install, upgrade, repair, modify...");
		if (isEnabled) {
			Console.WriteLine(" D = disable, restore the original boot loader");
		}
		if (isInstalled) {
			Console.WriteLine(" R = remove completely; delete all HackBGRT files and images");
		}
		Console.WriteLine(" C = cancel");

		var k = Console.ReadKey().Key;
		Console.WriteLine();
		if (k == ConsoleKey.I) {
			Install();
		} else if ((isEnabled && k == ConsoleKey.D) || (isInstalled && k == ConsoleKey.R)) {
			if (isEnabled) {
				Disable();
			}
			if (k == ConsoleKey.R) {
				Uninstall(); // NOTICE: It's imperative to Disable() first!
			}
		} else if (k == ConsoleKey.C) {
			throw new ExitSetup(1);
		} else {
			throw new SetupException("Invalid choice!");
		}
	}

	/**
	 * Run the setup.
	 *
	 * @param source Path to the installation files.
	 */
	public static void RunSetup(string source) {
		try {
			InitEspPath();
			HandleSecureBoot();
			var s = new Setup(source, Esp.Path);
			s.HandleUserAction();
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
