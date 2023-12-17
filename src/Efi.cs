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
	 * Information about an EFI boot entry.
	 */
	public class BootEntryData {
		public UInt32 Attributes;
		public string Label;
		public class DevicePathNode {
			public byte Type, SubType;
			public byte[] Data;
			public DevicePathNode(byte[] data) {
				Type = data[0];
				SubType = data[1];
				Data = data.Skip(4).ToArray();
			}
			public byte[] ToBytes() {
				var len = Data.Length + 4;
				return new byte[] { Type, SubType, (byte)(len & 0xff), (byte)(len >> 8) }.Concat(Data).ToArray();
			}
		}
		public List<DevicePathNode> DevicePathNodes;
		public byte[] Arguments;

		public BootEntryData(byte[] data) {
			Attributes = BitConverter.ToUInt32(data, 0);
			var pathNodesLength = BitConverter.ToUInt16(data, 4);
			Label = new string(BytesToUInt16s(data).Skip(3).TakeWhile(i => i != 0).Select(i => (char)i).ToArray());
			var pos = 6 + 2 * (Label.Length + 1);
			var pathNodesEnd = pos + pathNodesLength;
			DevicePathNodes = new List<DevicePathNode>();
			Arguments = new byte[0];
			while (pos + 4 <= pathNodesEnd) {
				var len = BitConverter.ToUInt16(data, pos + 2);
				if (len < 4 || pos + len > pathNodesEnd) {
					return; // throw new Exception("Bad entry.");
				}
				DevicePathNodes.Add(new DevicePathNode(data.Skip(pos).Take(len).ToArray()));
				pos += len;
			}
			Arguments = data.Skip(pathNodesEnd).ToArray();
		}
		public byte[] ToBytes() {
			return new byte[0]
				.Concat(BitConverter.GetBytes((UInt32) Attributes))
				.Concat(BitConverter.GetBytes((UInt16) DevicePathNodes.Sum(n => n.Data.Length + 4)))
				.Concat(Encoding.Unicode.GetBytes(Label + "\0"))
				.Concat(DevicePathNodes.SelectMany(n => n.ToBytes()))
				.Concat(Arguments)
				.ToArray();
		}
		public DevicePathNode FileNameNode {
			get {
				var d = DevicePathNodes;
				return d.Count > 1 && d[d.Count - 1].Type == 0x7F && d[d.Count - 2].Type == 0x04 ? d[d.Count - 2] : null;
			}
		}
		public bool HasFileName {
			get {
				return FileNameNode != null;
			}
		}
		public string FileName {
			get {
				if (!HasFileName) {
					return "";
				}
				return new string(Encoding.Unicode.GetChars(FileNameNode.Data).TakeWhile(c => c != '\0').ToArray());
			}
			set {
				if (!HasFileName) {
					throw new Exception("Logic error: Setting FileName on a bad boot entry.");
				}
				FileNameNode.Data = Encoding.Unicode.GetBytes(value + "\0");
			}
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
	private static Variable GetVariable(string name, string guid = EFI_GLOBAL_GUID) {
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
	private static void SetVariable(Variable v, bool dryRun = false) {
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
	 * Disable the said boot entry from BootOrder.
	 *
	 * @param label Label of the boot entry.
	 * @param fileName File name of the boot entry.
	 * @param dryRun Don't actually disable the entry.
	 * @return True, if the entry was found in BootOrder.
	 */
	public static bool DisableBootEntry(string label, string fileName, bool dryRun = false) {
		Variable bootOrder;
		try {
			bootOrder = GetVariable("BootOrder");
		} catch {
			return false;
		}
		if (bootOrder.Data == null) {
			return false;
		}
		Setup.Log($"Read EFI variable: {bootOrder}");
		var found = false;
		var bootOrderInts = new List<UInt16>();
		foreach (var num in BytesToUInt16s(bootOrder.Data)) {
			var entry = GetVariable(String.Format("Boot{0:X04}", num));
			if (entry.Data != null) {
				var entryData = new BootEntryData(entry.Data);
				if (entryData.Label == label && entryData.FileName == fileName) {
					found = true;
					continue;
				}
			}
			bootOrderInts.Add(num);
		}
		if (found) {
			bootOrder.Data = bootOrderInts.SelectMany(num => new byte[] { (byte)(num & 0xff), (byte)(num >> 8) }).ToArray();
			SetVariable(bootOrder, dryRun);
		}
		return found;
	}

	/**
	 * Create and enable the said boot entry from BootOrder.
	 *
	 * @param label Label of the boot entry.
	 * @param fileName File name of the boot entry.
	 * @param alwaysCopyFromMS If true, do not preserve any existing data.
	 * @param dryRun Don't actually create the entry.
	 */
	public static void MakeAndEnableBootEntry(string label, string fileName, bool alwaysCopyFromMS, bool dryRun = false) {
		Variable msEntry = null, ownEntry = null;
		UInt16 msNum = 0, ownNum = 0;

		// Find a free entry and the MS bootloader entry.
		Variable bootOrder = null;
		try {
			bootOrder = GetVariable("BootOrder");
		} catch {
			if (dryRun) {
				return;
			}
		}
		if (bootOrder == null || bootOrder.Data == null) {
			throw new Exception("MakeBootEntry: Could not read BootOrder. Maybe your computer is defective.");
		}
		var bootCurrent = GetVariable("BootCurrent");
		if (bootCurrent.Data == null) {
			throw new Exception("MakeBootEntry: Could not read BootCurrent. Maybe your computer is defective.");
		}
		Setup.Log($"Read EFI variable: {bootOrder}");
		Setup.Log($"Read EFI variable: {bootCurrent}");
		var bootOrderInts = new List<UInt16>(BytesToUInt16s(bootOrder.Data));
		foreach (var num in BytesToUInt16s(bootCurrent.Data).Concat(bootOrderInts).Concat(Enumerable.Range(0, 0xff).Select(i => (UInt16) i))) {
			var entry = GetVariable(String.Format("Boot{0:X04}", num));
			if (entry.Data == null) {
				// Use only Boot0000 .. Boot00FF because some firmwares expect that.
				if (ownEntry == null && num < 0x100) {
					ownNum = num;
					ownEntry = entry;
				}
			} else {
				var entryData = new BootEntryData(entry.Data);
				if (!entryData.HasFileName) {
					continue;
				}
				if (entryData.Label == label && entryData.FileName == fileName) {
					ownNum = num;
					ownEntry = entry;
				}
				if (msEntry == null && entryData.FileName.StartsWith("\\EFI\\Microsoft\\Boot\\bootmgfw.efi", StringComparison.OrdinalIgnoreCase)) {
					msNum = num;
					msEntry = entry;
				}
			}
			if (ownEntry != null && msEntry != null) {
				break;
			}
		}
		if (ownEntry == null) {
			throw new Exception("MakeBootEntry: Boot entry list is full.");
		} else if (msEntry == null) {
			throw new Exception("MakeBootEntry: Windows Boot Manager not found.");
		} else {
			Setup.Log($"Read EFI variable: {msEntry}");
			Setup.Log($"Read EFI variable: {ownEntry}");
			// Make a new boot entry using the MS entry as a starting point.
			var entryData = new BootEntryData(msEntry.Data);
			if (!alwaysCopyFromMS && ownEntry.Data != null) {
				entryData = new BootEntryData(ownEntry.Data);
				// Shim expects the arguments to be a filename or nothing.
				// But BCDEdit expects some Microsoft-specific data.
				// Modify the entry so that BCDEdit still recognises it
				// but the data becomes a valid UCS-2 / UTF-16LE file name.
				var str = new string(entryData.Arguments.Take(12).Select(c => (char) c).ToArray());
				if (str == "WINDOWS\0\x01\0\0\0") {
					entryData.Arguments[8] = (byte) 'X';
				} else if (str != "WINDOWS\0\x58\0\0\0") {
					// Not recognized. Clear the arguments.
					entryData.Arguments = new byte[0];
				}
			} else {
				entryData.Arguments = new byte[0];
				entryData.Label = label;
				entryData.FileName = fileName;
			}
			entryData.Attributes = 1; // LOAD_OPTION_ACTIVE
			ownEntry.Attributes = 7; // EFI_VARIABLE_NON_VOLATILE | EFI_VARIABLE_BOOTSERVICE_ACCESS | EFI_VARIABLE_RUNTIME_ACCESS
			ownEntry.Data = entryData.ToBytes();
			SetVariable(ownEntry, dryRun);
		}

		var msPos = bootOrderInts.IndexOf(msNum);
		var ownPos = bootOrderInts.IndexOf(ownNum);
		var mustAdd = ownPos == -1;
		var mustMove = 0 <= msPos && msPos <= ownPos;
		if (mustAdd || mustMove) {
			if (mustMove) {
				bootOrderInts.RemoveAt(ownPos);
			}
			bootOrderInts.Insert(msPos < 0 ? 0 : msPos, ownNum);
			bootOrder.Data = bootOrderInts.SelectMany(num => new byte[] { (byte)(num & 0xff), (byte)(num >> 8) }).ToArray();
			SetVariable(bootOrder, dryRun);
		}
	}

	/**
	 * Log the boot entries.
	 */
	public static void LogBootEntries() {
		try {
			var bootOrder = GetVariable("BootOrder");
			var bootCurrent = GetVariable("BootCurrent");
			Setup.Log($"LogBootOrder: {bootOrder}");
			Setup.Log($"LogBootOrder: {bootCurrent}");
			var bootOrderInts = new List<UInt16>(BytesToUInt16s(bootOrder.Data));
			// Windows can't enumerate EFI variables, and trying them all is too slow.
			// BootOrder + BootCurrent + the first 0xff entries should be enough.
			var seen = new HashSet<UInt16>();
			foreach (var num in bootOrderInts.Concat(BytesToUInt16s(bootCurrent.Data)).Concat(Enumerable.Range(0, 0xff).Select(i => (UInt16) i))) {
				if (seen.Contains(num)) {
					continue;
				}
				seen.Add(num);
				var entry = GetVariable(String.Format("Boot{0:X04}", num));
				if (entry.Data != null) {
					Setup.Log($"LogBootOrder: {entry}");
				}
			}
		} catch (Exception e) {
			Setup.Log($"LogBootOrder failed: {e.ToString()}");
		}
	}

	/**
	 * Retrieve HackBGRT log collected during boot.
	 */
	public static string GetHackBGRTLog() {
		try {
			var log = GetVariable("HackBGRTLog", EFI_HACKBGRT_GUID);
			if (log.Data == null) {
				return "(null)";
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
