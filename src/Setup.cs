using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Management;
using Microsoft.Win32;

[assembly: AssemblyInformationalVersionAttribute(GIT_DESCRIBE.data)]
[assembly: AssemblyProductAttribute("HackBGRT")]

/**
 * HackBGRT Setup.
 */
public class Setup {
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

	/**
	 * Boot loader type.
	 */
	public enum BootLoaderType {
		None,
		Own,
		Shim,
		Microsoft,
		Other,
	}

	/** @var The privileged actions. */
	protected static readonly string[] privilegedActions = new string[] {
		"install",
		"allow-secure-boot",
		"allow-bitlocker",
		"allow-bad-loader",
		"skip-shim",
		"enable-entry", "disable-entry",
		"enable-bcdedit", "disable-bcdedit",
		"enable-overwrite", "disable-overwrite",
		"disable",
		"uninstall",
		"boot-to-fw",
		"show-boot-log",
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

	/** @var User-defined EFI architecture? */
	protected bool UserDefinedArch = false;

	/** @var Arguments to forward to the elevated process. */
	protected string ForwardArguments;

	/** @var Dry run? */
	protected bool DryRun;

	/** @var Run in batch mode? */
	protected bool Batch;

	/** @var Is the loader signed? */
	protected bool LoaderIsSigned = false;

	/**
	 * Output a line.
	 */
	public static void WriteLine(string s = "") {
		Console.WriteLine(s);
		Log(s);
	}

	/**
	 * Output a line to the log file.
	 */
	public static void Log(string s) {
		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
		var prefix = $"{timestamp} | pid {pid} | ";
		File.AppendAllText("setup.log", prefix + s.Replace("\n", "\n" + prefix) + "\n");
	}

	/**
	 * Start another process.
	 *
	 * @param app Path to the application.
	 * @param args The argument string.
	 * @return The started process.
	 */
	public static Process StartProcess(string app, string args) {
		Log($"StartProcess: {app} {args}");
		try {
			var info = new ProcessStartInfo(app, args);
			info.UseShellExecute = false;
			return Process.Start(info);
		} catch (Exception e) {
			Log($"StartProcess failed: {e.ToString()}");
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
		Log($"Execute: {app} {args}");
		try {
			var info = new ProcessStartInfo(app, args);
			info.UseShellExecute = false;
			info.RedirectStandardOutput = true;
			var p = Process.Start(info);
			string output = p.StandardOutput.ReadToEnd();
			p.WaitForExit();
			Log($"Exit code: {p.ExitCode}, output:\n{output}\n\n");
			return (nullOnFail && p.ExitCode != 0) ? null : output;
		} catch (Exception e) {
			Log($"Execute failed: {e.ToString()}");
			return null;
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
		} catch (Exception e) {
			Log($"HasPrivileges failed: {e.ToString()}");
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
		Log($"RunElevated: {app} {args}");
		ProcessStartInfo startInfo = new ProcessStartInfo(app);
		startInfo.Arguments = args;
		startInfo.Verb = "runas";
		startInfo.UseShellExecute = true;
		Process p = Process.Start(startInfo);
		p.WaitForExit();
		return p.ExitCode;
	}

	/**
	 * Is this HackBGRT's own boot loader?
	 */
	public static BootLoaderType DetectLoader(string path) {
		try {
			var data = File.ReadAllBytes(path);
			string tmp = System.Text.Encoding.ASCII.GetString(data);
			if (tmp.IndexOf("HackBGRT") >= 0 || tmp.IndexOf("HackBgrt") >= 0) {
				return BootLoaderType.Own;
			} else if (tmp.IndexOf("UEFI shim") >= 0) {
				return BootLoaderType.Shim;
			} else if (tmp.IndexOf("Microsoft Corporation") >= 0) {
				return BootLoaderType.Microsoft;
			} else {
				return BootLoaderType.Other;
			}
		} catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException) {
			Log($"DetectLoader failed: {path} not found");
		} catch (Exception e) {
			Log($"DetectLoader failed: {e.ToString()}");
		}
		return BootLoaderType.None;
	}

	/**
	 * Detect the EFI architecture from a file.
	 */
	public static string DetectArchFromFile(string path) {
		try {
			var data = File.ReadAllBytes(path);
			var peArch = BitConverter.ToUInt16(data, BitConverter.ToInt32(data, 0x3c) + 4);
			return peArch switch {
				0x014c => "ia32",
				0x0200 => "ia64",
				0x8664 => "x64",
				0xaa64 => "aa64",
				0x01c2 => "arm",
				0x01c4 => "arm",
				_ => $"unknown-{peArch:x4}"
			};
		} catch {
			return null;
		}
	}

	/**
	 * Find or mount or manually choose the EFI System Partition.
	 */
	protected void InitEspPath() {
		if (DryRun) {
			Directory.CreateDirectory(Path.Combine("dry-run", "EFI"));
			Esp.TryPath("dry-run", false);
		}
		if (Esp.Location == null && !Esp.Find() && !Esp.Mount() && !Batch) {
			WriteLine("EFI System Partition was not found.");
			WriteLine("Press enter to exit, or give ESP path here: ");
			string s = Console.ReadLine();
			Log($"User input: {s}");
			if (s.Length == 1) {
				s = s + ":";
			}
			if (!Esp.TryPath(s, true)) {
				WriteLine("That's not a valid ESP path!");
			}
		}
		if (Esp.Location == null) {
			throw new SetupException("EFI System Partition was not found.");
		}
		WriteLine($"EFI System Partition location is {Esp.Location}");
	}

	/**
	 * Install a single file.
	 */
	protected void InstallFile(string name, string newName = null, bool prependInstallPath = true) {
		if (prependInstallPath) {
			newName = Path.Combine(InstallPath, newName == null ? name : newName);
		}
		try {
			File.Copy(name, newName, true);
		} catch (Exception e) {
			Log($"InstallFile failed: {e.ToString()}");
			throw new SetupException($"Failed to install file {name} to {newName}.");
		}
		WriteLine($"Installed {name} to {newName}.");
	}

	/**
	 * Install a single image file, with conversion to 24-bit BMP.
	 */
	protected void InstallImageFile(string name) {
		Log($"InstallImageFile: {name}");
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
		WriteLine($"Installed image {name} to {newName}.");
	}

	/**
	 * Install files to ESP.
	 *
	 * @param skipShim Whether to skip installing shim.
	 */
	protected void InstallFiles(bool skipShim) {
		var loaderSource = Path.Combine("efi-signed", $"boot{EfiArch}.efi");
		LoaderIsSigned = true;
		if (!File.Exists(loaderSource)) {
			loaderSource = Path.Combine("efi", $"boot{EfiArch}.efi");
			LoaderIsSigned = false;
			if (!File.Exists(loaderSource)) {
				throw new SetupException($"Missing boot{EfiArch}.efi, {EfiArch} is not supported!");
			}
		}
		var shimSource = Path.Combine("shim-signed", $"shim{EfiArch}.efi");
		var mmSource = Path.Combine("shim-signed", $"mm{EfiArch}.efi");
		if (!skipShim) {
			if (!File.Exists(shimSource)) {
				throw new SetupException($"Missing shim ({shimSource}), can't install shim for {EfiArch}!");
			}
			if (!File.Exists(mmSource)) {
				throw new SetupException($"Missing MokManager ({mmSource}), can't install shim for {EfiArch}!");
			}
		}
		try {
			if (!Directory.Exists(InstallPath)) {
				Directory.CreateDirectory(InstallPath);
			}
		} catch {
			throw new SetupException("Failed to copy files to ESP!");
		}

		InstallFile("config.txt");
		var lines = File.ReadAllLines("config.txt");
		Log($"config.txt:\n{String.Join("\n", lines)}");
		foreach (var line in lines.Where(s => s.StartsWith("image="))) {
			var delim = "path=";
			var i = line.IndexOf(delim);
			if (i > 0) {
				var dir = "\\EFI\\HackBGRT\\";
				if (line.Substring(i + delim.Length).StartsWith(dir)) {
					InstallImageFile(line.Substring(i + delim.Length + dir.Length));
				}
				if (!line.Substring(i + delim.Length).StartsWith("\\")) {
					InstallImageFile(line.Substring(i + delim.Length));
				}
			}
		}
		var loaderDest = "loader.efi";
		if (!skipShim) {
			InstallFile(shimSource, loaderDest);
			InstallFile(mmSource, $"mm{EfiArch}.efi");
			loaderDest = $"grub{EfiArch}.efi";
		}
		InstallFile(loaderSource, loaderDest);
		InstallFile(loaderSource, "\u4957\u444e\u574f\u0053\u0058"); // bytes "WINDOWS\0X\0" as UTF-16
		if (LoaderIsSigned) {
			InstallFile("certificate.cer");
		}
		WriteLine($"HackBGRT has been copied to {InstallPath}.");

		var enrollHashPath = $"EFI\\HackBGRT\\{loaderDest}";
		var enrollKeyPath = "EFI\\HackBGRT\\certificate.cer";
		if (skipShim) {
			if (LoaderIsSigned) {
				WriteLine($"Remember to enroll {enrollKeyPath} in your firmware!");
			} else {
				WriteLine("This HackBGRT build is not signed. You may need to disable Secure Boot.");
			}
		} else {
			WriteLine($"On first boot, select 'Enroll hash from disk' and enroll {enrollHashPath}.");
			if (LoaderIsSigned) {
				WriteLine($"Alternatively, select 'Enroll key from disk' and enroll {enrollKeyPath}.");
			}
		}
	}

	/**
	 * Enable HackBGRT with bcdedit.
	 */
	protected void EnableBCDEdit() {
		if (DryRun) {
			WriteLine("Dry run, skip enabling with BCDEdit.");
			return;
		}
		try {
			var re = new Regex("[{][0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[}]");
			var guid = re.Match(Execute("bcdedit", "/copy {bootmgr} /d HackBGRT", true)).Value;
			Execute("bcdedit", $"/set {guid} device partition={Esp.Location}", true);
			Execute("bcdedit", $"/set {guid} path \\EFI\\HackBGRT\\loader.efi", true);
			foreach (var arg in new string[] { "locale", "inherit", "default", "resumeobject", "displayorder", "toolsdisplayorder", "timeout" }) {
				Execute("bcdedit", $"/deletevalue {guid} {arg}", true);
			}
			var fwbootmgr = "{fwbootmgr}";
			Execute("bcdedit", $"/set {fwbootmgr} displayorder {guid} /addfirst", true);
			WriteLine("Enabled NVRAM entry for HackBGRT with BCDEdit.");
			// Verify that the entry was created.
			Execute("bcdedit", "/enum firmware", true);
			Efi.MakeAndEnableBootEntry("HackBGRT", "\\EFI\\HackBGRT\\loader.efi", false, DryRun);
			Execute("bcdedit", $"/enum {guid}", true);
			Efi.LogBootEntries();
		} catch (Exception e) {
			Log($"EnableBCDEdit failed: {e.ToString()}");
			throw new SetupException("Failed to enable HackBGRT with BCDEdit!");
		}
	}

	/**
	 * Disable HackBGRT with bcdedit.
	 */
	protected void DisableBCDEdit() {
		bool found = false, disabled = false;
		var fullOutput = Execute("bcdedit", "/enum firmware", true);
		if (fullOutput == null) {
			return;
		}
		var re = new Regex("[{][0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[}]");
		var guids = (from Match match in re.Matches(fullOutput) select match.Value).Distinct().ToArray();
		foreach (var guid in guids) {
			var entry = Execute("bcdedit", $"/enum {guid}", true);
			if (entry == null) {
				Log($"DisableBCDEdit failed to enum {guid}.");
			} else if (entry.IndexOf("HackBGRT") >= 0) {
				found = true;
				Log($"Disabling HackBGRT entry {guid}.");
				if (!DryRun && Execute("bcdedit", $"/delete {guid}", true) == null) {
					Log($"DisableBCDEdit failed to delete {guid}.");
				} else {
					disabled = true;
				}
			}
		}
		if (found) {
			if (!disabled) {
				throw new SetupException("Failed to disable HackBGRT with BCDEdit!");
			}
			WriteLine("Disabled NVRAM entry for HackBGRT with BCDEdit.");
		}
	}

	/**
	 * Enable HackBGRT boot entry.
	 */
	protected void EnableEntry() {
		Efi.MakeAndEnableBootEntry("HackBGRT", "\\EFI\\HackBGRT\\loader.efi", true, DryRun);
		WriteLine("Enabled NVRAM entry for HackBGRT.");
		// Verify that the entry was created.
		Efi.LogBootEntries();
		Execute("bcdedit", "/enum firmware", true);
	}

	/**
	 * Disable HackBGRT boot entry.
	 */
	protected void DisableEntry() {
		Efi.DisableBootEntry("HackBGRT", "\\EFI\\HackBGRT\\loader.efi", DryRun);
		WriteLine("Disabled NVRAM entry for HackBGRT.");
	}

	/**
	 * Enable HackBGRT by overwriting the MS boot loader.
	 */
	protected void OverwriteMsLoader() {
		var ms = Esp.MsLoaderPath;
		var backup = BackupLoaderPath;

		if (DetectLoader(ms) == BootLoaderType.Microsoft) {
			InstallFile(ms, backup, false);
		}
		if (DetectLoader(backup) != BootLoaderType.Microsoft) {
			// Duplicate check, but better to be sure...
			throw new SetupException("Missing MS boot loader backup!");
		}
		var msDir = Path.GetDirectoryName(ms);
		var msGrub = Path.Combine(msDir, $"grub{EfiArch}.efi");
		var msMm = Path.Combine(msDir, $"mm{EfiArch}.efi");
		try {
			InstallFile(Path.Combine(InstallPath, "loader.efi"), ms, false);
			InstallFile(Path.Combine(InstallPath, $"grub{EfiArch}.efi"), msGrub, false);
			InstallFile(Path.Combine(InstallPath, $"mm{EfiArch}.efi"), msMm, false);
		} catch (SetupException e) {
			WriteLine(e.Message);
			if (DetectLoader(ms) != BootLoaderType.Microsoft) {
				try {
					InstallFile(backup, ms, false);
				} catch (SetupException e2) {
					WriteLine(e2.Message);
					throw new SetupException("Rollback failed, your system may be unbootable! Create a rescue disk IMMEADIATELY!");
				}
			}
		}
	}

	/**
	 * Restore the MS boot loader if it was previously replaced.
	 */
	protected void RestoreMsLoader() {
		var ms = Esp.MsLoaderPath;
		if (DetectLoader(ms) == BootLoaderType.Own || DetectLoader(ms) == BootLoaderType.Shim) {
			WriteLine("Disabling an old version of HackBGRT.");
			InstallFile(BackupLoaderPath, ms, false);
			WriteLine($"{ms} has been restored.");
		}
		if (DetectLoader(BackupLoaderPath) == BootLoaderType.Own) {
			File.Delete(BackupLoaderPath);
			WriteLine($"{BackupLoaderPath} was messed up and has been removed.");
		}
	}

	/**
	 * Check that config.txt has a reasonable boot loader line.
	 */
	protected void VerifyLoaderConfig() {
		var lines = File.ReadAllLines("config.txt");
		var loader = lines.Where(s => s.StartsWith("boot=")).Select(s => s.Substring(5)).LastOrDefault();
		if (loader == null) {
			throw new SetupException("config.txt does not contain a boot=... line!");
		}
		WriteLine($"Verifying config: boot={loader}");
		if (loader == "MS") {
			var backup = BackupLoaderPath;
			var type = DetectLoader(backup);
			if (type == BootLoaderType.Own) {
				throw new SetupException($"boot=MS = {backup} = HackBGRT. Prepare your rescue disk!");
			}
			if (type == BootLoaderType.None) {
				if (DetectLoader(Esp.MsLoaderPath) != BootLoaderType.Microsoft) {
					throw new SetupException("config.txt contains boot=MS, but MS boot loader is not found!");
				}
			}
		} else if (!loader.StartsWith("\\")) {
			throw new SetupException($"Boot loader must be boot=MS or boot=\\valid\\path!");
		} else {
			switch (DetectLoader(Path.Combine(Esp.Location, loader.Substring(1).Replace("\\", Path.DirectorySeparatorChar.ToString())))) {
				case BootLoaderType.Own:
					throw new SetupException($"Boot loader points back to HackBGRT!");
				case BootLoaderType.None:
					throw new SetupException($"Boot loader does not exist!");
			}
		}
	}

	/**
	 * Configure HackBGRT.
	 */
	protected void Configure() {
		WriteLine("This setup program lets you edit just one image.");
		WriteLine("Edit config.txt manually for advanced configuration.");

		// Open splash.bmp in mspaint.
		WriteLine("Draw or copy your preferred image to splash.bmp.");
		try {
			StartProcess("mspaint", "splash.bmp").WaitForExit();
		} catch {
			WriteLine("Editing splash.bmp with mspaint failed!");
			WriteLine("Edit splash.bmp with your preferred editor.");
			WriteLine("Press any key to continue.");
			Console.ReadKey();
		}
		WriteLine();
	}

	/**
	 * Disable HackBGRT completely.
	 */
	protected void Disable() {
		RestoreMsLoader();
		DisableBCDEdit();
		DisableEntry();
		WriteLine("HackBGRT has been disabled.");
	}

	/**
	 * Uninstall HackBGRT completely.
	 */
	protected void Uninstall() {
		Disable();
		try {
			Directory.Delete(InstallPath, true);
			WriteLine($"HackBGRT has been removed from {InstallPath}.");
		} catch (Exception e) {
			Log($"Uninstall failed: {e.ToString()}");
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
			WriteLine("Secure Boot is disabled, good!");
		} else {
			if (secureBoot == 1) {
				WriteLine("Secure Boot is probably enabled.");
			} else {
				WriteLine("Secure Boot status could not be determined.");
			}
			WriteLine("It's very important to disable Secure Boot before installing.");
			if (LoaderIsSigned) {
				WriteLine("Alternatively, you can enroll the certificate.cer in your firmware.");
			}
			WriteLine("Otherwise your machine may become unbootable.");
			if (Batch) {
				if (allowSecureBoot) {
					return;
				}
				throw new SetupException("Aborting because of Secure Boot.");
			}
			WriteLine("Choose action (press a key):");
			WriteLine(" S = Enter EFI Setup to disable Secure Boot manually; requires reboot!");
			WriteLine(" I = Install anyway; THIS MAY BE DANGEROUS!");
			WriteLine(" C = Cancel");
			var k = Console.ReadKey().Key;
			Log($"User input: {k}");
			WriteLine();
			if (k == ConsoleKey.I) {
				WriteLine("Continuing. THIS MAY BE DANGEROUS!");
			} else if (k == ConsoleKey.S) {
				BootToFW();
			} else {
				throw new SetupException("Aborting because of Secure Boot.");
			}
		}
	}

	/**
	 * Check BitLocker status and inform the user.
	 *
	 * @param allowBitLocker Allow BitLocker to be enabled?
	 */
	protected void HandleBitLocker(bool allowBitLocker) {
		var output = Execute("manage-bde", "-status", true);
		if (output == null) {
			WriteLine("BitLocker status could not be determined.");
			return;
		}
		var reOn = new Regex(@"Conversion Status:\s*.*Encr|AES");
		var reOff = new Regex(@"Conversion Status:\s*(Fully Decrypted)|\s0[.,]0\s*%");
		var isOn = reOn.Match(output).Success;
		var isOff = reOff.Match(output).Success;
		if (!isOn && isOff) {
			WriteLine("BitLocker is disabled, good!");
			return;
		}
		if (isOn) {
			WriteLine("BitLocker is enabled. Make sure you have your recovery key!");
		} else {
			WriteLine("BitLocker status is unclear. Run manage-bde -status to check.");
		}
		if (Batch) {
			if (allowBitLocker) {
				return;
			}
			throw new SetupException("Aborting because of BitLocker.");
		}
		WriteLine("Choose action (press a key):");
		WriteLine(" I = Install anyway; THIS MAY BE DANGEROUS!");
		WriteLine(" C = Cancel");
		var k = Console.ReadKey().Key;
		Log($"User input: {k}");
		WriteLine();
		if (k == ConsoleKey.I) {
			WriteLine("Continuing. THIS MAY BE DANGEROUS!");
		} else {
			WriteLine("If you absolutely want HackBGRT, you can disable BitLocker.");
			throw new SetupException("Aborting because of BitLocker.");
		}
	}

	/**
	 * Boot to the UEFI setup.
	 */
	protected void BootToFW() {
		if (!Efi.CanBootToFW()) {
			throw new SetupException("On this computer, you will need to find the UEFI setup manually.");
		}
		WriteLine("Notice: if this fails, you can still enter the UEFI setup manually.");
		try {
			Efi.SetBootToFW();
			WriteLine("Rebooting now...");
			StartProcess("shutdown", "-f -r -t 1");
		} catch (Exception e) {
			Log($"BootToFW failed: {e.ToString()}");
			throw new SetupException("Failed to reboot to UEFI setup! Do it manually.");
		}
	}

	/**
	 * Detect the EFI architecture.
	 */
	protected static string DetectArchFromOS() {
		var arch = RuntimeInformation.OSArchitecture;
		return arch switch {
			Architecture.X86 => "ia32",
			Architecture.X64 => "x64",
			Architecture.Arm => "arm",
			Architecture.Arm64 => "aa64",
			_ => $"-unsupported-{arch}"
		};
	}

	/**
	 * Set the EFI architecture.
	 *
	 * @param arch The architecture.
	 */
	protected void SetArch(string arch) {
		var detectedArch = Environment.Is64BitOperatingSystem ? "x64" : "ia32";
		try {
			detectedArch = DetectArchFromOS();
		} catch {
			WriteLine($"Failed to detect OS architecture, assuming {detectedArch}.");
		}
		if (arch == "" || arch == null) {
			EfiArch = detectedArch;
			WriteLine($"Your OS uses arch={EfiArch}. This will be checked again during installation.");
		} else {
			EfiArch = arch;
			UserDefinedArch = true;
			WriteLine($"Using the given arch={arch}");
			if (arch != detectedArch) {
				WriteLine($"Warning: arch={arch} is not the same as the detected arch={detectedArch}!");
			}
		}
	}

	/**
	 * Initialize information for the Setup.
	 */
	protected void InitEspInfo() {
		InstallPath = Path.Combine(Esp.Location, "EFI", "HackBGRT");
		var detectedArch = DetectArchFromFile(Esp.MsLoaderPath);
		if (detectedArch == null) {
			WriteLine($"Failed to detect arch from MS boot loader, using arch={EfiArch}.");
		} else if (detectedArch == EfiArch || !UserDefinedArch) {
			WriteLine($"Detected arch={detectedArch} from MS boot loader, the installer will use that.");
			EfiArch = detectedArch;
		} else {
			WriteLine($"WARNING: You have set arch={EfiArch}, but detected arch={detectedArch} from MS boot loader.");
		}
	}

	/**
	 * Log the boot time.
	 *
	 * @return The boot time, or null if it couldn't be determined.
	 */
	protected DateTime? GetBootTime() {
		try {
			var query = new ObjectQuery("SELECT LastBootUpTime FROM Win32_OperatingSystem WHERE Primary='true'");
			foreach (var m in new ManagementObjectSearcher(query).Get()) {
				return ManagementDateTimeConverter.ToDateTime(m["LastBootUpTime"].ToString());
			}
		} catch (Exception e) {
			Log($"GetBootTime failed: {e.ToString()}");
		}
		return null;
	}

	/**
	 * Check if Hiberboot is enabled.
	 *
	 * @return True, if Hiberboot is enabled.
	 */
	protected bool IsHiberbootEnabled() {
		try {
			return (int) Registry.GetValue(
				"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power",
				"HiberbootEnabled",
				0
			) != 0;
		} catch {
			return false;
		}
	}

	/**
	 * Ask for user's choice and install/uninstall.
	 */
	protected void ShowMenu() {
		WriteLine();
		WriteLine("Choose action (press a key):");
		WriteLine(" I = install");
		WriteLine("     - creates a new EFI boot entry for HackBGRT");
		WriteLine(" J = install (alternative)");
		WriteLine("     - creates a new EFI boot entry with an alternative method");
		WriteLine("     - try this if the first option doesn't work");
		WriteLine(" O = install (legacy)");
		WriteLine("     - overwrites the MS boot loader; gets removed by Windows updates");
		WriteLine("     - use as last resort; may brick your system if configured incorrectly");
		WriteLine(" F = install files only");
		WriteLine("     - ok for updating config, doesn't touch boot entries");
		WriteLine(" D = disable");
		WriteLine("     - removes created entries, restores MS boot loader");
		WriteLine(" R = remove completely");
		WriteLine("     - disables, then deletes all files and images");
		WriteLine(" B = boot to UEFI setup");
		WriteLine("     - lets you disable Secure Boot");
		WriteLine("     - lets you move HackBGRT before Windows in boot order");
		WriteLine(" L = show boot log (what HackBGRT did during boot)");
		WriteLine(" C = cancel");

		var k = Console.ReadKey().Key;
		Log($"User input: {k}");
		WriteLine();
		if (k == ConsoleKey.I || k == ConsoleKey.J || k == ConsoleKey.O || k == ConsoleKey.F) {
			Configure();
		}
		if (k == ConsoleKey.I) {
			RunPrivilegedActions(new string[] { "install", "disable", "enable-bcdedit" });
		} else if (k == ConsoleKey.J) {
			RunPrivilegedActions(new string[] { "install", "disable", "enable-entry" });
		} else if (k == ConsoleKey.O) {
			RunPrivilegedActions(new string[] { "install", "disable", "enable-overwrite" });
		} else if (k == ConsoleKey.F) {
			RunPrivilegedActions(new string[] { "install" });
		} else if (k == ConsoleKey.D) {
			RunPrivilegedActions(new string[] { "disable" });
		} else if (k == ConsoleKey.R) {
			RunPrivilegedActions(new string[] { "uninstall" });
		} else if (k == ConsoleKey.B) {
			RunPrivilegedActions(new string[] { "boot-to-fw" });
		} else if (k == ConsoleKey.L) {
			RunPrivilegedActions(new string[] { "show-boot-log" });
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
		var args = String.Join(" ", actions);
		Log($"RunPrivilegedActions: {args}");
		if (!HasPrivileges() && !DryRun) {
			var self = Assembly.GetExecutingAssembly().Location;
			var result = 0;
			try {
				result = RunElevated(self, $"is-elevated {ForwardArguments} {args}");
			} catch (Exception e) {
				Setup.Log(e.ToString());
				throw new SetupException($"Privileged action ({args}) failed: {e.Message}");
			}
			if (result != 0) {
				throw new SetupException($"Privileged action ({args}) failed!");
			}
			WriteLine($"Privileged action ({args}) completed.");
			return;
		}

		InitEspPath();
		InitEspInfo();
		var bootLog = $"\n--- BOOT LOG START ---\n{Efi.GetHackBGRTLog()}\n--- BOOT LOG END ---";
		Setup.Log(bootLog);
		Efi.LogBGRT();
		Efi.LogBootEntries();
		if (GetBootTime() is DateTime bootTime) {
			var configTime = new[] { File.GetCreationTime("config.txt"), File.GetLastWriteTime("config.txt") }.Max();
			Log($"Boot time = {bootTime}, config.txt changed = {configTime}");
			if (configTime > bootTime) {
				WriteLine($"Windows was booted at {bootTime}. Remember to reboot after installing!");
			}
		}
		if (IsHiberbootEnabled()) {
			WriteLine("You may have to disable Fast Startup (Hiberboot) to reboot properly.");
		}
		bool allowSecureBoot = false;
		bool allowBitLocker = false;
		bool allowBadLoader = false;
		bool skipShim = false;
		Action<Action> verify = (Action revert) => {
			try {
				VerifyLoaderConfig();
			} catch (SetupException e) {
				if (allowBadLoader) {
					WriteLine($"Warning: {e.Message}");
				} else {
					WriteLine($"Error: {e.Message}");
					WriteLine($"Reverting. Use batch mode with allow-bad-loader to override.");
					revert();
					throw new SetupException("Check your configuration and try again.");
				}
			}
		};
		Action<Action, Action> enable = (Action enable, Action revert) => {
			if (skipShim) {
				HandleSecureBoot(allowSecureBoot);
			}
			HandleBitLocker(allowBitLocker);
			enable();
			verify(revert);
		};
		foreach (var arg in actions) {
			Log($"Running action '{arg}'.");
			if (arg == "install") {
				InstallFiles(skipShim);
			} else if (arg == "allow-secure-boot") {
				allowSecureBoot = true;
			} else if (arg == "allow-bitlocker") {
				allowBitLocker = true;
			} else if (arg == "allow-bad-loader") {
				allowBadLoader = true;
			} else if (arg == "skip-shim") {
				skipShim = true;
			} else if (arg == "enable-entry") {
				enable(() => EnableEntry(), () => DisableEntry());
			} else if (arg == "disable-entry") {
				DisableEntry();
			} else if (arg == "enable-bcdedit") {
				enable(() => EnableBCDEdit(), () => DisableBCDEdit());
			} else if (arg == "disable-bcdedit") {
				DisableBCDEdit();
			} else if (arg == "enable-overwrite") {
				enable(() => OverwriteMsLoader(), () => RestoreMsLoader());
			} else if (arg == "disable-overwrite") {
				RestoreMsLoader();
			} else if (arg == "disable") {
				Disable();
			} else if (arg == "uninstall") {
				Uninstall();
			} else if (arg == "boot-to-fw") {
				BootToFW();
			} else if (arg == "show-boot-log") {
				WriteLine(bootLog);
			} else {
				throw new SetupException($"Invalid action: '{arg}'!");
			}
			WriteLine($"Completed action '{arg}' successfully.");
		}
	}

	/**
	 * The Main program.
	 *
	 * @param args The arguments.
	 */
	public static void Main(string[] args) {
		var self = Assembly.GetExecutingAssembly().Location;
		Directory.SetCurrentDirectory(Path.GetDirectoryName(self));
		WriteLine($"HackBGRT installer version: {Version}");
		Log($"Args: {String.Join(" ", args)}");
		Environment.ExitCode = new Setup().Run(args);
	}

	/**
	 * Run the setup.
	 *
	 * @param args The arguments.
	 */
	protected int Run(string[] args) {
		DryRun = args.Contains("dry-run");
		Batch = args.Contains("batch");
		ForwardArguments = String.Join(" ", args.Where(s => s == "dry-run" || s == "batch" || s.StartsWith("arch=")));
		try {
			SetArch(args.Where(s => s.StartsWith("arch=")).Select(s => s.Substring(5)).LastOrDefault());
			if (args.Contains("is-elevated") && !HasPrivileges() && !DryRun) {
				WriteLine("This installer needs to be run as administrator!");
				return 1;
			}
			var actions = args.Where(s => privilegedActions.Contains(s));
			if (actions.Count() > 0) {
				RunPrivilegedActions(actions);
				return 0;
			}
			if (Batch) {
				throw new SetupException("No action specified!");
			}
			ShowMenu();
			WriteLine();
			WriteLine("All done!");
			return 0;
		} catch (ExitSetup e) {
			return e.Code;
		} catch (SetupException e) {
			WriteLine();
			WriteLine($"Error: {e.Message}");
			Log(e.ToString());
			return 1;
		} catch (Exception e) {
			WriteLine();
			WriteLine($"Unexpected error: {e.Message}");
			Log(e.ToString());
			if (e is MissingMemberException || e is TypeLoadException) {
				WriteLine("This installer requires a recent version of .Net Framework.");
				WriteLine("Use Windows Update or download manually:");
				WriteLine("https://dotnet.microsoft.com/en-us/download/dotnet-framework");
			} else {
				WriteLine("If this is the most current release, please report this bug.");
			}
			return 1;
		} finally {
			if (DryRun) {
				WriteLine("This was a dry run, your system was not actually modified.");
			}
			if (!Batch) {
				WriteLine("If you need to report a bug,\n - run this setup again with menu option L (show-boot-log)\n - then include the setup.log file with your report.");
				WriteLine("Press any key to quit.");
				Console.ReadKey();
			}
		}
	}
}
