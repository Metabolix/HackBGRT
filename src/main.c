#include <efi.h>
#include <efilib.h>

#include "types.h"
#include "config.h"
#include "util.h"

/**
 * The version.
 */
#ifdef GIT_DESCRIBE_W
	const CHAR16 version[] = GIT_DESCRIBE_W;
#else
	const CHAR16 version[] = L"unknown; not an official release?";
#endif

/**
 * The configuration.
 */
static struct HackBGRT_config config = {
	.log = 1,
	.action = HackBGRT_KEEP,
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
		Log(config.debug, L"GOP not found!\n");
		return;
	}
	UINTN best_i = gop->Mode->Mode;
	int best_w = config.old_resolution_x = gop->Mode->Info->HorizontalResolution;
	int best_h = config.old_resolution_y = gop->Mode->Info->VerticalResolution;
	w = (w <= 0 ? w < 0 ? best_w : 999999 : w);
	h = (h <= 0 ? h < 0 ? best_h : 999999 : h);

	Log(config.debug, L"Looking for resolution %dx%d...\n", w, h);
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
	Log(config.debug, L"Found resolution %dx%d.\n", best_w, best_h);
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
		Log(1, L"HackBGRT: Failed to allocate memory for XSDT.\n");
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
		Log(config.debug, L"RSDP @%x: revision = %d, OEM ID = %s\n", (UINTN)rsdp, rsdp->revision, TmpStr(rsdp->oem_id, 6));

		ACPI_SDT_HEADER* xsdt = (ACPI_SDT_HEADER *) (UINTN) rsdp->xsdt_address;
		if (!xsdt || CompareMem(xsdt->signature, "XSDT", 4) != 0 || !VerifyAcpiSdtChecksum(xsdt)) {
			Log(config.debug, L"* XSDT: missing or invalid\n");
			continue;
		}
		UINT64* entry_arr = (UINT64*)&xsdt[1];
		UINT32 entry_arr_length = (xsdt->length - sizeof(*xsdt)) / sizeof(UINT64);

		Log(config.debug, L"* XSDT @%x: OEM ID = %s, entry count = %d\n", (UINTN)xsdt, TmpStr(xsdt->oem_id, 6), entry_arr_length);

		int bgrt_count = 0;
		for (int j = 0; j < entry_arr_length; j++) {
			ACPI_SDT_HEADER *entry = (ACPI_SDT_HEADER *)((UINTN)entry_arr[j]);
			if (CompareMem(entry->signature, "BGRT", 4) != 0) {
				continue;
			}
			Log(config.debug, L" - ACPI table @%x: %s, revision = %d, OEM ID = %s\n", (UINTN)entry, TmpStr(entry->signature, 4), entry->revision, TmpStr(entry->oem_id, 6));
			switch (action) {
				case HackBGRT_KEEP:
					if (!bgrt) {
						Log(config.debug, L" -> Returning this one for later use.\n");
						bgrt = (ACPI_BGRT*) entry;
					}
					break;
				case HackBGRT_REMOVE:
					Log(config.debug, L" -> Deleting.\n");
					for (int k = j+1; k < entry_arr_length; ++k) {
						entry_arr[k-1] = entry_arr[k];
					}
					--entry_arr_length;
					entry_arr[entry_arr_length] = 0;
					xsdt->length -= sizeof(entry_arr[0]);
					--j;
					break;
				case HackBGRT_REPLACE:
					Log(config.debug, L" -> Replacing.\n");
					entry_arr[j] = (UINTN) bgrt;
			}
			bgrt_count += 1;
		}
		if (!bgrt_count && action == HackBGRT_REPLACE && bgrt) {
			Log(config.debug, L" - Adding missing BGRT.\n");
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
 * Plot Dot pixel
 */
static void plot_dot(const uint32_t x, const uint32_t y, const UINT8 r, const UINT8 g, const UINT8 b)
{
	EFI_GRAPHICS_OUTPUT_PROTOCOL* gop = GOP();
	if (!gop) {
		// Debug(L"GOP not found!\n");
		return;
	}

	const uint32_t w = 1, h = 1;
	const UINTN delta = w * sizeof(EFI_GRAPHICS_OUTPUT_BLT_PIXEL);

	EFI_GRAPHICS_OUTPUT_BLT_PIXEL pixel;
	pixel.Blue  = b;
	pixel.Green = g;
	pixel.Red   = r;
	pixel.Reserved = 0x00;

	gop->Blt(gop,
		&pixel,					// Dot data
		EfiBltBufferToVideo,	// Draw dot
		0, 0,					// UINTN SourceX, SourceY
		x, y,					// UINTN DestinationX, DestinationY
		w, h,					// UINTN Width, Height
		delta					// UINTN Delta
		);
}

/**
 * Load a PNG image file
 *
 * @param root_dir The root directory for loading a PNG.
 * @param path The PNG path within the root directory.
 * @return The loaded BMP, or 0 if not available.
 */
#include "../my_efilib/my_efilib.h"
#include "../upng/upng.h"

static void* init_bmp(uint32_t w, uint32_t h)
{
	BMP* bmp = 0;

	Log(config.debug, L"HackBGRT: init_bmp() (%d x %d).\n", w, h);

	// 3 = RGB 3byte
	// 54 = 24bit BMP has 54byte header
	// Padding for 4 byte alignment
	// const int pad = (w & 3);
	const UINT32 size = ((w * 3) + (w & 3)) * h + 54;
	Log(config.debug, L"HackBGRT: init_bmp() AllocatePool %ld.\n", size);
	BS->AllocatePool(EfiBootServicesData, size, (void*)&bmp);
	if (!bmp) return 0;

	// BI_RGB 24bit
	CopyMem(
		bmp,
		"\x42\x4d"
		"\x00\x00\x00\x00"
		"\x00\x00"
		"\x00\x00"
		"\x36\x00\x00\x00"
		"\x28\x00\x00\x00"
		"\x00\x00\x00\x00"
		"\x00\x00\x00\x00"
		"\x01\x00"
		"\x18\x00"
		"\x00\x00\x00\x00"
		"\x00\x00\x00\x00"
		"\x13\x0b\x00\x00"
		"\x13\x0b\x00\x00"
		"\x00\x00\x00\x00"
		"\x00\x00\x00\x00",
		54
	);

	// Windows Bitmap Byte Order = Little Endian
	bmp->file_size = DWORD_TO_BYTES_LE(size);
	bmp->width  = DWORD_TO_BYTES_LE(w);
	bmp->height = DWORD_TO_BYTES_LE(h);
	bmp->image_size = DWORD_TO_BYTES_LE(size - 54);

	return bmp;
}


static void* decode_png(void* buffer, UINTN size)
{
	// upng
	upng_t* upng;
	unsigned width, height, depth;
	unsigned x, y, d;

	upng = upng_new_from_bytes(buffer, size);
	if (!upng) {
		Print(L"HackBGRT: Failed to upng NULL\n");
		return 0;
	}

	if (upng_get_error(upng) != UPNG_EOK) {
		Print(L"HackBGRT: Failed to upng %u %u\n", upng_get_error(upng), upng_get_error_line(upng));
		upng_free(upng);
		return 0;
	}

	// Reads just the header, sets image properties
	if (upng_header(upng) != UPNG_EOK) {
		Print(L"HackBGRT: Failed to upng_header %u %u\n", upng_get_error(upng), upng_get_error_line(upng));
		upng_free(upng);
		return 0;
	}

	// Decodes image data
	if (upng_decode(upng) != UPNG_EOK) {
		Print(L"HackBGRT: Failed to upng_decode %u %u\n", upng_get_error(upng), upng_get_error_line(upng));
		upng_free(upng);
		return 0;
	}

	width  = upng_get_width(upng);
	height = upng_get_height(upng);
	depth  = upng_get_bpp(upng) / 8;
	if (depth < 1) depth = 1;

	BMP* bmp = init_bmp(width, height);
	if (!bmp) {
		Print(L"HackBGRT: Failed to init_bmp\n");
		upng_free(upng);
		return 0;
	}

	Log(config.debug, L"size: %ux%ux%u (%u)\n", width, height, upng_get_bpp(upng), upng_get_size(upng));
	Log(config.debug, L"format: %u\n", upng_get_format(upng));

	int decode_type = 0;
	int is_index_color = 0;
	if (upng_get_format(upng) == UPNG_RGB8 || upng_get_format(upng) == UPNG_RGBA8) {
		// 8-bit RGB
		decode_type = 1;
	} else
	if (upng_get_format(upng) == UPNG_RGB16 || upng_get_format(upng) == UPNG_RGBA16) {
		// 16-bit RGB
		decode_type = 2;
	} else
	if (upng_get_format(upng) == UPNG_LUMINANCE8 || upng_get_format(upng) == UPNG_LUMINANCE_ALPHA8) {
		// 8-bit Greyscale
		decode_type = 3;
	} else
	if (upng_get_format(upng) == UPNG_INDEX8) {
		// 8-bit Index Color
		decode_type = 3;
		is_index_color = 1;
	} else
	if (upng_get_format(upng) == UPNG_INDEX4) {
		// 4-bit Index Color
		decode_type = 4;
		is_index_color = 1;
	} else
	if (upng_get_format(upng) == UPNG_INDEX2) {
		// 2-bit Index Color
		decode_type = 5;
		is_index_color = 1;
	} else
	if (upng_get_format(upng) == UPNG_INDEX1) {
		// 1-bit Index Color
		decode_type = 6;
		is_index_color = 1;
	} else
	if (upng_get_format(upng) == UPNG_LUMINANCE4) {
		// 4-bit Greyscale
		decode_type = 4;
	} else
	if (upng_get_format(upng) == UPNG_LUMINANCE2) {
		// 2-bit Greyscale
		decode_type = 5;
	} else
	if (upng_get_format(upng) == UPNG_LUMINANCE1) {
		// 1-bit Greyscale B/W
		decode_type = 6;
	}

	if (!decode_type) {
		Print(L"HackBGRT: No Support PNG format %u\n", upng_get_format(upng));
		upng_free(upng);
		return 0;
	}

	const unsigned char* upng_palette = upng_get_palette(upng);
	if (is_index_color && !upng_palette) {
		Print(L"HackBGRT: Error No PLTE chunk Index Color Palette\n");
		upng_free(upng);
		return 0;
	}

	const int shift_4bit[] = {4, 0};
	const int shift_2bit[] = {6, 4, 2, 0};
	const int shift_1bit[] = {7, 6, 5, 4, 3, 2, 1, 0};
	int shift = 0;
	UINT8 c = 0;
	UINT16 u16 = 0;

	const unsigned char* upng_buffer = upng_get_buffer(upng);
	UINT32 bmp_width = ((width * 3) + (width & 3));
	for (y = 0; y != height; ++y) {
		for (x = 0; x != width; ++x) {
			UINT32 bmp_pos = bmp_width * (height - y - 1) + (x * 3) + 54;
			UINT32 png_pos = (y * width + x) * depth;

			if (decode_type == 1) {
				// 8-bit RGB
				for (d = 0; d < 3; ++d) {
					// B,G,R
					c = upng_buffer[png_pos + (3 - d - 1)];
					((UINT8*)bmp)[bmp_pos] = c;
					++bmp_pos;
				}
			} else
			if (decode_type == 2) {
				// 16-bit RGB
				for (d = 0; d < 3; ++d) {
					// // B,G,R Upper 8bit
					// c = upng_buffer[png_pos + (6 - (d*2) - 2)];
					// B,G,R 16bit to 8bit Nearest-Neighbor method
					u16 = upng_buffer[png_pos + (6 - (d*2) - 2)];
					u16 <<= 8;
					u16 |= upng_buffer[png_pos + (6 - (d*2) - 1)];
					if (u16 >= 0xFF7F) {
						c = 0xFF;
					} else {
						c = (u16 + 0x80) / 0x101;
					}
					((UINT8*)bmp)[bmp_pos] = c;
					++bmp_pos;
				}
			} else
			if (decode_type >= 3) {
				if (decode_type == 3) {
					// 8-bit
					c = upng_buffer[png_pos];
				} else
				if (decode_type == 4) {
					// 4-bit
					shift = png_pos & 1;
					png_pos >>= 1;
					c = upng_buffer[png_pos] >> shift_4bit[shift];
					c = (c & 0x0F);
					if (!is_index_color) {
						// B,G,R Grayscale 4bit
						c = c * 0x11;
					}
				} else
				if (decode_type == 5) {
					// 2-bit
					shift = png_pos & 3;
					png_pos >>= 2;
					c = upng_buffer[png_pos] >> shift_2bit[shift];
					c = (c & 0x03);
					if (!is_index_color) {
						// B,G,R Grayscale 2bit
						c = c * 0x55;
					}
				} else
				if (decode_type == 6) {
					// 1-bit
					shift = png_pos & 7;
					png_pos >>= 3;
					c = upng_buffer[png_pos] >> shift_1bit[shift];
					c = (c & 0x01);
					if (!is_index_color) {
						// B,G,R Grayscale B/W
						c = c * 0xFF;
					}
				}

				if (is_index_color) {
					// B,G,R Palette
					((UINT8*)bmp)[bmp_pos]   = upng_palette[c * 3 + 2];
					((UINT8*)bmp)[++bmp_pos] = upng_palette[c * 3 + 1];
					((UINT8*)bmp)[++bmp_pos] = upng_palette[c * 3];
				} else {
					// B,G,R Grayscale 8bit
					((UINT8*)bmp)[bmp_pos]   = c;
					((UINT8*)bmp)[++bmp_pos] = c;
					((UINT8*)bmp)[++bmp_pos] = c;
				}
				++bmp_pos;
			}

			// Debug Plot Dot pixel
			if (config.debug && 0) {
				UINT8 r = ((UINT8*)bmp)[bmp_pos - 1];
				UINT8 g = ((UINT8*)bmp)[bmp_pos - 2];
				UINT8 b = ((UINT8*)bmp)[bmp_pos - 3];
				plot_dot(x, y, r, g, b);
			}

			// Debug
			if ((x % 32) || (y % 32) || (x > 256) || (y > 256))
				continue;

			// B,G,R
			UINT8 r = ((UINT8*)bmp)[--bmp_pos];
			UINT8 g = ((UINT8*)bmp)[--bmp_pos];
			UINT8 b = ((UINT8*)bmp)[--bmp_pos];
			Log(config.debug, L"HackBGRT: bmp (%4d, %4d) #%02x%02x%02x.\n", x, y, r, g, b);
		}
	}

	// Frees the resources attached to a upng_t object
	upng_free(upng);

	return bmp;
}

static BMP* LoadPNG(EFI_FILE_HANDLE root_dir, const CHAR16* path) {
	void* buffer = 0;
	Log(config.debug, L"HackBGRT: Loading PNG %s.\n", path);
	UINTN size;
	buffer = LoadFile(root_dir, path, &size);
	if (!buffer) {
		Print(L"HackBGRT: Failed to load PNG (%s)!\n", path);
		BS->Stall(1000000);
		return 0;
	}

	BMP* bmp = decode_png(buffer, size);
	if (!bmp) {
		FreePool(buffer);
		Print(L"HackBGRT: Failed to decoce PNG (%s)!\n", path);
		BS->Stall(1000000);
		return 0;
	}

	return bmp;
}

//------------------------------------------------------------------------------
// jpg2tga.c
// JPEG to TGA file conversion example program.
// Public domain, Rich Geldreich <richgel99@gmail.com>
// Last updated Nov. 26, 2010
//------------------------------------------------------------------------------
#include "../picojpeg/picojpeg.h"

#define STRING(var) #var

#define PJPG_ENUM(DO) \
    DO(PJPEG_OK) \
    DO(PJPG_NO_MORE_BLOCKS) \
    DO(PJPG_BAD_DHT_COUNTS) \
    DO(PJPG_BAD_DHT_INDEX) \
    DO(PJPG_BAD_DHT_MARKER) \
    DO(PJPG_BAD_DQT_MARKER) \
    DO(PJPG_BAD_DQT_TABLE) \
    DO(PJPG_BAD_PRECISION) \
    DO(PJPG_BAD_HEIGHT) \
    DO(PJPG_BAD_WIDTH) \
    DO(PJPG_TOO_MANY_COMPONENTS) \
    DO(PJPG_BAD_SOF_LENGTH) \
    DO(PJPG_BAD_VARIABLE_MARKER) \
    DO(PJPG_BAD_DRI_LENGTH) \
    DO(PJPG_BAD_SOS_LENGTH) \
    DO(PJPG_BAD_SOS_COMP_ID) \
    DO(PJPG_W_EXTRA_BYTES_BEFORE_MARKER) \
    DO(PJPG_NO_ARITHMITIC_SUPPORT) \
    DO(PJPG_UNEXPECTED_MARKER) \
    DO(PJPG_NOT_JPEG) \
    DO(PJPG_UNSUPPORTED_MARKER) \
    DO(PJPG_BAD_DQT_LENGTH) \
    DO(PJPG_TOO_MANY_BLOCKS22) \
    DO(PJPG_UNDEFINED_QUANT_TABLE) \
    DO(PJPG_UNDEFINED_HUFF_TABLE) \
    DO(PJPG_NOT_SINGLE_SCAN) \
    DO(PJPG_UNSUPPORTED_COLORSPACE) \
    DO(PJPG_UNSUPPORTED_SAMP_FACTORS) \
    DO(PJPG_DECODE_ERROR) \
    DO(PJPG_BAD_RESTART_MARKER) \
    DO(PJPG_ASSERTION_ERROR) \
    DO(PJPG_BAD_SOS_SPECTRAL) \
    DO(PJPG_BAD_SOS_SUCCESSIVE) \
    DO(PJPG_STREAM_READ_ERROR) \
    DO(PJPG_NOTENOUGHMEM) \
    DO(PJPG_UNSUPPORTED_COMP_IDENT) \
    DO(PJPG_UNSUPPORTED_QUANT_TABLE) \
    DO(PJPG_UNSUPPORTED_MODE)

#define MAKE_STRINGS(VAR) #VAR,
const char* const PJPG_ERROR_MESSAGE[] = {
    PJPG_ENUM(MAKE_STRINGS)
};

static CHAR16* stringtoC16(const char * const str) {
    static CHAR16 s_tmp[32+1];
    CHAR16* d = s_tmp;
    char* p = (char*)str;
    while (*p) {
        // No Guard on Length Overflow
        *d++ = (CHAR16)*p++;
    }
    *d = 0;
    return s_tmp;
}

//------------------------------------------------------------------------------
#ifndef max
#define max(a,b)    (((a) > (b)) ? (a) : (b))
#endif
#ifndef min
#define min(a,b)    (((a) < (b)) ? (a) : (b))
#endif
//------------------------------------------------------------------------------
typedef unsigned char uint8;
typedef unsigned int uint;
//------------------------------------------------------------------------------
static unsigned char *g_pInFile;
static uint g_nInFileSize;
static uint g_nInFileOfs;
//------------------------------------------------------------------------------
unsigned char pjpeg_need_bytes_callback(unsigned char* pBuf, unsigned char buf_size, unsigned char *pBytes_actually_read, void *pCallback_data)
{
   uint n;
   pCallback_data;

   n = min(g_nInFileSize - g_nInFileOfs, buf_size);

   if ((g_nInFileOfs < 2048) || ((g_nInFileSize - g_nInFileOfs) < 2048)) {
      Log(config.debug, L"pjpeg_need_bytes_callback: buf_size %d, n %d, %d, %d\n", buf_size, n, g_nInFileOfs, g_nInFileSize);
   } else {
      Log(config.debug, L".");
   }

   memcpy(pBuf, &g_pInFile[g_nInFileOfs], n);
   *pBytes_actually_read = (unsigned char)(n);
   g_nInFileOfs += n;
   return 0;
}
//------------------------------------------------------------------------------
// Loads JPEG image from specified file. Returns NULL on failure.
// On success, the malloc()'d image's width/height is written to *x and *y, and
// the number of components (1 or 3) is written to *comps.
// pScan_type can be NULL, if not it'll be set to the image's pjpeg_scan_type_t.
// Not thread safe.
// If reduce is non-zero, the image will be more quickly decoded at approximately
// 1/8 resolution (the actual returned resolution will depend on the JPEG
// subsampling factor).
uint8 *pjpeg_load_from_file(void* buffer, UINTN size, int *ix, int *iy, int *comps, pjpeg_scan_type_t *pScan_type, int reduce)
{
   pjpeg_image_info_t image_info;
   int mcu_x = 0;
   int mcu_y = 0;
   uint row_pitch;
   uint8 *pImage;
   uint8 status;
   uint decoded_width, decoded_height;
   uint row_blocks_per_mcu, col_blocks_per_mcu;

   *ix = 0;
   *iy = 0;
   *comps = 0;
   if (pScan_type) *pScan_type = PJPG_GRAYSCALE;

   g_pInFile = (void*)buffer;
   g_nInFileOfs = 0;
   g_nInFileSize = size;

   Log(config.debug, L"pjpeg_load_from_file: Size %d.\n", size);
   status = pjpeg_decode_init(&image_info, pjpeg_need_bytes_callback, NULL, (unsigned char)reduce);
   if (status)
   {
      Print(L"pjpeg_decode_init() failed with status %u(%s)\n", status, stringtoC16(PJPG_ERROR_MESSAGE[status]));

      if (status == PJPG_UNSUPPORTED_MODE)
      {
         Print(L"Progressive JPEG files are not supported.\n");
      }

      free(g_pInFile);
      return NULL;
   }

   if (pScan_type)
      *pScan_type = image_info.m_scanType;

   // In reduce mode output 1 pixel per 8x8 block.
   decoded_width = reduce ? (image_info.m_MCUSPerRow * image_info.m_MCUWidth) / 8 : image_info.m_width;
   decoded_height = reduce ? (image_info.m_MCUSPerCol * image_info.m_MCUHeight) / 8 : image_info.m_height;

   // row_pitch = width byte size
   row_pitch = decoded_width * image_info.m_comps;
   pImage = (uint8 *)malloc(row_pitch * decoded_height);
   if (!pImage)
   {
      free(g_pInFile);
      return NULL;
   }

   row_blocks_per_mcu = image_info.m_MCUWidth >> 3;
   col_blocks_per_mcu = image_info.m_MCUHeight >> 3;

   for ( ; ; )
   {
      int y, x;
      uint8 *pDst_row;

      status = pjpeg_decode_mcu();

      if (status)
      {
         if (status != PJPG_NO_MORE_BLOCKS)
         {
            Print(L"pjpeg_decode_mcu() failed with status %u\n", status);

            free(pImage);
            free(g_pInFile);
            return NULL;
         }

         break;
      }

      if (mcu_y >= image_info.m_MCUSPerCol)
      {
         free(pImage);
         free(g_pInFile);
         return NULL;
      }

      if (reduce)
      {
         // In reduce mode, only the first pixel of each 8x8 block is valid.
         pDst_row = pImage + mcu_y * col_blocks_per_mcu * row_pitch + mcu_x * row_blocks_per_mcu * image_info.m_comps;
         if (image_info.m_scanType == PJPG_GRAYSCALE)
         {
            *pDst_row = image_info.m_pMCUBufR[0];
         }
         else
         {
            // uint y, x;
            for (y = 0; y < col_blocks_per_mcu; y++)
            {
               uint src_ofs = (y * 128U);
               for (x = 0; x < row_blocks_per_mcu; x++)
               {
                  pDst_row[0] = image_info.m_pMCUBufR[src_ofs];
                  pDst_row[1] = image_info.m_pMCUBufG[src_ofs];
                  pDst_row[2] = image_info.m_pMCUBufB[src_ofs];
                  pDst_row += 3;
                  src_ofs += 64;
               }

               pDst_row += row_pitch - 3 * row_blocks_per_mcu;
            }
         }
      }
      else
      {
         // Copy MCU's pixel blocks into the destination bitmap.
         pDst_row = pImage + (mcu_y * image_info.m_MCUHeight) * row_pitch + (mcu_x * image_info.m_MCUWidth * image_info.m_comps);

         for (y = 0; y < image_info.m_MCUHeight; y += 8)
         {
            const int by_limit = min(8, image_info.m_height - (mcu_y * image_info.m_MCUHeight + y));

            for (x = 0; x < image_info.m_MCUWidth; x += 8)
            {
               uint8 *pDst_block = pDst_row + x * image_info.m_comps;

               // Compute source byte offset of the block in the decoder's MCU buffer.
               uint src_ofs = (x * 8U) + (y * 16U);
               const uint8 *pSrcR = image_info.m_pMCUBufR + src_ofs;
               const uint8 *pSrcG = image_info.m_pMCUBufG + src_ofs;
               const uint8 *pSrcB = image_info.m_pMCUBufB + src_ofs;

               const int bx_limit = min(8, image_info.m_width - (mcu_x * image_info.m_MCUWidth + x));

               if (image_info.m_scanType == PJPG_GRAYSCALE)
               {
                  int bx, by;
                  for (by = 0; by < by_limit; by++)
                  {
                     uint8 *pDst = pDst_block;

                     for (bx = 0; bx < bx_limit; bx++)
                        *pDst++ = *pSrcR++;

                     pSrcR += (8 - bx_limit);

                     pDst_block += row_pitch;
                  }
               }
               else
               {
                  int bx, by;
                  for (by = 0; by < by_limit; by++)
                  {
                     uint8 *pDst = pDst_block;

                     for (bx = 0; bx < bx_limit; bx++)
                     {
                        pDst[0] = *pSrcR++;
                        pDst[1] = *pSrcG++;
                        pDst[2] = *pSrcB++;
                        pDst += 3;
                     }

                     pSrcR += (8 - bx_limit);
                     pSrcG += (8 - bx_limit);
                     pSrcB += (8 - bx_limit);

                     pDst_block += row_pitch;
                  }
               }
            }

            pDst_row += (row_pitch * 8);
         }
      }

      mcu_x++;
      if (mcu_x == image_info.m_MCUSPerRow)
      {
         mcu_x = 0;
         mcu_y++;
      }
   }

   free(g_pInFile);

   *ix = decoded_width;
   *iy = decoded_height;
   *comps = image_info.m_comps;

   return pImage;
}
//------------------------------------------------------------------------------
static void get_pixel(int* pDst, const uint8 *pSrc, int luma_only, int num_comps)
{
   int r, g, b;
   if (num_comps == 1)
   {
      r = g = b = pSrc[0];
   }
   else if (luma_only)
   {
      const int YR = 19595, YG = 38470, YB = 7471;
      r = g = b = (pSrc[0] * YR + pSrc[1] * YG + pSrc[2] * YB + 32768) / 65536;
   }
   else
   {
      r = pSrc[0]; g = pSrc[1]; b = pSrc[2];
   }
   pDst[0] = r; pDst[1] = g; pDst[2] = b;
}

//------------------------------------------------------------------------------
#define EXIT_FAILURE NULL

static void* decode_jpeg(void* buffer, UINTN size)
{
   int width, height, comps;
   pjpeg_scan_type_t scan_type;
   uint8 *pImage;
   int reduce = 0;
   UINT16 *p = L"";

   pImage = pjpeg_load_from_file(buffer, size, &width, &height, &comps, &scan_type, reduce);
   if (!pImage)
   {
      Print(L"Failed loading source image!\n");
      return EXIT_FAILURE;
   }

   Log(config.debug, L"Width: %d, Height: %d, Comps: %d\n", width, height, comps);

   switch (scan_type)
   {
      case PJPG_GRAYSCALE: p = L"GRAYSCALE"; break;
      case PJPG_YH1V1: p = L"H1V1"; break;
      case PJPG_YH2V1: p = L"H2V1"; break;
      case PJPG_YH1V2: p = L"H1V2"; break;
      case PJPG_YH2V2: p = L"H2V2"; break;
   }
   Log(config.debug, L"Scan type: %s\n", p);

	BMP* bmp = init_bmp(width, height);
	if (!bmp) {
		Print(L"HackBGRT: Failed to init_bmp\n");
		free(pImage);
		return 0;
	}

	int x, y, d;
	int a[3];
	UINT32 bmp_width = ((width * 3) + (width & 3));
	for (y = 0; y != height; ++y) {
        int pImagePos = (y * width) * comps;
		for (x = 0; x != width; ++x) {
			get_pixel(a, &pImage[pImagePos], (scan_type == PJPG_GRAYSCALE), comps);
            pImagePos += comps;

			// Log(config.debug, L"HackBGRT: bmp (%4d, %4d) #%04x.\n", x, y, a[0]);

			UINT32 bmp_pos = bmp_width * (height - y - 1) + (x * 3) + 54;
			for (d = 2; d >= 0; --d) {
				// B,G,R
				UINT8 c = (UINT8)(a[d] & 0xFF);
				((UINT8*)bmp)[bmp_pos] = c;
				++bmp_pos;
			}

			// Debug Plot Dot pixel
			if (config.debug && 0) {
				UINT8 r = ((UINT8*)bmp)[bmp_pos - 1];
				UINT8 g = ((UINT8*)bmp)[bmp_pos - 2];
				UINT8 b = ((UINT8*)bmp)[bmp_pos - 3];
				plot_dot(x, y, r, g, b);
			}

			// Debug
			if ((x % 32) || (y % 32) || (x > 256) || (y > 256))
				continue;

			// B,G,R
			UINT8 r = ((UINT8*)bmp)[--bmp_pos];
			UINT8 g = ((UINT8*)bmp)[--bmp_pos];
			UINT8 b = ((UINT8*)bmp)[--bmp_pos];
			Log(config.debug, L"HackBGRT: bmp (%4d, %4d) #%02x%02x%02x.\n", x, y, r, g, b);
		}
	}

   free(pImage);

   return bmp;
}

static BMP* LoadJPEG(EFI_FILE_HANDLE root_dir, const CHAR16* path) {
    void* buffer = 0;
    Log(config.debug, L"HackBGRT: Loading JPEG %s.\n", path);
    UINTN size;
    buffer = LoadFile(root_dir, path, &size);
    if (!buffer) {
        Print(L"HackBGRT: Failed to load JPEG (%s)!\n", path);
        BS->Stall(1000000);
        return 0;
    }

    BMP* bmp = decode_jpeg(buffer, size);
    if (!bmp) {
        FreePool(buffer);
        Print(L"HackBGRT: Failed to decoce JPEG (%s)!\n", path);
        BS->Stall(1000000);
        return 0;
    }

    return bmp;
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
		Log(1, L"HackBGRT: Failed to allocate a blank BMP!\n");
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
	Log(config.debug, L"HackBGRT: Loading %s.\n", path);
	BMP* bmp = 0;
	UINTN len = StrLen(path);
	CHAR16 last_char_2 = path[len - 2];
	Log(config.debug, L"HackBGRT: Filename Len %d, Last Char %c.\n", (int)len, last_char_2);
	if (last_char_2 == 'm' || last_char_2 == 'M') {
		// xxx.BMP
		UINTN size = 0;
		bmp = LoadFile(root_dir, path, &size);
		if (bmp) {
			if (size >= bmp->file_size && CompareMem(bmp, "BM", 2) == 0 && bmp->file_size - bmp->pixel_data_offset > 4 && bmp->width && bmp->height && (bmp->bpp == 32 || bmp->bpp == 24) && bmp->compression == 0) {
				// return bmp;
			} else {
				FreePool(bmp);
				Log(1, L"HackBGRT: Invalid BMP (%s)!\n", path);
				bmp = 0;
			}
		} else {
			Log(1, L"HackBGRT: Failed to load BMP (%s)!\n", path);
		}
	} else if (last_char_2 == 'n' || last_char_2 == 'N') {
		// xxx.PNG
		bmp = LoadPNG(root_dir, path);
	} else {
		// xxx.JPG
		// xxx.JPEG
		bmp = LoadJPEG(root_dir, path);
	}
	if (bmp) {
		Log(config.debug, L"HackBGRT: Load Success %s.\n", path);

		return bmp;
	}

	Log(1, L"HackBGRT: Failed to load IMAGE (%s)!\n", path);
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
			Log(1, L"HackBGRT: Failed to allocate memory for BGRT.\n");
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

	Log(config.debug,
		L"HackBGRT: BMP at (%d, %d), center (%d, %d), resolution (%d, %d), orientation %d.\n",
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
static EFI_HANDLE LoadApp(int print_failure, EFI_HANDLE image_handle, EFI_LOADED_IMAGE* image, const CHAR16* path) {
	EFI_DEVICE_PATH* boot_dp = FileDevicePath(image->DeviceHandle, (CHAR16*) path);
	EFI_HANDLE result = 0;
	Log(config.debug, L"HackBGRT: Loading application %s.\n", path);
	if (EFI_ERROR(BS->LoadImage(0, image_handle, boot_dp, 0, 0, &result))) {
		Log(config.debug || print_failure, L"HackBGRT: Failed to load application %s.\n", path);
	}
	return result;
}

/**
 * The main program.
 */
EFI_STATUS efi_main(EFI_HANDLE image_handle, EFI_SYSTEM_TABLE *ST_) {
	InitializeLib(image_handle, ST_);
	Log(0, L"HackBGRT version: %s\n", version);

	EFI_LOADED_IMAGE* image;
	if (EFI_ERROR(BS->HandleProtocol(image_handle, &LoadedImageProtocol, (void**) &image))) {
		Log(config.debug, L"HackBGRT: LOADED_IMAGE_PROTOCOL failed.\n");
		goto fail;
	}

	EFI_FILE_HANDLE root_dir = LibOpenRoot(image->DeviceHandle);

	CHAR16 **argv;
	int argc = GetShellArgcArgv(image_handle, &argv);

	if (argc <= 1) {
		const CHAR16* config_path = L"\\EFI\\HackBGRT\\config.txt";
		if (!ReadConfigFile(&config, root_dir, config_path)) {
			Log(1, L"HackBGRT: No config, no command line!\n", config_path);
			goto fail;
		}
	}
	for (int i = 1; i < argc; ++i) {
		ReadConfigLine(&config, root_dir, argv[i]);
	}
	if (config.debug) {
		Print(L"HackBGRT version: %s\n", version);
	}

	SetResolution(config.resolution_x, config.resolution_y);
	HackBgrt(root_dir);

	EFI_HANDLE next_image_handle = 0;
	static CHAR16 backup_boot_path[] = L"\\EFI\\HackBGRT\\bootmgfw-original.efi";
	static CHAR16 ms_boot_path[] = L"\\EFI\\Microsoft\\Boot\\bootmgfw.efi";
	int try_ms_quietly = 1;

	if (config.boot_path && StriCmp(config.boot_path, L"MS") != 0) {
		next_image_handle = LoadApp(1, image_handle, image, config.boot_path);
		try_ms_quietly = 0;
	}
	if (!next_image_handle) {
		config.boot_path = backup_boot_path;
		next_image_handle = LoadApp(!try_ms_quietly, image_handle, image, config.boot_path);
		if (!next_image_handle) {
			config.boot_path = ms_boot_path;
			next_image_handle = LoadApp(!try_ms_quietly, image_handle, image, config.boot_path);
			if (!next_image_handle) {
				goto fail;
			}
		}
		if (try_ms_quietly) {
			goto ready_to_boot;
		}
		Log(1, L"HackBGRT: Reverting to %s.\n", config.boot_path);
		Print(L"Press escape to cancel or any other key (or wait 15 seconds) to boot.\n");
		if (ReadKey(15000).ScanCode == SCAN_ESC) {
			goto fail;
		}
	} else ready_to_boot: if (config.debug) {
		Print(L"HackBGRT: Ready to boot.\n");
		Print(L"If all goes well, you can set debug=0 and log=0 in config.txt.\n");
		Print(L"Press escape to cancel or any other key (or wait 15 seconds) to boot.\n");
		if (ReadKey(15000).ScanCode == SCAN_ESC) {
			return 0;
		}
	}
	if (!config.log) {
		ClearLogVariable();
	}
	if (EFI_ERROR(BS->StartImage(next_image_handle, 0, 0))) {
		Log(1, L"HackBGRT: Failed to start %s.\n", config.boot_path);
		goto fail;
	}
	Log(1, L"HackBGRT: Started %s. Why are we still here?!\n", config.boot_path);
	Print(L"Please check that %s is not actually HackBGRT!\n", config.boot_path);
	goto fail;

	fail: {
		Log(1, L"HackBGRT has failed.\n");
		Print(L"Dumping log:\n\n");
		DumpLog();
		Print(L"If you can't boot into Windows, get install/recovery disk to fix your boot.\n");
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
// EFI_STATUS EFIAPI _EfiMain(EFI_HANDLE image_handle, EFI_SYSTEM_TABLE *ST_) {
// 	return EfiMain(image_handle, ST_);
// }
