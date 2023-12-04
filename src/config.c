#include "config.h"
#include "util.h"

BOOLEAN ReadConfigFile(struct HackBGRT_config* config, EFI_FILE_HANDLE base_dir, const CHAR16* path) {
	void* data = 0;
	UINTN data_bytes = 0;
	data = LoadFileWithPadding(base_dir, path, &data_bytes, 4);
	if (!data) {
		Log(1, L"Failed to load configuration (%s)!\n", path);
		return FALSE;
	}
	CHAR16* str;
	UINTN str_len;
	if (*(CHAR16*)data == 0xfeff) {
		// UCS-2
		str = data;
		str_len = data_bytes / sizeof(*str);
	} else {
		// UTF-8 -> UCS-2
		EFI_STATUS e = BS->AllocatePool(EfiBootServicesData, data_bytes * 2 + 2, (void**)&str);
		if (EFI_ERROR(e)) {
			BS->FreePool(data);
			return FALSE;
		}
		UINT8* str0 = data;
		for (UINTN i = str_len = 0; i < data_bytes;) {
			UINTN unicode = 0xfffd;
			if (str0[i] < 0x80) {
				unicode = str0[i];
				i += 1;
			} else if (str0[i] < 0xc0) {
				i += 1;
			} else if (str0[i] < 0xe0) {
				unicode = ((str0[i] & 0x1f) << 6) | ((str0[i+1] & 0x3f) << 0);
				i += 2;
			} else if (str0[i] < 0xf0) {
				unicode = ((str0[i] & 0x0f) << 12) | ((str0[i+1] & 0x3f) << 6) | ((str0[i+2] & 0x3f) << 0);
				i += 3;
			} else if (str0[i] < 0xf8) {
				unicode = ((str0[i] & 0x07) << 18) | ((str0[i+1] & 0x3f) << 12) | ((str0[i+2] & 0x3f) << 6) | ((str0[i+3] & 0x3f) << 0);
				i += 4;
			} else {
				i += 1;
			}
			if (unicode <= 0xffff) {
				str[str_len++] = unicode;
			} else {
				str[str_len++] = 0xfffd;
			}
		}
		str[str_len] = 0;
		BS->FreePool(data);
	}

	for (int i = 0; i < str_len;) {
		int j = i;
		while (j < str_len && str[j] != '\r' && str[j] != '\n') {
			++j;
		}
		while (j < str_len && (str[j] == '\r' || str[j] == '\n')) {
			str[j] = 0;
			++j;
		}
		ReadConfigLine(config, base_dir, &str[i]);
		i = j;
	}
	// NOTICE: string is not freed, because paths are not copied.
	return TRUE;
}

static void SetBMPWithRandom(struct HackBGRT_config* config, int weight, enum HackBGRT_action action, int x, int y, int o, const CHAR16* path) {
	config->image_weight_sum += weight;
	UINT32 random = (((UINT64) Random() & 0xffffffff) * config->image_weight_sum) >> 32;
	UINT32 limit = ((UINT64) 0xffffffff * weight) >> 32;
	Log(config->debug, L"%s n=%d, action=%d, x=%d, y=%d, o=%d, path=%s, rand=%x/%x\n", random <= limit ? L"Using" : L"Skipping", weight, action, x, y, o, path, random, limit);
	if (random <= limit) {
		config->action = action;
		config->image_path = path;
		config->orientation = o;
		config->image_x = x;
		config->image_y = y;
	}
}

static int ParseCoordinate(const CHAR16* str, enum HackBGRT_action action) {
	if (str && ((L'0' <= str[0] && str[0] <= L'9') || str[0] == L'-')) {
		return str[0] == L'-' ? -(int)Atoi(str+1) : (int)Atoi(str);
	}
	if ((str && StrnCmp(str, L"keep", 4) == 0) || action == HackBGRT_KEEP) {
		return HackBGRT_coord_keep;
	}
	return 0;
}

static void ReadConfigImage(struct HackBGRT_config* config, const CHAR16* line) {
	const CHAR16* n = StrStrAfter(line, L"n=");
	const CHAR16* x = StrStrAfter(line, L"x=");
	const CHAR16* y = StrStrAfter(line, L"y=");
	const CHAR16* o = StrStrAfter(line, L"o=");
	const CHAR16* f = StrStrAfter(line, L"path=");
	enum HackBGRT_action action = HackBGRT_KEEP;
	if (f) {
		action = HackBGRT_REPLACE;
	} else if (StrStr(line, L"remove")) {
		action = HackBGRT_REMOVE;
	} else if (StrStr(line, L"black")) {
		action = HackBGRT_REPLACE;
	} else if (StrStr(line, L"keep")) {
		action = HackBGRT_KEEP;
	} else {
		Log(1, L"Invalid image line: %s\n", line);
		return;
	}
	int weight = n && (!f || n < f) ? Atoi(n) : 1;
	int x_val = ParseCoordinate(x, action);
	int y_val = ParseCoordinate(y, action);
	int o_val = o ? ParseCoordinate(o, action) : HackBGRT_coord_keep;
	SetBMPWithRandom(config, weight, action, x_val, y_val, o_val, f);
}

static void ReadConfigResolution(struct HackBGRT_config* config, const CHAR16* line) {
	const CHAR16* x = line;
	const CHAR16* y = StrStrAfter(line, L"x");
	if (x && *x && y && *y) {
		config->resolution_x = *x == '-' ? -(int)Atoi(x+1) : (int)Atoi(x);
		config->resolution_y = *y == '-' ? -(int)Atoi(y+1) : (int)Atoi(y);
	} else {
		Log(1, L"Invalid resolution line: %s\n", line);
	}
}

void ReadConfigLine(struct HackBGRT_config* config, EFI_FILE_HANDLE base_dir, const CHAR16* line) {
	line = TrimLeft(line);
	if (line[0] == L'#' || line[0] == 0) {
		return;
	}

	if (StrnCmp(line, L"debug=", 6) == 0) {
		config->debug = (StrCmp(line, L"debug=1") == 0);
		return;
	}
	if (StrnCmp(line, L"log=", 4) == 0) {
		config->log = (StrCmp(line, L"log=1") == 0);
		return;
	}
	if (StrnCmp(line, L"image=", 6) == 0) {
		ReadConfigImage(config, line + 6);
		return;
	}
	if (StrnCmp(line, L"boot=", 5) == 0) {
		config->boot_path = line + 5;
		return;
	}
	if (StrnCmp(line, L"config=", 7) == 0) {
		ReadConfigFile(config, base_dir, line + 7);
		return;
	}
	if (StrnCmp(line, L"resolution=", 11) == 0) {
		ReadConfigResolution(config, line + 11);
		return;
	}
	Log(1, L"Unknown configuration directive: %s\n", line);
}
