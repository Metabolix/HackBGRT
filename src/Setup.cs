using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

/**
 * HackBGRT Setup.
 */
public class Setup: SetupHelper {
	/** @var Version of the setup program. */
	const string Version =
		#if GIT_DESCRIBE
			GIT_DESCRIBE.data
		#else
			"unknown; not an official release?"
		#endif
	;

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

	/** @var The privileged actions. */
	protected static readonly string[] privilegedActions = new string[] {
		"install",
		"allow-secure-boot",
		"enable-overwrite", "disable-overwrite",
		"disable",
		"uninstall",
	};

	/** @var The target directory. */
	protected string InstallPath;

	/** @var The backup MS boot loader path. */
	protected string BackupLoaderPath {
		get {
			return Path.Combine(InstallPath, "bootmgfw-original.efi");
		}
	}

	/** @var The EFI architecture identifier. */
	protected string EfiArch;

	/** @var Run in batch mode? */
	protected bool Batch;

	/**
	 * Find or mount or manually choose the EFI System Partition.
	 */
	protected void InitEspPath() {
		if (Esp.Location == null && !Esp.Find() && !Esp.Mount() && !Batch) {
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
		if (Esp.Location == null) {
			throw new SetupException("EFI System Partition was not found.");
		}
		Console.WriteLine("EFI System Partition location is " + Esp.Location);
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
				if (tmp.IndexOf("HackBGRT") >= 0 || tmp.IndexOf("HackBgrt") >= 0) {
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
	 * Install a single file.
	 */
	protected void InstallFile(string name, string newName = null) {
		newName = Path.Combine(InstallPath, newName == null ? name : newName);
		if (!Copy(name, newName)) {
			throw new SetupException($"Failed to install file {name} to {newName}.");
		}
		Console.WriteLine($"Installed {name} to {newName}.");
	}

	/**
	 * Install a single image file, with conversion to 24-bit BMP.
	 */
	protected void InstallImageFile(string name) {
		var newName = Path.Combine(InstallPath, name);
		// Load the image to check if it's valid.
		Bitmap img;
		try {
			img = new Bitmap(name);
		} catch {
			throw new SetupException($"Failed to load image {name}.");
		}
		// Copy the bitmap into an empty 24-bit image (required by EFI).
		using (Bitmap bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb)) {
			using (Graphics g = Graphics.FromImage(bmp)) {
				g.DrawImageUnscaledAndClipped(img, new Rectangle(Point.Empty, img.Size));
			}
			try {
				bmp.Save(newName, ImageFormat.Bmp);
			} catch {
				throw new SetupException($"Failed to install image {name} to {newName}.");
			}
		}
		Console.WriteLine($"Installed image {name} to {newName}.");
	}

	/**
	 * Install files to ESP.
	 */
	protected void InstallFiles() {
		try {
			if (!Directory.Exists(InstallPath)) {
				Directory.CreateDirectory(InstallPath);
			}
		} catch {
			throw new SetupException("Failed to copy files to ESP!");
		}

		InstallFile("config.txt");
		foreach (var line in File.ReadAllLines("config.txt").Where(s => s.StartsWith("image="))) {
			var delim = "path=\\EFI\\HackBGRT\\";
			var i = line.IndexOf(delim);
			if (i > 0) {
				InstallImageFile(line.Substring(i + delim.Length));
			}
		}
		InstallFile($"boot{EfiArch}.efi", "loader.efi");
		Console.WriteLine($"HackBGRT has been copied to {InstallPath}.");
	}

	/**
	 * Enable HackBGRT by overwriting the MS boot loader.
	 */
	protected void OverwriteMsLoader() {
		var MsLoader = new BootLoaderInfo(Esp.MsLoaderPath);
		var NewLoader = new BootLoaderInfo(Path.Combine(InstallPath, "loader.efi"));
		var MsLoaderBackup = new BootLoaderInfo(BackupLoaderPath);
		if (MsLoader.Type == BootLoaderType.MS) {
			if (!MsLoaderBackup.ReplaceWith(MsLoader)) {
				throw new SetupException("Failed to backup MS boot loader!");
			}
		}
		if (MsLoaderBackup.Type != BootLoaderType.MS) {
			// Duplicate check, but better to be sure...
			throw new SetupException("Failed to detect MS boot loader!");
		}
		if (!MsLoader.ReplaceWith(NewLoader)) {
			MsLoader.ReplaceWith(MsLoaderBackup);
			throw new SetupException("Couldn't copy new HackBGRT over the MS loader (bootmgfw.efi).");
		}
		Console.WriteLine($"Replaced {MsLoader.Path} with this app, stored backup in {MsLoaderBackup.Path}.");
	}

	/**
	 * Restore the MS boot loader if it was previously replaced.
	 */
	protected void RestoreMsLoader() {
		var MsLoader = new BootLoaderInfo(Esp.MsLoaderPath);
		if (MsLoader.Type == BootLoaderType.Own) {
			var MsLoaderBackup = new BootLoaderInfo(BackupLoaderPath);
			if (!MsLoader.ReplaceWith(MsLoaderBackup)) {
				throw new SetupException("Couldn't restore the old MS loader.");
			}
			Console.WriteLine($"{MsLoader.Path} has been restored.");
		}
	}

	/**
	 * Configure HackBGRT.
	 */
	protected void Configure() {
		Console.WriteLine("This setup program lets you edit just one image.");
		Console.WriteLine("Edit config.txt manually for advanced configuration.");

		// Open splash.bmp in mspaint.
		Console.WriteLine("Draw or copy your preferred image to splash.bmp.");
		try {
			StartProcess("mspaint", "splash.bmp").WaitForExit();
		} catch {
			Console.WriteLine("Editing splash.bmp with mspaint failed!");
			Console.WriteLine("Edit splash.bmp with your preferred editor.");
			Console.WriteLine("Press any key to continue.");
			Console.ReadKey();
		}
		Console.WriteLine();
	}

	/**
	 * Uninstall HackBGRT completely.
	 */
	protected void Uninstall() {
		RestoreMsLoader();
		try {
			Directory.Delete(InstallPath, true);
			Console.WriteLine("HackBGRT has been removed.");
		} catch {
			throw new SetupException($"The directory {InstallPath} couldn't be removed.");
		}
	}

	/**
	 * Check Secure Boot status and inform the user.
	 *
	 * @param allowSecureBoot Allow Secure Boot to be enabled?
	 */
	protected void HandleSecureBoot(bool allowSecureBoot) {
		int secureBoot = Efi.GetSecureBootStatus();
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
			if (Batch) {
				if (allowSecureBoot) {
					return;
				}
				throw new SetupException("Aborting because of Secure Boot.");
			}
			Console.WriteLine("Choose action (press a key):");
			bool canBootToFW = Efi.CanBootToFW();
			if (canBootToFW) {
				Console.WriteLine(" S = Enter EFI Setup to disable Secure Boot manually; requires reboot!");
			}
			Console.WriteLine(" I = Install anyway; THIS MAY BE DANGEROUS!");
			Console.WriteLine(" C = Cancel");
			var k = Console.ReadKey().Key;
			Console.WriteLine();
			if (k == ConsoleKey.I) {
				Console.WriteLine("Continuing. THIS MAY BE DANGEROUS!");
			} else if (canBootToFW && k == ConsoleKey.S) {
				Efi.SetBootToFW();
				Console.WriteLine();
				Console.WriteLine("Reboot your computer. You will then automatically enter the UEFI setup.");
				Console.WriteLine("Find and disable Secure Boot, then save and exit, then run this installer.");
				Console.WriteLine("Press R to reboot now, other key to exit.");
				var k2 = Console.ReadKey().Key;
				Console.WriteLine();
				if (k2 == ConsoleKey.R) {
					StartProcess("shutdown", "-f -r -t 1");
				}
				throw new ExitSetup(0);
			} else {
				throw new SetupException("Aborting because of Secure Boot.");
			}
		}
	}

	/**
	 * Initialize information for the Setup.
	 */
	protected void InitEspInfo() {
		InstallPath = Path.Combine(Esp.Location, "EFI", "HackBGRT");
		EfiArch = IntPtr.Size == 4 ? "ia32" : "x64";
		var MsLoader = new BootLoaderInfo(Esp.MsLoaderPath);
		if (MsLoader.Arch != null) {
			EfiArch = MsLoader.Arch;
		}
	}

	/**
	 * Ask for user's choice and install/uninstall.
	 */
	protected void ShowMenu() {
		Console.WriteLine();
		Console.WriteLine("Choose action (press a key):");
		Console.WriteLine(" I = install, enable, upgrade, repair, modify");
		Console.WriteLine(" F = install files only, don't enable");
		Console.WriteLine(" D = disable, restore the original boot loader");
		Console.WriteLine(" R = remove completely; delete all HackBGRT files and images");
		Console.WriteLine(" C = cancel");

		var k = Console.ReadKey().Key;
		Console.WriteLine();
		if (k == ConsoleKey.I || k == ConsoleKey.F) {
			Configure();
		}
		if (k == ConsoleKey.I) {
			RunPrivilegedActions(new string[] { "install", "enable-overwrite" });
		} else if (k == ConsoleKey.F) {
			RunPrivilegedActions(new string[] { "install" });
		} else if (k == ConsoleKey.D) {
			RunPrivilegedActions(new string[] { "disable" });
		} else if (k == ConsoleKey.R) {
			RunPrivilegedActions(new string[] { "uninstall" });
		} else if (k == ConsoleKey.C) {
			throw new ExitSetup(1);
		} else {
			throw new SetupException("Invalid choice!");
		}
	}

	/**
	 * Run privileged actions.
	 *
	 * @param actions The actions to run.
	 */
	protected void RunPrivilegedActions(IEnumerable<string> actions) {
		bool allowSecureBoot = false;
		foreach (var arg in actions) {
			Log($"Running action '{arg}'.");
			if (arg == "install") {
				InstallFiles();
			} else if (arg == "allow-secure-boot") {
				allowSecureBoot = true;
			} else if (arg == "enable-overwrite") {
				HandleSecureBoot(allowSecureBoot);
				OverwriteMsLoader();
			} else if (arg == "disable-overwrite") {
				RestoreMsLoader();
			} else if (arg == "disable") {
				RestoreMsLoader();
			} else if (arg == "uninstall") {
				Uninstall();
			} else {
				throw new SetupException($"Invalid action: '{arg}'!");
			}
			Console.WriteLine($"Completed action '{arg}' successfully.");
		}
	}

	/**
	 * The Main program.
	 *
	 * @param args The arguments.
	 */
	public static void Main(string[] args) {
		Console.WriteLine($"HackBGRT installer version: {Version}");
		var self = Assembly.GetExecutingAssembly().Location;
		Directory.SetCurrentDirectory(Path.GetDirectoryName(self));
		Environment.ExitCode = new Setup().Run(args);
	}

	/**
	 * Run the setup.
	 *
	 * @param args The arguments.
	 */
	protected int Run(string[] args) {
		Batch = args.Contains("batch");
		try {
			if (args.Contains("is-elevated") && !HasPrivileges()) {
				Console.WriteLine("This installer needs to be run as administrator!");
				return 1;
			}
			if (!HasPrivileges()) {
				var self = Assembly.GetExecutingAssembly().Location;
				return RunElevated(self, String.Join(" ", args.Prepend("is-elevated")));
			}
			InitEspPath();
			InitEspInfo();
			var actions = args.Where(s => privilegedActions.Contains(s));
			if (actions.Count() > 0) {
				RunPrivilegedActions(actions);
				return 0;
			}
			if (Batch) {
				throw new SetupException("No action specified!");
			}
			ShowMenu();
			return 0;
		} catch (ExitSetup e) {
			return e.Code;
		} catch (SetupException e) {
			Console.WriteLine("Error: {0}", e.Message);
			return 1;
		} catch (Exception e) {
			Console.WriteLine("Unexpected error!\n{0}", e.ToString());
			Console.WriteLine("If this is the most current release, please report this bug.");
			return 1;
		} finally {
			if (!Batch) {
				Console.WriteLine("Press any key to quit.");
				Console.ReadKey();
			}
		}
	}
}
