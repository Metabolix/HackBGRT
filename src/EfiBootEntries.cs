using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

/**
 * A class for handling EFI boot entries. Lazy access with cache.
 * Notice: Data is not updated after the first read.
 */
public class EfiBootEntries {
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
			Label = new string(Efi.BytesToUInt16s(data).Skip(3).TakeWhile(i => i != 0).Select(i => (char)i).ToArray());
			var pos = 6 + 2 * (Label.Length + 1);
			var pathNodesEnd = pos + pathNodesLength;
			DevicePathNodes = new List<DevicePathNode>();
			Arguments = new byte[0];
			while (pos + 4 <= pathNodesEnd) {
				var len = BitConverter.ToUInt16(data, pos + 2);
				if (len < 4 || pos + len > pathNodesEnd) {
					return; // throw new Exception("Bad entry.");
				}
				var node = new DevicePathNode(data.Skip(pos).Take(len).ToArray());
				DevicePathNodes.Add(node);
				if (node.Type == 0x7f && node.SubType == 0xff) {
					// End of entire device path.
					// Apparently some firmwares produce paths with unused nodes at the end.
					break;
				}
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
	 * Path to the Windows boot loader.
	 */
	public const string WindowsLoaderPath = "\\EFI\\Microsoft\\Boot\\bootmgfw.efi";

	/**
	 * Path to the HackBGRT loader.
	 */
	public const string OwnLoaderPath = "\\EFI\\HackBGRT\\loader.efi";

	private readonly Dictionary<UInt16, (Efi.Variable, BootEntryData)> cache;
	private readonly Efi.Variable BootOrder;
	private readonly Efi.Variable BootCurrent;
	private readonly List<UInt16> BootOrderInts;
	private readonly List<UInt16> BootCurrentInts;

	/**
	 * Constructor. Reads BootOrder and BootCurrent.
	 */
	public EfiBootEntries() {
		cache = new Dictionary<UInt16, (Efi.Variable, BootEntryData)>();
		BootOrder = Efi.GetVariable("BootOrder");
		BootCurrent = Efi.GetVariable("BootCurrent");
		if (BootOrder.Data == null) {
			throw new Exception("Could not read BootOrder.");
		}
		BootCurrentInts = new List<UInt16>(Efi.BytesToUInt16s(BootCurrent.Data ?? new byte[0]));
		BootOrderInts = new List<UInt16>(Efi.BytesToUInt16s(BootOrder.Data));
	}

	/**
	 * Get the boot entry with the given number.
	 *
	 * @param num Number of the boot entry.
	 * @return The boot entry.
	 */
	public (Efi.Variable, BootEntryData) GetEntry(UInt16 num) {
		if (!cache.ContainsKey(num)) {
			var v = Efi.GetVariable(String.Format("Boot{0:X04}", num));
			cache[num] = (v, v.Data == null ? null : new BootEntryData(v.Data));
		}
		return cache[num];
	}

	/**
	 * Find entry by file name.
	 *
	 * @param fileName File name of the boot entry.
	 * @return The boot entry.
	 */
	public (UInt16, Efi.Variable, BootEntryData) FindEntry(string fileName) {
		var rest = Enumerable.Range(0, 0xff).Select(i => (UInt16) i);
		var entryAccessOrder = BootCurrentInts.Concat(BootOrderInts).Concat(rest);
		foreach (var num in entryAccessOrder) {
			var (v, e) = GetEntry(num);
			if (fileName == null ? e == null : (e != null && e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))) {
				return (num, v, e);
			}
		}
		return (0xffff, null, null);
	}

	/**
	 * Get the Windows boot entry.
	 */
	public (UInt16, Efi.Variable, BootEntryData) WindowsEntry {
		get { return FindEntry(WindowsLoaderPath); }
	}

	/**
	 * Get the HackBGRT boot entry.
	 */
	public (UInt16, Efi.Variable, BootEntryData) OwnEntry {
		get { return FindEntry(OwnLoaderPath); }
	}

	/**
	 * Get a free boot entry.
	 */
	public (UInt16, Efi.Variable, BootEntryData) FreeEntry {
		get { return FindEntry(null); }
	}

	/**
	 * Disable the said boot entry from BootOrder.
	 *
	 * @param dryRun Don't actually write to NVRAM.
	 * @return True, if the entry was found in BootOrder.
	 */
	public bool DisableOwnEntry(bool dryRun = false) {
		var (ownNum, ownVar, _) = OwnEntry;
		if (ownVar == null) {
			Setup.Log("Own entry not found.");
			return false;
		}
		Setup.Log($"Old boot order: {BootOrder}");
		if (!BootOrderInts.Contains(ownNum)) {
			Setup.Log("Own entry not in BootOrder.");
		} else {
			Setup.Log($"Disabling own entry: {ownNum:X04}");
			BootOrderInts.Remove(ownNum);
			BootOrder.Data = BootOrderInts.SelectMany(num => new byte[] { (byte)(num & 0xff), (byte)(num >> 8) }).ToArray();
			Efi.SetVariable(BootOrder, dryRun);
			return true;
		}
		return false;
	}

