using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/**
 * Methods for handling EFI variables.
 */
public class Efi {
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern UInt32 GetFirmwareEnvironmentVariableEx(string lpName, string lpGuid, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuffer, UInt32 nSize, out UInt32 pdwAttributes);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern UInt32 SetFirmwareEnvironmentVariableEx(string lpName, string lpGuid, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuffer, UInt32 nSize, UInt32 pdwAttributes);

	[DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern bool AdjustTokenPrivileges(IntPtr htoken, bool disable, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr phtok);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr hObject);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern UInt32 GetSystemFirmwareTable(UInt32 provider, UInt32 table, [MarshalAs(UnmanagedType.LPArray)] byte[] buffer, UInt32 len);

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct TokPriv1Luid {
		public int Count;
		public long Luid;
		public int Attr;
	}

	/**
	 * Information about an EFI variable.
	 */
	public class Variable {
		public string Name, Guid;
		public UInt32 Attributes;
		public byte[] Data;

		/**
		 * Convert to string.
		 *
		 * @return String representation of this object.
		 */
		public override string ToString() {
			if (Data == null) {
				return $"{Name} Guid={Guid} Attributes={Attributes} Data=null";
			}
			var hex = BitConverter.ToString(Data).Replace("-", " ");
			var text = new string(Data.Select(c => 0x20 <= c && c <= 0x7f ? (char) c : ' ').ToArray());
			return $"{Name} Guid={Guid} Attributes={Attributes} Text='{text}' Bytes='{hex}'";
		}
	}

	/**
	 * GUID of the global EFI variables.
	 */
	public const string EFI_GLOBAL_GUID = "{8be4df61-93ca-11d2-aa0d-00e098032b8c}";

	/**
	 * GUID for HackBGRT EFI variables.
	 */
	public const string EFI_HACKBGRT_GUID = "{03c64761-075f-4dba-abfb-2ed89e18b236}";

	/**
	 * Directory containing EFI variables in Linux.
	 */
	public const string LinuxEfiDir = "/sys/firmware/efi/efivars";

