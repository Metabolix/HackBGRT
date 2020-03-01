#include <efi.h>
#include <efilib.h>

#include "types.h"
#include "config.h"
#include "util.h"

/**
 * The Print function signature.
 */
typedef UINTN print_t(IN CONST CHAR16 *fmt, ...);

/**
 * The function for debug printing; either Print or NullPrint.
 */
print_t* Debug = NullPrint;

/**
 * The configuration.
 */
static struct HackBGRT_config config = {
	.action = HackBGRT_KEEP
};

/**
 * Get the GOP (Graphics Output Protocol) pointer.
 */
static EFI_GRAPHICS_OUTPUT_PROTOCOL* GOP(void) {
	static EFI_GRAPHICS_OUTPUT_PROTOCOL* gop;
	if (!gop) {
		EFI_GUID GraphicsOutputProtocolGuid = EFI_GRAPHICS_OUTPUT_PROTOCOL_GUID;
		LibLocateProtocol(&GraphicsOutputProtocolGuid, (VOID **)&gop);
	}
	return gop;
}

/**
 * Set screen resolution. If there is no exact match, try to find a bigger one.
 *
 * @param w Horizontal resolution. 0 for max, -1 for current.
 * @param h Vertical resolution. 0 for max, -1 for current.
 */
static void SetResolution(int w, int h) {
	EFI_GRAPHICS_OUTPUT_PROTOCOL* gop = GOP();
	if (!gop) {
		Debug(L"GOP not found!\n");
		return;
	}
	UINTN best_i = gop->Mode->Mode;
	int best_w = gop->Mode->Info->HorizontalResolution;
	int best_h = gop->Mode->Info->VerticalResolution;
	w = (w <= 0 ? w < 0 ? best_w : 0x7fffffff : w);
	h = (h <= 0 ? h < 0 ? best_h : 0x7fffffff : h);

	Debug(L"Looking for resolution %dx%d...\n", w, h);
	for (UINT32 i = gop->Mode->MaxMode; i--;) {
		int new_w = 0, new_h = 0;

		EFI_GRAPHICS_OUTPUT_MODE_INFORMATION* info = 0;
		UINTN info_size;
		if (EFI_ERROR(gop->QueryMode(gop, i, &info_size, &info))) {
			continue;
		}
		if (info_size < sizeof(*info)) {
			FreePool(info);
			continue;
		}
		new_w = info->HorizontalResolution;
		new_h = info->VerticalResolution;
		FreePool(info);

		// Sum of missing w/h should be minimal.
		int new_missing = max(w - new_w, 0) + max(h - new_h, 0);
		int best_missing = max(w - best_w, 0) + max(h - best_h, 0);
		if (new_missing > best_missing) {
			continue;
		}
		// Sum of extra w/h should be minimal.
		int new_over = max(-w + new_w, 0) + max(-h + new_h, 0);
		int best_over = max(-w + best_w, 0) + max(-h + best_h, 0);
		if (new_missing == best_missing && new_over >= best_over) {
			continue;
		}
		best_w = new_w;
		best_h = new_h;
		best_i = i;
	}
	Debug(L"Found resolution %dx%d.\n", best_w, best_h);
	if (best_i != gop->Mode->Mode) {
		gop->SetMode(gop, best_i);
	}
}

/**
 * Select the correct coordinate (manual, automatic, native)
 *
 * @param value The configured coordinate value; has special values for automatic and native.
 * @param automatic The automatically calculated alternative.
 * @param native The original coordinate.
 * @see enum HackBGRT_coordinate
 */
static int SelectCoordinate(int value, int automatic, int native) {
	if (value == HackBGRT_coord_auto) {
		return automatic;
	}
	if (value == HackBGRT_coord_native) {
		return native;
	}
	return value;
}

/**
 * Create a new XSDT with the given number of entries.
 *
 * @param xsdt0 The old XSDT.
 * @param entries The number of SDT entries.
 * @return Pointer to a new XSDT.
 */
ACPI_SDT_HEADER* CreateXsdt(ACPI_SDT_HEADER* xsdt0, UINTN entries) {
	ACPI_SDT_HEADER* xsdt = 0;
	UINT32 xsdt_len = sizeof(ACPI_SDT_HEADER) + entries * sizeof(UINT64);
	BS->AllocatePool(EfiACPIReclaimMemory, xsdt_len, (void**)&xsdt);
	if (!xsdt) {
		Print(L"HackBGRT: Failed to allocate memory for XSDT.\n");
		return 0;
	}
	ZeroMem(xsdt, xsdt_len);
	CopyMem(xsdt, xsdt0, min(xsdt0->length, xsdt_len));
	xsdt->length = xsdt_len;
	SetAcpiSdtChecksum(xsdt);
	return xsdt;
}

