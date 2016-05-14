#include <efi.h>
#include <efilib.h>

#include "types.h"
#include "config.h"
#include "util.h"

/**
 * The Print function signature.
 */
typedef UINTN print_t(IN CHAR16 *fmt, ...);

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
 * Initialize (clear) a BGRT.
 *
 * @param bgrt The BGRT to initialize.
 */
static void InitBGRT(ACPI_BGRT* bgrt) {
	const char data[0x38] = "BGRT" "\x38\x00\x00\x00" "\x00" "\xd6" "Mtblx*" "HackBGRT" "\x20\x17\x00\x00" "PTL " "\x02\x00\x00\x00" "\x01\x00" "\x00" "\x00";
	CopyMem(bgrt, data, sizeof(data));
}

/**
 * Fill a BGRT as specified by the parameters.
 *
 * @param bgrt The BGRT to fill.
 * @param new_bmp The BMP to use.
 * @param new_x The x coordinate to use.
 * @param new_y The y coordinate to use.
 */
static void FillBGRT(ACPI_BGRT* bgrt, BMP* new_bmp, int new_x, int new_y) {
	BMP* old_bmp = (BMP*) (UINTN) bgrt->image_address;
	ACPI_BGRT bgrt0 = *bgrt;
	InitBGRT(bgrt);

	if (new_bmp) {
		bgrt->image_address = (UINTN) new_bmp;
	}
	BMP* bmp = (BMP*) (UINTN) bgrt->image_address;

	// Calculate the automatically centered position for the image.
	int x_auto = 0, y_auto = 0;
	if (GOP()) {
		x_auto = max(0, ((int)GOP()->Mode->Info->HorizontalResolution - (int)bmp->width) / 2);
		y_auto = max(0, ((int)GOP()->Mode->Info->VerticalResolution * 2/3 - (int)bmp->height) / 2);
	} else if (old_bmp) {
		x_auto = max(0, (int)bgrt0.image_offset_x + ((int)old_bmp->width - (int)bmp->width) / 2);
		y_auto = max(0, (int)bgrt0.image_offset_y + ((int)old_bmp->height - (int)bmp->height) / 2);
	}

	// Set the position (manual, automatic, original).
	bgrt->image_offset_x = SelectCoordinate(new_x, x_auto, bgrt0.image_offset_x);
	bgrt->image_offset_y = SelectCoordinate(new_y, y_auto, bgrt0.image_offset_y);
	Debug(L"HackBGRT: BMP at (%d, %d).\n", (int) bgrt->image_offset_x, (int) bgrt->image_offset_y);

	bgrt->header.checksum = 0;
	bgrt->header.checksum = CalculateAcpiChecksum(bgrt, bgrt->header.length);
}

/**
 * Find the BGRT and optionally destroy it or create if missing.
 *
 * @param action The intended action.
 * @return Pointer to the BGRT, or 0 if not found (or destroyed).
 */
