using System;
using System.IO;
using System.Text.RegularExpressions;

/**
 * EFI System Partition mounter.
 */
public sealed class Esp {
	/** The singleton instance of this class, if ESP is mounted. */
	private static Esp MountInstance;

	/** EFI System Partition location. */
	public static string Location { get; private set; }

	/** MS boot loader path on ESP. */
	public static string MsLoaderPath {
		get {
			return Path.Combine(new string[] { Location, "EFI", "Microsoft", "Boot", "bootmgfw.efi"});
		}
	}

	/**
	 * Constructor: do nothing.
	 */
	private Esp() {
	}

	/**
	 * Destructor: unmount.
	 */
	~Esp() {
		if (this == MountInstance) {
			Unmount();
		}
	}

	/**
	 * Try to find ESP at a path.
	 *
	 * @param tryPath The new path to try.
	 * @param requireMsLoader Look for MS boot loader specifically?
	 * @return true if the path was given and seems valid, false otherwise.
	 */
	public static bool TryPath(string tryPath, bool requireMsLoader = true) {
		if (MountInstance != null && Location != null) {
			Unmount();
		}
		Location = tryPath;
		if (Location != null && Location != "") {
			if (File.Exists(MsLoaderPath)) {
				Setup.Log($"Esp.TryPath: {Location} has MS boot loader");
				return true;
			}
			if (Directory.Exists(Path.Combine(Location, "EFI"))) {
				Setup.Log($"Esp.TryPath: {Location} has EFI directory but no loader");
				if (!requireMsLoader) {
					return true;
				}
			}
		}
		Location = null;
		return false;
	}

	/**
	 * Find the EFI System Partition, if it's already mounted.
	 *
	 * @return true if the drive was found.
	 */
	public static bool Find() {
		if (MountInstance != null) {
			return true;
		}
		Setup.Log("Esp.Find()");
		try {
			// Match "The EFI System Partition is mounted at E:\" with some language support.
			var re = new Regex(" EFI[^\n]*(?:\n[ \t]*)?([A-Z]:\\\\)");
			var m = re.Match(Setup.Execute("mountvol", "", false));
			if (m.Success && TryPath(m.Groups[1].Captures[0].Value)) {
				return true;
			}
			Setup.Log("Esp.Find: no match");
		} catch (Exception e) {
			Setup.Log($"Esp.Find: {e.ToString()}");
		}
		for (char c = 'A'; c <= 'Z'; ++c) {
			if (TryPath(c + ":\\")) {
				Setup.Log($"Esp.Find: found {c}");
				return true;
			}
		}
		Setup.Log("Esp.Find: not found");
		return false;
	}

	/**
	 * Mount the EFI System Partition.
	 *
	 * @return true if the drive was mounted, false otherwise.
	 */
	public static bool Mount() {
		if (MountInstance != null) {
			return true;
		}
		for (char c = 'A'; c <= 'Z'; ++c) {
			Setup.Log($"Esp.Mount: {c}");
			if (Setup.Execute("mountvol", c + ": /S", true) != null) {
				MountInstance = new Esp();
				if (TryPath(c + ":\\", false)) {
					return true;
				} else {
					throw new Exception("Mounted ESP at " + c + ":\\ but it seems to be invalid!");
				}
			}
		}
		return false;
	}

	/**
	 * Unmount the EFI System Partition, if necessary.
	 */
	private static void Unmount() {
		if (MountInstance != null) {
			Setup.Execute("mountvol", Location + " /D", true);
			Location = null;
			MountInstance = null;
		}
	}
}