/**
 * Update the ACPI tables as needed for the desired BGRT change.
 *
 * If action is REMOVE, all BGRT entries will be removed.
 * If action is KEEP, the first BGRT entry will be returned.
 * If action is REPLACE, the given BGRT entry will be stored in each XSDT.
 *
 * @param action The intended action.
 * @param bgrt The BGRT, if action is REPLACE.
 * @return Pointer to the BGRT, or 0 if not found (or destroyed).
 */
static ACPI_BGRT* HandleAcpiTables(enum HackBGRT_action action, ACPI_BGRT* bgrt) {
	for (int i = 0; i < ST->NumberOfTableEntries; i++) {
		EFI_GUID Acpi20TableGuid = ACPI_20_TABLE_GUID;
		EFI_GUID* vendor_guid = &ST->ConfigurationTable[i].VendorGuid;
		if (!CompareGuid(vendor_guid, &AcpiTableGuid) && !CompareGuid(vendor_guid, &Acpi20TableGuid)) {
			continue;
		}
		ACPI_20_RSDP* rsdp = (ACPI_20_RSDP *) ST->ConfigurationTable[i].VendorTable;
		if (CompareMem(rsdp->signature, "RSD PTR ", 8) != 0 || rsdp->revision < 2 || !VerifyAcpiRsdp2Checksums(rsdp)) {
			continue;
		}
		Debug(L"RSDP: revision = %d, OEM ID = %s\n", rsdp->revision, TmpStr(rsdp->oem_id, 6));

		ACPI_SDT_HEADER* xsdt = (ACPI_SDT_HEADER *) (UINTN) rsdp->xsdt_address;
		if (!xsdt || CompareMem(xsdt->signature, "XSDT", 4) != 0 || !VerifyAcpiSdtChecksum(xsdt)) {
			Debug(L"* XSDT: missing or invalid\n");
			continue;
		}
		UINT64* entry_arr = (UINT64*)&xsdt[1];
		UINT32 entry_arr_length = (xsdt->length - sizeof(*xsdt)) / sizeof(UINT64);

		Debug(L"* XSDT: OEM ID = %s, entry count = %d\n", TmpStr(xsdt->oem_id, 6), entry_arr_length);

		int bgrt_count = 0;
		for (int j = 0; j < entry_arr_length; j++) {
			ACPI_SDT_HEADER *entry = (ACPI_SDT_HEADER *)((UINTN)entry_arr[j]);
			if (CompareMem(entry->signature, "BGRT", 4) != 0) {
				continue;
			}
			Debug(L" - ACPI table: %s, revision = %d, OEM ID = %s\n", TmpStr(entry->signature, 4), entry->revision, TmpStr(entry->oem_id, 6));
			switch (action) {
				case HackBGRT_KEEP:
					if (!bgrt) {
						Debug(L" -> Returning this one for later use.\n");
						bgrt = (ACPI_BGRT*) entry;
					}
					break;
				case HackBGRT_REMOVE:
					Debug(L" -> Deleting.\n");
					for (int k = j+1; k < entry_arr_length; ++k) {
						entry_arr[k-1] = entry_arr[k];
					}
					--entry_arr_length;
					entry_arr[entry_arr_length] = 0;
					xsdt->length -= sizeof(entry_arr[0]);
					--j;
					break;
				case HackBGRT_REPLACE:
					Debug(L" -> Replacing.\n");
					entry_arr[j] = (UINTN) bgrt;
			}
			bgrt_count += 1;
		}
		if (!bgrt_count && action == HackBGRT_REPLACE && bgrt) {
			Debug(L" - Adding missing BGRT.\n");
			xsdt = CreateXsdt(xsdt, entry_arr_length + 1);
			entry_arr = (UINT64*)&xsdt[1];
			entry_arr[entry_arr_length++] = (UINTN) bgrt;
			rsdp->xsdt_address = (UINTN) xsdt;
			SetAcpiRsdp2Checksums(rsdp);
		}
		SetAcpiSdtChecksum(xsdt);
	}
	return bgrt;
}

