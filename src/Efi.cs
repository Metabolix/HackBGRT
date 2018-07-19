using System;
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
	}

	public const string EFI_GLOBAL_GUID = "{8be4df61-93ca-11d2-aa0d-00e098032b8c}";

	/**
	 * Enable the privilege to access EFI variables.
	 */
	public static void EnablePrivilege() {
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
	private static Variable GetVariable(string name, string guid = EFI_GLOBAL_GUID) {
		Variable result = new Variable();
		result.Name = name;
		result.Guid = guid;
		result.Data = null;
		result.Attributes = 0;
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
	 */
	private static void SetVariable(Variable v) {
		UInt32 r = SetFirmwareEnvironmentVariableEx(v.Name, v.Guid, v.Data, (UInt32) v.Data.Length, v.Attributes);
		if (r == 0) {
			switch (Marshal.GetLastWin32Error()) {
				case 87:
					throw new Exception("GetVariable: Invalid parameter");
				case 1314:
					throw new Exception("GetVariable: Privilege not held");
				default:
					throw new Exception("GetVariable: error " + Marshal.GetLastWin32Error());
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
}
