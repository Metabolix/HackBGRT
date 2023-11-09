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
		config.old_resolution_x = config.resolution_x = 0;
		config.old_resolution_y = config.resolution_y = 0;
		Debug(L"GOP not found!\n");
		return;
	}
	UINTN best_i = gop->Mode->Mode;
	int best_w = config.old_resolution_x = gop->Mode->Info->HorizontalResolution;
	int best_h = config.old_resolution_y = gop->Mode->Info->VerticalResolution;
	w = (w <= 0 ? w < 0 ? best_w : 999999 : w);
	h = (h <= 0 ? h < 0 ? best_h : 999999 : h);

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
	config.resolution_x = best_w;
	config.resolution_y = best_h;
	if (best_i != gop->Mode->Mode) {
		gop->SetMode(gop, best_i);
	}
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
		Debug(L"RSDP @%x: revision = %d, OEM ID = %s\n", (UINTN)rsdp, rsdp->revision, TmpStr(rsdp->oem_id, 6));

		ACPI_SDT_HEADER* xsdt = (ACPI_SDT_HEADER *) (UINTN) rsdp->xsdt_address;
		if (!xsdt || CompareMem(xsdt->signature, "XSDT", 4) != 0 || !VerifyAcpiSdtChecksum(xsdt)) {
			Debug(L"* XSDT: missing or invalid\n");
			continue;
		}
		UINT64* entry_arr = (UINT64*)&xsdt[1];
		UINT32 entry_arr_length = (xsdt->length - sizeof(*xsdt)) / sizeof(UINT64);

		Debug(L"* XSDT @%x: OEM ID = %s, entry count = %d\n", (UINTN)xsdt, TmpStr(xsdt->oem_id, 6), entry_arr_length);

		int bgrt_count = 0;
		for (int j = 0; j < entry_arr_length; j++) {
			ACPI_SDT_HEADER *entry = (ACPI_SDT_HEADER *)((UINTN)entry_arr[j]);
			if (CompareMem(entry->signature, "BGRT", 4) != 0) {
				continue;
			}
			Debug(L" - ACPI table @%x: %s, revision = %d, OEM ID = %s\n", (UINTN)entry, TmpStr(entry->signature, 4), entry->revision, TmpStr(entry->oem_id, 6));
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
 * Generate a BMP with the given size and color.
 *
 * @param w The width.
 * @param h The height.
 * @param r The red component.
 * @param g The green component.
 * @param b The blue component.
 * @return The generated BMP, or 0 on failure.
 */
static BMP* MakeBMP(int w, int h, UINT8 r, UINT8 g, UINT8 b) {
	BMP* bmp = 0;
	BS->AllocatePool(EfiBootServicesData, 54 + w * h * 4, (void**) &bmp);
	if (!bmp) {
		Print(L"HackBGRT: Failed to allocate a blank BMP!\n");
		BS->Stall(1000000);
		return 0;
	}
	*bmp = (BMP) {
		.magic_BM = { 'B', 'M' },
		.file_size = 54 + w * h * 4,
		.pixel_data_offset = 54,
		.dib_header_size = 40,
		.width = w,
		.height = h,
		.planes = 1,
		.bpp = 32,
	};
	UINT8* data = (UINT8*) bmp + bmp->pixel_data_offset;
	for (int y = 0; y < h; ++y) for (int x = 0; x < w; ++x) {
		*data++ = b;
		*data++ = g;
		*data++ = r;
		*data++ = 0;
	}
	return bmp;
}

/**
 * Load a bitmap or generate a black one.
 *
 * @param root_dir The root directory for loading a BMP.
 * @param path The BMP path within the root directory; NULL for a black BMP.
 * @return The loaded BMP, or 0 if not available.
 */
static BMP* LoadBMP(EFI_FILE_HANDLE root_dir, const CHAR16* path) {
	if (!path) {
		return MakeBMP(1, 1, 0, 0, 0); // empty path = black image
	}
	Debug(L"HackBGRT: Loading %s.\n", path);
	UINTN size = 0;
	BMP* bmp = LoadFile(root_dir, path, &size);
	if (bmp) {
		if (size >= bmp->file_size && CompareMem(bmp, "BM", 2) == 0 && bmp->file_size - bmp->pixel_data_offset > 4 && bmp->width && bmp->height && (bmp->bpp == 32 || bmp->bpp == 24) && bmp->compression == 0) {
			return bmp;
		}
		FreePool(bmp);
		Print(L"HackBGRT: Invalid BMP (%s)!\n", path);
	} else {
		Print(L"HackBGRT: Failed to load BMP (%s)!\n", path);
	}
	BS->Stall(1000000);
	return MakeBMP(16, 16, 255, 0, 0); // error = red image
}

/**
 * Crop a BMP to the given size.
 *
 * @param bmp The BMP to crop.
 * @param w The maximum width.
 * @param h The maximum height.
 */
static void CropBMP(BMP* bmp, int w, int h) {
	const int old_pitch = -(-(bmp->width * (bmp->bpp / 8)) & ~3);
	bmp->image_size = 0;
	bmp->width = min(bmp->width, w);
	bmp->height = min(bmp->height, h);
	const int h_max = (bmp->file_size - bmp->pixel_data_offset) / old_pitch;
	bmp->height = min(bmp->height, h_max);
	const int new_pitch = -(-(bmp->width * (bmp->bpp / 8)) & ~3);

	if (new_pitch < old_pitch) {
		for (int i = 1; i < bmp->height; ++i) {
			CopyMem(
				(UINT8*) bmp + bmp->pixel_data_offset + i * new_pitch,
				(UINT8*) bmp + bmp->pixel_data_offset + i * old_pitch,
				new_pitch
			);
		}
	}
	bmp->file_size = bmp->pixel_data_offset + bmp->height * new_pitch;
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

	// Get the old BMP and position (relative to screen center), if possible.
	const int old_valid = bgrt && VerifyAcpiSdtChecksum(bgrt);
	BMP* old_bmp = old_valid ? (BMP*) (UINTN) bgrt->image_address : 0;
	const int old_orientation = old_valid ? ((bgrt->status >> 1) & 3) : 0;
	const int old_swap = old_orientation & 1;
	const int old_reso_x = old_swap ? config.old_resolution_y : config.old_resolution_x;
	const int old_reso_y = old_swap ? config.old_resolution_x : config.old_resolution_y;
	const int old_x = old_bmp ? bgrt->image_offset_x + (old_bmp->width - old_reso_x) / 2 : 0;
	const int old_y = old_bmp ? bgrt->image_offset_y + (old_bmp->height - old_reso_y) / 2 : 0;

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

	*bgrt = (ACPI_BGRT) {
		.header = {
			.signature = "BGRT",
			.length = sizeof(*bgrt),
			.revision = 1,
			.oem_id = "Mtblx*",
			.oem_table_id = "HackBGRT",
			.oem_revision = 1,
			.asl_compiler_id = *(const UINT32*) "None",
			.asl_compiler_revision = 1,
		},
		.version = 1,
	};

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

	// Crop the image to screen.
	CropBMP(new_bmp, config.resolution_x, config.resolution_y);

	// Set the image address and orientation.
	bgrt->image_address = (UINTN) new_bmp;
	const int new_orientation = config.orientation == HackBGRT_coord_keep ? old_orientation : ((config.orientation / 90) & 3);
	bgrt->status = new_orientation << 1;

	// New center coordinates.
	const int new_x = config.image_x == HackBGRT_coord_keep ? old_x : config.image_x;
	const int new_y = config.image_y == HackBGRT_coord_keep ? old_y : config.image_y;
	const int new_swap = new_orientation & 1;
	const int new_reso_x = new_swap ? config.resolution_y : config.resolution_x;
	const int new_reso_y = new_swap ? config.resolution_x : config.resolution_y;

	// Calculate absolute position.
	const int max_x = new_reso_x - new_bmp->width;
	const int max_y = new_reso_y - new_bmp->height;
	bgrt->image_offset_x = max(0, min(max_x, new_x + (new_reso_x - new_bmp->width) / 2));
	bgrt->image_offset_y = max(0, min(max_y, new_y + (new_reso_y - new_bmp->height) / 2));

	Debug(
		L"HackBGRT: BMP at (%d, %d), center (%d, %d), resolution (%d, %d) with orientation %d applied.\n",
		(int) bgrt->image_offset_x, (int) bgrt->image_offset_y,
		new_x, new_y, new_reso_x, new_reso_y,
		new_orientation * 90
	);

	// Store this BGRT in the ACPI tables.
	SetAcpiSdtChecksum(bgrt);
	HandleAcpiTables(HackBGRT_REPLACE, bgrt);
}

/**
 * Load an application.
 */
static EFI_HANDLE LoadApp(print_t* print_failure, EFI_HANDLE image_handle, EFI_LOADED_IMAGE* image, const CHAR16* path) {
	EFI_DEVICE_PATH* boot_dp = FileDevicePath(image->DeviceHandle, (CHAR16*) path);
	EFI_HANDLE result = 0;
	Debug(L"HackBGRT: Loading application %s.\n", path);
	if (EFI_ERROR(BS->LoadImage(0, image_handle, boot_dp, 0, 0, &result))) {
		print_failure(L"HackBGRT: Failed to load application %s.\n", path);
	}
	return result;
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
	static CHAR16 backup_boot_path[] = L"\\EFI\\HackBGRT\\bootmgfw-original.efi";
	static CHAR16 ms_boot_path[] = L"\\EFI\\Microsoft\\Boot\\bootmgfw.efi";

	if (config.boot_path && StriCmp(config.boot_path, L"MS") != 0) {
		next_image_handle = LoadApp(Print, image_handle, image, config.boot_path);
	} else {
		config.boot_path = backup_boot_path;
		next_image_handle = LoadApp(Debug, image_handle, image, config.boot_path);
		if (!next_image_handle) {
			config.boot_path = ms_boot_path;
			next_image_handle = LoadApp(Debug, image_handle, image, config.boot_path);
		}
	}
	if (!next_image_handle) {
		config.boot_path = backup_boot_path;
		next_image_handle = LoadApp(Print, image_handle, image, config.boot_path);
		if (!next_image_handle) {
			config.boot_path = ms_boot_path;
			next_image_handle = LoadApp(Print, image_handle, image, config.boot_path);
			if (!next_image_handle) {
				goto fail;
			}
		}
		Print(L"HackBGRT: Reverting to %s.\n", config.boot_path);
		Print(L"Press escape to cancel or any other key (or wait 15 seconds) to boot.\n");
		if (ReadKey(15000).ScanCode == SCAN_ESC) {
			goto fail;
		}
	} else if (config.debug) {
		Print(L"HackBGRT: Ready to boot. Disable debug mode to skip this screen.\n");
		Print(L"Press escape to cancel or any other key (or wait 15 seconds) to boot.\n");
		if (ReadKey(15000).ScanCode == SCAN_ESC) {
			return 0;
		}
	}
	if (EFI_ERROR(BS->StartImage(next_image_handle, 0, 0))) {
		Print(L"HackBGRT: Failed to start %s.\n", config.boot_path);
		goto fail;
	}
	Print(L"HackBGRT: Started %s. Why are we still here?!\n", config.boot_path);
	Print(L"Please check that %s is not actually HackBGRT!\n", config.boot_path);
	goto fail;

	fail: {
		Print(L"HackBGRT has failed. Use parameter debug=1 for details.\n");
		Print(L"Get a Windows install disk or a recovery disk to fix your boot.\n");
		#ifdef GIT_DESCRIBE_W
			Print(L"HackBGRT version: " GIT_DESCRIBE_W L"\n");
		#else
			Print(L"HackBGRT version: unknown; not an official release?\n");
		#endif
		Print(L"Press any key (or wait 15 seconds) to exit.\n");
		ReadKey(15000);
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