/**
 * Load a bitmap or generate a black one.
 *
 * @param root_dir The root directory for loading a BMP.
 * @param path The BMP path within the root directory; NULL for a black BMP.
 * @return The loaded BMP, or 0 if not available.
 */
static BMP* LoadBMP(EFI_FILE_HANDLE root_dir, const CHAR16* path) {
	BMP* bmp = 0;
	if (!path) {
		BS->AllocatePool(EfiBootServicesData, 58, (void**) &bmp);
		if (!bmp) {
			Print(L"HackBGRT: Failed to allocate a blank BMP!\n");
			BS->Stall(1000000);
			return 0;
		}
		CopyMem(
			bmp,
			"\x42\x4d\x3a\x00\x00\x00\x00\x00\x00\x00\x36\x00\x00\x00\x28\x00"
			"\x00\x00\x01\x00\x00\x00\x01\x00\x00\x00\x01\x00\x18\x00\x00\x00"
			"\x00\x00\x04\x00\x00\x00\x13\x0b\x00\x00\x13\x0b\x00\x00\x00\x00"
			"\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00",
			58
		);
		return bmp;
	}
	Debug(L"HackBGRT: Loading %s.\n", path);
	bmp = LoadFile(root_dir, path, 0);
	if (!bmp) {
		Print(L"HackBGRT: Failed to load BMP (%s)!\n", path);
		BS->Stall(1000000);
		return 0;
	}
	return bmp;
}

/**
 * The main logic for BGRT modification.
 *
 * @param root_dir The root directory for loading a BMP.
 */
void HackBgrt(EFI_FILE_HANDLE root_dir) {
	// REMOVE: simply delete all BGRT entries.
	if (config.action == HackBGRT_REMOVE) {
		HandleAcpiTables(config.action, 0);
		return;
	}

	// KEEP/REPLACE: first get the old BGRT entry.
	ACPI_BGRT* bgrt = HandleAcpiTables(HackBGRT_KEEP, 0);

	// Get the old BMP and position, if possible.
	BMP* old_bmp = 0;
	int old_x = 0, old_y = 0;
	if (bgrt && VerifyAcpiSdtChecksum(bgrt)) {
		old_bmp = (BMP*) (UINTN) bgrt->image_address;
		old_x = bgrt->image_offset_x;
		old_y = bgrt->image_offset_y;
	}

	// Missing BGRT?
	if (!bgrt) {
		// Keep missing = do nothing.
		if (config.action == HackBGRT_KEEP) {
			return;
		}
		// Replace missing = allocate new.
		BS->AllocatePool(EfiACPIReclaimMemory, sizeof(*bgrt), (void**)&bgrt);
		if (!bgrt) {
			Print(L"HackBGRT: Failed to allocate memory for BGRT.\n");
			return;
		}
	}

	// Clear the BGRT.
	const char data[0x38] =
		"BGRT" "\x38\x00\x00\x00" "\x00" "\xd6" "Mtblx*" "HackBGRT"
		"\x20\x17\x00\x00" "PTL " "\x02\x00\x00\x00"
		"\x01\x00" "\x00" "\x00";
	CopyMem(bgrt, data, sizeof(data));

	// Get the image (either old or new).
	BMP* new_bmp = old_bmp;
	if (config.action == HackBGRT_REPLACE) {
		new_bmp = LoadBMP(root_dir, config.image_path);
	}

	// No image = no need for BGRT.
	if (!new_bmp) {
		HandleAcpiTables(HackBGRT_REMOVE, 0);
		return;
	}

	bgrt->image_address = (UINTN) new_bmp;

	// Calculate the automatically centered position for the image.
	int auto_x = 0, auto_y = 0;
	if (GOP()) {
		auto_x = max(0, ((int)GOP()->Mode->Info->HorizontalResolution - (int)new_bmp->width) / 2);
		auto_y = max(0, ((int)GOP()->Mode->Info->VerticalResolution * 2/3 - (int)new_bmp->height) / 2);
	} else if (old_bmp) {
		auto_x = max(0, old_x + ((int)old_bmp->width - (int)new_bmp->width) / 2);
		auto_y = max(0, old_y + ((int)old_bmp->height - (int)new_bmp->height) / 2);
	}

	// Set the position (manual, automatic, original).
	bgrt->image_offset_x = SelectCoordinate(config.image_x, auto_x, old_x);
	bgrt->image_offset_y = SelectCoordinate(config.image_y, auto_y, old_y);
	Debug(L"HackBGRT: BMP at (%d, %d).\n", (int) bgrt->image_offset_x, (int) bgrt->image_offset_y);

	// Store this BGRT in the ACPI tables.
	SetAcpiSdtChecksum(bgrt);
	HandleAcpiTables(HackBGRT_REPLACE, bgrt);
}

