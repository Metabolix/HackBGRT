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
				return true;
			}
			if (!requireMsLoader && Directory.Exists(Path.Combine(Location, "EFI"))) {
				return true;
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
		try {
			// Match "The EFI System Partition is mounted at E:\" with some language support.
			var re = new Regex(" EFI[^\n]*(?:\n[ \t]*)?([A-Z]:\\\\)");
			if (TryPath(re.Match(Setup.Execute("mountvol", "", false)).Groups[1].Captures[0].Value)) {
				return true;
			}
		} catch {
		}
		for (char c = 'A'; c <= 'Z'; ++c) {
			if (TryPath(c + ":\\")) {
				return true;
			}
		}
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