static ACPI_BGRT* FindBGRT(enum HackBGRT_action action) {
	ACPI_20_RSDP* good_rsdp = 0;
	ACPI_BGRT* bgrt = 0;

	for (int i = 0; i < ST->NumberOfTableEntries; i++) {
		EFI_GUID Acpi20TableGuid = ACPI_20_TABLE_GUID;
		EFI_GUID* vendor_guid = &ST->ConfigurationTable[i].VendorGuid;
		if (!CompareGuid(vendor_guid, &AcpiTableGuid) && !CompareGuid(vendor_guid, &Acpi20TableGuid)) {
			continue;
		}
		EFI_CONFIGURATION_TABLE *ect = &ST->ConfigurationTable[i];
		if (CompareMem(ect->VendorTable, "RSD PTR ", 8) != 0) {
			continue;
		}
		ACPI_20_RSDP* rsdp = (ACPI_20_RSDP *)ect->VendorTable;
		Debug(L"RSDP: revision = %d, OEM ID = %s\n", rsdp->revision, TmpStr(rsdp->oem_id, 6));

		if (rsdp->revision < 2) {
			Debug(L"* XSDT: N/A (revision < 2)\n");
			continue;
		}
		ACPI_SDT_HEADER* xsdt = (ACPI_SDT_HEADER *) (UINTN) rsdp->xsdt_address;
		if (!xsdt) {
			Debug(L"* XSDT: N/A (null)\n");
			continue;
		}
		if (CompareMem(xsdt->signature, "XSDT", 4) != 0) {
			Debug(L"* XSDT: N/A (invalid signature)\n");
			continue;
		}
		good_rsdp = rsdp;
		UINT64* entry_arr = (UINT64*)&xsdt[1];
		UINT32 entry_arr_length = (xsdt->length - sizeof(*xsdt)) / sizeof(UINT64);

		Debug(L"* XSDT: OEM ID = %s, entry count = %d\n", TmpStr(xsdt->oem_id, 6), entry_arr_length);

		for (int j = 0; j < entry_arr_length; j++) {
			ACPI_SDT_HEADER *entry = (ACPI_SDT_HEADER *)((UINTN)entry_arr[j]);
			Debug(L" - ACPI table: %s, revision = %d, OEM ID = %s\n", TmpStr(entry->signature, 4), entry->revision, TmpStr(entry->oem_id, 6));
			if (CompareMem(entry->signature, "BGRT", 4) == 0) {
				if (!bgrt && action != HackBGRT_REMOVE) {
					bgrt = (void*) entry;
				} else {
					if (bgrt) {
						Debug(L" -> Deleting; BGRT was already found!\n");
					} else {
						Debug(L" -> Deleting.\n");
					}
					for (int k = j+1; k < entry_arr_length; ++k) {
						entry_arr[k-1] = entry_arr[k];
					}
					--entry_arr_length;
					entry_arr[entry_arr_length] = 0;
					xsdt->length -= sizeof(entry_arr[0]);
					--j;
				}
			}
		}
	}
	if (action == HackBGRT_REMOVE) {
		return 0;
	}
	if (!good_rsdp) {
		Print(L"HackBGRT: RSDP or XSDT not found.\n");
		return 0;
	}
	if (!bgrt) {
		if (action == HackBGRT_KEEP) {
			Print(L"HackBGRT: BGRT not found.\n");
			return 0;
		}
		Debug(L"HackBGRT: BGRT not found, creating.\n");
		ACPI_20_RSDP* rsdp = good_rsdp;
		ACPI_SDT_HEADER* xsdt0 = (ACPI_SDT_HEADER *) (UINTN) rsdp->xsdt_address;
		ACPI_SDT_HEADER* xsdt = 0;
		UINT32 xsdt_len = xsdt0->length + sizeof(UINT64);
		BS->AllocatePool(EfiACPIReclaimMemory, xsdt_len, (void**)&xsdt);
		BS->AllocatePool(EfiACPIReclaimMemory, sizeof(*bgrt), (void**)&bgrt);
		if (!xsdt || !bgrt) {
			Print(L"HackBGRT: Failed to allocate memory for XSDT and BGRT.\n");
			return 0;
		}
		rsdp->xsdt_address = (UINTN) xsdt;
		CopyMem(xsdt, xsdt0, xsdt0->length);
		*(UINT64*)((char*)xsdt + xsdt->length) = (UINTN) bgrt;
		xsdt->length = xsdt_len;
		InitBGRT(bgrt);
	}
	return bgrt;
}

/**
 * Load a bitmap or generate one, or return 0 if not applicable.
 *
 * @param action Tells what to do.
 * @param root_dir The root directory for loading a BMP.
 * @param path The BMP path within the root directory.
 * @return The loaded BMP, or 0 if not needed or not available.
 */
static BMP* LoadBMP(enum HackBGRT_action action, EFI_FILE_HANDLE root_dir, const CHAR16* path) {
	BMP* bmp = 0;
	if (action == HackBGRT_KEEP || action == HackBGRT_REMOVE) {
		return 0;
	}
	if (action == HackBGRT_BLACK) {
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
	if (!path) {
		Print(L"HackBGRT: Missing BMP path. REPORT THIS BUG!");
		return 0;
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

	BMP* new_bmp = LoadBMP(config.action, root_dir, config.image_path);
	ACPI_BGRT* bgrt = FindBGRT(config.action);
	if (bgrt) {
		FillBGRT(bgrt, new_bmp, config.image_x, config.image_y);
	}

	if (!config.boot_path) {
		Print(L"HackBGRT: Boot path not specified.\n");
		goto fail;
	}

	Debug(L"HackBGRT: Loading and booting %s.\n", config.boot_path);
	EFI_DEVICE_PATH* boot_dp = FileDevicePath(image->DeviceHandle, (CHAR16*) config.boot_path);
	EFI_HANDLE next_image_handle;
	if (EFI_ERROR(BS->LoadImage(0, image_handle, boot_dp, 0, 0, &next_image_handle))) {
		Print(L"HackBGRT: LoadImage for new image (%s) failed.\n", config.boot_path);
		goto fail;
	}
	if (EFI_ERROR(BS->StartImage(next_image_handle, 0, 0))) {
		Print(L"HackBGRT: StartImage for %s failed.\n", config.boot_path);
		goto fail;
	}
	Print(L"HackBGRT: Started %s. Why are we still here?!\n", config.boot_path);
	goto fail;

	fail: {
		Print(L"HackBGRT has failed. Use parameter debug=1 for details.\nPress any key to exit.\n");
		WaitKey();
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