/**
 * The main program.
 */
EFI_STATUS EFIAPI EfiMain(EFI_HANDLE image_handle, EFI_SYSTEM_TABLE *ST_) {
	InitializeLib(image_handle, ST_);

	EFI_LOADED_IMAGE* image;
	if (EFI_ERROR(BS->HandleProtocol(image_handle, &LoadedImageProtocol, (void**) &image))) {
		Debug(L"HackBGRT: LOADED_IMAGE_PROTOCOL failed.\n");
		goto fail;
	}

	EFI_FILE_HANDLE root_dir = LibOpenRoot(image->DeviceHandle);

	CHAR16 **argv;
	int argc = GetShellArgcArgv(image_handle, &argv);

	if (argc <= 1) {
		const CHAR16* config_path = L"\\EFI\\HackBGRT\\config.txt";
		if (!ReadConfigFile(&config, root_dir, config_path)) {
			Print(L"HackBGRT: No config, no command line!\n", config_path);
			goto fail;
		}
	}
	for (int i = 1; i < argc; ++i) {
		ReadConfigLine(&config, root_dir, argv[i]);
	}
	Debug = config.debug ? Print : NullPrint;

	SetResolution(config.resolution_x, config.resolution_y);
	HackBgrt(root_dir);

	EFI_HANDLE next_image_handle = 0;
	if (!config.boot_path) {
		Print(L"HackBGRT: Boot path not specified.\n");
	} else {
		Debug(L"HackBGRT: Loading application %s.\n", config.boot_path);
		EFI_DEVICE_PATH* boot_dp = FileDevicePath(image->DeviceHandle, (CHAR16*) config.boot_path);
		if (EFI_ERROR(BS->LoadImage(0, image_handle, boot_dp, 0, 0, &next_image_handle))) {
			Print(L"HackBGRT: Failed to load application %s.\n", config.boot_path);
		}
	}
	if (!next_image_handle) {
		static CHAR16 default_boot_path[] = L"\\EFI\\HackBGRT\\bootmgfw-original.efi";
		Debug(L"HackBGRT: Loading application %s.\n", default_boot_path);
		EFI_DEVICE_PATH* boot_dp = FileDevicePath(image->DeviceHandle, default_boot_path);
		if (EFI_ERROR(BS->LoadImage(0, image_handle, boot_dp, 0, 0, &next_image_handle))) {
			Print(L"HackBGRT: Also failed to load application %s.\n", default_boot_path);
			goto fail;
		}
		Print(L"HackBGRT: Reverting to %s.\n", default_boot_path);
		Print(L"Press escape to cancel, any other key to boot.\n");
		if (ReadKey().ScanCode == SCAN_ESC) {
			goto fail;
		}
		config.boot_path = default_boot_path;
	}
	if (config.debug) {
		Print(L"HackBGRT: Ready to boot.\nPress escape to cancel, any other key to boot.\n");
		if (ReadKey().ScanCode == SCAN_ESC) {
			return 0;
		}
	}
	if (EFI_ERROR(BS->StartImage(next_image_handle, 0, 0))) {
		Print(L"HackBGRT: Failed to start %s.\n", config.boot_path);
		goto fail;
	}
	Print(L"HackBGRT: Started %s. Why are we still here?!\n", config.boot_path);
	goto fail;

	fail: {
		Print(L"HackBGRT has failed. Use parameter debug=1 for details.\n");
		Print(L"Get a Windows install disk or a recovery disk to fix your boot.\n");
		#ifdef GIT_DESCRIBE
			Print(L"HackBGRT version: " GIT_DESCRIBE L"\n");
		#else
			Print(L"HackBGRT version: unknown; not an official release?\n");
		#endif
		Print(L"Press any key to exit.\n");
		ReadKey();
		return 1;
	}
}

/**
 * Forward to EfiMain.
 *
 * Some compilers and architectures differ in underscore handling. This helps.
 */
EFI_STATUS EFIAPI _EfiMain(EFI_HANDLE image_handle, EFI_SYSTEM_TABLE *ST_) {
	return EfiMain(image_handle, ST_);
}