	/**
	 * Create the boot entry.
	 *
	 * @param alwaysCopyFromMS If true, do not preserve any existing data.
	 * @param dryRun Don't actually write to NVRAM.
	 */
	public void MakeOwnEntry(bool alwaysCopyFromMS, bool dryRun = false) {
		var (msNum, msVar, msEntry) = WindowsEntry;
		var (ownNum, ownVar, ownEntry) = OwnEntry;
		if (ownVar == null) {
			(ownNum, ownVar, ownEntry) = FreeEntry;
			if (ownVar == null) {
				throw new Exception("MakeOwnEntry: No free entry.");
			}
			Setup.Log($"Creating own entry {ownNum:X4}.");
		} else {
			Setup.Log($"Updating own entry {ownNum:X4}.");
		}

		Setup.Log($"Read EFI variable: {msVar}");
		Setup.Log($"Read EFI variable: {ownVar}");
		// Make a new boot entry using the MS entry as a starting point.
		if (!alwaysCopyFromMS && ownEntry != null) {
			// Shim expects the arguments to be a filename or nothing.
			// But BCDEdit expects some Microsoft-specific data.
			// Modify the entry so that BCDEdit still recognises it
			// but the data becomes a valid UCS-2 / UTF-16LE file name.
			var str = new string(ownEntry.Arguments.Take(12).Select(c => (char) c).ToArray());
			if (str == "WINDOWS\0\x01\0\0\0") {
				ownEntry.Arguments[8] = (byte) 'X';
			} else if (str != "WINDOWS\0\x58\0\0\0") {
				// Not recognized. Clear the arguments.
				ownEntry.Arguments = new byte[0];
			}
		} else {
			if (msEntry == null) {
				throw new Exception("MakeOwnEntry: Windows Boot Manager not found.");
			}
			ownEntry = msEntry;
			ownEntry.Arguments = new byte[0];
			ownEntry.Label = "HackBGRT";
			ownEntry.FileName = OwnLoaderPath;
		}
		ownEntry.Attributes = 1; // LOAD_OPTION_ACTIVE
		ownVar.Attributes = 7; // EFI_VARIABLE_NON_VOLATILE | EFI_VARIABLE_BOOTSERVICE_ACCESS | EFI_VARIABLE_RUNTIME_ACCESS
		ownVar.Data = ownEntry.ToBytes();
		Efi.SetVariable(ownVar, dryRun);
	}

	/**
	 * Enable the own boot entry.
	 *
	 * @param dryRun Don't actually write to NVRAM.
	 */
	public void EnableOwnEntry(bool dryRun = false) {
		var (ownNum, ownVar, _) = OwnEntry;
		if (ownVar == null) {
			Setup.Log("Own entry not found.");
			return;
		}
		var (msNum, _, _) = WindowsEntry;
		var msPos = BootOrderInts.IndexOf(msNum);
		var ownPos = BootOrderInts.IndexOf(ownNum);
		var mustAdd = ownPos == -1;
		var mustMove = 0 <= msPos && msPos <= ownPos;
		Setup.Log($"Old boot order: {BootOrder}");
		if (mustAdd || mustMove) {
			Setup.Log($"Enabling own entry: {ownNum:X04}");
			if (mustMove) {
				BootOrderInts.RemoveAt(ownPos);
			}
			BootOrderInts.Insert(msPos < 0 ? 0 : msPos, ownNum);
			BootOrder.Data = BootOrderInts.SelectMany(num => new byte[] { (byte)(num & 0xff), (byte)(num >> 8) }).ToArray();
			Efi.SetVariable(BootOrder, dryRun);
		}
	}

	/**
	 * Log the boot entries.
	 */
	public void LogEntries() {
		Setup.Log($"LogEntries: {BootOrder}");
		Setup.Log($"LogEntries: {BootCurrent}");
		// Windows can't enumerate EFI variables, and trying them all is too slow.
		// BootOrder + BootCurrent + the first 0xff entries should be enough.
		var seen = new HashSet<UInt16>();
		foreach (var num in BootOrderInts.Concat(BootCurrentInts).Concat(Enumerable.Range(0, 0xff).Select(i => (UInt16) i))) {
			if (seen.Contains(num)) {
				continue;
			}
			seen.Add(num);
			var (v, e) = GetEntry(num);
			if (e != null) {
				Setup.Log($"LogEntries: {v}");
			}
		}
	}

	/**
	 * Try to log the boot entries.
	 */
	public static void TryLogEntries() {
		try {
			new EfiBootEntries().LogEntries();
		} catch (Exception e) {
			Setup.Log($"LogEntries failed: {e.ToString()}");
		}
	}
}