	/**
	 * Enable the privilege to access EFI variables.
	 */
	public static void EnablePrivilege() {
		if (Directory.Exists(LinuxEfiDir)) {
			var linuxEfiFile = $"{LinuxEfiDir}/BootOrder-8be4df61-93ca-11d2-aa0d-00e098032b8c";
			if (File.Exists(linuxEfiFile)) {
				using (FileStream fs = File.OpenWrite(linuxEfiFile)) {
					// OpenWrite throws an exception on error.
				}
			}
			return;
		}

		const int SE_PRIVILEGE_ENABLED = 0x00000002;
		const int TOKEN_QUERY = 0x00000008;
		const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
		const string SE_SYSTEM_ENVIRONMENT_NAME = "SeSystemEnvironmentPrivilege";
		IntPtr htoken = IntPtr.Zero;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out htoken)) {
			throw new Exception("EnablePrivilege: OpenProcessToken failed: " + Marshal.GetLastWin32Error());
		}
		try {
			TokPriv1Luid tp;
			tp.Count = 1;
			tp.Luid = 0;
			tp.Attr = SE_PRIVILEGE_ENABLED;
			if (!LookupPrivilegeValue(null, SE_SYSTEM_ENVIRONMENT_NAME, ref tp.Luid)) {
				throw new Exception("EnablePrivilege: LookupPrivilegeValue failed: " + Marshal.GetLastWin32Error());
			}
			if (!AdjustTokenPrivileges(htoken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) {
				throw new Exception("EnablePrivilege: AdjustTokenPrivileges failed: " + Marshal.GetLastWin32Error());
			}
		} finally {
			CloseHandle(htoken);
		}
	}

	/**
	 * Get an EFI variable.
	 *
	 * @param name Name of the EFI variable.
	 * @param guid GUID of the EFI variable.
	 * @return Information about the EFI variable.
	 */
	public static Variable GetVariable(string name, string guid = EFI_GLOBAL_GUID) {
		Variable result = new Variable();
		result.Name = name;
		result.Guid = guid;
		result.Data = null;
		result.Attributes = 0;

		if (Directory.Exists(LinuxEfiDir)) {
			var linuxEfiFile = $"{LinuxEfiDir}/{name}-{guid.Substring(1, guid.Length - 2)}";
			if (File.Exists(linuxEfiFile)) {
				var d = File.ReadAllBytes(linuxEfiFile);
				result.Attributes = (UInt32)(d[0] + 0x100 * d[1] + 0x10000 * d[2] + 0x1000000 * d[3]);
				result.Data = d.Skip(4).ToArray();
			}
			return result;
		}

		for (UInt32 i = 4096; i <= 1024*1024; i *= 2) {
			byte[] buf = new byte[i];
			UInt32 len = GetFirmwareEnvironmentVariableEx(name, guid, buf, (UInt32) buf.Length, out result.Attributes);
			if (len == buf.Length) {
				continue;
			}
			if (len > 0 || Marshal.GetLastWin32Error() == 0) {
				result.Data = new byte[len];
				Array.Copy(buf, 0, result.Data, 0, len);
				return result;
			}
			switch (len != 0 ? 0 : Marshal.GetLastWin32Error()) {
				case 203:
					// Not found.
					return result;
				case 87:
					throw new Exception("GetVariable: Invalid parameter");
				case 1314:
					throw new Exception("GetVariable: Privilege not held");
				default:
					throw new Exception("GetVariable: error " + Marshal.GetLastWin32Error());
			}
		}
		throw new Exception("GetFirmwareEnvironmentVariable: too big data");
	}

	/**
	 * Set an EFI variable.
	 *
	 * @param v Information of the variable.
	 * @param dryRun Don't actually set the variable.
	 */
	public static void SetVariable(Variable v, bool dryRun = false) {
		Setup.WriteLine($"Writing EFI variable {v.Name} (see log for details)");
		Setup.Log($"Writing EFI variable: {v}");
		if (dryRun) {
			return;
		}

		if (Directory.Exists(LinuxEfiDir)) {
			var linuxEfiFile = $"{LinuxEfiDir}/{v.Name}-{v.Guid.Substring(1, v.Guid.Length - 2)}";
			var a = v.Attributes;
			var b = new byte[] { (byte) a, (byte) (a >> 8), (byte) (a >> 16), (byte) (a >> 24) };
			// FIXME: Just writing won't work: File.WriteAllBytes(linuxEfiFile, b.Concat(v.Data).ToArray());
			Setup.WriteLine("FIXME: Can't yet write EFI variables in Linux.");
			return;
		}

		UInt32 r = SetFirmwareEnvironmentVariableEx(v.Name, v.Guid, v.Data, (UInt32) v.Data.Length, v.Attributes);
		if (r == 0) {
			switch (Marshal.GetLastWin32Error()) {
				case 87:
					throw new Exception("SetVariable: Invalid parameter");
				case 1314:
					throw new Exception("SetVariable: Privilege not held");
				default:
					throw new Exception("SetVariable: error " + Marshal.GetLastWin32Error());
			}
		}
	}

	/**
	 * Check if Secure Boot is enabled.
	 *
	 * @return 0 for disabled, 1 for enabled, other for unknown.
	 */
	public static int GetSecureBootStatus() {
		// GetVariable("SecureBoot") reports always 1 (on Lenovo E335).
		// Windows registry seems to work better, though.
		try {
			return (int) Registry.GetValue(
				"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State",
				"UEFISecureBootEnabled",
				-1
			);
		} catch {
			return -1;
		}
	}

	/**
	 * Check if it's possible to reboot to EFI setup.
	 *
	 * @return True, if it's possible to reboot to EFI setup.
	 */
	public static bool CanBootToFW() {
		try {
			Variable tmp = GetVariable("OsIndicationsSupported");
			return tmp.Data != null && (tmp.Data[0] & 1) != 0;
		} catch {
			return false;
		}
	}

	/**
	 * Mark that the next reboot should go to EFI setup.
	 */
	public static void SetBootToFW() {
		Variable tmp = GetVariable("OsIndications");
		if (tmp.Data == null) {
			tmp.Data = new byte[8];
			tmp.Attributes = 7;
		}
		tmp.Data[0] |= 1;
		SetVariable(tmp);
	}

	/**
	 * Convert bytes into UInt16 values.
	 *
	 * @param bytes The byte array.
	 * @return An enumeration of UInt16 values.
	 */
	public static IEnumerable<UInt16> BytesToUInt16s(byte[] bytes) {
		// TODO: return bytes.Chunk(2).Select(b => (UInt16) (b[0] + 0x100 * b[1])).ToArray();
		return Enumerable.Range(0, bytes.Length / 2).Select(i => (UInt16) (bytes[2 * i] + 0x100 * bytes[2 * i + 1]));
	}

	/**
	 * Retrieve HackBGRT log collected during boot.
	 */
	public static string GetHackBGRTLog() {
		try {
			var log = GetVariable("HackBGRTLog", EFI_HACKBGRT_GUID);
			if (log.Data == null) {
				return "Log is empty.";
			}
			return new string(BytesToUInt16s(log.Data).Select(i => (char)i).ToArray());
		} catch (Exception e) {
			return $"Log not found: {e.ToString()}";
		}
	}

	/**
	 * Log the BGRT table (for debugging).
	 */
	public static void LogBGRT() {
		try {
			const UInt32 acpiBE = 0x41435049, bgrtLE = 0x54524742;
			UInt32 size = GetSystemFirmwareTable(acpiBE, bgrtLE, null, 0);
			byte[] buf = new byte[size];
			var ret = GetSystemFirmwareTable(acpiBE, bgrtLE, buf, size);
			if (ret == size) {
				var hex = BitConverter.ToString(buf).Replace("-", " ");
				Setup.Log($"LogBGRT: {size} bytes: {hex}");
			} else if (ret == 0) {
				Setup.Log($"LogBGRT: Win32Error {Marshal.GetLastWin32Error()}");
			} else {
				Setup.Log($"LogBGRT: Size problems: spec {0x38}, buf {size}, ret {ret}");
			}
		} catch (Exception e) {
			Setup.Log($"LogBGRT failed: {e}");
		}
	}
}
